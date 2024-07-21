// <copyright file="StringSerializer.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using Microsoft.Psi.Common;

namespace Microsoft.Psi.Serialization;

/// <summary>
/// Simple string serializer.
/// </summary>
internal sealed class StringSerializer : ISerializer<string>
{
    /// <inheritdoc />
    public bool? IsClearRequired => false;

    public TypeSchema Initialize(KnownSerializers serializers, TypeSchema targetSchema)
    {
        return targetSchema ?? TypeSchema.FromType(typeof(string), GetType().AssemblyQualifiedName, serializers.RuntimeInfo.SerializationSystemVersion);
    }

    public void Clone(string instance, ref string target, SerializationContext context)
    {
    }

    public void Serialize(BufferWriter writer, string instance, SerializationContext context)
    {
        writer.Write(instance);
    }

    public void Deserialize(BufferReader reader, ref string target, SerializationContext context)
    {
        target = reader.ReadString();
    }

    public void PrepareDeserializationTarget(BufferReader reader, ref string target, SerializationContext context)
    {
    }

    public void PrepareCloningTarget(string instance, ref string target, SerializationContext context)
    {
        target = instance;
    }

    public void Clear(ref string target, SerializationContext context)
    {
    }
}
