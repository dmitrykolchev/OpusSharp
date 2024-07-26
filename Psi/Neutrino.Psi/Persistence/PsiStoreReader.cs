// <copyright file="PsiStoreReader.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Neutrino.Psi.Common;
using Neutrino.Psi.Common.Intervals;
using Neutrino.Psi.Data;

namespace Neutrino.Psi.Persistence;

/// <summary>
/// Implements a reader that allows access to the multiple streams persisted in a single store.
/// The store reader abstracts read/write access to streams,
/// and provides the means to read only some of the streams present in the store.
/// The reader loads and exposes the metadata associated with the store prior to reading any data.
/// </summary>
public sealed class PsiStoreReader : IDisposable
{
    private readonly Dictionary<int, bool> _isIndexedStream = new();
    private readonly MessageReader _messageReader;
    private readonly MessageReader _largeMessageReader;
    private readonly Shared<MetadataCache> _metadataCache;
    private readonly Shared<PageIndexCache> _indexCache;
    private readonly HashSet<int> _enabledStreams = new();

    private TimeInterval _replayInterval = TimeInterval.Empty;
    private bool _useOriginatingTime = false;

    /// <summary>
    /// Initializes a new instance of the <see cref="PsiStoreReader"/> class.
    /// </summary>
    /// <param name="name">The name of the application that generated the persisted files, or the root name of the files.</param>
    /// <param name="path">The directory in which the main persisted file resides or will reside, or null to create a volatile data store.</param>
    /// <param name="metadataUpdateHandler">Delegate to call.</param>
    /// <param name="autoOpenAllStreams">Automatically open all streams.</param>
    public PsiStoreReader(string name, string path, Action<IEnumerable<Metadata>, RuntimeInfo> metadataUpdateHandler, bool autoOpenAllStreams = false)
    {
        Name = name;
        Path = PsiStore.GetPathToLatestVersion(name, path);
        AutoOpenAllStreams = autoOpenAllStreams;

        // open the data readers
        _messageReader = new MessageReader(PsiStoreCommon.GetDataFileName(Name), Path);
        _largeMessageReader = new MessageReader(PsiStoreCommon.GetLargeDataFileName(Name), Path);
        _indexCache = Shared.Create(new PageIndexCache(name, Path));
        _metadataCache = Shared.Create(new MetadataCache(name, Path, metadataUpdateHandler));
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PsiStoreReader"/> class.
    /// This provides a fast way to create a reader,
    /// by reusing the metadata and index already loaded by an existing store reader.
    /// </summary>
    /// <param name="other">Another reader pointing to the same store.</param>
    public PsiStoreReader(PsiStoreReader other)
    {
        Name = other.Name;
        Path = other.Path;
        AutoOpenAllStreams = other.AutoOpenAllStreams;
        _messageReader = new MessageReader(PsiStoreCommon.GetDataFileName(Name), Path);
        _largeMessageReader = new MessageReader(PsiStoreCommon.GetLargeDataFileName(Name), Path);
        _indexCache = other._indexCache.AddRef();
        _metadataCache = other._metadataCache.AddRef();
    }

    /// <summary>
    /// Gets the set of streams in this store.
    /// </summary>
    public IEnumerable<PsiStreamMetadata> AvailableStreams => _metadataCache.Resource.AvailableStreams;

    /// <summary>
    /// Gets the name of the store.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the path to the store (this is the path to the directory containing the data, index and catalog files).
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Gets a value indicating whether the reader should read all the messages in the store.
    /// </summary>
    public bool AutoOpenAllStreams { get; } = false;

    /// <summary>
    /// Gets the interval between the creation times of the first and last messages written to this store, across all streams.
    /// </summary>
    public TimeInterval MessageCreationTimeInterval => _metadataCache.Resource.MessageCreationTimeInterval;

    /// <summary>
    /// Gets the interval between the originating times of the first and last messages written to this store, across all streams.
    /// </summary>
    public TimeInterval MessageOriginatingTimeInterval => _metadataCache.Resource.MessageOriginatingTimeInterval;

    /// <summary>
    /// Gets the interval between the opened and closed times, across all streams.
    /// </summary>
    public TimeInterval StreamTimeInterval => _metadataCache.Resource.StreamTimeInterval;

    /// <summary>
    /// Gets the size of the store.
    /// </summary>
    public long Size => PsiStoreCommon.GetSize(Name, Path);

    /// <summary>
    /// Gets the number of streams in the store.
    /// </summary>
    public int StreamCount => _metadataCache.Resource.AvailableStreams.Count();

    /// <summary>
    /// Gets info about the runtime that was used to write to this store.
    /// </summary>
    public RuntimeInfo RuntimeInfo => _metadataCache.Resource.RuntimeInfo;

    /// <summary>
    /// Opens the specified stream for reading.
    /// </summary>
    /// <param name="name">The name of the stream to open.</param>
    /// <returns>The metadata describing the opened stream.</returns>
    public PsiStreamMetadata OpenStream(string name)
    {
        PsiStreamMetadata meta = GetMetadata(name);
        OpenStream(meta);
        return meta;
    }

    /// <summary>
    /// Opens the specified stream for reading.
    /// </summary>
    /// <param name="id">The id of the stream to open.</param>
    /// <returns>The metadata describing the opened stream.</returns>
    public PsiStreamMetadata OpenStream(int id)
    {
        PsiStreamMetadata meta = GetMetadata(id);
        OpenStream(meta);
        return meta;
    }

    /// <summary>
    /// Opens the specified stream for reading.
    /// </summary>
    /// <param name="meta">The metadata describing the stream to open.</param>
    /// <returns>True if the stream was successfully opened, false if no matching stream could be found.</returns>
    public bool OpenStream(PsiStreamMetadata meta)
    {
        if (_enabledStreams.Contains(meta.Id))
        {
            return false;
        }

        _enabledStreams.Add(meta.Id);
        IsIndexedStream(meta.Id); // update `isIndexedStream` dictionary
        return true;
    }

    /// <summary>
    /// Closes the specified stream. Messages from this stream will be skipped.
    /// </summary>
    /// <param name="name">The name of the stream to close.</param>
    public void CloseStream(string name)
    {
        PsiStreamMetadata meta = GetMetadata(name);
        CloseStream(meta.Id);
    }

    /// <summary>
    /// Closes the specified stream. Messages from this stream will be skipped.
    /// </summary>
    /// <param name="id">The id of the stream to close.</param>
    public void CloseStream(int id)
    {
        _enabledStreams.Remove(id);
    }

    /// <summary>
    /// Closes all the streams.
    /// </summary>
    public void CloseAllStreams()
    {
        _enabledStreams.Clear();
    }

    /// <summary>
    /// Checks whether the specified stream exist in this store.
    /// </summary>
    /// <param name="streamName">The name of the stream to look for.</param>
    /// <returns>True if a stream with the specified name exists, false otherwise.</returns>
    public bool Contains(string streamName)
    {
        return _metadataCache.Resource.TryGet(streamName, out _);
    }

    /// <summary>
    /// Returns a metadata descriptor for the specified stream.
    /// </summary>
    /// <param name="streamName">The name of the stream.</param>
    /// <returns>The metadata describing the specified stream.</returns>
    public PsiStreamMetadata GetMetadata(string streamName)
    {
        if (!_metadataCache.Resource.TryGet(streamName, out PsiStreamMetadata meta))
        {
            throw new ArgumentException($"The store {Name} does not contain a stream named {streamName}.");
        }

        return meta;
    }

    /// <summary>
    /// Returns a metadata descriptor for the specified stream.
    /// </summary>
    /// <param name="id">The id of the stream.</param>
    /// <returns>The metadata describing the specified stream.</returns>
    public PsiStreamMetadata GetMetadata(int id)
    {
        if (!_metadataCache.Resource.TryGet(id, out PsiStreamMetadata meta))
        {
            throw new ArgumentException("A stream with this id could not be found: " + id);
        }

        return meta;
    }

    /// <summary>
    /// Closes all associated files.
    /// </summary>
    public void Dispose()
    {
        _messageReader.Dispose();
        _largeMessageReader.Dispose();
        _metadataCache.Dispose();
        _indexCache.Dispose();
    }

    /// <summary>
    /// Moves the reader to the start of the specified interval and restricts the read to messages within the interval.
    /// </summary>
    /// <param name="interval">The interval for reading data.</param>
    /// <param name="useOriginatingTime">Indicates whether the interval refers to originating times or creation times.</param>
    public void Seek(TimeInterval interval, bool useOriginatingTime = false)
    {
        _replayInterval = interval;
        _useOriginatingTime = useOriginatingTime;
        IndexEntry indexEntry = _indexCache.Resource.Search(interval.Left, useOriginatingTime);
        _messageReader.Seek(indexEntry.ExtentId, indexEntry.Position);
    }

    /// <summary>
    /// Gets the current temporal extents of the store by time and originating time.
    /// </summary>
    /// <returns>A pair of TimeInterval objects that represent the times and originating times of the first and last messages currently in the store.</returns>
    public (TimeInterval, TimeInterval) GetLiveStoreExtents()
    {
        Envelope envelope;

        // Get the times of the first message
        Seek(new TimeInterval(DateTime.MinValue, DateTime.MaxValue), true);
        DateTime firstMessageCreationTime = DateTime.MinValue;
        DateTime firstMessageOriginatingTime = DateTime.MinValue;
        DateTime lastMessageCreationTime = DateTime.MinValue;
        DateTime lastMessageOriginatingTime = DateTime.MinValue;
        if (_messageReader.MoveNext())
        {
            envelope = _messageReader.Current;
            firstMessageCreationTime = envelope.CreationTime;
            firstMessageOriginatingTime = envelope.OriginatingTime;
            lastMessageCreationTime = envelope.CreationTime;
            lastMessageOriginatingTime = envelope.OriginatingTime;
        }

        // Get the last Index Entry from the cache and seek to it
        IndexEntry indexEntry = _indexCache.Resource.Search(DateTime.MaxValue, true);
        Seek(new TimeInterval(indexEntry.OriginatingTime, DateTime.MaxValue), true);

        // Find the last message in the extent
        while (_messageReader.MoveNext())
        {
            envelope = _messageReader.Current;
            lastMessageCreationTime = envelope.CreationTime;
            lastMessageOriginatingTime = envelope.OriginatingTime;
        }

        _metadataCache.Resource.Update();

        return (new TimeInterval(firstMessageCreationTime, lastMessageCreationTime), new TimeInterval(firstMessageOriginatingTime, lastMessageOriginatingTime));
    }

    /// <summary>
    /// Positions the reader to the next message from any one of the enabled streams.
    /// </summary>
    /// <param name="envelope">The envelope associated with the message read.</param>
    /// <returns>True if there are more messages, false if no more messages are available.</returns>
    public bool MoveNext(out Envelope envelope)
    {
        envelope = default;
        do
        {
            bool hasData = AutoOpenAllStreams ? _messageReader.MoveNext() : _messageReader.MoveNext(_enabledStreams);
            if (!hasData)
            {
                if (!PsiStoreMonitor.IsStoreLive(Name, Path))
                {
                    return false;
                }

                bool acquired = false;
                try
                {
                    acquired = _messageReader.DataReady.WaitOne(100); // DataReady is a pulse event, and might be missed
                }
                catch (AbandonedMutexException)
                {
                    // If the writer goes away while we're still reading from the store we'll receive this exception
                }

                hasData = AutoOpenAllStreams ? _messageReader.MoveNext() : _messageReader.MoveNext(_enabledStreams);
                if (acquired)
                {
                    _messageReader.DataReady.ReleaseMutex();
                }

                if (!hasData)
                {
                    return false;
                }
            }

            DateTime messageTime = _useOriginatingTime ? _messageReader.Current.OriginatingTime : _messageReader.Current.CreationTime;
            if (_replayInterval.PointIsWithin(messageTime))
            {
                envelope = _messageReader.Current;
                _metadataCache.Resource.Update();
                return true;
            }

            if (_replayInterval.Right < messageTime)
            {
                CloseStream(_messageReader.Current.SourceId);
            }
        }
        while (AutoOpenAllStreams || _enabledStreams.Count() > 0);

        return false;
    }

    /// <summary>
    /// Reads the next message from any one of the enabled streams (in serialized form) into the specified buffer.
    /// </summary>
    /// <param name="buffer">A buffer to read into.</param>
    /// <returns>Number of bytes read into the specified buffer.</returns>
    public int Read(ref byte[] buffer)
    {
        int streamId = _messageReader.Current.SourceId;

        // if the entry is an index entry, we need to load it from the large message file
        if (IsIndexedStream(streamId))
        {
            IndexEntry indexEntry;
            unsafe
            {
                _messageReader.Read((byte*)&indexEntry, sizeof(IndexEntry));
            }

            int extentId = indexEntry.ExtentId - int.MinValue;
            _largeMessageReader.Seek(extentId, indexEntry.Position);
            if (!_largeMessageReader.MoveNext())
            {
                throw new ArgumentException($"Invalid index entry (extent: {extentId}, position: {indexEntry.Position}, current: {_largeMessageReader.CurrentExtentId})");
            }

            return _largeMessageReader.Read(ref buffer);
        }

        return _messageReader.Read(ref buffer);
    }

    /// <summary>
    /// Reads the message from the specified position, without changing the current cursor position.
    /// Cannot be used together with MoveNext/Read.
    /// </summary>
    /// <param name="indexEntry">The position to read from.</param>
    /// <param name="buffer">A buffer to read into.</param>
    /// <returns>Number of bytes read into the specified buffer.</returns>
    public int ReadAt(IndexEntry indexEntry, ref byte[] buffer)
    {
        if (indexEntry.ExtentId < 0)
        {
            int extentId = indexEntry.ExtentId - int.MinValue;
            _largeMessageReader.Seek(extentId, indexEntry.Position);
            if (!_largeMessageReader.MoveNext())
            {
                throw new ArgumentException($"Invalid index entry (extent: {indexEntry.ExtentId - int.MinValue}, position: {indexEntry.Position}, current: {_largeMessageReader.CurrentExtentId})");
            }

            return _largeMessageReader.Read(ref buffer);
        }

        _messageReader.Seek(indexEntry.ExtentId, indexEntry.Position);
        if (!_messageReader.MoveNext())
        {
            throw new ArgumentException($"Invalid index entry (extent: {indexEntry.ExtentId}, position: {indexEntry.Position}, current: {_messageReader.CurrentExtentId})");
        }

        return _messageReader.Read(ref buffer);
    }

    /// <summary>
    /// Returns the position of the next message from any one of the enabled streams.
    /// </summary>
    /// <returns>The position of the message, excluding the envelope.</returns>
    public IndexEntry ReadIndex()
    {
        IndexEntry indexEntry;
        int streamId = _messageReader.Current.SourceId;

        // if the entry is an index entry, we just return it
        if (IsIndexedStream(streamId))
        {
            unsafe
            {
                _messageReader.Read((byte*)&indexEntry, sizeof(IndexEntry));
            }
        }
        else
        {
            // we need to make one on the fly
            indexEntry.Position = _messageReader.CurrentMessageStart;
            indexEntry.ExtentId = _messageReader.CurrentExtentId;
            indexEntry.CreationTime = _messageReader.Current.CreationTime;
            indexEntry.OriginatingTime = _messageReader.Current.OriginatingTime;
        }

        return indexEntry;
    }

    internal void EnsureMetadataUpdate()
    {
        _metadataCache.Resource.Update();
    }

    private bool IsIndexedStream(int id)
    {
        if (_isIndexedStream.TryGetValue(id, out bool isIndexed))
        {
            return isIndexed;
        }

        if (_metadataCache.Resource.TryGet(id, out PsiStreamMetadata meta))
        {
            isIndexed = meta.IsIndexed;
            _isIndexedStream.Add(id, isIndexed);
            return isIndexed;
        }

        return false;
    }
}
