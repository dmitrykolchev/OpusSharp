// <copyright file="TimeIntervalAnnotation.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System.Collections.Generic;

namespace Neutrino.Psi.Data.Annotations;

/// <summary>
/// Represents a time interval annotation.
/// </summary>
public class TimeIntervalAnnotation
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TimeIntervalAnnotation"/> class.
    /// </summary>
    /// <param name="interval">The interval over which the annotation occurs.</param>
    /// <param name="track">The name of the annotation track.</param>
    /// <param name="attributeValues">The set of attribute values for the annotation.</param>
    public TimeIntervalAnnotation(TimeInterval interval, string track, Dictionary<string, IAnnotationValue> attributeValues)
    {
        Interval = interval;
        Track = track;
        AttributeValues = attributeValues;
    }

    /// <summary>
    /// Gets or sets the interval over which this annotation occurs.
    /// </summary>
    public TimeInterval Interval { get; set; }

    /// <summary>
    /// Gets or sets the track of the this annotation.
    /// </summary>
    public string Track { get; set; }

    /// <summary>
    /// Gets or sets the collection of values in the annotation.
    /// </summary>
    public Dictionary<string, IAnnotationValue> AttributeValues { get; set; }
}
