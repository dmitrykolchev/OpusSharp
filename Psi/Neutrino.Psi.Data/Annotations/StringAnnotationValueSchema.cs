// <copyright file="StringAnnotationValueSchema.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System.Drawing;

namespace Neutrino.Psi.Data.Annotations;

/// <summary>
/// Represents a string annotation value schema.
/// </summary>
public class StringAnnotationValueSchema : AnnotationValueSchema<string>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="StringAnnotationValueSchema"/> class.
    /// </summary>
    /// <param name="defaultValue">The default value for new instances of the schema.</param>
    /// <param name="fillColor">The fill color.</param>
    /// <param name="textColor">The text color.</param>
    public StringAnnotationValueSchema(string defaultValue, Color fillColor, Color textColor)
        : base(defaultValue, fillColor, textColor)
    {
    }

    /// <inheritdoc/>
    public override string CreateValue(string value)
    {
        return value;
    }
}
