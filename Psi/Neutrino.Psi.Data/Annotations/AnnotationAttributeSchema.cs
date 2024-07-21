// <copyright file="AnnotationAttributeSchema.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System.Collections.Generic;

namespace Microsoft.Psi.Data.Annotations;

/// <summary>
/// Represents an annotation attribute schema.
/// </summary>
public class AnnotationAttributeSchema
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AnnotationAttributeSchema"/> class.
    /// </summary>
    /// <param name="name">The annotation attribute name.</param>
    /// <param name="description">The annotation attribute description.</param>
    /// <param name="valueSchema">The annotation value schema for this attribute.</param>
    public AnnotationAttributeSchema(string name, string description, IAnnotationValueSchema valueSchema)
    {
        Name = name;
        Description = description;
        ValueSchema = valueSchema;
    }

    /// <summary>
    /// Gets the name of the annotation attribute.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets a description of the annotation attribute.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Gets the annotation value schema for this annotation attribute.
    /// </summary>
    public IAnnotationValueSchema ValueSchema { get; }

    /// <summary>
    /// Creates an attribute value from a specified string.
    /// </summary>
    /// <param name="value">The attribute value as a string.</param>
    /// <returns>An attribute value which can be used to populate a time interval annotation.</returns>
    public Dictionary<string, IAnnotationValue> CreateAttribute(string value)
    {
        return new() { { Name, ValueSchema.CreateAnnotationValue(value) } };
    }
}
