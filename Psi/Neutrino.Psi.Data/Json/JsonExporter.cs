// <copyright file="JsonExporter.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Threading;
using Neutrino.Psi.Common;
using Neutrino.Psi.Components;
using Neutrino.Psi.Data;
using Neutrino.Psi.Executive;
using Neutrino.Psi.Streams;
using Newtonsoft.Json.Linq;

namespace Neutrino.Psi.Data.Json;

/// <summary>
/// Component that writes messages to a multi-stream JSON store.
/// </summary>
public class JsonExporter : Subpipeline, IDisposable
{
    private readonly JsonStoreWriter _writer;
    private readonly Merger<Message<JToken>, string> _merger;
    private readonly Pipeline _pipeline;
    private readonly ManualResetEvent _throttle = new(true);

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonExporter"/> class.
    /// </summary>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="name">The name of the application that generated the persisted files, or the root name of the files.</param>
    /// <param name="path">The directory in which the main persisted file resides or will reside, or null to create a volatile data store.</param>
    /// <param name="createSubdirectory">If true, a numbered sub-directory is created for this store.</param>
    public JsonExporter(Pipeline pipeline, string name, string path, bool createSubdirectory = true)
        : this(pipeline, name, new JsonStoreWriter(name, path, createSubdirectory))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonExporter"/> class.
    /// </summary>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="name">The name of the application that generated the persisted files, or the root name of the files.</param>
    /// <param name="writer">The underlying store writer.</param>
    protected JsonExporter(Pipeline pipeline, string name, JsonStoreWriter writer)
        : base(pipeline, $"{nameof(JsonExporter)}[{name}]")
    {
        _pipeline = pipeline;
        _writer = writer;
        _merger = new Merger<Message<JToken>, string>(pipeline, (_, m) =>
        {
            _throttle.WaitOne();
            _writer.Write(m.Data.Data, m.Data.Envelope);
        });
    }

    /// <summary>
    /// Gets the name of the store being written to.
    /// </summary>
    public new string Name => _writer.Name;

    /// <summary>
    /// Gets the path to the store being written to if the store is persisted to disk, or null if the store is volatile.
    /// </summary>
    public string Path => _writer.Path;

    /// <summary>
    /// Closes the store.
    /// </summary>
    public override void Dispose()
    {
        base.Dispose();
        _writer?.Dispose();
        _throttle.Dispose();
    }

    /// <summary>
    /// Writes the specified stream to this multi-stream store.
    /// </summary>
    /// <typeparam name="T">The type of messages in the stream.</typeparam>
    /// <param name="source">The source stream to write.</param>
    /// <param name="name">The name of the persisted stream.</param>
    /// <param name="deliveryPolicy">An optional delivery policy.</param>
    public void Write<T>(Emitter<T> source, string name, DeliveryPolicy<T> deliveryPolicy = null)
    {
        // add another input to the merger to hook up the serializer to
        // and check for duplicate names in the process
        Receiver<Message<JToken>> mergeInput = _merger.Add(name);

        // name the stream if it's not already named
        source.Name ??= name;

        // tell the writer to write the serialized stream
        JsonStreamMetadata metadata = _writer.OpenStream(source.Id, name, typeof(T).AssemblyQualifiedName);

        // register this stream with the store catalog
        _pipeline.ConfigurationStore.Set(Exporter.StreamMetadataNamespace, name, metadata);

        // hook up the serializer
        JsonSerializerComponent<T> serializer = new(_pipeline);

        // The merger input receiver will throttle the serializer as long as it is busy writing data.
        // This will cause messages to be queued or dropped at the serializer (per the user-supplied
        // deliveryPolicy) until the merger is able to service the next serialized data message.
        serializer.PipeTo(mergeInput, DeliveryPolicy.Throttle);
        source.PipeTo(serializer, deliveryPolicy);
    }

    /// <summary>
    /// Writes the specified stream to this multi-stream store.
    /// </summary>
    /// <param name="source">The source stream to write.</param>
    /// <param name="metadata">The stream metadata of the stream.</param>
    /// <param name="deliveryPolicy">An optional delivery policy.</param>
    internal void Write(Emitter<Message<JToken>> source, JsonStreamMetadata metadata, DeliveryPolicy<Message<JToken>> deliveryPolicy = null)
    {
        Receiver<Message<JToken>> mergeInput = _merger.Add(metadata.Name); // this checks for duplicates
        _writer.OpenStream(metadata);
        Operators.PipeTo(source, mergeInput, deliveryPolicy);
    }

    private sealed class JsonSerializerComponent<T> : ConsumerProducer<T, Message<JToken>>
    {
        public JsonSerializerComponent(Pipeline pipeline)
            : base(pipeline)
        {
        }

        protected override void Receive(T data, Envelope e)
        {
            JToken token = JToken.FromObject(data);
            Message<JToken> resultMsg = Message.Create(token, e);
            Out.Post(resultMsg, e.OriginatingTime);
        }
    }
}
