// <copyright file="StringArraySerializer.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using Microsoft.Psi.Common;

namespace Microsoft.Psi.Serialization;

/// <summary>
/// Version of array optimized for arrays of strings.
/// </summary>
internal sealed class StringArraySerializer : ISerializer<string[]>
{
    private const int LatestSchemaVersion = 2;

    /// <inheritdoc />
    public bool? IsClearRequired => false;

    public TypeSchema Initialize(KnownSerializers serializers, TypeSchema targetSchema)
    {
        serializers.GetHandler<string>(); // register element type
        Type type = typeof(string[]);
        string name = TypeSchema.GetContractName(type, serializers.RuntimeInfo.SerializationSystemVersion);
        TypeMemberSchema elementsMember = new("Elements", typeof(string).AssemblyQualifiedName, true);
        TypeSchema schema = new(
            type.AssemblyQualifiedName,
            TypeFlags.IsCollection,
            new TypeMemberSchema[] { elementsMember },
            name,
            TypeSchema.GetId(name),
            LatestSchemaVersion,
            GetType().AssemblyQualifiedName,
            serializers.RuntimeInfo.SerializationSystemVersion);
        return targetSchema ?? schema;
    }

    public void Serialize(BufferWriter writer, string[] instance, SerializationContext context)
    {
        writer.Write(instance.Length);
        foreach (string item in instance)
        {
            writer.Write(item);
        }
    }

    public void PrepareDeserializationTarget(BufferReader reader, ref string[] target, SerializationContext context)
    {
        int size = reader.ReadInt32();
        Array.Resize(ref target, size);
    }

    public void Deserialize(BufferReader reader, ref string[] target, SerializationContext context)
    {
        for (int i = 0; i < target.Length; i++)
        {
            target[i] = reader.ReadString();
        }
    }

    public void PrepareCloningTarget(string[] instance, ref string[] target, SerializationContext context)
    {
        Array.Resize(ref target, instance.Length);
    }

    public void Clone(string[] instance, ref string[] target, SerializationContext context)
    {
        Array.Copy(instance, target, instance.Length);
    }

    public void Clear(ref string[] target, SerializationContext context)
    {
    }
}
