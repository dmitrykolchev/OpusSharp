﻿// <copyright file="DictionarySerializer.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using Microsoft.Psi.Common;

namespace Microsoft.Psi.Serialization;

/// <summary>
/// Provides serialization and cloning methods for <see cref="Dictionary{TKey, TValue}"/> objects.
/// </summary>
internal sealed class DictionarySerializer<TKey, TValue> : ISerializer<Dictionary<TKey, TValue>>
{
    // Bumping the schema version number from the auto-generated version which defaults to RuntimeInfo.CurrentRuntimeVersion (2)
    private const int LatestSchemaVersion = 3;

    private ISerializer<Dictionary<TKey, TValue>> _innerSerializer;

    /// <inheritdoc />
    public bool? IsClearRequired => true;

    /// <inheritdoc />
    public TypeSchema Initialize(KnownSerializers serializers, TypeSchema targetSchema)
    {
        if (targetSchema?.Version <= 2)
        {
            // maintain backward compatibility with older serialized data
            _innerSerializer = new ClassSerializer<Dictionary<TKey, TValue>>();
        }
        else
        {
            // otherwise default to the new implementation
            _innerSerializer = new DictionarySerializerImpl();
        }

        return _innerSerializer.Initialize(serializers, targetSchema);
    }

    /// <inheritdoc />
    public void Serialize(BufferWriter writer, Dictionary<TKey, TValue> instance, SerializationContext context)
    {
        _innerSerializer.Serialize(writer, instance, context);
    }

    /// <inheritdoc />
    public void PrepareDeserializationTarget(BufferReader reader, ref Dictionary<TKey, TValue> target, SerializationContext context)
    {
        _innerSerializer.PrepareDeserializationTarget(reader, ref target, context);
    }

    /// <inheritdoc />
    public void Deserialize(BufferReader reader, ref Dictionary<TKey, TValue> target, SerializationContext context)
    {
        _innerSerializer.Deserialize(reader, ref target, context);
    }

    /// <inheritdoc />
    public void PrepareCloningTarget(Dictionary<TKey, TValue> instance, ref Dictionary<TKey, TValue> target, SerializationContext context)
    {
        _innerSerializer.PrepareCloningTarget(instance, ref target, context);
    }

    /// <inheritdoc />
    public void Clone(Dictionary<TKey, TValue> instance, ref Dictionary<TKey, TValue> target, SerializationContext context)
    {
        _innerSerializer.Clone(instance, ref target, context);
    }

    /// <inheritdoc />
    public void Clear(ref Dictionary<TKey, TValue> target, SerializationContext context)
    {
        _innerSerializer.Clear(ref target, context);
    }

    /// <summary>
    /// Provides serialization and cloning methods for <see cref="Dictionary{TKey, TValue}"/> objects.
    /// </summary>
    private class DictionarySerializerImpl : ISerializer<Dictionary<TKey, TValue>>
    {
        private SerializationHandler<IEqualityComparer<TKey>> comparerHandler;
        private SerializationHandler<KeyValuePair<TKey, TValue>[]> entriesHandler;
        private SetComparerDelegate setComparerImpl;

        private delegate void SetComparerDelegate(Dictionary<TKey, TValue> target, IEqualityComparer<TKey> value);

        /// <inheritdoc />
        public bool? IsClearRequired => true;

        /// <inheritdoc />
        public TypeSchema Initialize(KnownSerializers serializers, TypeSchema targetSchema)
        {
            // Add a comparerHandler of type IEqualityComparer. This will take care of serializing,
            // deserializing and cloning the Comparer member of Dictionary. Because the comparer field
            // is private, we will also need to generate a dynamic method so that we can set the
            // comparer field upon deserialization or cloning. This method should be invoked right after
            // clearing the target Dictionary, before adding the deserialized or cloned entries to it.
            comparerHandler = serializers.GetHandler<IEqualityComparer<TKey>>();
            setComparerImpl = GenerateSetComparerMethod();

            // Use an array serializer to serialize the dictionary elements as an array of key-value pairs
            entriesHandler = serializers.GetHandler<KeyValuePair<TKey, TValue>[]>();

            System.Type type = typeof(Dictionary<TKey, TValue>);
            string name = TypeSchema.GetContractName(type, serializers.RuntimeInfo.SerializationSystemVersion);

            // Treat the Dictionary as a class with 2 members - a comparer and an array of key-value pairs
            TypeMemberSchema comparerMember = new("Comparer", typeof(IEqualityComparer<TKey>).AssemblyQualifiedName, true);
            TypeMemberSchema entriesMember = new("KeyValuePairs", typeof(KeyValuePair<TKey, TValue>[]).AssemblyQualifiedName, true);
            TypeSchema schema = new(
                type.AssemblyQualifiedName,
                TypeFlags.IsClass,
                new[] { comparerMember, entriesMember },
                name,
                TypeSchema.GetId(name),
                LatestSchemaVersion,
                GetType().AssemblyQualifiedName,
                serializers.RuntimeInfo.SerializationSystemVersion);

            return targetSchema ?? schema;
        }

        /// <inheritdoc />
        public void Serialize(BufferWriter writer, Dictionary<TKey, TValue> instance, SerializationContext context)
        {
            comparerHandler.Serialize(writer, instance.Comparer, context);
            entriesHandler.Serialize(writer, instance.ToArray(), context);
        }

        /// <inheritdoc />
        public void PrepareDeserializationTarget(BufferReader reader, ref Dictionary<TKey, TValue> target, SerializationContext context)
        {
            if (target == null)
            {
                target = new Dictionary<TKey, TValue>();
            }

            // Note that we don't clear an existing target dictionary as we want to preserve its elements until the
            // call to Deserialize which will attempt to re-use any existing target elements for deserializing into.
        }

        /// <inheritdoc />
        public void Deserialize(BufferReader reader, ref Dictionary<TKey, TValue> target, SerializationContext context)
        {
            IEqualityComparer<TKey> targetComparer = target.Comparer;
            comparerHandler.Deserialize(reader, ref targetComparer, context);

            // Deserialize into an array of existing target elements, then add them back to the dictionary
            KeyValuePair<TKey, TValue>[] targetElements = target.ToArray();

            // Following this call, targetElements will be an array containing the deserialized items
            entriesHandler.Deserialize(reader, ref targetElements, context);

            // Clear and add the deserialized items into the target dictionary
            target.Clear();

            // Call dynamic method to set the private comparer field
            setComparerImpl(target, targetComparer);

            foreach (KeyValuePair<TKey, TValue> element in targetElements)
            {
                target.Add(element.Key, element.Value);
            }
        }

        /// <inheritdoc />
        public void PrepareCloningTarget(Dictionary<TKey, TValue> instance, ref Dictionary<TKey, TValue> target, SerializationContext context)
        {
            if (target == null)
            {
                target = new Dictionary<TKey, TValue>(instance.Count);
            }

            // Note that we don't clear an existing target dictionary as we want to preserve its elements until the
            // call to Clone which will attempt to re-use any existing target elements for cloning into.
        }

        /// <inheritdoc />
        public void Clone(Dictionary<TKey, TValue> instance, ref Dictionary<TKey, TValue> target, SerializationContext context)
        {
            IEqualityComparer<TKey> targetComparer = target.Comparer;
            comparerHandler.Clone(instance.Comparer, ref targetComparer, context);

            // Clone into an array of existing target elements, then add them back to the dictionary
            KeyValuePair<TKey, TValue>[] targetElements = target.ToArray();

            // Following this call, targetElements will be an array containing the cloned items
            entriesHandler.Clone(instance.ToArray(), ref targetElements, context);

            // Clear and add the cloned items into the target dictionary
            target.Clear();

            // Call dynamic method to set the private comparer field
            setComparerImpl(target, targetComparer);

            foreach (KeyValuePair<TKey, TValue> element in targetElements)
            {
                target.Add(element.Key, element.Value);
            }
        }

        /// <inheritdoc />
        public void Clear(ref Dictionary<TKey, TValue> target, SerializationContext context)
        {
            // Note that this clears the items in the dictionary, but does not actually remove them
            // i.e. the dictionary will contain the cleared items upon returning from this method.
            KeyValuePair<TKey, TValue>[] items = target.ToArray();
            entriesHandler.Clear(ref items, context);

            IEqualityComparer<TKey> comparer = target.Comparer;
            comparerHandler.Clear(ref comparer, context);
        }

        private SetComparerDelegate GenerateSetComparerMethod()
        {
            // Create DynamicMethod using delegate's Invoke method as prototype
            System.Reflection.MethodInfo prototype = typeof(SetComparerDelegate).GetMethod(nameof(SetComparerDelegate.Invoke));
            DynamicMethod method = new(prototype.Name, prototype.ReturnType, prototype.GetParameters().Select(p => p.ParameterType).ToArray(), GetType(), true);
            ILGenerator il = method.GetILGenerator();

            // Get the comparer field (uses the first field of type IEquialityComparer<TKey>)
            System.Reflection.FieldInfo field = Generator.GetAllFields(typeof(Dictionary<TKey, TValue>)).Where(fi => fi.FieldType == typeof(IEqualityComparer<TKey>)).First();

            // Generate code to set the comparer field directly (in place of field.SetValue(target, value))
            il.Emit(OpCodes.Ldarg_0); // target
            il.Emit(OpCodes.Ldarg_1); // value
            il.Emit(OpCodes.Stfld, field); // target.comparer = value
            il.Emit(OpCodes.Ret);

            return (SetComparerDelegate)method.CreateDelegate(typeof(SetComparerDelegate));
        }
    }
}
