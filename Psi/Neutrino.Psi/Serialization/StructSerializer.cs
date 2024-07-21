// <copyright file="StructSerializer.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using Neutrino.Psi.Common;

namespace Neutrino.Psi.Serialization;

/// <summary>
/// Auto-generated serializer for complex value types (that is, structs having one or more non-primitive fields).
/// Implementers of ISerializer should instantiate and call this class to do the heavy lifting.
/// </summary>
/// <typeparam name="T">The value type this serializer knows how to handle.</typeparam>
internal sealed class StructSerializer<T> : ISerializer<T>
{
    // we use delegates (instead of generating a full class) because dynamic delegates (unlike dynamic types)
    // can access the private fields of the target type.
    private SerializeDelegate<T> _serializeImpl;
    private DeserializeDelegate<T> _deserializeImpl;
    private CloneDelegate<T> _cloneImpl;
    private ClearDelegate<T> _clearImpl;

    /// <inheritdoc />
    public bool? IsClearRequired => null; // depends on the generated implementation

    public TypeSchema Initialize(KnownSerializers serializers, TypeSchema targetSchema)
    {
        TypeSchema runtimeSchema = TypeSchema.FromType(typeof(T), GetType().AssemblyQualifiedName, serializers.RuntimeInfo.SerializationSystemVersion);
        System.Collections.Generic.IEnumerable<System.Reflection.MemberInfo> members = runtimeSchema.GetCompatibleMemberSet(targetSchema);

        _serializeImpl = Generator.GenerateSerializeMethod<T>(il => Generator.EmitSerializeFields(typeof(T), serializers, il, members));
        _deserializeImpl = Generator.GenerateDeserializeMethod<T>(il => Generator.EmitDeserializeFields(typeof(T), serializers, il, members));
        _cloneImpl = Generator.GenerateCloneMethod<T>(il => Generator.EmitCloneFields(typeof(T), serializers, il));
        _clearImpl = Generator.GenerateClearMethod<T>(il => Generator.EmitClearFields(typeof(T), serializers, il));

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
        _cloneImpl(instance, ref target, context);
    }

    public void Clear(ref T target, SerializationContext context)
    {
        _clearImpl(ref target, context);
    }

    public void PrepareCloningTarget(T instance, ref T target, SerializationContext context)
    {
    }

    public void PrepareDeserializationTarget(BufferReader reader, ref T target, SerializationContext context)
    {
    }
}
