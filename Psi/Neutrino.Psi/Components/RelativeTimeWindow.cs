// <copyright file="RelativeTimeWindow.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Collections.Generic;
using System.Linq;


namespace Neutrino.Psi.Components;

/// <summary>
/// Implements a time-based windowing component.
/// </summary>
/// <typeparam name="TInput">The type of input messages.</typeparam>
/// <typeparam name="TOutput">The type of output messages.</typeparam>
public class RelativeTimeWindow<TInput, TOutput> : ConsumerProducer<TInput, TOutput>
{
    private readonly IRecyclingPool<Message<TInput>> _recycler = RecyclingPool.Create<Message<TInput>>();
    private readonly RelativeTimeInterval _relativeTimeInterval;
    private readonly Func<IEnumerable<Message<TInput>>, TOutput> _selector;
    private readonly Queue<Message<TInput>> _buffer = new();

    private int anchorMessageSequenceId = -1;
    private DateTime anchorMessageOriginatingTime = DateTime.MinValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="RelativeTimeWindow{TInput, TOutput}"/> class.
    /// </summary>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="relativeTimeInterval">The relative time interval over which to gather messages.</param>
    /// <param name="selector">Select output message from collected window of input messages.</param>
    /// <param name="name">An optional name for the component.</param>
    public RelativeTimeWindow(Pipeline pipeline, RelativeTimeInterval relativeTimeInterval, Func<IEnumerable<Message<TInput>>, TOutput> selector, string name = nameof(RelativeTimeWindow<TInput, TOutput>))
        : base(pipeline, name)
    {
        _relativeTimeInterval = relativeTimeInterval;
        _selector = selector;
        In.Unsubscribed += _ => OnUnsubscribed();
    }

    /// <inheritdoc />
    protected override void Receive(TInput value, Envelope envelope)
    {
        _buffer.Enqueue(new Message<TInput>(value, envelope).DeepClone(_recycler));
        ProcessRemoval();
        ProcessWindow(_buffer, false, Out);
    }

    private bool RemoveCondition(Message<TInput> message)
    {
        return anchorMessageOriginatingTime > DateTime.MinValue && message.OriginatingTime < (anchorMessageOriginatingTime + _relativeTimeInterval).Left;
    }

    private void ProcessWindow(IEnumerable<Message<TInput>> messageList, bool final, Emitter<TOutput> emitter)
    {
        Message<TInput>[] messages = messageList.ToArray();
        int anchorMessageIndex = 0;
        if (anchorMessageSequenceId >= 0)
        {
            for (int i = 0; i < messages.Length; i++)
            {
                if (messages[i].Envelope.SequenceId == anchorMessageSequenceId)
                {
                    anchorMessageIndex = i + 1;
                    break;
                }
            }
        }

        if (anchorMessageIndex < messages.Length)
        {
            // compute the time interval from the next point we should output
            TimeInterval window = messages[anchorMessageIndex].OriginatingTime + _relativeTimeInterval;

            // decide whether we should output - only output when we have seen enough (or know that nothing further is to be seen - `final`).
            // evidence that nothing else will appear in the window
            bool shouldOutputNextMessage = final || messages.Last().OriginatingTime >= window.Right;

            if (shouldOutputNextMessage)
            {
                // compute the buffer to return
                TOutput ret = _selector(messages.Where(m => window.PointIsWithin(m.OriginatingTime)));

                // post it with the originating time of the anchor message
                emitter.Post(ret, messages[anchorMessageIndex].OriginatingTime);

                // set the sequence id for the last originating message that was posted
                anchorMessageSequenceId = messages[anchorMessageIndex].SequenceId;
                anchorMessageOriginatingTime = messages[anchorMessageIndex].OriginatingTime;
            }
        }
    }

    private void DequeueBuffer()
    {
        Message<TInput> free = _buffer.Dequeue();
        _recycler?.Recycle(free);
    }

    private bool CheckRemoval()
    {
        return _buffer.Count > 0 && RemoveCondition(_buffer.Peek());
    }

    private void ProcessRemoval()
    {
        while (CheckRemoval())
        {
            DequeueBuffer();
        }
    }

    private void OnUnsubscribed()
    {
        // give the processor an opportunity now with `final` flag set
        while (_buffer.Count > 0)
        {
            ProcessWindow(_buffer, true, Out);
            if (CheckRemoval())
            {
                ProcessRemoval();
                ProcessWindow(_buffer, true, Out);
            }

            if (_buffer.Count > 0)
            {
                // continue processing with trailing buffer
                DequeueBuffer();
            }
        }
    }
}
