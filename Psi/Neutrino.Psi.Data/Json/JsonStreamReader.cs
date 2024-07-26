// <copyright file="JsonStreamReader.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using Neutrino.Psi.Common;
using Neutrino.Psi.Common.Intervals;
using Newtonsoft.Json.Linq;

namespace Neutrino.Psi.Data.Json;

/// <summary>
/// Represents a stream reader for JSON data stores.
/// </summary>
public class JsonStreamReader : IStreamReader, IDisposable
{
    private readonly Dictionary<int, Action<JToken, Envelope>> _outputs = new();
    private readonly string _extension;

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonStreamReader"/> class.
    /// </summary>
    /// <param name="name">The name of the application that generated the persisted files, or the root name of the files.</param>
    /// <param name="path">The directory in which the main persisted file resides or will reside, or null to create a volatile data store.</param>
    /// <param name="extension">The extension for the underlying file.</param>
    public JsonStreamReader(string name, string path, string extension)
        : this(extension)
    {
        Reader = new JsonStoreReader(name, path, extension);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonStreamReader"/> class.
    /// </summary>
    /// <param name="name">The name of the application that generated the persisted files, or the root name of the files.</param>
    /// <param name="path">The directory in which the main persisted file resides or will reside, or null to create a volatile data store.</param>
    public JsonStreamReader(string name, string path)
        : this(name, path, JsonStoreBase.DefaultExtension)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonStreamReader"/> class.
    /// </summary>
    /// <param name="that">Existing <see cref="JsonStreamReader"/> used to initialize new instance.</param>
    public JsonStreamReader(JsonStreamReader that)
        : this(that.Name, that.Path, that._extension)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonStreamReader"/> class.
    /// </summary>
    /// <param name="extension">The extension for the underlying file.</param>
    public JsonStreamReader(string extension = JsonStoreBase.DefaultExtension)
    {
        this._extension = extension;
    }

    /// <inheritdoc />
    public IEnumerable<IStreamMetadata> AvailableStreams => Reader?.AvailableStreams;

    /// <inheritdoc />
    public string Name => Reader?.Name;

    /// <inheritdoc />
    public string Path => Reader?.Path;

    /// <inheritdoc />
    public TimeInterval MessageOriginatingTimeInterval
    {
        get
        {
            TimeInterval timeInterval = TimeInterval.Empty;
            foreach (IStreamMetadata metadata in AvailableStreams)
            {
                TimeInterval metadataTimeInterval = new(metadata.FirstMessageOriginatingTime, metadata.LastMessageOriginatingTime);
                timeInterval = TimeInterval.Coverage(new TimeInterval[] { timeInterval, metadataTimeInterval });
            }

            return timeInterval;
        }
    }

    /// <inheritdoc />
    public TimeInterval MessageCreationTimeInterval
    {
        get
        {
            TimeInterval timeInterval = TimeInterval.Empty;
            foreach (IStreamMetadata metadata in AvailableStreams)
            {
                TimeInterval metadataTimeInterval = new(metadata.FirstMessageCreationTime, metadata.LastMessageCreationTime);
                timeInterval = TimeInterval.Coverage(new TimeInterval[] { timeInterval, metadataTimeInterval });
            }

            return timeInterval;
        }
    }

    /// <inheritdoc />
    public TimeInterval StreamTimeInterval
    {
        get
        {
            TimeInterval timeInterval = TimeInterval.Empty;
            foreach (IStreamMetadata metadata in AvailableStreams)
            {
                TimeInterval metadataTimeInterval = new(metadata.OpenedTime, metadata.ClosedTime);
                timeInterval = TimeInterval.Coverage(new TimeInterval[] { timeInterval, metadataTimeInterval });
            }

            return timeInterval;
        }
    }

    /// <inheritdoc/>
    public long? Size => Reader?.Size;

    /// <inheritdoc/>
    public int? StreamCount => Reader?.AvailableStreams.Count();

    /// <summary>
    /// Gets or sets the underlying store reader.
    /// </summary>
    protected JsonStoreReader Reader { get; set; }

    /// <summary>
    /// Closes all open streams.
    /// </summary>
    public void CloseAllStreams()
    {
        _outputs.Clear();
        Reader.CloseAllStreams();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Reader?.Dispose();
    }

    /// <inheritdoc />
    public virtual IStreamReader OpenNew()
    {
        return new JsonStreamReader(this);
    }

    /// <inheritdoc />
    public IStreamMetadata OpenStream<T>(string streamName, Action<T, Envelope> target, Func<T> allocator = null, Action<T> deallocator = null, Action<SerializationException> errorHandler = null)
    {
        if (string.IsNullOrWhiteSpace(streamName))
        {
            throw new ArgumentNullException(nameof(streamName));
        }

        if (target == null)
        {
            throw new ArgumentNullException(nameof(target));
        }

        if (allocator != null)
        {
            throw new NotSupportedException($"Allocators are not supported by {nameof(JsonStreamReader)} and must be null.");
        }

        if (deallocator != null)
        {
            throw new NotSupportedException($"Deallocators are not supported by {nameof(JsonStreamReader)} and must be null.");
        }

        JsonStreamMetadata metadata = Reader.OpenStream(streamName);

        if (_outputs.ContainsKey(metadata.Id))
        {
            throw new ArgumentException($"Stream named '{streamName}' was already opened and can only be opened once.", nameof(streamName));
        }

        _outputs[metadata.Id] = (token, envelope) => target(token.ToObject<T>(), envelope);

        return metadata;
    }

    /// <inheritdoc />
    public IStreamMetadata OpenStreamIndex<T>(string streamName, Action<Func<IStreamReader, T>, Envelope> target, Func<T> allocator = null)
    {
        throw new NotSupportedException($"{nameof(JsonStreamReader)} does not support indexing.");
    }

    /// <inheritdoc />
    public void ReadAll(ReplayDescriptor descriptor, CancellationToken cancelationToken = default)
    {
        bool hasMoreData = true;
        Reader.Seek(descriptor);
        while (hasMoreData)
        {
            if (cancelationToken.IsCancellationRequested)
            {
                return;
            }

            hasMoreData = Reader.MoveNext(out Envelope envelope);
            if (hasMoreData)
            {
                hasMoreData = Reader.Read(out JToken token);
                _outputs[envelope.SourceId](token, envelope);
            }
        }
    }

    /// <inheritdoc />
    public void Seek(TimeInterval interval, bool useOriginatingTime = false)
    {
        throw new NotSupportedException($"{nameof(JsonStreamReader)} does not support seeking.");
    }

    /// <inheritdoc />
    public bool MoveNext(out Envelope envelope)
    {
        throw new NotSupportedException($"{nameof(JsonStreamReader)} does not support stream-style access.");
    }

    /// <inheritdoc />
    public bool IsLive()
    {
        throw new NotSupportedException($"{nameof(JsonStreamReader)} does not support stream-style access.");
    }

    /// <inheritdoc />
    public IStreamMetadata GetStreamMetadata(string name)
    {
        throw new NotSupportedException($"{nameof(JsonStreamReader)} does not support metadata.");
    }

    /// <inheritdoc />
    public T GetSupplementalMetadata<T>(string streamName)
    {
        throw new NotSupportedException($"{nameof(JsonStreamReader)} does not support supplemental metadata.");
    }

    /// <inheritdoc />
    public bool ContainsStream(string name)
    {
        throw new NotSupportedException($"{nameof(JsonStreamReader)} does not support this API.");
    }
}
