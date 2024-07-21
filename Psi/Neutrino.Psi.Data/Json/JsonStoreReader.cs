// <copyright file="JsonStoreReader.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Microsoft.Psi.Persistence;

namespace Microsoft.Psi.Data.Json;

/// <summary>
/// Represents a reader for JSON data stores.
/// </summary>
public class JsonStoreReader : JsonStoreBase
{
    private readonly List<JsonStreamMetadata> _catalog = null;
    private readonly List<int> _enabledStreams = new();
    private readonly TimeInterval _originatingTimeInterval;

    private ReplayDescriptor _descriptor = ReplayDescriptor.ReplayAll;
    private bool _hasMoreData = false;
    private Envelope _envelope = default;
    private JToken _data = null;
    private System.IO.StreamReader _streamReader = null;
    private JsonReader _jsonReader = null;

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonStoreReader"/> class.
    /// </summary>
    /// <param name="name">The name of the application that generated the persisted files, or the root name of the files.</param>
    /// <param name="path">The directory in which the main persisted file resides or will reside, or null to create a volatile data store.</param>
    /// <param name="extension">The extension for the underlying file.</param>
    public JsonStoreReader(string name, string path, string extension = DefaultExtension)
        : base(extension)
    {
        Name = name;
        Path = PsiStore.GetPathToLatestVersion(name, path);

        // load catalog
        string metadataPath = System.IO.Path.Combine(Path, PsiStoreCommon.GetCatalogFileName(Name) + Extension);
        Size = new FileInfo(metadataPath).Length;
        using (System.IO.StreamReader file = File.OpenText(metadataPath))
        using (JsonTextReader reader = new(file))
        {
            _catalog = Serializer.Deserialize<List<JsonStreamMetadata>>(reader);
        }

        // compute originating time interval
        _originatingTimeInterval = TimeInterval.Empty;
        foreach (JsonStreamMetadata metadata in _catalog)
        {
            TimeInterval metadataTimeInterval = new(metadata.FirstMessageOriginatingTime, metadata.LastMessageOriginatingTime);
            _originatingTimeInterval = TimeInterval.Coverage(new TimeInterval[] { _originatingTimeInterval, metadataTimeInterval });
        }
    }

    /// <summary>
    /// Gets an enumerable of stream metadata contained in the underlying data store.
    /// </summary>
    public IEnumerable<JsonStreamMetadata> AvailableStreams => _catalog;

    /// <summary>
    /// Gets the originating time interval (earliest to latest) of the messages in the data store.
    /// </summary>
    public TimeInterval OriginatingTimeInterval => _originatingTimeInterval;

    /// <summary>
    /// Gets the size of the json store.
    /// </summary>
    public long Size { get; }

    /// <summary>
    /// Closes the specified stream.
    /// </summary>
    /// <param name="streamName">The name of the stream.</param>
    public void CloseStream(string streamName)
    {
        JsonStreamMetadata metadata = GetMetadata(streamName);
        CloseStream(metadata.Id);
    }

    /// <summary>
    /// Closes the specified stream.
    /// </summary>
    /// <param name="id">The id of the stream.</param>
    public void CloseStream(int id)
    {
        _enabledStreams.Remove(id);
    }

    /// <summary>
    /// Close all streams.
    /// </summary>
    public void CloseAllStreams()
    {
        _enabledStreams.Clear();
    }

    /// <summary>
    /// Determines whether the data store contains the specified stream.
    /// </summary>
    /// <param name="streamName">The name of the stream.</param>
    /// <returns>true if store contains the specified stream, otherwise false.</returns>
    public bool Contains(string streamName)
    {
        if (string.IsNullOrWhiteSpace(streamName))
        {
            throw new ArgumentNullException(nameof(streamName));
        }

        return _catalog.FirstOrDefault(m => m.Name == streamName) != null;
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        _streamReader?.Dispose();
        _streamReader = null;
        _jsonReader?.Close();
        _jsonReader = null;
    }

    /// <summary>
    /// Gets the stream metadata for the specified stream.
    /// </summary>
    /// <param name="streamName">The name of the stream.</param>
    /// <returns>The stream metadata.</returns>
    public JsonStreamMetadata GetMetadata(string streamName)
    {
        if (string.IsNullOrWhiteSpace(streamName))
        {
            throw new ArgumentNullException(nameof(streamName));
        }

        JsonStreamMetadata metadata = _catalog.FirstOrDefault(m => m.Name == streamName);
        if (metadata == null)
        {
            throw new ArgumentException($"Stream named '{streamName}' was not found.", nameof(streamName));
        }

        return metadata;
    }

    /// <summary>
    /// Gets the stream metadata for the specified stream.
    /// </summary>
    /// <param name="id">The id of the stream.</param>
    /// <returns>The stream metadata.</returns>
    public JsonStreamMetadata GetMetadata(int id)
    {
        JsonStreamMetadata metadata = _catalog.FirstOrDefault(m => m.Id == id);
        if (metadata == null)
        {
            throw new ArgumentException($"Stream id '{id}' was not found.", nameof(id));
        }

        return metadata;
    }

    /// <summary>
    /// Opens the stream for the specified stream.
    /// </summary>
    /// <param name="streamName">The name of the stream.</param>
    /// <returns>The stream metadata.</returns>
    public JsonStreamMetadata OpenStream(string streamName)
    {
        JsonStreamMetadata metadata = GetMetadata(streamName);
        OpenStream(metadata);
        return metadata;
    }

    /// <summary>
    /// Opens the stream for the specified stream.
    /// </summary>
    /// <param name="id">The id of the stream.</param>
    /// <returns>The stream metadata.</returns>
    public JsonStreamMetadata OpenStream(int id)
    {
        JsonStreamMetadata metadata = GetMetadata(id);
        OpenStream(metadata);
        return metadata;
    }

    /// <summary>
    /// Opens the stream for the specified stream.
    /// </summary>
    /// <param name="metadata">The metadata of the stream.</param>
    /// <returns>true if the stream was opened; otherwise false.</returns>
    public bool OpenStream(JsonStreamMetadata metadata)
    {
        if (metadata == null)
        {
            throw new ArgumentNullException(nameof(metadata));
        }

        if (_enabledStreams.Contains(metadata.Id))
        {
            return false;
        }

        _enabledStreams.Add(metadata.Id);
        return true;
    }

    /// <summary>
    /// Positions the reader to the next message.
    /// </summary>
    /// <param name="envelope">The envelope associated with the message read.</param>
    /// <returns>True if there are more messages, false if no more messages are available.</returns>
    public bool MoveNext(out Envelope envelope)
    {
        do
        {
            envelope = _envelope;
            if (!_hasMoreData)
            {
                return false;
            }

            DateTime messageTime = envelope.OriginatingTime;

            // read data
            _hasMoreData = ReadData(out _data);
            if (!_hasMoreData)
            {
                return false;
            }

            if (_descriptor.Interval.PointIsWithin(messageTime) && _enabledStreams.Contains(envelope.SourceId))
            {
                // data was within time interval and stream was opened
                return true;
            }
            else if (_descriptor.Interval.Right < messageTime)
            {
                // data was outside of time interval, close stream
                CloseStream(envelope.SourceId);
            }

            // read closing object, opening object tags, envelope
            _hasMoreData =
                _jsonReader.Read() && _jsonReader.TokenType == JsonToken.StartObject &&
                _jsonReader.Read() && ReadEnvelope(out _envelope);
        }
        while (_enabledStreams.Count() > 0);

        return false;
    }

    /// <summary>
    /// Reads the next message from any one of the enabled streams (in serialized form) into the specified buffer.
    /// </summary>
    /// <param name="data">The data associated with the message read.</param>
    /// <returns>True if there are more messages, false if no more messages are available.</returns>
    public bool Read(out JToken data)
    {
        // read closing object, opening object tags, envelope
        _hasMoreData =
            _jsonReader.Read() && _jsonReader.TokenType == JsonToken.StartObject &&
            _jsonReader.Read() && ReadEnvelope(out _envelope);
        data = _data;
        return _hasMoreData;
    }

    /// <summary>
    /// Seek to envelope in stream according to specified replay descriptor.
    /// </summary>
    /// <param name="descriptor">The replay descriptor.</param>
    public void Seek(ReplayDescriptor descriptor)
    {
        _descriptor = descriptor;

        // load data
        string dataPath = System.IO.Path.Combine(Path, PsiStoreCommon.GetDataFileName(Name) + Extension);
        _streamReader?.Dispose();
        _streamReader = File.OpenText(dataPath);
        _jsonReader = new JsonTextReader(_streamReader);

        // iterate through data store until we either reach the end or we find the start of the replay descriptor
        while (_jsonReader.Read())
        {
            // data stores are arrays of messages, messages start as objects
            if (_jsonReader.TokenType == JsonToken.StartObject)
            {
                // read envelope
                if (!_jsonReader.Read() || !ReadEnvelope(out _envelope))
                {
                    throw new InvalidDataException("Messages must be an ordered object: {\"Envelope\": <Envelope>, \"Data\": <Data>}. Deserialization needs to read the envelope before the data to know what type of data to deserialize.");
                }

                if (_descriptor.Interval.Left < _envelope.OriginatingTime)
                {
                    // found start of interval
                    break;
                }

                // skip data
                if (!ReadData(out _data))
                {
                    throw new InvalidDataException("Messages must be an ordered object: {\"Envelope\": <Envelope>, \"Data\": <Data>}. Deserialization needs to read the envelope before the data to know what type of data to deserialize.");
                }
            }
        }
    }

    private bool ReadData(out JToken data)
    {
        _hasMoreData = _jsonReader.TokenType == JsonToken.PropertyName && string.Equals(_jsonReader.Value, "Data") && _jsonReader.Read();
        data = _hasMoreData ? JToken.ReadFrom(_jsonReader) : null;
        _hasMoreData = _hasMoreData && _jsonReader.TokenType == JsonToken.EndObject && _jsonReader.Read();
        return _hasMoreData;
    }

    private bool ReadEnvelope(out Envelope envelope)
    {
        _hasMoreData = _jsonReader.TokenType == JsonToken.PropertyName && string.Equals(_jsonReader.Value, "Envelope") && _jsonReader.Read();
        envelope = _hasMoreData ? Serializer.Deserialize<Envelope>(_jsonReader) : default;
        if (_hasMoreData)
        {
            JsonStreamMetadata metadata = _catalog.FirstOrDefault(m => m.Id == _envelope.SourceId);
            if (metadata == null)
            {
                throw new InvalidDataException($"Message source/stream id ({_envelope.SourceId}) was not found in catalog.");
            }
        }

        _hasMoreData = _hasMoreData && _jsonReader.TokenType == JsonToken.EndObject && _jsonReader.Read();
        return _hasMoreData;
    }
}
