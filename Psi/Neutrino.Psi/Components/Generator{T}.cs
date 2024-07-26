// <copyright file="Generator{T}.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Collections;
using System.Collections.Generic;
using Neutrino.Psi.Common;
using Neutrino.Psi.Common.Intervals;
using Neutrino.Psi.Executive;
using Neutrino.Psi.Streams;

namespace Neutrino.Psi.Components;


/// <summary>
/// Generates messages by lazily enumerating a sequence of data,
/// at the pace dictated by the pipeline.
/// </summary>
/// <typeparam name="T">The output type.</typeparam>
/// <remarks>
/// The static functions provided by the <see cref="Generators"/> wrap <see cref="Generator{T}"/>
/// and are designed to make the common cases easier.
/// </remarks>
public class Generator<T> : Generator, IProducer<T>, IDisposable
{
    private readonly Enumerator _enumerator;

    /// <summary>
    /// Initializes a new instance of the <see cref="Generator{T}"/> class.
    /// </summary>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="enumerator">A lazy enumerator of data.</param>
    /// <param name="interval">The interval used to increment time on each generated message.</param>
    /// <param name="alignDateTime">If non-null, this parameter specifies a time to align the generator messages with. If the parameter
    /// is non-null, the messages will have originating times that align with the specified time.</param>
    /// <param name="isInfiniteSource">If true, mark this Generator instance as representing an infinite source (e.g., a live-running sensor).
    /// If false (default), it represents a finite source (e.g., Generating messages based on a finite file or IEnumerable).</param>
    /// <param name="name">An optional name for the component.</param>
    public Generator(Pipeline pipeline, IEnumerator<T> enumerator, TimeSpan interval, DateTime? alignDateTime = null, bool isInfiniteSource = false, string name = nameof(Generator))
        : this(pipeline, CreateEnumerator(pipeline, enumerator, interval, alignDateTime), null, isInfiniteSource, name)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Generator{T}"/> class.
    /// </summary>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="enumerator">A lazy enumerator of data.</param>
    /// <param name="startTime">The explicit start time of the data in the enumeration. Supply this parameter when the enumeration contains
    /// data values with absolute originating times (e.g. [value, time] pairs read from a file), and you want to propose a pipeline replay
    /// time to take this into account. Otherwise, pipeline playback will be determined by the prevailing replay descriptor (taking into
    /// account any other components in the pipeline which may have proposed replay times.</param>
    /// <param name="isInfiniteSource">If true, mark this Generator instance as representing an infinite source (e.g., a live-running sensor).
    /// If false (default), it represents a finite source (e.g., Generating messages based on a finite file or IEnumerable).</param>
    /// <param name="name">An optional name for the component.</param>
    public Generator(Pipeline pipeline, IEnumerator<(T, DateTime)> enumerator, DateTime? startTime = null, bool isInfiniteSource = false, string name = nameof(Generator))
        : base(pipeline, isInfiniteSource, name)
    {
        Out = pipeline.CreateEmitter<T>(this, nameof(Out));
        _enumerator = new Enumerator(enumerator);

        // if data has a defined start time, use this to propose a replay time
        if (startTime != null)
        {
            TimeInterval interval = TimeInterval.LeftBounded(startTime.Value);
            pipeline.ProposeReplayTime(interval);
        }
    }

    /// <summary>
    /// Gets the output stream.
    /// </summary>
    public Emitter<T> Out { get; }

    /// <inheritdoc/>
    public void Dispose()
    {
        _enumerator?.Dispose();
    }

    /// <summary>
    /// Called to generate the next value.
    /// </summary>
    /// <param name="currentTime">The originating time that triggered the current call.</param>
    /// <returns>The originating time at which to generate the next value.</returns>
    protected override DateTime GenerateNext(DateTime currentTime)
    {
        if (!_enumerator.MoveNext())
        {
            return DateTime.MaxValue; // no more data
        }

        Out.Post(_enumerator.Current.value, _enumerator.Current.time);

        // ensure that the originating times in the enumerated sequence are strictly increasing
        if (_enumerator.Next.time <= _enumerator.Current.time)
        {
            throw new InvalidOperationException("The generated sequence contains timestamps that are out of order. Originating times in the enumerated data must be strictly increasing.");
        }

        return _enumerator.Next.time;
    }

    private static IEnumerator<(T value, DateTime time)> CreateEnumerator(Pipeline pipeline, IEnumerator<T> enumerator, TimeSpan interval, DateTime? alignDateTime)
    {
        // Use the pipeline start time as the origin time for the data. This assumes that the pipeline is
        // already running, so we should not access the enumerator before the pipeline starts running.
        DateTime startTime = pipeline.StartTime;

        if (alignDateTime.HasValue)
        {
            if (alignDateTime.Value > startTime)
            {
                startTime += TimeSpan.FromTicks((alignDateTime.Value - startTime).Ticks % interval.Ticks);
            }
            else
            {
                startTime += TimeSpan.FromTicks(interval.Ticks - (((startTime - alignDateTime.Value).Ticks - 1) % interval.Ticks) - 1);
            }
        }

        // Ensure that generated messages remain within the pipeline replay descriptor.
        // An infinite replay descriptor will have an end time of DateTime.MaxValue.
        DateTime endTime = pipeline.ReplayDescriptor.End;
        DateTime nextTime = startTime;

        while (enumerator.MoveNext() && nextTime <= endTime)
        {
            yield return (enumerator.Current, nextTime);
            nextTime += interval;
        }
    }

    /// <summary>
    /// Wraps an enumerator and provides the ability to look-ahead to the next value.
    /// </summary>
    internal class Enumerator : IEnumerator<(T value, DateTime time)>
    {
        private static (T, DateTime) _end = (default, DateTime.MaxValue);
        private readonly IEnumerator<(T, DateTime)> _enumerator;
        private (T, DateTime) _current;
        private bool _onNext;
        private bool _atEnd;

        /// <summary>
        /// Initializes a new instance of the <see cref="Enumerator"/> class.
        /// </summary>
        /// <param name="enumerator">The underlying enumerator of values.</param>
        public Enumerator(IEnumerator<(T, DateTime)> enumerator)
        {
            _enumerator = enumerator;
        }

        /// <inheritdoc/>
        public (T value, DateTime time) Current
        {
            get
            {
                if (_onNext)
                {
                    // if the enumerator is pointing to the next value, return the cached current value
                    return _current;
                }

                // otherwise return the enumerator's current value, or the sentinel value if we have reached the end
                return _atEnd ? _end : _enumerator.Current;
            }
        }

        /// <summary>
        /// Gets the next value in the enumeration.
        /// </summary>
        public (T value, DateTime time) Next
        {
            get
            {
                if (!_onNext)
                {
                    // cache the current value, then advance the enumerator to the next value
                    _current = _enumerator.Current;
                    _atEnd = !_enumerator.MoveNext();
                    _onNext = true;
                }

                // return the enumerator's current value, or the sentinel value if we have reached the end
                return _atEnd ? _end : _enumerator.Current;
            }
        }

        /// <inheritdoc/>
        object IEnumerator.Current => Current;

        /// <inheritdoc/>
        public bool MoveNext()
        {
            if (_onNext)
            {
                // since the enumerator is already on the next value, we don't need to move it - just clear the flag
                _onNext = false;
            }
            else
            {
                _atEnd = !_enumerator.MoveNext();
            }

            return !_atEnd;
        }

        /// <inheritdoc/>
        public void Reset()
        {
            _enumerator.Reset();
            _onNext = false;
            _atEnd = false;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            _enumerator.Dispose();
        }
    }
}
