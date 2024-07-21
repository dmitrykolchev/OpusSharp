// <copyright file="Exporter.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Psi.Common;
using Microsoft.Psi.Components;
using Microsoft.Psi.Persistence;
using Microsoft.Psi.Serialization;

namespace Microsoft.Psi.Data;


/// <summary>
/// Component that writes messages to a multi-stream store.
/// </summary>
/// <remarks>
/// The store can be backed by a file on disk, can be ephemeral (in-memory) for inter-process communication
/// or can be a network protocol for cross-machine communication.
/// </remarks>
public abstract class Exporter : Subpipeline, IDisposable
{
    public static readonly string StreamMetadataNamespace = "store\\metadata";

    private readonly PsiStoreWriter _writer;
    private readonly Merger<Message<BufferReader>, string> _merger;
    private readonly Pipeline _pipeline;
    private readonly ManualResetEvent _throttle = new(true);
    private readonly KnownSerializers _serializers;

    /// <summary>
    /// Initializes a new instance of the <see cref="Exporter"/> class.
    /// </summary>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="name">The name of the application that generated the persisted files, or the root name of the files.</param>
    /// <param name="path">The directory in which the main persisted file resides or will reside, or null to create a volatile data store.</param>
    /// <param name="createSubdirectory">If true, a numbered sub-directory is created for this store.</param>
    /// <param name="serializers">
    /// A collection of known serializers, or null to infer it from the data being written to the store.
    /// The known serializer set can be accessed and modified afterwards via the <see cref="Serializers"/> property.
    /// </param>
    protected internal Exporter(Pipeline pipeline, string name, string path, bool createSubdirectory = true, KnownSerializers serializers = null)
        : base(pipeline, $"{nameof(Exporter)}[{name}]")
    {
        _pipeline = pipeline;
        _serializers = serializers ?? new KnownSerializers(knownSerializers: KnownSerializers.Default);
        _writer = new PsiStoreWriter(name, path, createSubdirectory);
        _pipeline.PipelineRun += (_, e) => _writer.InitializeStreamOpenedTimes(e.StartOriginatingTime);

        // write the version info
        _writer.WriteToCatalog(_serializers.RuntimeInfo);

        // copy the schemas present so far and also make sure the catalog captures schemas added in the future
        _serializers.SchemaAdded += (o, e) => _writer.WriteToCatalog(e);
        foreach (TypeSchema schema in _serializers.Schemas.Values)
        {
            _writer.WriteToCatalog(schema);
        }

        _merger = new Merger<Message<BufferReader>, string>(this, (_, m) =>
        {
            Throttle.WaitOne();
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
    /// Gets stream metadata.
    /// </summary>
    public IEnumerable<IStreamMetadata> Metadata => _writer.Metadata;

    /// <summary>
    /// Gets the set of types that this Importer can deserialize.
    /// Types can be added or re-mapped using the <see cref="KnownSerializers.Register{T}(string, CloningFlags)"/> method.
    /// </summary>
    public KnownSerializers Serializers => _serializers;

    /// <summary>
    /// Gets the event that allows remoting to throttle data reading to match a specified network bandwidth.
    /// </summary>
    internal ManualResetEvent Throttle => _throttle;

    /// <summary>
    /// Closes the store.
    /// </summary>
    public override void Dispose()
    {
        base.Dispose();
        if (_writer != null)
        {
            _writer.CloseAllStreams(_pipeline.FinalOriginatingTime);
            _writer.Dispose();
        }

        _throttle.Dispose();
    }

    /// <summary>
    /// Writes the messages from the specified stream to the matching stream in this store.
    /// </summary>
    /// <typeparam name="TMessage">The type of messages in the stream.</typeparam>
    /// <param name="source">The source stream to write.</param>
    /// <param name="name">The name of the stream.</param>
    /// <param name="largeMessages">Indicates whether the stream contains large messages (typically >4k). If true, the messages will be written to the large message file.</param>
    /// <param name="deliveryPolicy">An optional delivery policy.</param>
    public void Write<TMessage>(IProducer<TMessage> source, string name, bool largeMessages = false, DeliveryPolicy<TMessage> deliveryPolicy = null)
    {
        WriteToStorage(source.Out, name, largeMessages, deliveryPolicy);
    }

    /// <summary>
    /// Writes the messages from the specified stream to the matching stream in this store.
    /// Additionally stores supplemental metadata value.
    /// </summary>
    /// <typeparam name="TMessage">The type of messages in the stream.</typeparam>
    /// <typeparam name="TSupplementalMetadata">The type of supplemental stream metadata.</typeparam>
    /// <param name="source">The source stream to write.</param>
    /// <param name="supplementalMetadataValue">Supplemental metadata value.</param>
    /// <param name="name">The name of the stream.</param>
    /// <param name="largeMessages">Indicates whether the stream contains large messages (typically >4k). If true, the messages will be written to the large message file.</param>
    /// <param name="deliveryPolicy">An optional delivery policy.</param>
    public void Write<TMessage, TSupplementalMetadata>(IProducer<TMessage> source, TSupplementalMetadata supplementalMetadataValue, string name, bool largeMessages = false, DeliveryPolicy<TMessage> deliveryPolicy = null)
    {
        PsiStreamMetadata meta = WriteToStorage(source.Out, name, largeMessages, deliveryPolicy);
        WriteSupplementalMetadataToCatalog(meta, supplementalMetadataValue);
    }

    /// <summary>
    /// Stores supplemental metadata representing distinct dictionary keys seen on the stream.
    /// </summary>
    /// <typeparam name="TKey">The type of dictionary key in the stream.</typeparam>
    /// <typeparam name="TValue">The type of dictionary value in the stream.</typeparam>
    /// <param name="source">The source stream to write.</param>
    public void SummarizeDistinctKeysInSupplementalMetadata<TKey, TValue>(Emitter<Dictionary<TKey, TValue>> source)
    {
        if (!_writer.TryGetMetadata(source.Id, out PsiStreamMetadata meta))
        {
            throw new ArgumentException("Stream metadata is not available because the stream has not been written.");
        }

        // update supplemental meta to reflect distinct keys seen
        source.Aggregate(
            new SortedSet<TKey>(),
            (set, dict) =>
            {
                foreach (TKey key in dict.Keys)
                {
                    if (!set.Contains(key))
                    {
                        set.UnionWith(dict.Keys);
                        WriteSupplementalMetadataToCatalog(meta, new DistinctKeysSupplementalMetadata<TKey>(set));
                        return set;
                    }
                }

                return set;
            },
            DeliveryPolicy.Unlimited);
    }

    /// <summary>
    /// Writes the envelopes of messages from the specified stream to the store.
    /// </summary>
    /// <typeparam name="T">The type of messages in the stream.</typeparam>
    /// <param name="source">The source stream to write.</param>
    /// <param name="name">The name of the stream.</param>
    /// <param name="deliveryPolicy">An optional delivery policy.</param>
    public void WriteEnvelopes<T>(IProducer<T> source, string name, DeliveryPolicy<T> deliveryPolicy = null)
    {
        // make sure we can serialize this type
        SerializationHandler<int> handler = _serializers.GetHandler<int>();

        // add another input to the merger to hook up the serializer to
        // and check for duplicate names in the process
        Receiver<Message<BufferReader>> mergeInput = _merger.Add(name);

        // name the stream if it's not already named
        MessageEnvelopeConnector<T> connector = new(source.Out.Pipeline, this, null);

        // defaults to lossless delivery policy unless otherwise specified
        source.PipeTo(connector, deliveryPolicy ?? DeliveryPolicy.Unlimited);
        source.Out.Name = connector.Out.Name = name;
        source.Out.Closed += closeTime => _writer.CloseStream(source.Out.Id, closeTime);

        // tell the writer to write the serialized stream. If the pipeline is running
        // when the write command arrives, the stream is opened at the current pipeline time
        // o/w if the pipeline is not yet running, the opened time is set to minvalue, and
        // will be updated  on pipeline start-up.
        DateTime openedTime = _pipeline.IsRunning ? _pipeline.GetCurrentTime() : DateTime.MinValue;
        PsiStreamMetadata meta = _writer.OpenStream(source.Out.Id, name, false, handler.Name, openedTime);

        // register this stream with the store catalog
        _pipeline.ConfigurationStore.Set(Exporter.StreamMetadataNamespace, name, meta);

        // hook up the serializer
        SerializerComponent<int> serializer = new(this, _serializers);

        // The serializer and merger will act synchronously and throttle the connector for as long as
        // the merger is busy writing data. This will cause messages to be queued or dropped at the input
        // to the connector (per the user-supplied deliveryPolicy) until the merger is able to accept
        // the next serialized data message.
        serializer.PipeTo(mergeInput, allowWhileRunning: true, DeliveryPolicy.SynchronousOrThrottle);
        connector.PipeTo(serializer, allowWhileRunning: true, DeliveryPolicy.SynchronousOrThrottle);
    }

    /// <summary>
    /// Writes the messages from the specified stream to the matching stream in this store.
    /// </summary>
    /// <typeparam name="TMessage">The type of messages in the stream.</typeparam>
    /// <param name="source">The source stream to write.</param>
    /// <param name="name">The name of the stream.</param>
    /// <param name="metadata">Source stream metadata.</param>
    /// <param name="deliveryPolicy">An optional delivery policy.</param>
    internal void Write<TMessage>(Emitter<TMessage> source, string name, PsiStreamMetadata metadata, DeliveryPolicy<TMessage> deliveryPolicy = null)
    {
        WriteToStorage(source, name, metadata.IsIndexed, deliveryPolicy).UpdateSupplementalMetadataFrom(metadata);
    }

    internal void Write(Emitter<Message<BufferReader>> source, PsiStreamMetadata metadata, DeliveryPolicy<Message<BufferReader>> deliveryPolicy = null)
    {
        Receiver<Message<BufferReader>> mergeInput = _merger.Add(metadata.Name); // this checks for duplicates

        Connector<Message<BufferReader>> connector = CreateInputConnectorFrom<Message<BufferReader>>(source.Pipeline, null);
        source.PipeTo(connector);
        source.Name ??= metadata.Name;
        connector.Out.Name = metadata.Name;

        _writer.OpenStream(metadata);

        // defaults to lossless delivery policy unless otherwise specified
        connector.PipeTo(mergeInput, true, deliveryPolicy ?? DeliveryPolicy.Unlimited);
    }

    /// <summary>
    /// Writes the messages from the specified stream to the matching stream in this store.
    /// </summary>
    /// <typeparam name="TMessage">The type of messages in the stream.</typeparam>
    /// <param name="source">The source stream to write.</param>
    /// <param name="name">The name of the stream.</param>
    /// <param name="largeMessages">Indicates whether the stream contains large messages (typically >4k). If true, the messages will be written to the large message file.</param>
    /// <param name="deliveryPolicy">An optional delivery policy.</param>
    /// <returns>Stream metadata.</returns>
    private PsiStreamMetadata WriteToStorage<TMessage>(Emitter<TMessage> source, string name, bool largeMessages = false, DeliveryPolicy<TMessage> deliveryPolicy = null)
    {
        // make sure we can serialize this type
        SerializationHandler<TMessage> handler = _serializers.GetHandler<TMessage>();

        // add another input to the merger to hook up the serializer to
        // and check for duplicate names in the process
        Receiver<Message<BufferReader>> mergeInput = _merger.Add(name);

        // name the stream if it's not already named
        MessageConnector<TMessage> connector = new(source.Pipeline, this, null);

        // defaults to lossless delivery policy unless otherwise specified
        source.PipeTo(connector, deliveryPolicy ?? DeliveryPolicy.Unlimited);
        source.Name = connector.Out.Name = name;
        source.Closed += closeTime => _writer.CloseStream(source.Id, closeTime);

        // tell the writer to write the serialized stream. If the pipeline is running
        // when the write command arrives, the stream is opened at the current pipeline time
        // o/w if the pipeline is not yet running, the opened time is set to minvalue, and
        // will be updated  on pipeline start-up.
        DateTime openedTime = _pipeline.IsRunning ? _pipeline.GetCurrentTime() : DateTime.MinValue;
        PsiStreamMetadata meta = _writer.OpenStream(source.Id, name, largeMessages, handler.Name, openedTime);

        // register this stream with the store catalog
        _pipeline.ConfigurationStore.Set(Exporter.StreamMetadataNamespace, name, meta);

        // hook up the serializer
        SerializerComponent<TMessage> serializer = new(this, _serializers);

        // The serializer and merger will act synchronously and throttle the connector for as long as
        // the merger is busy writing data. This will cause messages to be queued or dropped at the input
        // to the connector (per the user-supplied deliveryPolicy) until the merger is able to accept
        // the next serialized data message.
        serializer.PipeTo(mergeInput, allowWhileRunning: true, DeliveryPolicy.SynchronousOrThrottle);
        connector.PipeTo(serializer, allowWhileRunning: true, DeliveryPolicy.SynchronousOrThrottle);

        return meta;
    }

    /// <summary>
    /// Writes the supplemental metadata for a specified stream to the catalog.
    /// </summary>
    /// <typeparam name="TSupplementalMetadata">The type of the supplemental metadata.</typeparam>
    /// <param name="meta">The psi stream metadata.</param>
    /// <param name="supplementalMetadata">The supplemental metadata.</param>
    private void WriteSupplementalMetadataToCatalog<TSupplementalMetadata>(PsiStreamMetadata meta, TSupplementalMetadata supplementalMetadata)
    {
        SerializationHandler<TSupplementalMetadata> handler = _serializers.GetHandler<TSupplementalMetadata>();
        BufferWriter writer = new(default(byte[]));
        handler.Serialize(writer, supplementalMetadata, new SerializationContext(_serializers));
        meta.SetSupplementalMetadata(typeof(TSupplementalMetadata).AssemblyQualifiedName, writer.Buffer);
        _writer.WriteToCatalog(meta);
    }

    /// <summary>
    /// Represents distinct keys on a dictionary stream.
    /// </summary>
    /// <typeparam name="T">Type of keys.</typeparam>
    public class DistinctKeysSupplementalMetadata<T>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DistinctKeysSupplementalMetadata{T}"/> class.
        /// </summary>
        /// <param name="keys">Set of distinct keys.</param>
        internal DistinctKeysSupplementalMetadata(SortedSet<T> keys)
        {
            Keys = keys.ToArray();
        }

        /// <summary>
        /// Gets distinct keys.
        /// </summary>
        public T[] Keys { get; private set; }
    }
}
