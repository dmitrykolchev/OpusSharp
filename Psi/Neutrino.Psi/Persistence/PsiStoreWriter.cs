// <copyright file="PsiStoreWriter.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Neutrino.Psi.Common;
using Neutrino.Psi.Data;
using Neutrino.Psi.Serialization;

namespace Neutrino.Psi.Persistence;

/// <summary>
/// Implements a writer that can write multiple streams to the same file,
/// while cataloging and indexing them.
/// </summary>
public sealed class PsiStoreWriter : IDisposable
{
    /// <summary>
    /// The size of a catalog file extent.
    /// </summary>
    public const int CatalogExtentSize = 512 * 1024;

    /// <summary>
    /// The size of the index file extent.
    /// </summary>
    public const int IndexExtentSize = 1024 * 1024;

    /// <summary>
    /// The frequency (in bytes) of index entries.
    /// Consecutive index entries point to locations that are at least this many bytes apart.
    /// </summary>
    public const int IndexPageSize = 4096;

    private readonly string _name;
    private readonly string _path;
    private readonly InfiniteFileWriter _catalogWriter;
    private readonly InfiniteFileWriter _pageIndexWriter;
    private readonly MessageWriter _writer;
    private readonly ConcurrentDictionary<int, PsiStreamMetadata> _metadata = new();
    private readonly BufferWriter _metadataBuffer = new(128);
    private readonly BufferWriter _indexBuffer = new(24);

    /// <summary>
    /// This file is opened in exclusive share mode when the exporter is constructed, and is
    /// deleted when it gets disposed. Other processes can check the live status of the store
    /// by attempting to also open this file. If that fails, then the store is still live.
    /// </summary>
    private readonly FileStream _liveMarkerFile;

    private MessageWriter _largeMessageWriter;
    private int _unindexedBytes = IndexPageSize;
    private IndexEntry _nextIndexEntry;

    /// <summary>
    /// Initializes a new instance of the <see cref="PsiStoreWriter"/> class.
    /// </summary>
    /// <param name="name">The name of the application that generated the persisted files, or the root name of the files.</param>
    /// <param name="path">The directory in which to create the partition, or null to create a volatile data store.</param>
    /// <param name="createSubdirectory">If true, a numbered sub-directory is created for this store.</param>
    public PsiStoreWriter(string name, string path, bool createSubdirectory = true)
    {
        _name = name;
        if (path != null)
        {
            int id = 0;
            _path = System.IO.Path.GetFullPath(path);
            if (createSubdirectory)
            {
                // if the root directory already exists, look for the next available id
                if (Directory.Exists(_path))
                {
                    IEnumerable<int> existingIds = Directory.EnumerateDirectories(_path, _name + ".????")
                        .Select(d => d.Split('.').Last())
                        .Where(n => int.TryParse(n, out _))
                        .Select(n => int.Parse(n));

                    id = (existingIds.Count() == 0) ? 0 : existingIds.Max() + 1;
                }

                _path = System.IO.Path.Combine(_path, $"{_name}.{id:0000}");
            }

            if (!Directory.Exists(_path))
            {
                Directory.CreateDirectory(_path);
            }
        }

        // Open the live store marker file in exclusive file share mode.  This will fail
        // if another process is already writing a store with the same name and path.
        _liveMarkerFile = File.Open(PsiStoreMonitor.GetLiveMarkerFileName(Name, Path), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        _catalogWriter = new InfiniteFileWriter(_path, PsiStoreCommon.GetCatalogFileName(_name), CatalogExtentSize);
        _pageIndexWriter = new InfiniteFileWriter(_path, PsiStoreCommon.GetIndexFileName(_name), IndexExtentSize);
        _writer = new MessageWriter(PsiStoreCommon.GetDataFileName(_name), _path);

        // write the first index entry
        UpdatePageIndex(0, default);
    }

    /// <summary>
    /// Gets the name of the store.
    /// </summary>
    public string Name => _name;

    /// <summary>
    /// Gets the path to the store.
    /// </summary>
    public string Path => _path;

    /// <summary>
    /// Gets stream metadata.
    /// </summary>
    public IEnumerable<IStreamMetadata> Metadata => _metadata.Values;

    /// <summary>
    /// Closes the store.
    /// </summary>
    public void Dispose()
    {
        _pageIndexWriter.Dispose();
        _catalogWriter.Dispose();
        _writer.Dispose();
        _largeMessageWriter?.Dispose();
        _liveMarkerFile?.Dispose();

        // If the live store marker file exists, try to delete it.
        string liveMarkerFilePath = PsiStoreMonitor.GetLiveMarkerFileName(Name, Path);
        if (File.Exists(liveMarkerFilePath))
        {
            try
            {
                File.Delete(liveMarkerFilePath);
            }
            catch
            {
            }
        }
    }

    /// <summary>
    /// Creates a stream to write messages to.
    /// The stream characteristics are extracted from the provided metadata descriptor.
    /// </summary>
    /// <param name="metadata">The metadata describing the stream to open.</param>
    /// <returns>The complete metadata for the stream just created.</returns>
    public PsiStreamMetadata OpenStream(PsiStreamMetadata metadata)
    {
        return OpenStream(metadata.Id, metadata.Name, metadata.IsIndexed, metadata.TypeName, metadata.OpenedTime).UpdateSupplementalMetadataFrom(metadata);
    }

    /// <summary>
    /// Creates a stream to write messages to.
    /// </summary>
    /// <param name="id">The id of the stream, unique for this store. All messages with this stream id will be written to this stream.</param>
    /// <param name="name">The name of the stream. This name can be later used to open the stream for reading.</param>
    /// <param name="indexed">Indicates whether the stream is indexed or not. Indexed streams have a small index entry in the main data file and the actual message body in a large data file.</param>
    /// <param name="typeName">A name identifying the type of the messages in this stream. This is usually a fully-qualified type name or a data contract name, but can be anything that the caller wants.</param>
    /// <param name="streamOpenedTime">The opened time for the stream.</param>
    /// <returns>The complete metadata for the stream just created.</returns>
    public PsiStreamMetadata OpenStream(int id, string name, bool indexed, string typeName, DateTime streamOpenedTime)
    {
        if (_metadata.ContainsKey(id))
        {
            throw new InvalidOperationException($"The stream id {id} has already been registered with this writer.");
        }

        PsiStreamMetadata psiStreamMetadata = new(name, id, typeName)
        {
            OpenedTime = streamOpenedTime,
            IsPersisted = true,
            IsIndexed = indexed,
            StoreName = _name,
            StorePath = _path,
        };
        _metadata[id] = psiStreamMetadata;
        WriteToCatalog(psiStreamMetadata);

        // make sure we have a large file if needed
        if (indexed)
        {
            _largeMessageWriter ??= new MessageWriter(PsiStoreCommon.GetLargeDataFileName(_name), _path);
        }

        return psiStreamMetadata;
    }

    /// <summary>
    /// Attempt to get stream metadata (available once stream has been opened).
    /// </summary>
    /// <param name="streamId">The id of the stream, unique for this store.</param>
    /// <param name="metadata">The metadata for the stream, if it has previously been opened.</param>
    /// <returns>True if stream metadata if stream has been opened so that metadata is available.</returns>
    public bool TryGetMetadata(int streamId, out PsiStreamMetadata metadata)
    {
        return _metadata.TryGetValue(streamId, out metadata);
    }

    /// <summary>
    /// Closes the stream and persists the stream statistics.
    /// </summary>
    /// <param name="streamId">The id of the stream to close.</param>
    /// <param name="originatingTime">The originating time when the stream was closed.</param>
    public void CloseStream(int streamId, DateTime originatingTime)
    {
        PsiStreamMetadata meta = _metadata[streamId];
        if (!meta.IsClosed)
        {
            // When a store is being rewritten (e.g. to crop, repair, etc.) the OpenedTime can
            // be after the closing originatingTime originally stored. Originating times remain
            // in the timeframe of the original store, while Opened/ClosedTimes in the rewritten
            // store reflect the wall-clock time at which each stream was rewritten.
            // Technically, the rewritten streams are opened, written, and closed in quick
            // succession, but we record an interval at least large enough to envelope the
            // originating time interval. However, a stream with zero or one message will still
            // show an empty interval (ClosedTime = OpenedTime).
            meta.ClosedTime =
                meta.OpenedTime <= originatingTime ? // opened before/at closing time?
                originatingTime : // using closing time
                meta.OpenedTime + meta.MessageOriginatingTimeInterval.Span; // o/w assume closed after span of messages
            meta.IsClosed = true;
            WriteToCatalog(meta);
        }
    }

    /// <summary>
    /// Closes the streams and persists the stream statistics.
    /// </summary>
    /// <param name="originatingTime">The originating time when the streams are closed.</param>
    public void CloseAllStreams(DateTime originatingTime)
    {
        foreach (int streamId in _metadata.Keys)
        {
            CloseStream(streamId, originatingTime);
        }
    }

    /// <summary>
    /// Initialize stream opened times.
    /// </summary>
    /// <param name="originatingTime">The originating time when the streams are opened.</param>
    public void InitializeStreamOpenedTimes(DateTime originatingTime)
    {
        foreach (PsiStreamMetadata meta in _metadata.Values)
        {
            meta.OpenedTime = originatingTime;
            WriteToCatalog(meta);
        }
    }

    /// <summary>
    /// Writes a message (envelope + data) to the store. The message is associated with the open stream that matches the id in the envelope.
    /// </summary>
    /// <param name="buffer">The payload to write. This might be written to the main data file or the large data file, depending on stream configuration. </param>
    /// <param name="envelope">The envelope of the message, identifying the stream, the time and the sequence number of the message.</param>
    public void Write(BufferReader buffer, Envelope envelope)
    {
        PsiStreamMetadata meta = _metadata[envelope.SourceId];
        meta.Update(envelope, buffer.RemainingLength);
        int bytes;
        if (meta.IsIndexed)
        {
            // write the object index entry in the data file and the buffer in the large data file
            IndexEntry indexEntry = default;
            indexEntry.ExtentId = int.MinValue + _largeMessageWriter.CurrentExtentId; // negative value indicates an index into the large file
            indexEntry.Position = _largeMessageWriter.CurrentMessageStart;
            indexEntry.CreationTime = envelope.CreationTime;
            indexEntry.OriginatingTime = envelope.OriginatingTime;
            unsafe
            {
                _indexBuffer.Write((byte*)&indexEntry, sizeof(IndexEntry));
            }

            // write the buffer to the large message file
            _largeMessageWriter.Write(buffer, envelope);

            // note that our page index points to the data file, so we need to update it with the bytes written to the data file
            bytes = _writer.Write(_indexBuffer, envelope);
            _indexBuffer.Reset();
        }
        else
        {
            bytes = _writer.Write(buffer, envelope);
        }

        UpdatePageIndex(bytes, envelope);
    }

    /// <summary>
    /// Writes the runtime info to the catalog.
    /// </summary>
    /// <param name="runtimeInfo">The runtime info.</param>
    internal void WriteToCatalog(RuntimeInfo runtimeInfo)
    {
        WriteToCatalog((Metadata)runtimeInfo);
    }

    /// <summary>
    /// Writes the type schema to the catalog.
    /// </summary>
    /// <param name="typeSchema">The type schema.</param>
    internal void WriteToCatalog(TypeSchema typeSchema)
    {
        WriteToCatalog((Metadata)typeSchema);
    }

    /// <summary>
    /// Writes the psi stream metadata to the catalog.
    /// </summary>
    /// <param name="typeSchema">The psi stream metadata schema.</param>
    internal void WriteToCatalog(PsiStreamMetadata typeSchema)
    {
        WriteToCatalog((Metadata)typeSchema);
    }

    /// <summary>
    /// Writes details about a stream to the stream catalog.
    /// </summary>
    /// <param name="metadata">The stream descriptor to write.</param>
    private void WriteToCatalog(Metadata metadata)
    {
        lock (_catalogWriter)
        {
            metadata.Serialize(_metadataBuffer);

            _catalogWriter.Write(_metadataBuffer);
            _catalogWriter.Flush();
            _metadataBuffer.Reset();
        }
    }

    /// <summary>
    /// Updates the seek index (which is an index into the main data file) if needed (every <see cref="IndexPageSize"/> bytes).
    /// </summary>
    /// <param name="bytes">Number of bytes written so far to the data file.</param>
    /// <param name="lastEnvelope">The envelope of the last message written.</param>
    private void UpdatePageIndex(int bytes, Envelope lastEnvelope)
    {
        if (lastEnvelope.OriginatingTime > _nextIndexEntry.OriginatingTime)
        {
            _nextIndexEntry.OriginatingTime = lastEnvelope.OriginatingTime;
        }

        if (lastEnvelope.CreationTime > _nextIndexEntry.CreationTime)
        {
            _nextIndexEntry.CreationTime = lastEnvelope.CreationTime;
        }

        _unindexedBytes += bytes;

        // only write an index entry if we exceeded the page size
        // The index identifies the upper bound on time and originating time for messages written so far
        if (_unindexedBytes >= IndexPageSize)
        {
            _nextIndexEntry.Position = _writer.CurrentMessageStart;
            _nextIndexEntry.ExtentId = _writer.CurrentExtentId;

            unsafe
            {
                unsafe
                {
                    IndexEntry indexEntry = _nextIndexEntry;
                    int totalBytes = sizeof(IndexEntry);
                    _pageIndexWriter.ReserveBlock(totalBytes);
                    _pageIndexWriter.WriteToBlock((byte*)&indexEntry, totalBytes);
                    _pageIndexWriter.CommitBlock();
                }
            }

            _unindexedBytes = 0;
        }
    }
}
