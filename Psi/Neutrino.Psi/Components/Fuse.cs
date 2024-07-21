// <copyright file="Fuse.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Collections.Generic;
using System.Linq;


namespace Microsoft.Psi.Components;

/// <summary>
/// Component that fuses multiple streams based on a specified interpolator.
/// </summary>
/// <typeparam name="TPrimary">The type the messages on the primary stream.</typeparam>
/// <typeparam name="TSecondary">The type messages on the secondary stream.</typeparam>
/// <typeparam name="TInterpolation">The type of the interpolation result on the secondary stream.</typeparam>
/// <typeparam name="TOut">The type of output message.</typeparam>
public class Fuse<TPrimary, TSecondary, TInterpolation, TOut> : IProducer<TOut>
{
    private readonly Pipeline _pipeline;
    private readonly string _name;
    private readonly Queue<Message<TPrimary>> _primaryQueue = new(); // to be paired
    private readonly Interpolator<TSecondary, TInterpolation> _interpolator;
    private readonly Func<TPrimary, TInterpolation[], TOut> _outputCreator;
    private readonly Func<TPrimary, IEnumerable<int>> _secondarySelector;
    private (Queue<Message<TSecondary>> Queue, DateTime? ClosedOriginatingTime)[] _secondaryQueues;
    private Receiver<TSecondary>[] _inSecondaries;
    private bool[] _receivedSecondary;
    private IEnumerable<int> _defaultSecondarySet;

    // temp buffers
    private TInterpolation[] _lastValues;
    private InterpolationResult<TInterpolation>[] _lastResults;

    /// <summary>
    /// Initializes a new instance of the <see cref="Fuse{TPrimary, TSecondary, TInterpolation, TOut}"/> class.
    /// </summary>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="interpolator">Interpolator to use when joining the streams.</param>
    /// <param name="outputCreator">Mapping function from messages to output.</param>
    /// <param name="secondaryCount">Number of secondary streams.</param>
    /// <param name="secondarySelector">Selector function mapping primary messages to a set of secondary stream indices.</param>
    /// <param name="name">An optional name for the component.</param>
    public Fuse(
        Pipeline pipeline,
        Interpolator<TSecondary, TInterpolation> interpolator,
        Func<TPrimary, TInterpolation[], TOut> outputCreator,
        int secondaryCount = 1,
        Func<TPrimary, IEnumerable<int>> secondarySelector = null,
        string name = null)
        : base()
    {
        _pipeline = pipeline;
        _name = name ?? $"Fuse({interpolator})";
        Out = pipeline.CreateEmitter<TOut>(this, nameof(Out));
        InPrimary = pipeline.CreateReceiver<TPrimary>(this, ReceivePrimary, nameof(InPrimary));
        _interpolator = interpolator;
        _outputCreator = outputCreator;
        _secondarySelector = secondarySelector;
        _inSecondaries = new Receiver<TSecondary>[secondaryCount];
        _receivedSecondary = new bool[secondaryCount];
        _secondaryQueues = new ValueTuple<Queue<Message<TSecondary>>, DateTime?>[secondaryCount];
        _lastValues = new TInterpolation[secondaryCount];
        _lastResults = new InterpolationResult<TInterpolation>[secondaryCount];
        _defaultSecondarySet = Enumerable.Range(0, secondaryCount);
        for (int i = 0; i < secondaryCount; i++)
        {
            _secondaryQueues[i] = (new Queue<Message<TSecondary>>(), null);
            int id = i; // needed to make the closure below byval
            Receiver<TSecondary> receiver = pipeline.CreateReceiver<TSecondary>(this, (d, e) => ReceiveSecondary(id, d, e), "InSecondary" + i);
            receiver.Unsubscribed += closedOriginatingTime => SecondaryClosed(id, closedOriginatingTime);
            _inSecondaries[i] = receiver;
            _receivedSecondary[i] = false;
        }
    }

    /// <inheritdoc />
    public Emitter<TOut> Out { get; }

    /// <summary>
    /// Gets primary input receiver.
    /// </summary>
    public Receiver<TPrimary> InPrimary { get; }

    /// <summary>
    /// Gets collection of secondary receivers.
    /// </summary>
    public IList<Receiver<TSecondary>> InSecondaries => _inSecondaries;

    /// <inheritdoc/>
    public override string ToString()
    {
        return _name;
    }

    /// <summary>
    /// Add input receiver.
    /// </summary>
    /// <returns>Receiver.</returns>
    public Receiver<TSecondary> AddInput()
    {
        // use the sync context to protect the queues from concurrent access
        Scheduling.SynchronizationLock syncContext = Out.SyncContext;
        syncContext.Lock();

        try
        {
            int lastIndex = _inSecondaries.Length;
            int count = lastIndex + 1;
            Array.Resize(ref _inSecondaries, count);
            Receiver<TSecondary> newInput = _inSecondaries[lastIndex] = _pipeline.CreateReceiver<TSecondary>(this, (d, e) => ReceiveSecondary(lastIndex, d, e), "InSecondary" + lastIndex);
            newInput.Unsubscribed += closedOriginatingTime => SecondaryClosed(lastIndex, closedOriginatingTime);

            Array.Resize(ref _receivedSecondary, count);
            _receivedSecondary[count - 1] = false;

            Array.Resize(ref _secondaryQueues, count);
            _secondaryQueues[lastIndex] = (new Queue<Message<TSecondary>>(), null);
            Array.Resize(ref _lastResults, count);
            Array.Resize(ref _lastValues, count);
            _defaultSecondarySet = Enumerable.Range(0, count);
            return newInput;
        }
        finally
        {
            syncContext.Release();
        }
    }

    private void ReceivePrimary(TPrimary message, Envelope e)
    {
        TPrimary clone = message.DeepClone(InPrimary.Recycler);
        _primaryQueue.Enqueue(Message.Create(clone, e));
        Publish();
    }

    private void ReceiveSecondary(int id, TSecondary message, Envelope e)
    {
        TSecondary clone = message.DeepClone(InSecondaries[id].Recycler);
        _secondaryQueues[id].Queue.Enqueue(Message.Create(clone, e));
        Publish();
    }

    private void SecondaryClosed(int index, DateTime closedOriginatingTime)
    {
        _secondaryQueues[index].ClosedOriginatingTime = closedOriginatingTime;
        Publish();
    }

    private void Publish()
    {
        while (_primaryQueue.Count > 0)
        {
            Message<TPrimary> primary = _primaryQueue.Peek();
            bool ready = true;
            IEnumerable<int> secondarySet = (_secondarySelector != null) ? _secondarySelector(primary.Data) : _defaultSecondarySet;
            foreach (int secondary in secondarySet)
            {
                (Queue<Message<TSecondary>> Queue, DateTime? ClosedOriginatingTime) secondaryQueue
                    = _secondaryQueues[secondary];
                InterpolationResult<TInterpolation> interpolationResult
                    = _interpolator.Interpolate(primary.OriginatingTime, secondaryQueue.Queue, secondaryQueue.ClosedOriginatingTime);
                if (interpolationResult.Type == InterpolationResultType.InsufficientData)
                {
                    // we need to wait longer
                    return;
                }

                _lastResults[secondary] = interpolationResult;
                _lastValues[secondary] = interpolationResult.Value;
                ready = ready && interpolationResult.Type == InterpolationResultType.Created;
            }

            // if all secondaries have an interpolated value, publish the resulting set
            if (ready)
            {
                // publish
                TOut result = _outputCreator(primary.Data, _lastValues);
                Out.Post(result, primary.OriginatingTime);
                Array.Clear(_lastValues, 0, _lastValues.Length);
            }

            // if we got here, all secondaries either successfully interpolated a value, or we have confirmation that they will never be able to interpolate
            foreach (int secondary in secondarySet)
            {
                (Queue<Message<TSecondary>> Queue, DateTime? ClosedOriginatingTime) secondaryQueue =
                    _secondaryQueues[secondary];

                // clear the secondary queue as needed
                while (secondaryQueue.Queue.Count != 0 && secondaryQueue.Queue.Peek().OriginatingTime < _lastResults[secondary].ObsoleteTime)
                {
                    InSecondaries[secondary].Recycle(secondaryQueue.Queue.Dequeue());
                }
            }

            Array.Clear(_lastResults, 0, _lastResults.Length);
            InPrimary.Recycle(primary);
            _primaryQueue.Dequeue();
        }
    }
}
