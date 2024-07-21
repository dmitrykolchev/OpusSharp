// <copyright file="ReplayDescriptor.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;


namespace Microsoft.Psi;

/// <summary>
/// Descriptor for pipeline replay.
/// </summary>
public sealed class ReplayDescriptor
{
    /// <summary>
    /// Replay all messages (not in real time, disregarding originating time and not enforcing replay clock).
    /// </summary>
    public static readonly ReplayDescriptor ReplayAll = new(TimeInterval.Infinite, false);

    /// <summary>
    /// Replay all messages in real time (preserving originating time and enforcing replay clock).
    /// </summary>
    public static readonly ReplayDescriptor ReplayAllRealTime = new(TimeInterval.Infinite, true);

    private readonly TimeInterval _interval;
    private readonly bool _enforceReplayClock;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReplayDescriptor"/> class.
    /// </summary>
    /// <param name="start">Starting message time.</param>
    /// <param name="end">Ending message time.</param>
    /// <param name="enforceReplayClock">Whether to enforce replay clock.</param>
    public ReplayDescriptor(DateTime start, DateTime end, bool enforceReplayClock = true)
        : this(new TimeInterval(start, end), enforceReplayClock)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ReplayDescriptor"/> class.
    /// </summary>
    /// <remarks>No ending message time (infinite).</remarks>
    /// <param name="start">Starting message time.</param>
    /// <param name="enforceReplayClock">Whether to enforce replay clock (optional).</param>
    public ReplayDescriptor(DateTime start, bool enforceReplayClock = true)
        : this(new TimeInterval(start, DateTime.MaxValue), enforceReplayClock)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ReplayDescriptor"/> class.
    /// </summary>
    /// <param name="interval">Time interval to replay.</param>
    /// <param name="enforceReplayClock">Whether to enforce replay clock.</param>
    public ReplayDescriptor(TimeInterval interval, bool enforceReplayClock = true)
    {
        _interval = interval ?? TimeInterval.Infinite;
        _enforceReplayClock = enforceReplayClock;
    }

    /// <summary>
    /// Gets time interval to replay.
    /// </summary>
    public TimeInterval Interval => _interval;

    /// <summary>
    /// Gets starting message time.
    /// </summary>
    public DateTime Start => Interval.Left;

    /// <summary>
    /// Gets ending message time.
    /// </summary>
    public DateTime End => Interval.Right;

    /// <summary>
    /// Gets a value indicating whether to enforce replay clock.
    /// </summary>
    public bool EnforceReplayClock => _enforceReplayClock;

    /// <summary>
    /// Reduce this replay descriptor to that which intersects the given time interval.
    /// </summary>
    /// <param name="interval">Intersecting time interval.</param>
    /// <returns>Reduced replay descriptor.</returns>
    public ReplayDescriptor Intersect(TimeInterval interval)
    {
        if (interval == null)
        {
            return this;
        }

        DateTime start = new(Math.Max(_interval.Left.Ticks, interval.Left.Ticks));
        DateTime end = new(Math.Min(_interval.Right.Ticks, interval.Right.Ticks));
        if (end < start)
        {
            end = start;
        }

        return new ReplayDescriptor(start, end, _enforceReplayClock);
    }
}
