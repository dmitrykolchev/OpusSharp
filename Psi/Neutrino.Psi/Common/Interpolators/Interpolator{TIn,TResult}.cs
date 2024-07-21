// <copyright file="Interpolator{TIn,TResult}.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Collections.Generic;


namespace Microsoft.Psi;

/// <summary>
/// Defines a base abstract class for stream interpolators.
/// </summary>
/// <typeparam name="TIn">The type of the input messages.</typeparam>
/// <typeparam name="TResult">The type of the interpolation result.</typeparam>
public abstract class Interpolator<TIn, TResult>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Interpolator{TIn, TOut}"/> class.
    /// </summary>
    public Interpolator()
    {
    }

    /// <summary>
    /// Interpolates a set of messages at a given time.
    /// </summary>
    /// <param name="interpolationTime">The time to interpolate.</param>
    /// <param name="messages">The set of messages from a stream.</param>
    /// <param name="closedOriginatingTime">An optional date-time that, when present, indicates at what time the stream was closed.</param>
    /// <returns>An interpolation result <see cref="InterpolationResult{T}"/>.</returns>
    public abstract InterpolationResult<TResult> Interpolate(DateTime interpolationTime, IEnumerable<Message<TIn>> messages, DateTime? closedOriginatingTime);
}
