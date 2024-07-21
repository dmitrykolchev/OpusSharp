// <copyright file="SerializationContext.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Microsoft.Psi.Serialization;

/// <summary>
/// Maintains the objects and types seen during serialization, to enable polymorphism,
/// single-instanced references (multiple references to same object) and circular dependencies.
/// </summary>
public class SerializationContext
{
    private readonly KnownSerializers _serializers;
    private Action<int, Type> _polymorphicTypePublisher;
    private Dictionary<object, int> _serialized;
    private int _nextSerializedId;
    private Dictionary<object, int> _deserialized;
    private Dictionary<int, object> _deserializedById;
    private int _nextDeserializedId;

    /// <summary>
    /// Initializes a new instance of the <see cref="SerializationContext"/> class.
    /// This will become internal. Use Serializer.Schema instead.
    /// </summary>
    public SerializationContext()
        : this(KnownSerializers.Default)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SerializationContext"/> class, with the specified serialization overrides.
    /// </summary>
    /// <param name="serializers">The set of custom serializers to use instead of the default ones.</param>
    public SerializationContext(KnownSerializers serializers)
    {
        _serializers = serializers ?? KnownSerializers.Default;
    }

    internal KnownSerializers Serializers => _serializers;

    internal Action<int, Type> PolymorphicTypePublisher
    {
        set => _polymorphicTypePublisher = value;
    }

    /// <summary>
    /// Clears the object caches used to identify multiple references to the same instance.
    /// You must call this method before reusing the context object to serialize another object graph.
    /// </summary>
    public void Reset()
    {
        _serialized?.Clear();
        _deserialized?.Clear();
        _deserializedById?.Clear();
        _nextSerializedId = 0;
        _nextDeserializedId = 0;
    }

    internal void PublishPolymorphicType(int id, Type type)
    {
        _polymorphicTypePublisher?.Invoke(id, type);
    }

    internal bool GetOrAddSerializedObjectId(object obj, out int id)
    {
        if (obj == null || obj is string)
        {
            id = _nextSerializedId++;
            return false;
        }

        if (_serialized == null)
        {
            _serialized = new Dictionary<object, int>(ReferenceEqualsComparer.Default);
        }

        if (_serialized.TryGetValue(obj, out id))
        {
            return true;
        }

        // not found
        id = _nextSerializedId++;
        _serialized.Add(obj, id);
        return false;
    }

    internal IEnumerable<T> GetSerializedObjects<T>()
        where T : class
    {
        return _serialized.Keys.Where(o => o is T).Select(o => o as T);
    }

    internal object GetDeserializedObject(int id)
    {
        if (_deserializedById != null)
        {
            return _deserializedById[id];
        }

        return null;
    }

    internal void AddDeserializedObject(object obj)
    {
        int id = _nextDeserializedId++;

        // We need the serialized and deserialized collections to have the same ids when cloning, so we have to count nulls and strings too.
        if (obj == null || obj is string)
        {
            return;
        }

        if (obj is string)
        {
            return;
        }

        if (_deserialized == null)
        {
            _deserialized = new Dictionary<object, int>(ReferenceEqualsComparer.Default);
            _deserializedById = new Dictionary<int, object>();
        }

        _deserialized.Add(obj, id);
        _deserializedById.Add(id, obj);
    }

    internal bool ContainsDeserializedObject(object obj)
    {
        if (_deserialized == null)
        {
            return false;
        }

        if (obj == null || obj is string)
        {
            return false;
        }

        return _deserialized.ContainsKey(obj);
    }

    internal IEnumerable<T> GetDeserializedObjects<T>()
        where T : class
    {
        return _deserialized.Keys.Where(o => o is T).Select(o => o as T);
    }

    private class ReferenceEqualsComparer : EqualityComparer<object>
    {
        public static readonly new ReferenceEqualsComparer Default = new();

        public override bool Equals(object obj1, object obj2)
        {
            return object.ReferenceEquals(obj1, obj2);
        }

        public override int GetHashCode(object obj)
        {
            return RuntimeHelpers.GetHashCode(obj);
        }
    }
}
