// <copyright file="LastReproducibleInterpolator.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Collections.Generic;
using System.Linq;
using Neutrino.Psi.Common.Intervals;

namespace Neutrino.Psi.Common.Interpolators;


/// <summary>
/// Implements a reproducible interpolator that selects the last value from a specified window.
/// </summary>
/// <typeparam name="T">The type of messages.</typeparam>
/// <remarks>The interpolator results do not depend on the wall-clock time of the messages arriving
/// on the secondary stream, i.e., they are based on originating times of messages. As a result,
/// the interpolator might introduce an extra delay as it might have to wait for enough messages on the
/// secondary stream to prove that the interpolation result is correct, irrespective of any other messages
/// that might arrive later.</remarks>
public sealed class LastReproducibleInterpolator<T> : ReproducibleInterpolator<T>
{
    private readonly RelativeTimeInterval _relativeTimeInterval;
    private readonly bool _orDefault;
    private readonly T _defaultValue;
    private readonly string _name;

    /// <summary>
    /// Initializes a new instance of the <see cref="LastReproducibleInterpolator{T}"/> class.
    /// </summary>
    /// <param name="relativeTimeInterval">The relative time interval within which to search for the first message.</param>
    /// <param name="orDefault">Indicates whether to output a default value when no result is found.</param>
    /// <param name="defaultValue">An optional default value to use.</param>
    public LastReproducibleInterpolator(RelativeTimeInterval relativeTimeInterval, bool orDefault, T defaultValue = default)
    {
        _relativeTimeInterval = relativeTimeInterval;
        _orDefault = orDefault;
        _defaultValue = defaultValue;
        _name =
            (_orDefault ? $"{nameof(Reproducible)}.{nameof(Available.LastOrDefault)}" : $"{nameof(Reproducible)}.{nameof(Available.Last)}") +
            _relativeTimeInterval.ToString();
    }

    /// <inheritdoc/>
    public override InterpolationResult<T> Interpolate(DateTime interpolationTime, IEnumerable<Message<T>> messages, DateTime? closedOriginatingTime)
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
                    InterpolationResult<T>.Create(_defaultValue, DateTime.MinValue) :
                    InterpolationResult<T>.DoesNotExist(DateTime.MinValue);
            }
            else
            {
                // otherwise if the stream is not closed yet, insufficient data
                return InterpolationResult<T>.InsufficientData();
            }
        }

        // Look for the last match that's still within the window
        Message<T> lastMatchingMessage = default;
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

                // We stop searching when we reach a message that is beyond the window end
                if (messageIsOutsideWindowEnd)
                {
                    break;
                }

                // keep track of the best match so far and its delta
                lastMatchingMessage = message;
                found = true;
            }
        }

        // Compute whether the last available message is beyond the lookup window
        TimeSpan lastMessageDelta = messages.Last().OriginatingTime - interpolationTime;
        bool lastMessageIsOutsideWindowEnd = _relativeTimeInterval.RightEndpoint.Inclusive ? lastMessageDelta > _relativeTimeInterval.Right : lastMessageDelta >= _relativeTimeInterval.Right;

        // If we found a last message in the lookup window
        if (found)
        {
            // Then we need to make sure it is indeed provably the last message we will see in
            // that window. For this to be the case, either the stream has to be closed, or the
            // last matching message has to be on the right end of an inclusive window, or the
            // last message we have available must be beyond the right end of the lookup window.
            bool provablyLast =
                closedOriginatingTime.HasValue ||
                (_relativeTimeInterval.RightEndpoint.Inclusive && (lastMatchingMessage.OriginatingTime - interpolationTime == _relativeTimeInterval.Right)) ||
                lastMessageIsOutsideWindowEnd;

            // If we can prove this is indeed the last message
            if (provablyLast)
            {
                // Return the matching message value as the matched result.
                // All messages strictly before the matching message are obsolete.
                return InterpolationResult<T>.Create(lastMatchingMessage.Data, lastMatchingMessage.OriginatingTime);
            }
            else
            {
                // O/w return insufficient data.
                return InterpolationResult<T>.InsufficientData();
            }
        }

        // If we arrive here, it means we did not find a last message in the lookback window,
        // which also means there is no message in the lookback window.

        // If the stream was closed or last message is at or past the upper search bound,
        if (closedOriginatingTime.HasValue || lastMessageIsOutsideWindowEnd)
        {
            // Then no future messages will match better, so return DoesNotExist or the default value
            // (according to the parameter)
            DateTime windowLeftDateTime = interpolationTime.BoundedAdd(_relativeTimeInterval.Left);
            return _orDefault ?
                InterpolationResult<T>.Create(_defaultValue, windowLeftDateTime) :
                InterpolationResult<T>.DoesNotExist(windowLeftDateTime);
        }
        else
        {
            // Otherwise signal insufficient data.
            return InterpolationResult<T>.InsufficientData();
        }
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return _name;
    }
}
