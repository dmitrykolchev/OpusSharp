// <copyright file="SimpleArraySerializer.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using Neutrino.Psi.Common;

namespace Neutrino.Psi.Serialization;

/// <summary>
/// Generates efficient code to serialize and deserialize instances of an array containing instances of
/// a primitive type or a simple struct (i.e. a struct that has only primitive fields).
/// The underlying element type is assumed to not contain any reference fields or struct fields. Thus, the serializer simply copies memory
/// and does not invoke the element serializer for each element.
/// </summary>
/// <typeparam name="T">The type of objects this serializer knows how to handle.</typeparam>
internal sealed class SimpleArraySerializer<T> : ISerializer<T[]>
{
    private const int LatestSchemaVersion = 2;

    // for performance reasons, we want serialization to perform block-copy operations over the entire array in one go
    // however, since this class is generic, the C# compiler doesn't let us get the address of the first element of the array
    // thus, we rely on generated IL code to do so and wrap the generated IL in delegates.
    private static readonly SerializeDelegate<T[]> SerializeFn = Generator.GenerateSerializeMethod<T[]>(il => Generator.EmitPrimitiveArraySerialize(typeof(T), il));
    private static readonly DeserializeDelegate<T[]> DeserializeFn = Generator.GenerateDeserializeMethod<T[]>(il => Generator.EmitPrimitiveArrayDeserialize(typeof(T), il));

    /// <inheritdoc />
    public bool? IsClearRequired => false;

    public TypeSchema Initialize(KnownSerializers serializers, TypeSchema targetSchema)
    {
        serializers.GetHandler<T>(); // register element type
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

    public void Serialize(BufferWriter writer, T[] instance, SerializationContext context)
    {
        writer.Write(instance.Length);

        // invoke the generated code
        if (instance.Length > 0)
        {
            SerializeFn(writer, instance, context);
        }
    }

    public void PrepareDeserializationTarget(BufferReader reader, ref T[] target, SerializationContext context)
    {
        int size = reader.ReadInt32();
        Array.Resize(ref target, size);
    }

    public void Deserialize(BufferReader reader, ref T[] target, SerializationContext context)
    {
        // invoke the generated code
        if (target.Length > 0)
        {
            DeserializeFn(reader, ref target, context);
        }
    }

    public void PrepareCloningTarget(T[] instance, ref T[] target, SerializationContext context)
    {
        Array.Resize(ref target, instance.Length);
    }

    public void Clone(T[] instance, ref T[] target, SerializationContext context)
    {
        Array.Copy(instance, target, instance.Length);
    }

    public void Clear(ref T[] target, SerializationContext context)
    {
        // nothing to clear since T is a pure value type
    }
}
