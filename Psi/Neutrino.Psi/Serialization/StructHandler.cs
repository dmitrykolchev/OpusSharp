// <copyright file="StructHandler.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Runtime.CompilerServices;
using Microsoft.Psi.Common;

namespace Microsoft.Psi.Serialization;

/// <summary>
/// Internal wrapper that implements both a typed and an untyped version of the serialization contract.
/// The typed contract enables efficient calling (no type lookup and parameter boxing), while the untyped contract is used for polymorphic fields.
/// The handler also covers the case of cloning a struct into a boxed struct.
/// </summary>
/// <typeparam name="T">The type of objects the handler understands.</typeparam>
internal sealed class StructHandler<T> : SerializationHandler<T>
{
    private static readonly CloneDelegate<object> CopyToBox = Generator.GenerateCloneMethod<object>(il => Generator.EmitCopyToBox(typeof(T), il));

    // the inner serializer and serializerEx
    private readonly ISerializer<T> _innerSerializer;

    public StructHandler(ISerializer<T> innerSerializer, string contractName, int id)
        : base(contractName, id)
    {
        _innerSerializer = innerSerializer;
        if (innerSerializer != null && innerSerializer.IsClearRequired.HasValue)
        {
            IsClearRequired = innerSerializer.IsClearRequired.Value;
        }

        if (typeof(T).IsByRef)
        {
            throw new InvalidOperationException("Cannot use a value type handler with a class serializer");
        }
    }

    // this attribute is not really needed (the method in its current form is going to be inlined anyway)
    // but I kept it to document the expectation of inlining
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Serialize(BufferWriter writer, T instance, SerializationContext context)
    {
        _innerSerializer.Serialize(writer, instance, context);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Deserialize(BufferReader reader, ref T target, SerializationContext context)
    {
        _innerSerializer.Deserialize(reader, ref target, context);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Clone(T instance, ref T target, SerializationContext context)
    {
        _innerSerializer.Clone(instance, ref target, context);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Clear(ref T target, SerializationContext context)
    {
        _innerSerializer.Clear(ref target, context);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal override void UntypedSerialize(BufferWriter writer, object instance, SerializationContext context)
    {
        _innerSerializer.Serialize(writer, (T)instance, context);
    }

    internal override void UntypedDeserialize(BufferReader reader, ref object target, SerializationContext context)
    {
        T typedTarget = default;
        if (target is T)
        {
            typedTarget = (T)target;
        }
        else
        {
            target = default(T); // when the target is of a different type, we have to allocate a new object (via boxing)
        }

        context.AddDeserializedObject(target);
        _innerSerializer.Deserialize(reader, ref typedTarget, context);
        CopyToBox(typedTarget, ref target, context);
    }

    internal override void UntypedClone(object instance, ref object target, SerializationContext context)
    {
        T typedTarget = default;
        if (target is T)
        {
            typedTarget = (T)target;
        }
        else
        {
            target = default(T); // when the target is of a different type, we have to allocate a new object (via boxing)
        }

        context.AddDeserializedObject(target);
        _innerSerializer.Clone((T)instance, ref typedTarget, context);
        CopyToBox(typedTarget, ref target, context);
    }

    internal override void UntypedClear(ref object target, SerializationContext context)
    {
        T typedTarget = default;
        if (target is T)
        {
            typedTarget = (T)target;
        }
        else
        {
            target = default(T); // when the target is of a different type, we have to allocate a new object (via boxing)
        }

        context.AddDeserializedObject(target);
        _innerSerializer.Clear(ref typedTarget, context);
        CopyToBox(typedTarget, ref target, context);
    }
}
