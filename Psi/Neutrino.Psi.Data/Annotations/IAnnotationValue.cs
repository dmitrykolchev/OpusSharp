// <copyright file="IAnnotationValue.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System.Drawing;

namespace Microsoft.Psi.Data.Annotations;

/// <summary>
/// Defines an annotation value in an untyped fashion.
/// </summary>
public interface IAnnotationValue
{
    /// <summary>
    /// Gets a string representation of the annotation value.
    /// </summary>
    public string ValueAsString { get; }

    /// <summary>
    /// Gets the color for drawing the annotation value area's interior.
    /// </summary>
    public Color FillColor { get; }

    /// <summary>
    /// Gets the color for drawing the annotation value text.
    /// </summary>
    public Color TextColor { get; }
}
