// <copyright file="DynamicWindow.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Collections.Generic;
using System.Linq;


namespace Microsoft.Psi.Components;

/// <summary>
/// Component that implements a dynamic window stream operator.
/// </summary>
/// <typeparam name="TWindow">The type of messages on the window stream.</typeparam>
/// <typeparam name="TInput">The type of messages on the input stream.</typeparam>
/// <typeparam name="TOutput">The type of messages on the output stream.</typeparam>
/// <remarks>The component implements a dynamic window operator over a stream of data. Messages
/// on the incoming <see cref="WindowIn"/>stream are used to compute a relative time
/// interval over the in input stream. The output is created by a function that has access
/// to the window message and the computed buffer of messages on the input stream.</remarks>
public class DynamicWindow<TWindow, TInput, TOutput> : ConsumerProducer<TInput, TOutput>
{
    private readonly List<Message<TWindow>> _windowBuffer = [];
    private readonly List<Message<TInput>> _inputBuffer = [];
    private readonly Func<Message<TWindow>, (TimeInterval Window, DateTime ObsoleteTime)> _dynamicWindowFunction;
    private readonly Func<Message<TWindow>, IEnumerable<Message<TInput>>, TOutput> _outputCreator;

    private DateTime _minimumObsoleteTime = DateTime.MinValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="DynamicWindow{TWindow, TInput, TOutput}"/> class.
    /// </summary>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="windowCreator">The function that creates the actual window to use at every point, and specified the time point previous to which no future windows will extend.</param>
    /// <param name="outputCreator">A function that creates output messages given a message on the window-defining stream and a buffer of messages on the source stream.</param>
    /// <param name="name">An optional name for the component.</param>
    public DynamicWindow(
        Pipeline pipeline,
        Func<Message<TWindow>, (TimeInterval, DateTime)> windowCreator,
        Func<Message<TWindow>, IEnumerable<Message<TInput>>, TOutput> outputCreator,
        string name = nameof(DynamicWindow<TWindow, TInput, TOutput>))
        : base(pipeline, name)
    {
        _dynamicWindowFunction = windowCreator;
        _outputCreator = outputCreator;
        WindowIn = pipeline.CreateReceiver<TWindow>(this, ReceiveWindow, nameof(WindowIn));
        In.Unsubscribed += _ => Publish(true);
    }

    /// <summary>
    /// Gets the received for the input stream of window messages.
    /// </summary>
    public Receiver<TWindow> WindowIn { get; }

    /// <inheritdoc/>
    protected override void Receive(TInput data, Envelope envelope)
    {
        _inputBuffer.Add(Message.Create(data.DeepClone(In.Recycler), envelope));
        Publish(false);
    }

    private void ReceiveWindow(TWindow data, Envelope envelope)
    {
        _windowBuffer.Add(Message.Create(data.DeepClone(WindowIn.Recycler), envelope));
        Publish(false);
    }

    private void Publish(bool final)
    {
        while (TryPublish(final))
        {
        }
    }

    private bool TryPublish(bool final)
    {
        if (_windowBuffer.Count == 0)
        {
            return false;
        }

        (var timeInterval, var obsoleteTime) = _dynamicWindowFunction(_windowBuffer[0]);
        if (timeInterval.IsNegative)
        {
            throw new ArgumentException("Dynamic window must be a positive time interval.");
        }

        if (timeInterval.Left < _minimumObsoleteTime)
        {
            throw new ArgumentException("Dynamic window must not extend before previous obsolete time.");
        }

        if (!timeInterval.IsFinite)
        {
            throw new ArgumentException("Dynamic window must be finite (bounded at both ends).");
        }

        if (!final && (_inputBuffer.Count == 0 || _inputBuffer[_inputBuffer.Count - 1].OriginatingTime < timeInterval.RightEndpoint.Point))
        {
            return false;
        }

        // if we have enough data, find the index of where to start and where to end
        int startIndex = _inputBuffer.FindIndex(m => timeInterval.PointIsWithin(m.OriginatingTime));
        int endIndex = _inputBuffer.FindLastIndex(m => timeInterval.PointIsWithin(m.OriginatingTime));

        // if endIndex is -1 (all inputBuffer messages are after the time interval)
        if (endIndex == -1)
        {
            // then post an empty buffer
            PostAndClearObsoleteInputs(obsoleteTime, Enumerable.Empty<Message<TInput>>());
            return true;
        }
        else if (startIndex == -1)
        {
            // o/w if the startIndex is -1 (all inputBuffer messages are before the time interval)
            // we cannot post yet, we are still waiting for data messages in the temporal range of the
            // entity, so return false
            return false;
        }
        else if (endIndex >= startIndex)
        {
            // o/w if the endIndex is strictly larger than the start index, then we have some overlap
            PostAndClearObsoleteInputs(obsoleteTime, _inputBuffer.GetRange(startIndex, endIndex - startIndex + 1));
            return true;
        }
        else
        {
            // o/w if the endindex is strictly smaller than the startindex, that means the temporal interval
            // is caught in between the two different indices (endindex -> startindex)
            // in this case, we can post an empty buffer
            PostAndClearObsoleteInputs(obsoleteTime, Enumerable.Empty<Message<TInput>>());
            return true;
        }
    }

    private void PostAndClearObsoleteInputs(DateTime obsoleteTime, IEnumerable<Message<TInput>> inputs)
    {
        // check that obsolete times don't backtrack
        if (obsoleteTime < _minimumObsoleteTime)
        {
            throw new ArgumentException("Dynamic window with obsolete time prior to previous window.");
        }

        _minimumObsoleteTime = obsoleteTime;

        // post output
        Message<TWindow> sourceMessage = _windowBuffer[0];
        TOutput value = _outputCreator(sourceMessage, inputs);
        Out.Post(value, sourceMessage.OriginatingTime);

        // remove & recycle window and obsolete inputs
        _windowBuffer.RemoveAt(0);
        WindowIn.Recycler.Recycle(sourceMessage.Data);

        if (_inputBuffer.Any())
        {
            int obsoleteIndex = _inputBuffer.FindIndex(m => m.OriginatingTime >= obsoleteTime);

            // if the is no message larger than or equal to the obsolete time
            if (obsoleteIndex == -1)
            {
                // then all messages are obsolete
                obsoleteIndex = _inputBuffer.Count;
            }

            for (int i = 0; i < obsoleteIndex; i++)
            {
                In.Recycler.Recycle(_inputBuffer[i].Data);
            }

            _inputBuffer.RemoveRange(0, obsoleteIndex);
        }
    }
}
