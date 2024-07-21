// <copyright file="RelativeIndexWindow.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Collections.Generic;
using System.Linq;


namespace Microsoft.Psi.Components;

/// <summary>
/// Implements an index-based windowing component.
/// </summary>
/// <typeparam name="TInput">The type of input messages.</typeparam>
/// <typeparam name="TOutput">The type of output messages.</typeparam>
public class RelativeIndexWindow<TInput, TOutput> : ConsumerProducer<TInput, TOutput>
{
    private readonly IRecyclingPool<Message<TInput>> _recycler = RecyclingPool.Create<Message<TInput>>();
    private readonly int _bufferSize;
    private readonly int _windowSize;
    private readonly int _trimLeft;
    private readonly int _trimRight;
    private readonly Func<IEnumerable<Message<TInput>>, TOutput> _selector;

    private readonly int anchorMessageIndex;
    private readonly Queue<Message<TInput>> buffer = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="RelativeIndexWindow{TInput, TOutput}"/> class.
    /// </summary>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="relativeIndexInterval">The relative index interval over which to gather messages.</param>
    /// <param name="selector">Select output message from collected window of input messages.</param>
    /// <param name="name">An optional name for the component.</param>
    public RelativeIndexWindow(Pipeline pipeline, IntInterval relativeIndexInterval, Func<IEnumerable<Message<TInput>>, TOutput> selector, string name = nameof(RelativeIndexWindow<TInput, TOutput>))
        : base(pipeline, name)
    {
        if (relativeIndexInterval.IsNegative)
        {
            // normalize to positive form
            relativeIndexInterval = new IntInterval(relativeIndexInterval.RightEndpoint, relativeIndexInterval.LeftEndpoint);
        }

        IntervalEndpoint<int> left = relativeIndexInterval.LeftEndpoint;
        IntervalEndpoint<int> right = relativeIndexInterval.RightEndpoint;
        _trimLeft = left.Point > 0 ? left.Point : 0;
        _trimRight = right.Point < 0 ? -right.Point : 0;
        _windowSize = relativeIndexInterval.Span + 1 - (left.Inclusive ? 0 : 1) - (right.Inclusive ? 0 : 1);
        _bufferSize = _windowSize + _trimLeft + _trimRight;
        anchorMessageIndex = left.Point == 0 ? 0 : Math.Abs(left.Point) - (left.Inclusive ? 0 : 1);
        _selector = selector;
    }

    /// <inheritdoc />
    protected override void Receive(TInput value, Envelope envelope)
    {
        buffer.Enqueue(new Message<TInput>(value, envelope).DeepClone(_recycler)); // clone and add the new message
        if (buffer.Count > _bufferSize)
        {
            Message<TInput> free = buffer.Dequeue();
            _recycler?.Recycle(free);
        }

        // emit buffers of windowSize (otherwise continue accumulating)
        if (buffer.Count == _bufferSize)
        {
            IEnumerable<Message<TInput>> messages = buffer.Skip(_trimLeft).Take(_windowSize);
            Out.Post(_selector(messages), buffer.Skip(anchorMessageIndex).First().OriginatingTime);
        }
    }
}
