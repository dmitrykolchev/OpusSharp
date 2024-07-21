// <copyright file="EnumerableSerializer.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Psi.Common;

namespace Microsoft.Psi.Serialization;

/// <summary>
/// Generates efficient code to serialize and deserialize an IEnumerable.
/// </summary>
/// <typeparam name="T">The type of objects this serializer knows how to handle.</typeparam>
internal sealed class EnumerableSerializer<T> : ISerializer<IEnumerable<T>>
{
    private const int LatestSchemaVersion = 2;

    private SerializationHandler<T> _elementHandler;

    /// <inheritdoc />
    public bool? IsClearRequired => true;

    public TypeSchema Initialize(KnownSerializers serializers, TypeSchema targetSchema)
    {
        _elementHandler = serializers.GetHandler<T>(); // register element type
        Type type = typeof(T[]);
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

    public void Serialize(BufferWriter writer, IEnumerable<T> instance, SerializationContext context)
    {
        writer.Write(instance.Count());
        foreach (T element in instance)
        {
            _elementHandler.Serialize(writer, element, context);
        }
    }

    public void PrepareDeserializationTarget(BufferReader reader, ref IEnumerable<T> target, SerializationContext context)
    {
        int size = reader.ReadInt32();
        T[] buffTarget = target as T[];
        PrepareTarget(ref buffTarget, size, context);
        target = buffTarget;
    }

    public void Deserialize(BufferReader reader, ref IEnumerable<T> target, SerializationContext context)
    {
        T[] bufTarget = (T[])target; // by now target is guaranteed to be an array, because of PrepareDeserializationTarget
        for (int i = 0; i < bufTarget.Length; i++)
        {
            _elementHandler.Deserialize(reader, ref bufTarget[i], context);
        }
    }

    public void PrepareCloningTarget(IEnumerable<T> instance, ref IEnumerable<T> target, SerializationContext context)
    {
        T[] buffTarget = target as T[];
        PrepareTarget(ref buffTarget, target.Count(), context);
        target = buffTarget;
    }

    public void Clone(IEnumerable<T> instance, ref IEnumerable<T> target, SerializationContext context)
    {
        T[] bufTarget = (T[])target; // by now target is guaranteed to be an array, because of PrepareCloningTarget
        int i = 0;
        foreach (T item in instance)
        {
            _elementHandler.Clone(item, ref bufTarget[i++], context);
        }
    }

    public void Clear(ref IEnumerable<T> target, SerializationContext context)
    {
        T[] buffTarget = target.ToArray(); // this might allocate if target is not already an array
        for (int i = 0; i < buffTarget.Length; i++)
        {
            _elementHandler.Clear(ref buffTarget[i], context);
        }

        target = buffTarget;
    }

    private void PrepareTarget(ref T[] target, int size, SerializationContext context)
    {
        if (target != null && target.Length > size && (!_elementHandler.IsClearRequired.HasValue || _elementHandler.IsClearRequired.Value))
        {
            // use a separate context to clear the unused objects, so that we don't corrupt the current context
            SerializationContext clearContext = new(context.Serializers);

            // only clear the extra items that we won't use during cloning or deserialization
            for (int i = size; i < target.Length; i++)
            {
                _elementHandler.Clear(ref target[i], clearContext);
            }
        }

        Array.Resize(ref target, size);
    }
}
