// <copyright file="Zip.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Collections.Generic;
using System.Linq;


namespace Neutrino.Psi.Components;

/// <summary>
/// Zip one or more streams (T) into a single stream while ensuring delivery in originating time order.
/// </summary>
/// <remarks>Messages are produced in originating-time order; potentially delayed in wall-clock time.
/// If multiple messages arrive with the same originating time, they are added in the output array in
/// the order of stream ids.</remarks>
/// <typeparam name="T">The type of the messages.</typeparam>
public class Zip<T> : IProducer<T[]>
{
    private readonly Pipeline _pipeline;
    private readonly string _name;
    private readonly IList<Receiver<T>> _inputs = new List<Receiver<T>>();
    private readonly IList<(T data, Envelope envelope, IRecyclingPool<T> recycler)> _buffer = new List<(T, Envelope, IRecyclingPool<T>)>();

    /// <summary>
    /// Initializes a new instance of the <see cref="Zip{T}"/> class.
    /// </summary>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="name">An optional name for this component.</param>
    public Zip(Pipeline pipeline, string name = nameof(Zip<T>))
    {
        _pipeline = pipeline;
        _name = name;
        Out = pipeline.CreateEmitter<T[]>(this, nameof(Out));
    }

    /// <summary>
    /// Gets the output emitter.
    /// </summary>
    public Emitter<T[]> Out { get; }

    /// <summary>
    /// Add input receiver.
    /// </summary>
    /// <param name="name">The unique debug name of the receiver.</param>
    /// <returns>Receiver.</returns>
    public Receiver<T> AddInput(string name)
    {
        Scheduling.SynchronizationLock syncContext = Out.SyncContext; // protect collections from concurrent access
        syncContext.Lock();

        try
        {
            Receiver<T> receiver = null; // captured in receiver action closure
            receiver = _pipeline.CreateReceiver<T>(this, (m, e) => Receive(m, e, receiver.Recycler), name);
            receiver.Unsubscribed += _ => Publish();
            _inputs.Add(receiver);
            return receiver;
        }
        finally
        {
            syncContext.Release();
        }
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return _name;
    }

    private void Receive(T data, Envelope envelope, IRecyclingPool<T> recycler)
    {
        T clonedData = data.DeepClone(recycler);
        _buffer.Add((data: clonedData, envelope, recycler));
        Publish();
    }

    private void Publish()
    {
        // find out the earliest last originating time across inputs
        DateTime frontier = _inputs.Min(i => i.LastEnvelope.OriginatingTime);

        // get the groups of messages ready to be published
        IEnumerable<IGrouping<DateTime, (T data, Envelope envelope, IRecyclingPool<T> recycler)>> eligible = _buffer
            .Where(m => m.envelope.OriginatingTime <= frontier)
            .OrderBy(m => m.envelope.OriginatingTime)
            .ThenBy(m => m.envelope.SourceId)
            .GroupBy(m => m.envelope.OriginatingTime);

        foreach (IGrouping<DateTime, (T data, Envelope envelope, IRecyclingPool<T> recycler)> group in eligible.ToArray())
        {
            Out.Post(group.Select(t => t.data).ToArray(), group.Key);

            foreach ((T data, Envelope envelope, IRecyclingPool<T> recycler) in group)
            {
                _buffer.Remove((data, envelope, recycler));
                recycler.Recycle(data);
            }
        }
    }
}
