// <copyright file="Clock.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;

namespace Neutrino.Psi;


/// <summary>
/// Represents virtual time.
/// </summary>
public class Clock
{
    private readonly TimeSpan _offsetInRealTime;
    private readonly DateTime _originInRealTime;
    private readonly double _virtualTimeDilateFactor;
    private readonly double _virtualTimeDilateFactorInverse;

    /// <summary>
    /// Initializes a new instance of the <see cref="Clock"/> class.
    /// </summary>
    /// <param name="virtualTimeOffset">The delta between virtual time and real time. A negative value will result in times in the past, a positive value will result in times in the future.</param>
    /// <param name="timeDilationFactor">if set to a value greater than 1, virtual time passes faster than real time by this factor.</param>
    public Clock(TimeSpan virtualTimeOffset = default(TimeSpan), float timeDilationFactor = 1)
    {
        _offsetInRealTime = virtualTimeOffset;
        _originInRealTime = Time.GetCurrentTime();
        _virtualTimeDilateFactorInverse = timeDilationFactor;
        _virtualTimeDilateFactor = (timeDilationFactor == 0) ? 0 : 1.0 / timeDilationFactor;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Clock"/> class.
    /// </summary>
    /// <param name="virtualNow">The desired current virtual time.</param>
    /// <param name="replaySpeedFactor">if set to a value greater than 1, virtual time passes faster than real time by this factor.</param>
    public Clock(DateTime virtualNow, float replaySpeedFactor = 1)
    {
        DateTime now = Time.GetCurrentTime();
        _offsetInRealTime = virtualNow - now;
        _originInRealTime = now;
        _virtualTimeDilateFactorInverse = replaySpeedFactor;
        _virtualTimeDilateFactor = (replaySpeedFactor == 0) ? 0 : 1.0 / replaySpeedFactor;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Clock"/> class.
    /// </summary>
    /// <param name="clock">Clock from which to take parameters.</param>
    internal Clock(Clock clock)
    {
        _offsetInRealTime = clock._offsetInRealTime;
        _originInRealTime = clock._originInRealTime;
        _virtualTimeDilateFactorInverse = clock._virtualTimeDilateFactorInverse;
        _virtualTimeDilateFactor = clock._virtualTimeDilateFactor;
    }

    /// <summary>
    /// Gets the origin in real time.
    /// </summary>
    public DateTime RealTimeOrigin => _originInRealTime;

    /// <summary>
    /// Gets the offset origin in real time.
    /// </summary>
    public DateTime Origin => _originInRealTime + _offsetInRealTime;

    /// <summary>
    /// Returns the virtual time with high resolution (1us), in the virtual time frame of reference.
    /// </summary>
    /// <returns>The current time in the adjusted frame of reference.</returns>
    public DateTime GetCurrentTime()
    {
        return ToVirtualTime(Time.GetCurrentTime());
    }

    /// <summary>
    /// Returns the absolute time represented by the number of 100ns ticks from system boot.
    /// </summary>
    /// <param name="ticksFromSystemBoot">The number of 100ns ticks since system boot.</param>
    /// <returns>The absolute time.</returns>
    public DateTime GetTimeFromElapsedTicks(long ticksFromSystemBoot)
    {
        return ToVirtualTime(Time.GetTimeFromElapsedTicks(ticksFromSystemBoot));
    }

    /// <summary>
    /// Returns the virtual time, given the current time mapping.
    /// </summary>
    /// <param name="realTime">A time in the real time frame.</param>
    /// <returns>The corresponding time in the adjusted frame of reference.</returns>
    public DateTime ToVirtualTime(DateTime realTime)
    {
        return _originInRealTime + ToVirtualTime(realTime - _originInRealTime) + _offsetInRealTime;
    }

    /// <summary>
    /// Returns the virtual time span, given a real time span.
    /// </summary>
    /// <param name="realTimeInterval">Real time span.</param>
    /// <returns>Virtual time span.</returns>
    public TimeSpan ToVirtualTime(TimeSpan realTimeInterval)
    {
        return new TimeSpan((long)(realTimeInterval.Ticks * _virtualTimeDilateFactor));
    }

    /// <summary>
    /// Returns the real time corresponding to the virtual time, given the current time mapping.
    /// </summary>
    /// <param name="virtualTime">A time in the virtual time frame.</param>
    /// <returns>The corresponding time in the real time frame of reference.</returns>
    public DateTime ToRealTime(DateTime virtualTime)
    {
        return _originInRealTime + ToRealTime(virtualTime - _originInRealTime - _offsetInRealTime);
    }

    /// <summary>
    /// Returns the real time span, given a virtual time span.
    /// </summary>
    /// <param name="virtualTimeInterval">Virtual time span.</param>
    /// <returns>Real time span.</returns>
    public TimeSpan ToRealTime(TimeSpan virtualTimeInterval)
    {
        return new TimeSpan((long)(virtualTimeInterval.Ticks * _virtualTimeDilateFactorInverse));
    }
}
