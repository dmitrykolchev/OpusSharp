// <copyright file="AdjacentValuesInterpolator{TIn,TResult}.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Collections.Generic;
using System.Linq;


namespace Microsoft.Psi.Common.Interpolators;

/// <summary>
/// Implements an interpolator based on the values adjacent to the interpolation time, i.e. the
/// nearest values before and after the interpolation time.
/// </summary>
/// <typeparam name="TIn">The type of the messages to interpolate.</typeparam>
/// <typeparam name="TResult">The type of the output interpolation result.</typeparam>
/// <remarks>The interpolator results do not depend on the wall-clock time of the messages arriving
/// on the secondary stream, i.e., they are based on originating times of messages. As a result,
/// the interpolator might introduce an extra delay as it might have to wait for enough messages on the
/// secondary stream to prove that the interpolation result is correct, irrespective of any other messages
/// that might arrive later.</remarks>
public class AdjacentValuesInterpolator<TIn, TResult> : ReproducibleInterpolator<TIn, TResult>
{
    private readonly Func<TIn, TIn, double, TResult> _interpolatorFunc;
    private readonly bool _orDefault;
    private readonly TResult _defaultValue;
    private readonly TimeSpan _maxSpan;
    private readonly string _name;

    /// <summary>
    /// Initializes a new instance of the <see cref="AdjacentValuesInterpolator{TIn, TOut}"/> class.
    /// </summary>
    /// <param name="interpolatorFunc">A function which produces an interpolation result, given the two nearest values
    /// and the ratio between them.</param>
    /// <param name="orDefault">Indicates whether to output a default value when no result is found.</param>
    /// <param name="defaultValue">An optional default value to use.</param>
    /// <param name="maxSpan">The maximal timespan between adjacent messages for which interpolation will be run.</param>
    /// <param name="name">An optional name for the interpolator (defaults to AdjacentValues).</param>
    public AdjacentValuesInterpolator(Func<TIn, TIn, double, TResult> interpolatorFunc, bool orDefault, TResult defaultValue = default, TimeSpan? maxSpan = null, string name = null)
    {
        _interpolatorFunc = interpolatorFunc;
        _orDefault = orDefault;
        _defaultValue = defaultValue;
        _maxSpan = maxSpan ?? TimeSpan.MaxValue;

        name ??= "AdjacentValues";
        _name = _orDefault ? $"{nameof(Reproducible)}.{name}OrDefault" : $"{nameof(Reproducible)}.{name}";
    }

    /// <inheritdoc/>
    public override InterpolationResult<TResult> Interpolate(DateTime interpolationTime, IEnumerable<Message<TIn>> messages, DateTime? closedOriginatingTime)
    {
        // If no messages available,
        if (!messages.Any())
        {
            // If stream is closed,
            if (closedOriginatingTime.HasValue)
            {
                // Then no other value or better match will appear, so depending on orDefault,
                // either create a default value or return does not exist.
                return _orDefault ?
                    InterpolationResult<TResult>.Create(_defaultValue, DateTime.MinValue) :
                    InterpolationResult<TResult>.DoesNotExist(DateTime.MinValue);
            }
            else
            {
                // otherwise if the stream is not closed yet, insufficient data
                return InterpolationResult<TResult>.InsufficientData();
            }
        }

        Message<TIn> lastMessage = default;

        foreach (Message<TIn> message in messages)
        {
            // If we have a message past or at the interpolation time
            if (message.OriginatingTime >= interpolationTime)
            {
                // Then if we have a previous message
                if (lastMessage != default)
                {
                    // If the time between these messages is less than maxSpan
                    if (message.OriginatingTime - lastMessage.OriginatingTime < _maxSpan)
                    {
                        // Then interpolate and return the result
                        double ratio = (interpolationTime - lastMessage.OriginatingTime).Ticks / (double)(message.OriginatingTime - lastMessage.OriginatingTime).Ticks;
                        return InterpolationResult<TResult>.Create(
                            _interpolatorFunc(lastMessage.Data, message.Data, ratio),
                            message.OriginatingTime == interpolationTime ? message.OriginatingTime : lastMessage.OriginatingTime);
                    }
                    else
                    {
                        // O/w we cannot interpolate at that location, so depending on orDefault,
                        // either create a default value or return does not exist.
                        return _orDefault ?
                            InterpolationResult<TResult>.Create(_defaultValue, DateTime.MinValue) :
                            InterpolationResult<TResult>.DoesNotExist(DateTime.MinValue);
                    }
                }
                else if (message.OriginatingTime == interpolationTime)
                {
                    // o/w if the message is right at the interpolation time, we don't need the previous message
                    return InterpolationResult<TResult>.Create(_interpolatorFunc(default, message.Data, 1), message.OriginatingTime);
                }
                else
                {
                    // o/w since we have no previous message, depending on orDefault,
                    // either create a default value or return does not exist.
                    return _orDefault ?
                        InterpolationResult<TResult>.Create(_defaultValue, DateTime.MinValue) :
                        InterpolationResult<TResult>.DoesNotExist(DateTime.MinValue);
                }
            }

            lastMessage = message;
        }

        // If we are here, that means we have not seen enough data to create an interpolation result.
        // If the stream has closed
        if (closedOriginatingTime.HasValue)
        {
            // Then we will never get enough data to interpolate, so depending on orDefault
            // either create a default value or return does not exist.
            return _orDefault ?
                InterpolationResult<TResult>.Create(_defaultValue, DateTime.MinValue) :
                InterpolationResult<TResult>.DoesNotExist(lastMessage != default ? lastMessage.OriginatingTime : DateTime.MinValue);
        }
        else
        {
            // O/w we might get more data so simply wait
            return InterpolationResult<TResult>.InsufficientData();
        }
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return _name;
    }
}
