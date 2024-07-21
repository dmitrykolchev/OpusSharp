// <copyright file="AnnotationValueSchema{T}.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System.Drawing;

namespace Microsoft.Psi.Data.Annotations;

/// <summary>
/// Represents an annotation value schema.
/// </summary>
/// <typeparam name="T">The datatype of the values contained in the schema.</typeparam>
public abstract class AnnotationValueSchema<T> : IAnnotationValueSchema
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AnnotationValueSchema{T}"/> class.
    /// </summary>
    /// <param name="defaultValue">The default value for new instances of the schema.</param>
    /// <param name="fillColor">The fill color.</param>
    /// <param name="textColor">The text color.</param>
    public AnnotationValueSchema(T defaultValue, Color fillColor, Color textColor)
    {
        DefaultValue = defaultValue;
        FillColor = fillColor;
        TextColor = textColor;
    }

    /// <summary>
    /// Gets the default value for this schema.
    /// </summary>
    public T DefaultValue { get; }

    /// <summary>
    /// Gets the fill color.
    /// </summary>
    public Color FillColor { get; }

    /// <summary>
    /// Gets the text color.
    /// </summary>
    public Color TextColor { get; }

    /// <inheritdoc/>
    public IAnnotationValue GetDefaultAnnotationValue()
    {
        return new AnnotationValue<T>(DefaultValue, FillColor, TextColor);
    }

    /// <inheritdoc/>
    public IAnnotationValue CreateAnnotationValue(string value)
    {
        return new AnnotationValue<T>(CreateValue(value), FillColor, TextColor);
    }

    /// <inheritdoc/>
    public bool IsValid(IAnnotationValue annotationValue)
    {
        return annotationValue.FillColor.Equals(FillColor) && annotationValue.TextColor.Equals(TextColor);
    }

    /// <summary>
    /// Creates a value from the specified string.
    /// </summary>
    /// <param name="value">The value specified as a string.</param>
    /// <returns>The value.</returns>
    public abstract T CreateValue(string value);
}
