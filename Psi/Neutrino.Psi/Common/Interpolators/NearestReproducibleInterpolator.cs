// <copyright file="NearestReproducibleInterpolator.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Collections.Generic;
using System.Linq;

namespace Neutrino.Psi.Common.Interpolators;

/// <summary>
/// Implements a reproducible interpolator that selects the nearest value from a specified window.
/// </summary>
/// <typeparam name="T">The type of messages.</typeparam>
/// <remarks>The interpolator results do not depend on the wall-clock time of the messages arriving
/// on the secondary stream, i.e., they are based on originating times of messages. As a result,
/// the interpolator might introduce an extra delay as it might have to wait for enough messages on the
/// secondary stream to prove that the interpolation result is correct, irrespective of any other messages
/// that might arrive later.</remarks>
public sealed class NearestReproducibleInterpolator<T> : ReproducibleInterpolator<T>
{
    private readonly RelativeTimeInterval _relativeTimeInterval;
    private readonly bool _orDefault;
    private readonly T _defaultValue;
    private readonly string _name;

    /// <summary>
    /// Initializes a new instance of the <see cref="NearestReproducibleInterpolator{T}"/> class.
    /// </summary>
    /// <param name="relativeTimeInterval">The relative time interval within which to search for the first message.</param>
    /// <param name="orDefault">Indicates whether to output a default value when no result is found.</param>
    /// <param name="defaultValue">An optional default value to use.</param>
    public NearestReproducibleInterpolator(RelativeTimeInterval relativeTimeInterval, bool orDefault, T defaultValue = default)
    {
        _relativeTimeInterval = relativeTimeInterval;
        _orDefault = orDefault;
        _defaultValue = defaultValue;

        if (_relativeTimeInterval == RelativeTimeInterval.Zero)
        {
            _name = _orDefault ? $"{nameof(Reproducible)}.{nameof(Reproducible.ExactOrDefault)}" : $"{nameof(Reproducible)}.{nameof(Reproducible.Exact)}";
        }
        else
        {
            _name =
                (_orDefault ? $"{nameof(Reproducible)}.{nameof(Reproducible.NearestOrDefault)}" : $"{nameof(Reproducible)}.{nameof(Reproducible.Nearest)}") +
                _relativeTimeInterval.ToString();
        }
    }

    /// <inheritdoc/>
    public override InterpolationResult<T> Interpolate(DateTime interpolationTime, IEnumerable<Message<T>> messages, DateTime? closedOriginatingTime)
    {
        int count = messages.Count();

        // If no messages available
        if (count == 0)
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

        // Look for the nearest match
        Message<T> nearestMatch = default;
        TimeSpan minDistance = TimeSpan.MaxValue;
        DateTime upperBound = (_relativeTimeInterval.Right < TimeSpan.Zero) ? interpolationTime.BoundedAdd(_relativeTimeInterval.Right) : interpolationTime;

        int i = 0;
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

                TimeSpan distance = delta.Duration();

                // We stop searching either when we reach a message that is beyond the window end
                // when the distance (absolute delta) exceeds the minimum distance.
                if (messageIsOutsideWindowEnd || (distance > minDistance))
                {
                    break;
                }

                // keep track of the nearest match so far and its delta
                nearestMatch = message;
                minDistance = distance;
            }

            i++;
        }

        // If minDistance is anything other than MaxValue, we found a nearest matching message.
        if (minDistance < TimeSpan.MaxValue)
        {
            // Check if we need to satisfy additional conditions

            // If the nearest match is the last available message and the stream has not closed
            if ((i == count) && !closedOriginatingTime.HasValue)
            {
                // Then other messages might arrive that might constitute an even better match.
                // We need to guarantee that nearestMatch is indeed provably the nearest match. If it has an
                // originating time that occurs at or after the interpolation time (or the
                // upper boundary of the window, whichever occurs earlier in time), then this
                // must be true as we will never see a closer match in any of the messages
                // that may arrive in the future (if the stream was closed then we know that no
                // messages may arrive in the future). However if it is before the interpolation time,
                // then we will need to see a message beyond the match/window time to
                // be sure that there is no closer match (i.e. as long as we haven't seen a
                // message at or past the match/window time, it is always possible that
                // a future message will show up with a distance that is closer to the
                // interpolation time.)
                if (nearestMatch.OriginatingTime < upperBound)
                {
                    // Signal insufficient data to continue waiting for more messages.
                    return InterpolationResult<T>.InsufficientData();
                }
            }

            // Return the matching message value as the matched result.
            // All messages strictly before the matching message are obsolete.
            return InterpolationResult<T>.Create(nearestMatch.Data, nearestMatch.OriginatingTime);
        }

        // If we arrive here, it means no suitable match was found among the messages.
        // If the stream is closed, or if the last message is at or past the upper search bound
        // then it is the case that no future messages will be closer.
        if (closedOriginatingTime.HasValue || messages.Last().OriginatingTime >= upperBound)
        {
            // Then, no matched value exists at that time, and we either return DoesNotExist or
            // the default value (according to the parameter)
            return _orDefault ?
                InterpolationResult<T>.Create(_defaultValue, upperBound) :
                InterpolationResult<T>.DoesNotExist(upperBound);
        }
        else
        {
            // Otherwise a better future message might arrive, therefore we signal insufficient data.
            return InterpolationResult<T>.InsufficientData();
        }
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return _name;
    }
}
