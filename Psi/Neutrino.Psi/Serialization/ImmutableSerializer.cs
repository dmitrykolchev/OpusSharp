// <copyright file="ImmutableSerializer.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.Psi.Common;

namespace Microsoft.Psi.Serialization;


/// <summary>
/// Auto-generated serializer for immutable types (both reference and value type).
/// </summary>
/// <typeparam name="T">The type of objects this serializer knows how to handle.</typeparam>
internal class ImmutableSerializer<T> : ISerializer<T>
{
    private SerializeDelegate<T> _serializeImpl;
    private DeserializeDelegate<T> _deserializeImpl;

    public ImmutableSerializer()
    {
    }

    /// <inheritdoc />
    public bool? IsClearRequired => false;

    public TypeSchema Initialize(KnownSerializers serializers, TypeSchema targetSchema)
    {
        TypeSchema runtimeSchema = TypeSchema.FromType(typeof(T), GetType().AssemblyQualifiedName, serializers.RuntimeInfo.SerializationSystemVersion);
        IEnumerable<System.Reflection.MemberInfo> members = runtimeSchema.GetCompatibleMemberSet(targetSchema);

        _serializeImpl = Generator.GenerateSerializeMethod<T>(il => Generator.EmitSerializeFields(typeof(T), serializers, il, members));
        _deserializeImpl = Generator.GenerateDeserializeMethod<T>(il => Generator.EmitDeserializeFields(typeof(T), serializers, il, members));

        return targetSchema ?? runtimeSchema;
    }

    public void Serialize(BufferWriter writer, T instance, SerializationContext context)
    {
        _serializeImpl(writer, instance, context);
    }

    public void Deserialize(BufferReader reader, ref T target, SerializationContext context)
    {
        _deserializeImpl(reader, ref target, context);
    }

    public void Clone(T instance, ref T target, SerializationContext context)
    {
        target = instance;
    }

    public void Clear(ref T target, SerializationContext context)
    {
        // nothing to clear
    }

    public void PrepareCloningTarget(T instance, ref T target, SerializationContext context)
    {
        // we won't be cloning anything, but we want the object graph in SerializationContext to remember the correct instance.
        target = instance;
    }

    public void PrepareDeserializationTarget(BufferReader reader, ref T target, SerializationContext context)
    {
        // always allocate a new target
        target = default;
        target ??= (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    }
}
