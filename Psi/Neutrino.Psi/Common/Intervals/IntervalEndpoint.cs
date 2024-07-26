// <copyright file="IntervalEndpoint.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

namespace Neutrino.Psi.Common.Intervals;

/// <summary>
/// Represents a bounded/unbounded, inclusive/exclusive interval endpoint value.
/// </summary>
/// <typeparam name="TPoint">Type of point value.</typeparam>
public class IntervalEndpoint<TPoint> : IIntervalEndpoint<TPoint>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IntervalEndpoint{TPoint}"/> class.
    /// </summary>(arbitrary)
    /// <remarks>This is a bounded instance.</remarks>
    /// <param name="point">Point value.</param>
    /// <param name="inclusive">Whether the point itself is included.</param>
    public IntervalEndpoint(TPoint point, bool inclusive)
    {
        Bounded = true;
        Point = point;
        Inclusive = inclusive;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IntervalEndpoint{TPoint}"/> class.
    /// </summary>
    /// <remarks>This is an unbounded instance with a min/max point value.</remarks>
    /// <param name="minOrMax">Min/max point value (e.g. DateTime.MinValue, double.MinValue, ...)</param>
    public IntervalEndpoint(TPoint minOrMax)
        : this(minOrMax, false)
    {
        Bounded = false;
    }

    /// <summary>
    /// Gets a value indicating whether the endpoint is bounded.
    /// </summary>
    public bool Bounded
    {
        get; private set;
    }

    /// <summary>
    /// Gets a value indicating whether the endpoint is inclusive.
    /// </summary>
    public bool Inclusive
    {
        get; private set;
    }

    /// <summary>
    /// Gets the point value.
    /// </summary>
    public TPoint Point
    {
        get; private set;
    }
}
