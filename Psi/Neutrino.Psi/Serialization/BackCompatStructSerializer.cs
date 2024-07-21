// <copyright file="BackCompatStructSerializer.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

namespace Neutrino.Psi.Serialization;

/// <summary>
/// Provides a base class for authoring backwards compatible custom serializers (for reading) for struct types.
/// </summary>
/// <typeparam name="T">The type of objects handled by the custom serializer.</typeparam>
public abstract class BackCompatStructSerializer<T> : BackCompatSerializer<T>
    where T : struct
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BackCompatStructSerializer{T}"/> class.
    /// </summary>
    /// <param name="schemaVersion">The current schema version.</param>
    public BackCompatStructSerializer(int schemaVersion)
        : base(schemaVersion, new StructSerializer<T>())
    {
    }
}
