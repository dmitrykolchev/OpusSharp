// <copyright file="ClassSerializer.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using Neutrino.Psi.Common;

namespace Neutrino.Psi.Serialization;


/// <summary>
/// Auto-generated serializer for reference types.
/// Implementers of ISerializer should instantiate and call this class to do the heavy lifting.
/// </summary>
/// <typeparam name="T">The type of objects this serializer knows how to handle.</typeparam>
internal class ClassSerializer<T> : ISerializer<T>
{
    // we use delegates (instead of generating a full class) because dynamic delegates (unlike dynamic types)
    // can access the private fields of the target type.
    private SerializeDelegate<T> _serializeImpl;
    private DeserializeDelegate<T> _deserializeImpl;
    private CloneDelegate<T> _cloneImpl;
    private ClearDelegate<T> _clearImpl;

    /// <summary>
    /// Initializes a new instance of the <see cref="ClassSerializer{T}"/> class.
    /// The serializer will handle all public and private fields (including property-backing fields and read-only fields)
    /// of the underlying type.
    /// </summary>
    public ClassSerializer()
    {
    }

    /// <inheritdoc />
    public bool? IsClearRequired => null; // depends on the generated implementation

    public TypeSchema Initialize(KnownSerializers serializers, TypeSchema targetSchema)
    {
        TypeSchema runtimeSchema = TypeSchema.FromType(typeof(T), GetType().AssemblyQualifiedName, serializers.RuntimeInfo.SerializationSystemVersion);
        IEnumerable<System.Reflection.MemberInfo> members = runtimeSchema.GetCompatibleMemberSet(targetSchema);

        _deserializeImpl = Generator.GenerateDeserializeMethod<T>(il => Generator.EmitDeserializeFields(typeof(T), serializers, il, members));
        _serializeImpl = Generator.GenerateSerializeMethod<T>(il => Generator.EmitSerializeFields(typeof(T), serializers, il, members));
        _cloneImpl = Generator.GenerateCloneMethod<T>(il => Generator.EmitCloneFields(typeof(T), serializers, il));
        _clearImpl = Generator.GenerateClearMethod<T>(il => Generator.EmitClearFields(typeof(T), serializers, il));

        return targetSchema ?? runtimeSchema;
    }

    /// <summary>
    /// Serializes the given instance to the specified stream.
    /// </summary>
    /// <param name="writer">The stream writer to serialize to.</param>
    /// <param name="instance">The instance to serialize.</param>
    /// <param name="context">A context object containing accumulated type mappings and object references.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Serialize(BufferWriter writer, T instance, SerializationContext context)
    {
        try
        {
            _serializeImpl(writer, instance, context);
        }
        catch (NotSupportedException)
        {
            if (instance.GetType().BaseType == typeof(MulticastDelegate))
            {
                throw new NotSupportedException("Cannot serialize Func/Action/Delegate. A common cause is serializing streams of IEnumerables holding closure references. A solution is to reify with `.ToList()` or similar.");
            }

            throw;
        }
    }

    /// <summary>
    /// Deserializes an instance from the specified stream into the specified target object.
    /// </summary>
    /// <param name="reader">The stream reader to deserialize from.</param>
    /// <param name="target">An instance to deserialize into.</param>
    /// <param name="context">A context object containing accumulated type mappings and object references.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Deserialize(BufferReader reader, ref T target, SerializationContext context)
    {
        _deserializeImpl(reader, ref target, context);
    }

    /// <summary>
    /// Deep-clones the given object into an existing allocation.
    /// </summary>
    /// <param name="instance">The instance to clone.</param>
    /// <param name="target">An existing instance to clone into.</param>
    /// <param name="context">A context object containing accumulated type and object references.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Clone(T instance, ref T target, SerializationContext context)
    {
        try
        {
            _cloneImpl(instance, ref target, context);
        }
        catch (NotSupportedException)
        {
            if (instance.GetType().BaseType == typeof(MulticastDelegate))
            {
                throw new NotSupportedException("Cannot clone Func/Action/Delegate. A common cause is posting or cloning IEnumerables holding closure references. A solution is to reify with `.ToList()` or similar before posting/cloning.");
            }

            throw;
        }
    }

    /// <summary>
    /// Provides an opportunity to clear an instance before caching it for future reuse as a cloning or deserialization target.
    /// The method is expected to call Serializer.Clear on all reference-type fields.
    /// </summary>
    /// <param name="target">The instance to clear.</param>
    /// <param name="context">A context object containing accumulated type mappings and object references.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Clear(ref T target, SerializationContext context)
    {
        _clearImpl(ref target, context);
    }

    /// <summary>
    /// Prepares an empty object to clone into. This method is expected to allocate a new empty target object if the provided one is insufficient.
    /// </summary>
    /// <param name="instance">The instance to clone.</param>
    /// <param name="target">An existing instance to clone into. Could be null.</param>
    /// <param name="context">A context object containing accumulated type mappings and object references.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void PrepareCloningTarget(T instance, ref T target, SerializationContext context)
    {
        target ??= (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    }

    /// <summary>
    /// Prepares an empty object to deserialize into. This method is expected to allocate a new empty target object if the provided one is insufficient.
    /// </summary>
    /// <param name="reader">The stream reader to deserialize from.</param>
    /// <param name="target">An optional existing instance to deserialize into. Could be null.</param>
    /// <param name="context">A context object containing accumulated type mappings and object references.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void PrepareDeserializationTarget(BufferReader reader, ref T target, SerializationContext context)
    {
        target ??= (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    }
}
