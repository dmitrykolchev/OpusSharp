// <copyright file="TimeIntervalAnnotationSet.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Psi.Data.Annotations;

/// <summary>
/// Represents a set of overlapping time-interval annotations that belong to separate tracks but end at the same time.
/// </summary>
/// <remarks>
/// This data structure provides the basis for persisting overlapping time interval annotations
/// in \psi streams. It captures a set of overlapping time interval annotations that are on
/// different tracks but end at the same time, captured by <see cref="EndTime"/>.
/// When persisted to a stream, the originating time of the <see cref="Message{TimeIntervalAnnotationSet}"/>
/// should correspond to the <see cref="EndTime"/>.
/// </remarks>
public class TimeIntervalAnnotationSet
{
    private readonly Dictionary<string, TimeIntervalAnnotation> data = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="TimeIntervalAnnotationSet"/> class.
    /// </summary>
    /// <param name="timeIntervalAnnotation">The time interval annotation.</param>
    public TimeIntervalAnnotationSet(TimeIntervalAnnotation timeIntervalAnnotation)
    {
        data.Add(timeIntervalAnnotation.Track, timeIntervalAnnotation);
    }

    /// <summary>
    /// Gets the end time for the annotation set.
    /// </summary>
    public DateTime EndTime => data.Values.First().Interval.Right;

    /// <summary>
    /// Gets the set of tracks spanned by these time interval annotations.
    /// </summary>
    public IEnumerable<string> Tracks => data.Keys;

    /// <summary>
    /// Gets the set of annotations.
    /// </summary>
    public IEnumerable<TimeIntervalAnnotation> Annotations => data.Values;

    /// <summary>
    /// Gets the time interval annotation for a specified track name.
    /// </summary>
    /// <param name="track">The track name.</param>
    /// <returns>The corresponding time interval annotation.</returns>
    public TimeIntervalAnnotation this[string track] => data[track];

    /// <summary>
    /// Adds a specified time interval annotation.
    /// </summary>
    /// <param name="timeIntervalAnnotation">The time interval annotation to add.</param>
    public void AddAnnotation(TimeIntervalAnnotation timeIntervalAnnotation)
    {
        if (timeIntervalAnnotation.Interval.Right != EndTime)
        {
            throw new ArgumentException("Cannot add a time interval annotation with a different end time to a time interval annotation set.");
        }

        data.Add(timeIntervalAnnotation.Track, timeIntervalAnnotation);
    }

    /// <summary>
    /// Removes an annotation specified by a track name.
    /// </summary>
    /// <param name="track">The track name for the annotation to remove.</param>
    public void RemoveAnnotation(string track)
    {
        if (data.Count() == 1)
        {
            throw new InvalidOperationException("Cannot remove the last time interval annotation from a time interval annotation set.");
        }

        data.Remove(track);
    }

    /// <summary>
    /// Gets a value indicating whether the annotation set contains an annotation for the specified track.
    /// </summary>
    /// <param name="track">The track name.</param>
    /// <returns>True if the annotation set contains an annotation for the specified track, otherwise false.</returns>
    public bool ContainsTrack(string track)
    {
        return data.ContainsKey(track);
    }
}
