// <copyright file="SimpleSerializer.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using Microsoft.Psi.Common;

namespace Microsoft.Psi.Serialization;

/// <summary>
/// Default class for custom serializers of primitive types.
/// </summary>
/// <typeparam name="T">A primitive type (pure value type).</typeparam>
internal sealed class SimpleSerializer<T> : ISerializer<T>
{
    private SerializeDelegate<T> _serializeImpl;
    private DeserializeDelegate<T> _deserializeImpl;

    /// <inheritdoc />
    public bool? IsClearRequired => false;

    public TypeSchema Initialize(KnownSerializers serializers, TypeSchema targetSchema)
    {
        TypeSchema schema = TypeSchema.FromType(typeof(T), GetType().AssemblyQualifiedName, serializers.RuntimeInfo.SerializationSystemVersion);
        _serializeImpl = Generator.GenerateSerializeMethod<T>(il => Generator.EmitPrimitiveSerialize(typeof(T), il));
        _deserializeImpl = Generator.GenerateDeserializeMethod<T>(il => Generator.EmitPrimitiveDeserialize(typeof(T), il));
        return targetSchema ?? schema;
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
        // uncomment to verify inlining in release builds
        // Generator.DumpStack();
        target = instance;
    }

    public void Clear(ref T target, SerializationContext context)
    {
    }

    public void PrepareCloningTarget(T instance, ref T target, SerializationContext context)
    {
    }

    public void PrepareDeserializationTarget(BufferReader reader, ref T target, SerializationContext context)
    {
    }
}
