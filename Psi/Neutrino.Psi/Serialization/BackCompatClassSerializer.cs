// <copyright file="BackCompatClassSerializer.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

namespace Neutrino.Psi.Serialization;

/// <summary>
/// Provides a base class for authoring backwards compatible custom serializers (for reading) for class types.
/// </summary>
/// <typeparam name="T">The type of objects handled by the custom serializer.</typeparam>
public abstract class BackCompatClassSerializer<T> : BackCompatSerializer<T>
    where T : class
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BackCompatClassSerializer{T}"/> class.
    /// </summary>
    /// <param name="schemaVersion">The current schema version.</param>
    public BackCompatClassSerializer(int schemaVersion)
        : base(schemaVersion, new ClassSerializer<T>())
    {
    }
}
