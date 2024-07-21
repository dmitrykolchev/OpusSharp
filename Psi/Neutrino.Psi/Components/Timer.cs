// <copyright file="Timer.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;

namespace Neutrino.Psi.Components;

/// <summary>
/// A simple producer component that wakes up on a predefined interval and publishes a simple message.
/// This is useful for components that need to poll some resource. Such components can simply subscribe to this
/// clock component rather than registering a timer on their own.
/// </summary>
public abstract class Timer : ISourceComponent, IDisposable
{
    private readonly Pipeline _pipeline;
    private readonly string _name;

    /// <summary>
    /// The interval on which to publish messages.
    /// </summary>
    private readonly TimeSpan _timerInterval;

    /// <summary>
    /// Delegate we need to hold on to, so that it doesn't get garbage collected.
    /// </summary>
    private Time.TimerDelegate _timerDelegate;

    /// <summary>
    /// The id of the multimedia timer we use under the covers.
    /// </summary>
    private Platform.ITimer _timer;

    /// <summary>
    /// The start time of the timer.
    /// </summary>
    private DateTime _startTime;

    /// <summary>
    /// The end time of the timer.
    /// </summary>
    private DateTime _endTime;

    /// <summary>
    /// True if the timer is set.
    /// </summary>
    private bool _running;

    /// <summary>
    /// An action to call when done.
    /// </summary>
    private Action<DateTime> _notifyCompletionTime;

    /// <summary>
    /// Initializes a new instance of the <see cref="Timer"/> class.
    /// The timer fires off messages at the rate specified  by timerInterval.
    /// </summary>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="timerInterval">The timer firing interval, in ms.</param>
    /// <param name="name">An optional name for the component.</param>
    public Timer(Pipeline pipeline, uint timerInterval, string name = nameof(Timer))
    {
        _pipeline = pipeline;
        _name = name;
        _timerInterval = TimeSpan.FromMilliseconds(timerInterval);
    }

    /// <summary>
    /// Finalizes an instance of the <see cref="Timer"/> class.
    /// Releases the underlying unmanaged timer.
    /// </summary>
    ~Timer()
    {
        if (_running)
        {
            _timer.Stop();
        }
    }

    /// <summary>
    /// Called when the component is stopped.
    /// </summary>
    public void Dispose()
    {
        StopTimer();
    }

    /// <inheritdoc/>
    public void Start(Action<DateTime> notifyCompletionTime)
    {
        _notifyCompletionTime = notifyCompletionTime;

        _startTime = _pipeline.StartTime;
        _endTime = _pipeline.ReplayDescriptor.End;
        uint realTimeInterval = (uint)_pipeline.ConvertToRealTime(_timerInterval).TotalMilliseconds;
        _timerDelegate = new Time.TimerDelegate(PublishTime);
        _timer = Platform.Specific.TimerStart(realTimeInterval, _timerDelegate);
        _running = true;
    }

    /// <inheritdoc/>
    public void Stop(DateTime finalOriginatingTime, Action notifyCompleted)
    {
        StopTimer();
        notifyCompleted();
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return _name;
    }

    /// <summary>
    /// Called by the timer. Override to publish actual messages.
    /// </summary>
    /// <param name="absoluteTime">The current (virtual) time.</param>
    /// <param name="relativeTime">The time elapsed since the generator was started.</param>
    protected abstract void Generate(DateTime absoluteTime, TimeSpan relativeTime);

    /// <summary>
    /// Wakes up every timerInterval to publish a new message.
    /// </summary>
    /// <param name="timerID">The parameter is not used.</param>
    /// <param name="msg">The parameter is not used.</param>
    /// <param name="userCtx">The parameter is not used.</param>
    /// <param name="dw1">The parameter is not used.</param>
    /// <param name="dw2">The parameter is not used.</param>
    private void PublishTime(uint timerID, uint msg, UIntPtr userCtx, UIntPtr dw1, UIntPtr dw2)
    {
        DateTime now = _pipeline.GetCurrentTime();
        if (now >= _endTime)
        {
            StopTimer();
            _notifyCompletionTime(_endTime);
        }
        else
        {
            // publish a new message.
            Generate(now, now - _startTime);
        }
    }

    private void StopTimer()
    {
        if (_running)
        {
            _timer.Stop();
            _running = false;
        }

        GC.SuppressFinalize(this);
    }
}
