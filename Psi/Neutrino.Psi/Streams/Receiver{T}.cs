// <copyright file="Receiver{T}.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Collections.Generic;
using Neutrino.Psi.Common;
using Neutrino.Psi.Common.PerfCounters;
using Neutrino.Psi.Diagnostics;
using Neutrino.Psi.Executive;
using Neutrino.Psi.Scheduling;
using Neutrino.Psi.Serialization;

namespace Neutrino.Psi.Streams;

/// <summary>
/// A receiver that calls the wrapped delegate to deliver messages by reference (hence, unsafe).
/// The wrapped delegate must not modify or store the message or any part of the message.
/// </summary>
/// <remarks>
/// The Receiver class uses the Scheduler to deliver messages.
/// However, the workitem unit scheduled by the Receiver is the whole receiver queue, not a single message.
/// In other words, the Receiver simply schedules itself, and there will be only one workitem present in the scheduler queue for any given Receiver.
/// This guarantees message delivery order regardless of the kind of scheduling used by the scheduler.
/// </remarks>
/// <typeparam name="T">The type of messages that can be received.</typeparam>
[Serializer(typeof(Receiver<>.NonSerializer))]
public sealed class Receiver<T> : IReceiver, IConsumer<T>
{
    private readonly Action<Message<T>> _onReceived;
    private readonly PipelineElement _element;
    private readonly Pipeline _pipeline;
    private readonly Scheduler _scheduler;
    private readonly SchedulerContext _schedulerContext;
    private readonly SynchronizationLock _syncContext;
    private readonly bool _enforceIsolation;
    private readonly List<UnsubscribedHandler> _unsubscribedHandlers = new();
    private readonly Lazy<DiagnosticsCollector.ReceiverCollector> _receiverDiagnosticsCollector;

    private Envelope _lastEnvelope;
    private DeliveryQueue<T> _awaitingDelivery;
    private IPerfCounterCollection<ReceiverCounters> _counters;
    private Func<T, int> _computeDataSize = null;

    /// <summary>
    /// Initializes a new instance of the <see cref="Receiver{T}"/> class.
    /// </summary>
    /// <param name="id">The unique receiver id.</param>
    /// <param name="name">The debug name of the receiver.</param>
    /// <param name="element">The pipeline element associated with the receiver.</param>
    /// <param name="owner">The component that owns this receiver.</param>
    /// <param name="onReceived">The action to execute when a message is delivered to the receiver.</param>
    /// <param name="context">The synchronization context of the receiver.</param>
    /// <param name="pipeline">The pipeline in which to create the receiver.</param>
    /// <param name="enforceIsolation">A value indicating whether to enforce cloning of messages as they arrive at the receiver.</param>
    /// <remarks>
    /// The <paramref name="enforceIsolation"/> flag primarily affects synchronous delivery of messages, when the action is
    /// executed on the same thread on which the message was posted. If this value is set to true, the runtime will enforce
    /// isolation by cloning the message before passing it to the receiver action. If set to false, then the message will be
    /// passed by reference to the action without cloning, if and only if the receiver action executes synchronously. This
    /// should be used with caution, as any modifications that the action may make to the received message will be reflected
    /// in the source message posted by the upstream component. When in doubt, keep this value set to true to ensure that
    /// messages are always cloned. Regardless of the value set here, isolation is always enforced when messages are queued
    /// and delivered asynchronously.
    /// </remarks>
    internal Receiver(int id, string name, PipelineElement element, object owner, Action<Message<T>> onReceived, SynchronizationLock context, Pipeline pipeline, bool enforceIsolation = true)
    {
        _scheduler = pipeline.Scheduler;
        _schedulerContext = pipeline.SchedulerContext;
        _lastEnvelope = default;
        _onReceived = m =>
        {
            _lastEnvelope = m.Envelope;
            PipelineElement.TrackStateObjectOnContext(onReceived, owner, pipeline)(m);
        };
        Id = id;
        Name = name;
        _element = element;
        Owner = owner;
        _syncContext = context;
        _enforceIsolation = enforceIsolation;
        Recycler = RecyclingPool.Create<T>();
        _pipeline = pipeline;
        _receiverDiagnosticsCollector = new Lazy<DiagnosticsCollector.ReceiverCollector>(() => _pipeline.DiagnosticsCollector?.GetReceiverDiagnosticsCollector(pipeline, element, this), true);
    }

    /// <summary>
    /// Receiver unsubscribed handler.
    /// </summary>
    /// <param name="finalOriginatingTime">Originating time of final message posted.</param>
    public delegate void UnsubscribedHandler(DateTime finalOriginatingTime);

    /// <summary>
    /// Event invoked after this receiver is unsubscribed from its source emitter.
    /// </summary>
    public event UnsubscribedHandler Unsubscribed
    {
        add => _unsubscribedHandlers.Add(value);

        remove => _unsubscribedHandlers.Remove(value);
    }

    /// <inheritdoc />
    IEmitter IReceiver.Source => Source;

    /// <inheritdoc />
    public int Id { get; }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public Type Type => typeof(T);

    /// <inheritdoc />
    public object Owner { get; }

    /// <summary>
    /// Gets the delivery policy for this receiver.
    /// </summary>
    public DeliveryPolicy<T> DeliveryPolicy { get; private set; }

    /// <summary>
    /// Gets receiver message recycler.
    /// </summary>
    public IRecyclingPool<T> Recycler { get; }

    /// <summary>
    /// Gets the envelope of the last message delivered.
    /// </summary>
    public Envelope LastEnvelope => _lastEnvelope;

    /// <inheritdoc />
    Receiver<T> IConsumer<T>.In => this;

    internal Emitter<T> Source { get; private set; }

    /// <inheritdoc />
    public void Dispose()
    {
        _counters?.Clear();
    }

    /// <summary>
    /// Recycle message.
    /// </summary>
    /// <param name="freeMessage">Message to recycle.</param>
    public void Recycle(Message<T> freeMessage)
    {
        Recycle(freeMessage.Data);
    }

    /// <summary>
    /// Recycle item.
    /// </summary>
    /// <param name="item">Item to recycle.</param>
    public void Recycle(T item)
    {
        Recycler.Recycle(item);
    }

    /// <summary>
    /// Enable performance counters.
    /// </summary>
    /// <param name="name">Instance name.</param>
    /// <param name="perf">Performance counters implementation (platform specific).</param>
    public void EnablePerfCounters(string name, IPerfCounters<ReceiverCounters> perf)
    {
        const string Category = "Microsoft Psi message delivery";

        if (_counters != null)
        {
            throw new InvalidOperationException("Perf counters are already enabled for this receiver");
        }

#pragma warning disable SA1118 // Parameter must not span multiple lines
        perf.AddCounterDefinitions(
            Category,
            new Tuple<ReceiverCounters, string, string, PerfCounterType>[]
            {
                Tuple.Create(ReceiverCounters.Total, "Total messages / second", "Number of messages received per second", PerfCounterType.RateOfCountsPerSecond32),
                Tuple.Create(ReceiverCounters.Dropped, "Dropped messages / second", "Number of messages dropped per second", PerfCounterType.RateOfCountsPerSecond32),
                Tuple.Create(ReceiverCounters.Processed, "Messages / second", "Number of messages processed per second", PerfCounterType.RateOfCountsPerSecond32),
                Tuple.Create(ReceiverCounters.ProcessingTime, "Processing time (ns)", "The time it takes the component to process a message", PerfCounterType.NumberOfItems32),
                Tuple.Create(ReceiverCounters.PipelineExclusiveDelay, "Exclusive pipeline delay (ns)", "The delta between the originating time of the message and the time the message was received.", PerfCounterType.NumberOfItems32),
                Tuple.Create(ReceiverCounters.IngestTime, "Ingest time (ns)", "The delta between the time the message was posted and the time the message was received.", PerfCounterType.NumberOfItems32),
                Tuple.Create(ReceiverCounters.TimeInQueue, "Time in queue (ns)", "The time elapsed between posting of the message and beginning its processing", PerfCounterType.NumberOfItems32),
                Tuple.Create(ReceiverCounters.ProcessingDelay, "Total processing delay (ns)", "The time elapsed between posting of the message and completing its processing.", PerfCounterType.NumberOfItems32),
                Tuple.Create(ReceiverCounters.PipelineInclusiveDelay, "Inclusive pipeline delay (ns)", "The end-to-end delay, from originating time to the time when processing completed.", PerfCounterType.NumberOfItems32),
                Tuple.Create(ReceiverCounters.QueueSize, "Queue size", "The number of messages waiting in the delivery queue", PerfCounterType.NumberOfItems32),
                Tuple.Create(ReceiverCounters.MaxQueueSize, "Max queue size", "The maximum number of messages ever waiting at the same time in the delivery queue", PerfCounterType.NumberOfItems32),
                Tuple.Create(ReceiverCounters.ThrottlingRequests, "Throttling requests / second", "The number of throttling requests issued due to queue full, per second", PerfCounterType.RateOfCountsPerSecond32),
                Tuple.Create(ReceiverCounters.OutstandingUnrecycled, "Unrecycled messages", "The number of messages that are still in use by the component", PerfCounterType.NumberOfItems32),
                Tuple.Create(ReceiverCounters.AvailableRecycled, "Recycled messages", "The number of messages that are available for recycling", PerfCounterType.NumberOfItems32),
            });
#pragma warning restore SA1118 // Parameter must not span multiple lines

        _counters = perf.Enable(Category, name);
        _awaitingDelivery.EnablePerfCounters(_counters);
    }

    internal void OnSubscribe(Emitter<T> source, bool allowSubscribeWhileRunning, DeliveryPolicy<T> policy)
    {
        if (Source != null)
        {
            throw new InvalidOperationException("This receiver is already connected to a source emitter.");
        }

        if (!allowSubscribeWhileRunning && (_pipeline.IsRunning || source.Pipeline.IsRunning))
        {
            throw new InvalidOperationException("Attempting to connect a receiver to an emitter while pipeline is already running. Make all connections before running the pipeline.");
        }

        if (source.Pipeline != _pipeline)
        {
            throw new InvalidOperationException("Receiver cannot subscribe to an emitter from a different pipeline. Use a Connector if you need to connect emitters and receivers from different pipelines.");
        }

        Source = source;
        DeliveryPolicy = policy;
        _awaitingDelivery = new DeliveryQueue<T>(policy, Recycler);
        _pipeline.DiagnosticsCollector?.PipelineElementReceiverSubscribe(_pipeline, _element, this, source, DeliveryPolicy.Name);
    }

    internal void OnUnsubscribe()
    {
        if (Source != null)
        {
            Source = null;
            OnUnsubscribed(_pipeline.GetCurrentTime());
        }
    }

    internal void Receive(Message<T> message)
    {
        bool hasDiagnosticsCollector = _receiverDiagnosticsCollector.Value != null;
        DateTime diagnosticsTime = DateTime.MinValue;

        if (hasDiagnosticsCollector)
        {
            diagnosticsTime = _pipeline.GetCurrentTime();
            _receiverDiagnosticsCollector.Value.MessageEmitted(message.Envelope, diagnosticsTime);
        }

        if (_counters != null)
        {
            DateTime messageTimeReal = _scheduler.Clock.ToRealTime(message.CreationTime);
            DateTime messageOriginatingTimeReal = _scheduler.Clock.ToRealTime(message.OriginatingTime);
            _counters.Increment(ReceiverCounters.Total);
            _counters.RawValue(ReceiverCounters.IngestTime, (Time.GetCurrentTime() - messageTimeReal).Ticks / 10);
            _counters.RawValue(ReceiverCounters.PipelineExclusiveDelay, (message.CreationTime - messageOriginatingTimeReal).Ticks / 10);
            _counters.RawValue(ReceiverCounters.OutstandingUnrecycled, Recycler.OutstandingAllocationCount);
            _counters.RawValue(ReceiverCounters.AvailableRecycled, Recycler.AvailableAllocationCount);
        }

        // First, only clone the message if the component requires isolation, to allow for clone-free
        // operation on the synchronous execution path if enforceIsolation is set to false.
        if (_enforceIsolation)
        {
            message.Data = message.Data.DeepClone(Recycler);
        }

        if (DeliveryPolicy.AttemptSynchronousDelivery && _awaitingDelivery.IsEmpty && message.SequenceId != int.MaxValue)
        {
            // fast path - try to deliver synchronously for as long as we can
            // however, if this thread already has a lock on the owner it means some other receiver is in our call stack (we have a delivery loop),
            // so bail out because executing the delegate would break the exclusive execution promise of the receivers
            // An existing lock can also indicate that a downstream component wants us to slow down (throttle)
            bool delivered = _scheduler.TryExecute(
                _syncContext,
                _onReceived,
                message,
                message.OriginatingTime,
                _schedulerContext,
                hasDiagnosticsCollector,
                out DateTime receiverStartTime,
                out DateTime receiverEndTime);

            if (delivered)
            {
                if (_receiverDiagnosticsCollector.Value != null)
                {
                    _receiverDiagnosticsCollector.Value.MessageProcessed(
                        message.Envelope,
                        receiverStartTime,
                        receiverEndTime,
                        _pipeline.DiagnosticsConfiguration.TrackMessageSize ? ComputeDataSize(message.Data) : 0,
                        diagnosticsTime);

                    _receiverDiagnosticsCollector.Value.UpdateDiagnosticState(Owner.ToString());
                }

                if (_enforceIsolation)
                {
                    // recycle the cloned copy if synchronous execution succeeded
                    Recycler.Recycle(message.Data);
                }

                return;
            }
        }

        // slow path - we need to queue the message, and let the scheduler do the rest
        // we need to clone the message before queuing, but only if we didn't already
        if (!_enforceIsolation)
        {
            message.Data = message.Data.DeepClone(Recycler);
        }

        _awaitingDelivery.Enqueue(message, _receiverDiagnosticsCollector.Value, diagnosticsTime, StartThrottling, out QueueTransition stateTransition);

        // if the queue was empty or if the next message is a closing message, we need to schedule delivery
        if (stateTransition.ToNotEmpty || stateTransition.ToClosing)
        {
            // allow scheduling past finalization when throttling to ensure that we get a chance to unthrottle
            _scheduler.Schedule(_syncContext, DeliverNext, message.OriginatingTime, _schedulerContext, true, _awaitingDelivery.IsThrottling);
        }
    }

    internal void DeliverNext()
    {
        DateTime currentTime = _scheduler.Clock.GetCurrentTime();

        if (_awaitingDelivery.TryDequeue(out Message<T> message, out QueueTransition stateTransition, currentTime, _receiverDiagnosticsCollector.Value, StopThrottling))
        {
            if (message.SequenceId == int.MaxValue)
            {
                // emitter was closed
                OnUnsubscribed(message.OriginatingTime);
                return;
            }

            DateTime start = (_counters != null) ? Time.GetCurrentTime() : default;

            if (_receiverDiagnosticsCollector.Value == null)
            {
                _onReceived(message);
            }
            else
            {
                DateTime receiverStartTime = _pipeline.GetCurrentTime();
                _onReceived(message);
                DateTime receiverEndTime = _pipeline.GetCurrentTime();

                _receiverDiagnosticsCollector.Value.MessageProcessed(
                    message.Envelope,
                    receiverStartTime,
                    receiverEndTime,
                    _pipeline.DiagnosticsConfiguration.TrackMessageSize ? ComputeDataSize(message.Data) : 0,
                    currentTime);

                _receiverDiagnosticsCollector.Value.UpdateDiagnosticState(Owner.ToString());
            }

            if (_counters != null)
            {
                DateTime end = Time.GetCurrentTime();
                DateTime messageTimeReal = _scheduler.Clock.ToRealTime(message.CreationTime);
                DateTime messageOriginatingTimeReal = _scheduler.Clock.ToRealTime(message.OriginatingTime);
                _counters.RawValue(ReceiverCounters.TimeInQueue, (start - messageTimeReal).Ticks / 10);
                _counters.RawValue(ReceiverCounters.ProcessingTime, (end - start).Ticks / 10);
                _counters.Increment(ReceiverCounters.Processed);
                _counters.RawValue(ReceiverCounters.ProcessingDelay, (end - messageTimeReal).Ticks / 10);
                _counters.RawValue(ReceiverCounters.PipelineInclusiveDelay, (end - messageOriginatingTimeReal).Ticks / 10);
            }

            // recycle the item we dequeued
            Recycler.Recycle(message.Data);

            if (!stateTransition.ToEmpty)
            {
                // allow scheduling past finalization when throttling to ensure that we get a chance to unthrottle
                _scheduler.Schedule(_syncContext, DeliverNext, _awaitingDelivery.NextMessageTime, _schedulerContext, true, _awaitingDelivery.IsThrottling);
            }
        }
    }

    private void StartThrottling(QueueTransition stateTransition)
    {
        // if queue is full (as decided between local policy and global policy), lock the emitter.syncContext (which we might already own) until we make more room
        if (stateTransition.ToStartThrottling)
        {
            _counters?.Increment(ReceiverCounters.ThrottlingRequests);
            Source.Pipeline.Scheduler.Freeze(Source.SyncContext);
            _receiverDiagnosticsCollector.Value?.PipelineElementReceiverThrottle(true);
        }
    }

    private void StopThrottling(QueueTransition stateTransition)
    {
        if (stateTransition.ToStopThrottling)
        {
            Source.Pipeline.Scheduler.Thaw(Source.SyncContext);
            _receiverDiagnosticsCollector.Value?.PipelineElementReceiverThrottle(false);
        }
    }

    private void OnUnsubscribed(DateTime lastOriginatingTime)
    {
        _pipeline.DiagnosticsCollector?.PipelineElementReceiverUnsubscribe(_pipeline, _element, this, Source);
        _lastEnvelope = new Envelope(DateTime.MaxValue, DateTime.MaxValue, Id, int.MaxValue);
        foreach (UnsubscribedHandler handler in _unsubscribedHandlers)
        {
            PipelineElement.TrackStateObjectOnContext(() => handler(lastOriginatingTime), Owner, _pipeline).Invoke();
        }

        // clear the source only after all handlers have run to avoid this node being finalized prematurely
        Source = null;
    }

    /// <summary>
    /// Computes data size by running through serialization.
    /// </summary>
    /// <param name="data">Message data.</param>
    /// <returns>Data size (bytes).</returns>
    private int ComputeDataSize(T data)
    {
        if (_computeDataSize == null)
        {
            KnownSerializers serializers = KnownSerializers.Default;
            SerializationContext context = new(serializers);
            SerializationHandler<T> handler = serializers.GetHandler<T>();
            BufferWriter writer = new(16);
            _computeDataSize = m =>
            {
                writer.Reset();
                context.Reset();
                try
                {
                    handler.Serialize(writer, m, context);
                }
                catch (NotSupportedException)
                {
                    // cannot serialize Type, IntPtr, UIntPtr, MemberInfo, StackTrace, ...
                    _computeDataSize = _ => 0; // stop trying
                    return 0;
                }

                return writer.Position;
            };
        }

        return _computeDataSize(data);
    }

    private class NonSerializer : NonSerializer<Receiver<T>>
    {
    }
}
