// <copyright file="LastAvailableInterpolator.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Collections.Generic;
using System.Linq;
using Neutrino.Psi.Common.Intervals;

namespace Neutrino.Psi.Common.Interpolators;

/// <summary>
/// Implements a greedy interpolator that selects the last value from a specified window. The
/// interpolator only considers messages available in the window on the secondary stream at
/// the moment the primary stream message arrives. As such, it belongs to the class of greedy
/// interpolators and does not guarantee reproducible results.
/// </summary>
/// <typeparam name="T">The type of messages.</typeparam>
public sealed class LastAvailableInterpolator<T> : GreedyInterpolator<T>
{
    private readonly RelativeTimeInterval _relativeTimeInterval;
    private readonly bool _orDefault;
    private readonly T _defaultValue;
    private readonly string _name;

    /// <summary>
    /// Initializes a new instance of the <see cref="LastAvailableInterpolator{T}"/> class.
    /// </summary>
    /// <param name="relativeTimeInterval">The relative time interval within which to search for the first message.</param>
    /// <param name="orDefault">Indicates whether to output a default value when no result is found.</param>
    /// <param name="defaultValue">An optional default value to use.</param>
    public LastAvailableInterpolator(RelativeTimeInterval relativeTimeInterval, bool orDefault, T defaultValue = default)
    {
        _relativeTimeInterval = relativeTimeInterval;
        _orDefault = orDefault;
        _defaultValue = defaultValue;
        _name =
            (_orDefault ? $"{nameof(Available)}.{nameof(Available.LastOrDefault)}" : $"{nameof(Available)}.{nameof(Available.Last)}") +
            _relativeTimeInterval.ToString();
    }

    /// <inheritdoc/>
    public override InterpolationResult<T> Interpolate(DateTime interpolationTime, IEnumerable<Message<T>> messages, DateTime? closedOriginatingTime)
    {
        // If no messages available,
        if (messages.Count() == 0)
        {
            // Then depending on orDefault, either create a default value or return does not exist.
            return _orDefault ?
                InterpolationResult<T>.Create(_defaultValue, DateTime.MinValue) :
                InterpolationResult<T>.DoesNotExist(DateTime.MinValue);
        }

        Message<T> lastMessage = default;
        bool found = false;

        foreach (Message<T> message in messages)
        {
            TimeSpan delta = message.OriginatingTime - interpolationTime;

            // Determine if the message is on the right side of the window start
            bool messageIsAfterWindowStart = _relativeTimeInterval.LeftEndpoint.Inclusive ? delta >= _relativeTimeInterval.Left : delta > _relativeTimeInterval.Left;

            // Only consider messages that occur within the lookback window.
            if (messageIsAfterWindowStart)
            {
                // Determine if the message is outside the window end
                bool messageIsOutsideWindowEnd = _relativeTimeInterval.RightEndpoint.Inclusive ? delta > _relativeTimeInterval.Right : delta >= _relativeTimeInterval.Right;

                // We stop searching either when we reach a message that is beyond the lookahead window
                if (messageIsOutsideWindowEnd)
                {
                    break;
                }

                // keep track of message and keep going
                lastMessage = message;
                found = true;
            }
        }

        // If we found a last message in the window
        if (found)
        {
            // Return the matching message value as the matched result.
            // All messages before the matching message are obsolete.
            return InterpolationResult<T>.Create(lastMessage.Data, lastMessage.OriginatingTime);
        }
        else
        {
            // o/w, that means no match was found, which implies there was no message in the
            // window. In that case, either return DoesNotExist or the default value.
            DateTime windowLeft = interpolationTime.BoundedAdd(_relativeTimeInterval.Left);
            return _orDefault ?
                InterpolationResult<T>.Create(_defaultValue, windowLeft) :
                InterpolationResult<T>.DoesNotExist(windowLeft);
        }
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return _name;
    }
}
