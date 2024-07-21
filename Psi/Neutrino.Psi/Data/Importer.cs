// <copyright file="Importer.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Collections.Generic;
using Microsoft.Psi.Common;
using Microsoft.Psi.Components;
using Microsoft.Psi.Executive;
using Microsoft.Psi.Serialization;


namespace Microsoft.Psi.Data;

/// <summary>
/// Component that reads messages via a specified <see cref="IStreamReader"/> and publishes them on streams.
/// </summary>
/// <remarks>
/// Reads either at the full speed allowed by available resources or at the desired rate
/// specified by the <see cref="Pipeline"/>. The store metadata is available immediately after open
/// (before the pipeline is running) via the <see cref="AvailableStreams"/> property.
/// </remarks>
public class Importer : Subpipeline, IDisposable
{
    private readonly Pipeline _pipeline;
    private readonly IStreamReader _streamReader;
    private readonly Func<StreamImporter> _getStreamImporter;

    /// <summary>
    /// Initializes a new instance of the <see cref="Importer"/> class.
    /// </summary>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="streamReader">Stream reader.</param>
    /// <param name="usePerStreamReaders">Flag indicating whether to use per-stream readers.</param>
    public Importer(Pipeline pipeline, IStreamReader streamReader, bool usePerStreamReaders)
        : base(pipeline, $"{nameof(Importer)}[{streamReader.Name}]")
    {
        _pipeline = pipeline;
        _streamReader = streamReader;

        _getStreamImporter = () => new StreamImporter(this, pipeline.ConfigurationStore, streamReader.OpenNew());
        if (!usePerStreamReaders)
        {
            // cache single shared importer
            StreamImporter sharedImporter = _getStreamImporter();
            _getStreamImporter = () => sharedImporter;
        }
    }

    /// <summary>
    /// Gets the name of the store, or null if this is a volatile store.
    /// </summary>
    public string StoreName => _streamReader.Name;

    /// <summary>
    /// Gets the path of the store, or null if this is a volatile store.
    /// </summary>
    public string StorePath => _streamReader.Path;

    /// <summary>
    /// Gets the set of types that this Importer can deserialize.
    /// Types can be added or re-mapped using the <see cref="KnownSerializers.Register{T}(string, CloningFlags)"/> method.
    /// </summary>
    public KnownSerializers Serializers
    {
        get
        {
            if (_streamReader is PsiStoreStreamReader storeStreamReader)
            {
                return storeStreamReader.GetSerializers();
            }

            return KnownSerializers.Default;
        }
    }

    /// <summary>
    /// Gets the metadata of all the streams in this store.
    /// </summary>
    public IEnumerable<IStreamMetadata> AvailableStreams => _streamReader.AvailableStreams;

    /// <summary>
    /// Gets the interval between the creation times of the first and last messages written to this store, across all streams.
    /// </summary>
    public TimeInterval MessageCreationTimeInterval => _streamReader.MessageCreationTimeInterval;

    /// <summary>
    /// Gets the interval between the originating times of the first and last messages written to this store, across all streams.
    /// </summary>
    public TimeInterval MessageOriginatingTimeInterval => _streamReader.MessageOriginatingTimeInterval;

    /// <summary>
    /// Gets the interval between the opened times and closed times, across all streams.
    /// </summary>
    public TimeInterval StreamTimeInterval => _streamReader.StreamTimeInterval;

    /// <summary>
    /// Returns the metadata for a specified stream.
    /// </summary>
    /// <param name="streamName">The name of the stream.</param>
    /// <returns>The metadata associated with the stream.</returns>
    public IStreamMetadata GetMetadata(string streamName)
    {
        return _streamReader.GetStreamMetadata(streamName);
    }

    /// <summary>
    /// Returns the supplemental metadata for a specified stream.
    /// </summary>
    /// <typeparam name="T">Type of supplemental metadata.</typeparam>
    /// <param name="streamName">The name of the stream.</param>
    /// <returns>The metadata associated with the stream.</returns>
    public T GetSupplementalMetadata<T>(string streamName)
    {
        if (_streamReader.GetStreamMetadata(streamName) is not PsiStreamMetadata psiStreamMetadata)
        {
            throw new NotSupportedException($"Supplemental metadata is only available on {nameof(PsiStreamMetadata)} from a {nameof(PsiStoreStreamReader)}.");
        }

        return psiStreamMetadata.GetSupplementalMetadata<T>(Serializers);
    }

    /// <summary>
    /// Indicates whether the store contains the specified stream.
    /// </summary>
    /// <param name="streamName">The name of the stream.</param>
    /// <returns>True if the store contains a stream with the specified name, false otherwise.</returns>
    public bool Contains(string streamName)
    {
        return _streamReader.ContainsStream(streamName);
    }

    /// <summary>
    /// Copies the specified stream to an exporter without deserializing the data.
    /// </summary>
    /// <param name="streamName">The name of the stream to copy.</param>
    /// <param name="writer">The store to copy to.</param>
    /// <param name="deliveryPolicy">An optional delivery policy.</param>
    public void CopyStream(string streamName, Exporter writer, DeliveryPolicy<Message<BufferReader>> deliveryPolicy = null)
    {
        _getStreamImporter().CopyStream(streamName, writer, deliveryPolicy);
    }

    /// <summary>
    /// Opens the specified stream for reading and returns a stream instance that can be used to consume the messages.
    /// </summary>
    /// <typeparam name="T">The expected type of the stream to open.
    /// This type will be used to deserialize the stream messages.</typeparam>
    /// <param name="streamName">The name of the stream to open.</param>
    /// <param name="allocator">An optional allocator of messages.</param>
    /// <param name="deallocator">An optional deallocator to use after the messages have been sent out (defaults to disposing <see cref="IDisposable"/> messages.)</param>
    /// <returns>A stream that publishes the data read from the store.</returns>
    public IProducer<T> OpenStream<T>(string streamName, Func<T> allocator = null, Action<T> deallocator = null)
    {
        // preserve the envelope of the deserialized message in the output connector
        return BridgeOut(_getStreamImporter().OpenStream(streamName, allocator, deallocator), streamName);
    }

    /// <summary>
    /// Opens the specified stream for reading if the stream exists, and returns a stream instance that can be used to consume the messages.
    /// </summary>
    /// <typeparam name="T">The expected type of the stream to open.
    /// This type will be used to deserialize the stream messages.</typeparam>
    /// <param name="streamName">The name of the stream to open.</param>
    /// <param name="allocator">An optional allocator of messages.</param>
    /// <param name="deallocator">An optional deallocator to use after the messages have been sent out (defaults to disposing <see cref="IDisposable"/> messages.)</param>
    /// <returns>A stream that publishes the data read from the store, or null if the stream does not exist.</returns>
    public IProducer<T> OpenStreamOrDefault<T>(string streamName, Func<T> allocator = null, Action<T> deallocator = null)
    {
        return Contains(streamName) ? OpenStream(streamName, allocator, deallocator) : null;
    }

    /// <summary>
    /// Opens the specified stream as dynamic for reading and returns a stream instance that can be used to consume the messages.
    /// The returned stream will publish data read from the store once the pipeline is running.
    /// </summary>
    /// <remarks>Messages are deserialized as dynamic primitives and/or ExpandoObject of dynamic.</remarks>
    /// <param name="streamName">The name of the stream to open.</param>
    /// <param name="allocator">An optional allocator of messages.</param>
    /// <param name="deallocator">An optional deallocator to use after the messages have been sent out (defaults to disposing <see cref="IDisposable"/> messages.)</param>
    /// <returns>A stream of dynamic that publishes the data read from the store.</returns>
    public IProducer<dynamic> OpenDynamicStream(string streamName, Func<dynamic> allocator = null, Action<dynamic> deallocator = null)
    {
        // preserve the envelope of the deserialized message in the output connector
        return BridgeOut(_getStreamImporter().OpenDynamicStream(streamName, allocator, deallocator), streamName);
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        base.Dispose();
        _streamReader.Dispose();
    }

    /// <summary>
    /// Opens the specified stream as raw `Message` of `BufferReader` for reading and returns a stream instance that can be used to consume the messages.
    /// The returned stream will publish data read from the store once the pipeline is running.
    /// </summary>
    /// <remarks>Messages are not deserialized.</remarks>
    /// <param name="meta">The meta of the stream to open.</param>
    /// <returns>A stream of raw messages that publishes the data read from the store.</returns>
    internal IProducer<Message<BufferReader>> OpenRawStream(PsiStreamMetadata meta)
    {
        return BridgeOut(_getStreamImporter().OpenRawStream(meta), meta.Name);
    }

    /// <summary>
    /// Bridge output stream out to parent pipeline.
    /// </summary>
    /// <typeparam name="T">Type of stream messages.</typeparam>
    /// <param name="stream">Stream of messages.</param>
    /// <param name="name">Stream name.</param>
    /// <returns>Bridged stream.</returns>
    private Connector<T> BridgeOut<T>(IProducer<T> stream, string name)
    {
        // preserve the envelope of the deserialized message in the output connector
        Connector<T> connector = new(this, _pipeline, name, true);
        return stream.PipeTo(connector, DeliveryPolicy.SynchronousOrThrottle);
    }

    /// <summary>
    /// Component that reads messages via a specified <see cref="IStreamReader"/> and publishes them on streams.
    /// </summary>
    /// <remarks>
    /// Reads either at the full speed allowed by available resources or at the desired rate
    /// specified by the <see cref="Pipeline"/>. The store metadata is available immediately after open
    /// (before the pipeline is running) via the <see cref="AvailableStreams"/> property.
    /// </remarks>
    private class StreamImporter : ISourceComponent, IDisposable
    {
        private readonly IStreamReader _streamReader;
        private readonly Dictionary<string, object> _streams = [];
        private readonly Pipeline _pipeline;
        private readonly KeyValueStore _configurationStore;
        private readonly Receiver<bool> _loopBack;
        private readonly Emitter<bool> _next;

        private bool _stopping;
        private long _finalTicks;
        private Action<DateTime> _notifyCompletionTime;
        private Action _outputPreviousMessage = () => { };

        /// <summary>
        /// Initializes a new instance of the <see cref="StreamImporter"/> class.
        /// </summary>
        /// <param name="pipeline">The pipeline to add the component to.</param>
        /// <param name="configurationStore">Configuration store in which to store catalog meta.</param>
        /// <param name="streamReader">Stream reader.</param>
        internal StreamImporter(Pipeline pipeline, KeyValueStore configurationStore, IStreamReader streamReader)
        {
            _streamReader = streamReader;
            _pipeline = pipeline;
            _configurationStore = configurationStore;
            _next = pipeline.CreateEmitter<bool>(this, nameof(this.Next));
            _loopBack = pipeline.CreateReceiver<bool>(this, Next, nameof(_loopBack));
            _next.PipeTo(_loopBack, DeliveryPolicy.Unlimited);
        }

        /// <summary>
        /// Closes the store and disposes of the current instance.
        /// </summary>
        public void Dispose()
        {
            _streamReader.Dispose();
            _loopBack.Dispose();
        }

        /// <inheritdoc />
        public void Start(Action<DateTime> notifyCompletionTime)
        {
            _notifyCompletionTime = notifyCompletionTime;
            ReplayDescriptor replay = _pipeline.ReplayDescriptor;
            _streamReader.Seek(replay.Interval, true);
            _next.Post(true, replay.Start);
        }

        /// <inheritdoc />
        public void Stop(DateTime finalOriginatingTime, Action notifyCompleted)
        {
            _stopping = true;
            notifyCompleted();
        }

        /// <summary>
        /// Copies the specified stream to an exporter without deserializing the data.
        /// </summary>
        /// <param name="streamName">The name of the stream to copy.</param>
        /// <param name="writer">The store to copy to.</param>
        /// <param name="deliveryPolicy">An optional delivery policy.</param>
        internal void CopyStream(string streamName, Exporter writer, DeliveryPolicy<Message<BufferReader>> deliveryPolicy = null)
        {
            // create the copy pipeline
            if (_streamReader.GetStreamMetadata(streamName) is not PsiStreamMetadata psiStreamMetadata)
            {
                throw new NotSupportedException($"Copying streams requires a {nameof(PsiStoreStreamReader)}.");
            }

            IProducer<Message<BufferReader>> raw = OpenRawStream(psiStreamMetadata);
            writer.Write(raw.Out, psiStreamMetadata, deliveryPolicy);
        }

        /// <summary>
        /// Opens the specified stream for reading and returns a stream instance that can be used to consume the messages.
        /// The returned stream will publish data read from the store once the pipeline is running.
        /// </summary>
        /// <typeparam name="T">The expected type of the stream to open.
        /// This type will be used to deserialize the stream messages.</typeparam>
        /// <param name="streamName">The name of the stream to open.</param>
        /// <param name="allocator">An optional allocator of messages.</param>
        /// <param name="deallocator">An optional deallocator to use after the messages have been sent out (defaults to disposing <see cref="IDisposable"/> messages.)</param>
        /// <returns>A stream that publishes the data read from the store.</returns>
        internal IProducer<T> OpenStream<T>(string streamName, Func<T> allocator = null, Action<T> deallocator = null)
        {
            // Setup the default deallocator
            deallocator ??= data =>
            {
                if (data is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            };

            if (_streams.TryGetValue(streamName, out object stream))
            {
                return (IProducer<T>)stream; // if the types don't match, invalid cast exception is the appropriate error
            }

            IStreamMetadata meta = _streamReader.GetStreamMetadata(streamName);

            TimeInterval originatingLifetime = meta.MessageCount == 0 ? TimeInterval.Empty : new TimeInterval(meta.FirstMessageOriginatingTime, meta.LastMessageOriginatingTime);
            if (originatingLifetime != null && !originatingLifetime.IsEmpty && originatingLifetime.IsFinite)
            {
                // propose a replay time that covers the stream lifetime
                _pipeline.ProposeReplayTime(originatingLifetime);
            }

            // register this stream with the store catalog
            _configurationStore.Set(Exporter.StreamMetadataNamespace, streamName, meta);

            Emitter<T> emitter = _pipeline.CreateEmitter<T>(this, streamName);
            _streamReader.OpenStream(
                streamName,
                (data, envelope) =>
                {
                    // do not deliver messages past the stream closing time
                    if (meta.ClosedTime == default || envelope.OriginatingTime <= meta.ClosedTime)
                    {
                        // If the replay descriptor enforces the replay clock and the message creation time is ahead
                        // of the pipeline time
                        if (_pipeline.ReplayDescriptor.EnforceReplayClock && envelope.CreationTime > _pipeline.GetCurrentTime())
                        {
                            // Then clone the message in order to hold on to it and publish it later.
                            T clone = allocator != null ? allocator() : default;
                            data.DeepClone(ref clone);

                            // Hold onto the data in the outputPreviousMessage closure, which is called
                            // in Next(). Persisting as a closure allows for capturing data of varying types (T).
                            _outputPreviousMessage = () =>
                            {
                                emitter.Deliver(clone, envelope);
                                deallocator(clone);
                            };
                        }
                        else
                        {
                            // call Deliver rather than Post to preserve the original envelope
                            emitter.Deliver(data, envelope);
                        }
                    }
                },
                allocator,
                deallocator);

            _streams[streamName] = emitter;
            return emitter;
        }

        /// <summary>
        /// Opens the specified stream as dynamic for reading and returns a stream instance that can be used to consume the messages.
        /// The returned stream will publish data read from the store once the pipeline is running.
        /// </summary>
        /// <remarks>Messages are deserialized as dynamic primitives and/or ExpandoObject of dynamic.</remarks>
        /// <param name="streamName">The name of the stream to open.</param>
        /// <param name="allocator">An optional allocator of messages.</param>
        /// <param name="deallocator">An optional deallocator to use after the messages have been sent out (defaults to disposing <see cref="IDisposable"/> messages.)</param>
        /// <returns>A stream of dynamic that publishes the data read from the store.</returns>
        internal IProducer<dynamic> OpenDynamicStream(string streamName, Func<dynamic> allocator = null, Action<dynamic> deallocator = null)
        {
            if (_streamReader is not PsiStoreStreamReader)
            {
                throw new NotSupportedException($"Opening dynamic streams requires a {nameof(PsiStoreStreamReader)}.");
            }

            return OpenStream(streamName, allocator, deallocator);
        }

        /// <summary>
        /// Opens the specified stream as raw `Message` of `BufferReader` for reading and returns a stream instance that can be used to consume the messages.
        /// The returned stream will publish data read from the store once the pipeline is running.
        /// </summary>
        /// <remarks>Messages are not deserialized.</remarks>
        /// <param name="meta">The meta of the stream to open.</param>
        /// <returns>A stream of raw messages that publishes the data read from the store.</returns>
        internal IProducer<Message<BufferReader>> OpenRawStream(PsiStreamMetadata meta)
        {
            if (_streamReader is not PsiStoreStreamReader)
            {
                throw new NotSupportedException($"Opening raw streams requires a {nameof(PsiStoreStreamReader)}.");
            }

            return OpenStream<Message<BufferReader>>(meta.Name);
        }

        /// <summary>
        /// Attempts to move the reader to the next message (across all streams).
        /// </summary>
        /// <param name="moreDataPromised">Indicates whether an absence of messages should be reported as the end of the store.</param>
        /// <param name="env">The envelope of the last message we read.</param>
        private void Next(bool moreDataPromised, Envelope env)
        {
            _outputPreviousMessage(); // deliver previous message (if any)
            _outputPreviousMessage = () => { };

            if (_stopping)
            {
                return;
            }

            bool result = _streamReader.MoveNext(out Envelope e); // causes target to be called
            if (result)
            {
                // we want messages to be scheduled and delivered based on their original creation time, not originating time
                // the check below is just to ensure we don't fail because of some timing issue when writing the data
                // (since there is no ordering guarantee across streams) note that we are posting a message of a message, and
                // once the outer message is stripped by the splitter, the inner message will still have the correct
                // originating time
                DateTime nextTime = (env.OriginatingTime > e.CreationTime) ? env.OriginatingTime : e.CreationTime;
                _next.Post(true, nextTime.AddTicks(1));
                _finalTicks = Math.Max(_finalTicks, Math.Max(e.OriginatingTime.Ticks, nextTime.Ticks));
            }
            else
            {
                // retry at least once, even if there is no active writer
                bool willHaveMoreData = _streamReader.IsLive();
                if (willHaveMoreData || moreDataPromised)
                {
                    _next.Post(willHaveMoreData, env.OriginatingTime.AddTicks(1));
                }
                else
                {
                    _notifyCompletionTime(new DateTime(_finalTicks));
                }
            }
        }
    }
}
