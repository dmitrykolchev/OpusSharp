// <copyright file="ArraySerializer.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using Microsoft.Psi.Common;

namespace Microsoft.Psi.Serialization;

/// <summary>
/// Generates efficient code to serialize and deserialize instances of an array.
/// </summary>
/// <typeparam name="T">The type of objects this serializer knows how to handle.</typeparam>
internal sealed class ArraySerializer<T> : ISerializer<T[]>
{
    private const int LatestSchemaVersion = 2;

    private SerializationHandler<T> _elementHandler;

    /// <inheritdoc />
    public bool? IsClearRequired => true;

    public TypeSchema Initialize(KnownSerializers serializers, TypeSchema targetSchema)
    {
        Type type = typeof(T[]);
        _elementHandler = serializers.GetHandler<T>(); // register element type
        string name = TypeSchema.GetContractName(type, serializers.RuntimeInfo.SerializationSystemVersion);
        TypeMemberSchema elementsMember = new("Elements", typeof(T).AssemblyQualifiedName, true);
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

    public void Serialize(BufferWriter writer, T[] instance, SerializationContext context)
    {
        writer.Write(instance.Length);
        foreach (T element in instance)
        {
            _elementHandler.Serialize(writer, element, context);
        }
    }

    public void PrepareDeserializationTarget(BufferReader reader, ref T[] target, SerializationContext context)
    {
        int size = reader.ReadInt32();
        PrepareTarget(ref target, size, context);
    }

    public void Deserialize(BufferReader reader, ref T[] target, SerializationContext context)
    {
        for (int i = 0; i < target.Length; i++)
        {
            _elementHandler.Deserialize(reader, ref target[i], context);
        }
    }

    public void PrepareCloningTarget(T[] instance, ref T[] target, SerializationContext context)
    {
        PrepareTarget(ref target, instance.Length, context);
    }

    public void Clone(T[] instance, ref T[] target, SerializationContext context)
    {
        for (int i = 0; i < instance.Length; i++)
        {
            _elementHandler.Clone(instance[i], ref target[i], context);
        }
    }

    public void Clear(ref T[] target, SerializationContext context)
    {
        for (int i = 0; i < target.Length; i++)
        {
            _elementHandler.Clear(ref target[i], context);
        }
    }

    private void PrepareTarget(ref T[] target, int size, SerializationContext context)
    {
        if (target != null && target.Length > size && (!_elementHandler.IsClearRequired.HasValue || _elementHandler.IsClearRequired.Value))
        {
            // use a separate context to clear the unused objects, so that we don't corrupt the current context
            SerializationContext clearContext = new(context.Serializers);

            // only clear the extra items that we won't use during cloning or deserialization (those get cleared by cloning/deserialization).
            for (int i = size; i < target.Length; i++)
            {
                _elementHandler.Clear(ref target[i], clearContext);
            }
        }

        Array.Resize(ref target, size);
    }
}
