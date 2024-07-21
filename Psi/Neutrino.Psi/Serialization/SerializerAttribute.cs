// <copyright file="SerializerAttribute.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;

namespace Microsoft.Psi.Serialization;

/// <summary>
/// Identifies the custom serializer to use when serializing an instance of the class or struct to which this attribute is applied.
/// </summary>
[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class | AttributeTargets.Enum)]
public class SerializerAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SerializerAttribute"/> class.
    /// </summary>
    /// <param name="serializerType">he type of serializer to use when serializing instances of the class or struct annotated with this attribute.</param>
    public SerializerAttribute(Type serializerType)
    {
        SerializerType = serializerType;
    }

    /// <summary>
    /// Gets the type of serializer to use when serializing instances of the class or struct annotated with this attribute.
    /// </summary>
    public Type SerializerType { get; }
}
