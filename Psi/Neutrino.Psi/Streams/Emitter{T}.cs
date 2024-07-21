// <copyright file="Emitter{T}.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Psi.Executive;
using Microsoft.Psi.Scheduling;
using Microsoft.Psi.Serialization;

namespace Microsoft.Psi;

/// <summary>
/// Represents a stream of messages.
/// An emitter is similar to a .Net Event, in that it is used to propagate information to a set of subscriber that is only known at runtime.
/// While a subscriber to an event is of type delegate, a subscriber to an emitter is of type <see cref="Receiver{T}"/> (which wraps a delegate).
/// </summary>
/// <typeparam name="T">The type of messages in the stream.</typeparam>
[Serializer(typeof(Emitter<>.NonSerializer))]
public sealed class Emitter<T> : IEmitter, IProducer<T>
{
    private readonly object _owner;
    private readonly Pipeline _pipeline;
    private readonly int _id;
    private readonly List<ClosedHandler> _closedHandlers = new();
    private readonly ValidateMessageHandler _messageValidator;
    private readonly object _receiversLock = new();
    private readonly SynchronizationLock _syncContext;
    private string _name;
    private int _nextSeqId;
    private Envelope _lastEnvelope;
    private volatile Receiver<T>[] _receivers = new Receiver<T>[0];
    private IPerfCounterCollection<EmitterCounters> _counters;

    /// <summary>
    /// Initializes a new instance of the <see cref="Emitter{T}"/> class.
    /// This constructor is intended to be used by the framework.
    /// </summary>
    /// <param name="id">The id of this stream.</param>
    /// <param name="name">The name of this stream.</param>
    /// <param name="owner">The owning component.</param>
    /// <param name="syncContext">The synchronization context this emitter operates in.</param>
    /// <param name="pipeline">The pipeline to associate with.</param>
    /// <param name="messageValidator">An optional message validator.</param>
    internal Emitter(int id, string name, object owner, SynchronizationLock syncContext, Pipeline pipeline, ValidateMessageHandler messageValidator = null)
    {
        _id = id;
        _name = name;
        _owner = owner;
        _syncContext = syncContext;
        _pipeline = pipeline;
        _messageValidator = messageValidator;
    }

    /// <summary>
    /// Emitter closed handler.
    /// </summary>
    /// <param name="finalOriginatingTime">Originating time of final message posted.</param>
    public delegate void ClosedHandler(DateTime finalOriginatingTime);

    /// <summary>
    /// Validate message handler.
    /// </summary>
    /// <param name="data">The data of the message being validated.</param>
    /// <param name="envelope">The envelope of the message being validated.</param>
    public delegate void ValidateMessageHandler(T data, Envelope envelope);

    /// <summary>
    /// Event invoked after this emitter is closed.
    /// </summary>
    internal event ClosedHandler Closed
    {
        add => _closedHandlers.Add(value);

        remove => _closedHandlers.Remove(value);
    }

    /// <inheritdoc />
    public string Name
    {
        get => _name;

        set
        {
            _name = value;
            _pipeline.DiagnosticsCollector?.EmitterRenamed(this);
        }
    }

    /// <inheritdoc />
    public int Id => _id;

    /// <inheritdoc />
    public Type Type => typeof(T);

    /// <inheritdoc />
    public Pipeline Pipeline => _pipeline;

    /// <summary>
    /// Gets the envelope of the last message posted on this emitter.
    /// </summary>
    public Envelope LastEnvelope => _lastEnvelope;

    /// <summary>
    /// Gets a value indicating whether this emitter has subscribers.
    /// </summary>
    public bool HasSubscribers => _receivers.Count() > 0;

    /// <inheritdoc />
    public object Owner => _owner;

    /// <inheritdoc />
    Emitter<T> IProducer<T>.Out => this;

    internal SynchronizationLock SyncContext => _syncContext;

    /// <inheritdoc />
    public void Close(DateTime originatingTime)
    {
        if (_lastEnvelope.SequenceId != int.MaxValue)
        {
            Envelope e = CreateEnvelope(originatingTime);
            e.SequenceId = int.MaxValue; // special "closing" ID
            Deliver(new Message<T>(default, e));

            lock (_receiversLock)
            {
                _receivers = new Receiver<T>[0];
            }

            foreach (ClosedHandler handler in _closedHandlers)
            {
                PipelineElement.TrackStateObjectOnContext(() => handler(originatingTime), Owner, _pipeline).Invoke();
            }
        }
    }

    /// <summary>
    /// Synchronously calls all subscribers.
    /// When the call returns, the message is assumed to be unchanged and reusable (that is, no downstream component is referencing it or any of its parts).
    /// </summary>
    /// <param name="message">The message to post.</param>
    /// <param name="originatingTime">The time of the real-world event that led to the creation of this message.</param>
    public void Post(T message, DateTime originatingTime)
    {
#if DEBUG
        PipelineElement.CheckStateObjectOnContext(_owner, _pipeline);
#endif

        Envelope e = CreateEnvelope(originatingTime);
        Deliver(new Message<T>(message, e));
    }

    /// <inheritdoc />
    public string DebugView(string debugName = null)
    {
        return DebugExtensions.DebugView(this, debugName);
    }

    /// <summary>
    /// Enable performance counters.
    /// </summary>
    /// <param name="name">Instance name.</param>
    /// <param name="perf">Performance counters implementation (platform specific).</param>
    public void EnablePerfCounters(string name, IPerfCounters<EmitterCounters> perf)
    {
        const string Category = "Microsoft Psi message submission";

        if (_counters != null)
        {
            throw new InvalidOperationException("Perf counters are already enabled for emitter " + Name);
        }

        perf.AddCounterDefinitions(
            Category,
            new Tuple<EmitterCounters, string, string, PerfCounterType>[]
            {
                Tuple.Create(EmitterCounters.MessageCount, "Total messages / second", "Number of messages received per second", PerfCounterType.RateOfCountsPerSecond32),
                Tuple.Create(EmitterCounters.MessageLatency, "Message latency (microseconds)", "The end-to-end latency, from originating time to the time when processing completed.", PerfCounterType.NumberOfItems32),
            });

        _counters = perf.Enable(Category, name);
    }

    /// <summary>
    /// Allows a receiver to subscribe to messages from this emitter.
    /// </summary>
    /// <param name="receiver">The receiver subscribing to this emitter.</param>
    /// <param name="allowSubscribeWhileRunning"> If true, bypasses checks that subscriptions are not 
    /// made while pipelines are running.</param>
    /// <param name="deliveryPolicy">The desired policy to use when delivering messages to the 
    /// specified receiver.</param>
    internal void Subscribe(Receiver<T> receiver, bool allowSubscribeWhileRunning, DeliveryPolicy<T> deliveryPolicy)
    {
        receiver.OnSubscribe(this, allowSubscribeWhileRunning, deliveryPolicy);

        lock (_receiversLock)
        {
            Receiver<T>[] newSet = _receivers.Concat(new[] { receiver }).ToArray();
            _receivers = newSet;
        }
    }

    internal void Unsubscribe(Receiver<T> receiver)
    {
        lock (_receiversLock)
        {
            Receiver<T>[] newSet = _receivers.Except(new[] { receiver }).ToArray();
            _receivers = newSet;
        }

        receiver.OnUnsubscribe();
    }

    internal int GetNextId()
    {
        return Interlocked.Increment(ref _nextSeqId);
    }

    internal void Deliver(T data, Envelope e)
    {
        e.SourceId = Id;
        Deliver(new Message<T>(data, e));
    }

    private void Validate(T data, Envelope e)
    {
        // make sure the data is consistent
        if (e.SequenceId <= _lastEnvelope.SequenceId)
        {
            throw new InvalidOperationException(
                $"Attempted to post a message with a sequence ID that is out of order.\n" +
                $"This may be caused by simultaneous calls to Emitter.Post() from multiple threads.\n" +
                $"Emitter: {Name}\n" +
                $"Current message sequence ID: {e.SequenceId}\n" +
                $"Previous message sequence ID: {_lastEnvelope.SequenceId}\n");
        }

        if (e.OriginatingTime <= _lastEnvelope.OriginatingTime)
        {
            throw new InvalidOperationException(
                $"Attempted to post a message without strictly increasing originating times.\n" +
                $"Emitter: {Name}\n" +
                $"Current message originating time: {e.OriginatingTime.TimeOfDay}\n" +
                $"Previous message originating time: {_lastEnvelope.OriginatingTime.TimeOfDay}\n");
        }

        if (e.CreationTime < _lastEnvelope.CreationTime)
        {
            throw new InvalidOperationException(
                $"Attempted to post a message that is out of order in wall-clock time.\n" +
                $"Emitter: {Name}\n" +
                $"Current message creation time: {e.CreationTime.TimeOfDay}\n" +
                $"Previous message creation time: {_lastEnvelope.CreationTime.TimeOfDay}\n");
        }

        // additional message validation checks
        _messageValidator?.Invoke(data, e);
    }

    private void Deliver(Message<T> msg)
    {
        if (_lastEnvelope.SequenceId == int.MaxValue)
        {
            // emitter is closed
            return;
        }

        if (_lastEnvelope.SequenceId != 0 && msg.SequenceId != int.MaxValue)
        {
            Validate(msg.Data, msg.Envelope);
        }

        _lastEnvelope = msg.Envelope;

        if (_counters != null)
        {
            _counters.Increment(EmitterCounters.MessageCount);
            _counters.RawValue(EmitterCounters.MessageLatency, (msg.CreationTime - msg.OriginatingTime).Ticks / 10);
        }

        // capture the "receivers" member to avoid locking
        Receiver<T>[] activeSet = _receivers;
        foreach (Receiver<T> rec in activeSet)
        {
            rec.Receive(msg);
        }
    }

    private Envelope CreateEnvelope(DateTime originatingTime)
    {
        return new Envelope(originatingTime, _pipeline.GetCurrentTime(), Id, GetNextId());
    }

    private class NonSerializer : NonSerializer<Emitter<T>>
    {
    }
}
