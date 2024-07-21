// <copyright file="BufferSerializer.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Runtime.CompilerServices;
using Neutrino.Psi.Common;

namespace Neutrino.Psi.Serialization;

/// <summary>
/// Implements efficient code to serialize and deserialize BufferReader instances.
/// </summary>
internal sealed class BufferSerializer : ISerializer<BufferReader>
{
    private const int LatestSchemaVersion = 2;

    /// <inheritdoc />
    public bool? IsClearRequired => false;

    public TypeSchema Initialize(KnownSerializers serializers, TypeSchema targetSchema)
    {
        serializers.GetHandler<byte>(); // register element type
        Type type = typeof(byte[]);
        string name = TypeSchema.GetContractName(type, serializers.RuntimeInfo.SerializationSystemVersion);
        TypeMemberSchema elementsMember = new("Elements", typeof(byte).AssemblyQualifiedName, true);
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Serialize(BufferWriter writer, BufferReader instance, SerializationContext context)
    {
        int length = instance.RemainingLength;
        writer.Write(length);
        if (length > 0)
        {
            writer.WriteEx(instance.Buffer, instance.Position, length);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void PrepareDeserializationTarget(BufferReader reader, ref BufferReader target, SerializationContext context)
    {
        int length = reader.ReadInt32();
        target ??= new BufferReader();
        target.Reset(length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Deserialize(BufferReader reader, ref BufferReader target, SerializationContext context)
    {
        if (target.RemainingLength > 0)
        {
            reader.Read(target.Buffer, target.RemainingLength);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void PrepareCloningTarget(BufferReader instance, ref BufferReader target, SerializationContext context)
    {
        int length = instance.RemainingLength;
        target = target ?? new BufferReader();
        target.Reset(length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Clone(BufferReader instance, ref BufferReader target, SerializationContext context)
    {
        int length = instance.RemainingLength;
        Buffer.BlockCopy(instance.Buffer, instance.Position, target.Buffer, 0, length);
    }

    public void Clear(ref BufferReader target, SerializationContext context)
    {
        // nothing to clear
    }
}
