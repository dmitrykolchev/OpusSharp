// <copyright file="JsonGenerator.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Collections.Generic;
using System.Linq;
using Neutrino.Psi.Common;
using Neutrino.Psi.Common.Intervals;
using Neutrino.Psi.Components;
using Neutrino.Psi.Executive;
using Neutrino.Psi.Streams;
using Newtonsoft.Json.Linq;

namespace Neutrino.Psi.Data.Json;

/// <summary>
/// Component that plays back data from a JSON store.
/// </summary>
public class JsonGenerator : Generator, IDisposable
{
    private readonly Pipeline _pipeline;
    private readonly JsonStoreReader _reader;
    private readonly HashSet<string> _streams;
    private readonly Dictionary<int, ValueTuple<object, Action<JToken, DateTime>>> _emitters;

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonGenerator"/> class.
    /// </summary>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="storeName">The name of the application that generated the persisted files, or the root name of the files.</param>
    /// <param name="storePath">The directory in which the main persisted file resides.</param>
    /// <param name="name">An optional name for the component.</param>
    public JsonGenerator(Pipeline pipeline, string storeName, string storePath, string name = nameof(JsonGenerator))
        : this(pipeline, new JsonStoreReader(storeName, storePath), name)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonGenerator"/> class.
    /// </summary>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="reader">The underlying store reader.</param>
    /// <param name="name">An optional name for the component.</param>
    protected JsonGenerator(Pipeline pipeline, JsonStoreReader reader, string name = nameof(JsonGenerator))
        : base(pipeline, name: name)
    {
        _pipeline = pipeline;
        _reader = reader;
        _streams = new HashSet<string>();
        _emitters = new Dictionary<int, ValueTuple<object, Action<JToken, DateTime>>>();
        _reader.Seek(ReplayDescriptor.ReplayAll);
    }

    /// <summary>
    /// Gets the name of the application that generated the persisted files, or the root name of the files.
    /// </summary>
    public string Name => _reader.Name;

    /// <summary>
    /// Gets the directory in which the main persisted file resides.
    /// </summary>
    public string Path => _reader.Path;

    /// <summary>
    /// Gets an enumerable of stream metadata contained in the underlying data store.
    /// </summary>
    public IEnumerable<IStreamMetadata> AvailableStreams => _reader.AvailableStreams;

    /// <summary>
    /// Gets the originating time interval (earliest to latest) of the messages in the underlying data store.
    /// </summary>
    public TimeInterval OriginatingTimeInterval => _reader.OriginatingTimeInterval;

    /// <summary>
    /// Determines whether the underlying data store contains the specified stream.
    /// </summary>
    /// <param name="streamName">The name of the stream.</param>
    /// <returns>true if store contains the specified stream, otherwise false.</returns>
    public bool Contains(string streamName)
    {
        return _reader.AvailableStreams.Any(av => av.Name == streamName);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _reader?.Dispose();
    }

    /// <summary>
    /// Gets the stream metadata for the specified stream.
    /// </summary>
    /// <param name="streamName">The name of the stream.</param>
    /// <returns>The stream metadata.</returns>
    public IStreamMetadata GetMetadata(string streamName)
    {
        return _reader.AvailableStreams.FirstOrDefault(av => av.Name == streamName);
    }

    /// <summary>
    /// Opens the specified stream for reading and returns an emitter for use in the pipeline.
    /// </summary>
    /// <typeparam name="T">Type of data in underlying stream.</typeparam>
    /// <param name="streamName">The name of the stream.</param>
    /// <returns>The newly created emitter that generates messages from the stream of type <typeparamref name="T"/>.</returns>
    public Emitter<T> OpenStream<T>(string streamName)
    {
        // if stream already opened, return emitter
        if (_streams.Contains(streamName))
        {
            IStreamMetadata m = GetMetadata(streamName);
            (object, Action<JToken, DateTime>) e = _emitters[m.Id];

            // if the types don't match, invalid cast exception is the appropriate error
            return (Emitter<T>)e.Item1;
        }

        // open stream in underlying reader
        JsonStreamMetadata metadata = _reader.OpenStream(streamName);

        // register this stream with the store catalog
        _pipeline.ConfigurationStore.Set(Exporter.StreamMetadataNamespace, streamName, metadata);

        // create emitter
        Emitter<T> emitter = _pipeline.CreateEmitter<T>(this, streamName);
        _emitters[metadata.Id] = ValueTuple.Create<Emitter<T>, Action<JToken, DateTime>>(
            emitter,
            (token, originatingTime) =>
            {
                T t = token.ToObject<T>();
                emitter.Post(t, originatingTime);
            });
        _streams.Add(streamName);

        return emitter;
    }

    /// <summary>
    /// GenerateNext is called by the Generator base class when the next sample should be read.
    /// </summary>
    /// <param name="currentTime">The originating time of the message that triggered the current call to GenerateNext.</param>
    /// <returns>The originating time at which to read the next sample.</returns>
    protected override DateTime GenerateNext(DateTime currentTime)
    {
        if (_reader.MoveNext(out Envelope env))
        {
            _reader.Read(out JToken data);
            _emitters[env.SourceId].Item2(data, env.OriginatingTime);
            return env.OriginatingTime;
        }

        return DateTime.MaxValue;
    }
}
