// <copyright file="SharedContainer.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Threading;
using Neutrino.Psi.Serialization;

namespace Neutrino.Psi.Common;

/// <summary>
/// Provides a container that tracks the usage of a resource (such as a large memory allocation) and allows reusing it once not in use
/// This class performs AddRef and Release and overrides serialization to preempt cloning from making deep copies of the resource.
/// This class is for internal use only. The Shared class is the public-facing API for this functionality.
/// </summary>
/// <typeparam name="T">The type of data held by this container.</typeparam>
[Serializer(typeof(SharedContainer<>.CustomSerializer))]
internal class SharedContainer<T>
    where T : class
{
    private readonly SharedPool<T> _sharedPool;
    private int _refCount;
    private T _resource;

    internal SharedContainer(T resource, SharedPool<T> pool)
    {
        _sharedPool = pool;
        _resource = resource;
        _refCount = 1;
    }

    public T Resource => _resource;

    public SharedPool<T> SharedPool => _sharedPool;

    public void AddRef()
    {
        Interlocked.Increment(ref _refCount);
    }

    public void Release()
    {
        if (_resource == null)
        {
            return;
        }

        int newVal = Interlocked.Decrement(ref _refCount);
        if (newVal == 0)
        {
            // return it to the pool
            if (_sharedPool != null)
            {
                _sharedPool.Recycle(_resource);
            }
            else
            {
                if (_resource is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }

            _resource = null;
        }
        else if (newVal < 0)
        {
            throw new InvalidOperationException("The referenced object has been released too many times.");
        }
    }

    private class CustomSerializer : ISerializer<SharedContainer<T>>
    {
        public const int LatestSchemaVersion = 2;
        private SerializationHandler<T> handler;

        /// <inheritdoc />
        public bool? IsClearRequired => false;

        public TypeSchema Initialize(KnownSerializers serializers, TypeSchema targetSchema)
        {
            handler = serializers.GetHandler<T>();
            Type type = typeof(SharedContainer<T>);
            string name = TypeSchema.GetContractName(type, serializers.RuntimeInfo.SerializationSystemVersion);
            TypeMemberSchema resourceMember = new("resource", typeof(T).AssemblyQualifiedName, true);
            TypeSchema schema = new(
                type.AssemblyQualifiedName,
                TypeFlags.IsClass,
                new TypeMemberSchema[] { resourceMember },
                name,
                TypeSchema.GetId(name),
                LatestSchemaVersion,
                GetType().AssemblyQualifiedName,
                serializers.RuntimeInfo.SerializationSystemVersion);
            return targetSchema ?? schema;
        }

        public void Serialize(BufferWriter writer, SharedContainer<T> instance, SerializationContext context)
        {
            // only serialize the resource.
            // The refCount needs not be serialized (it will be always 1 when deserializing)
            // The shared pool cannot be serialized, and needs to be provided by the deserializer, by providing a deserializing target that is already pool-aware
            handler.Serialize(writer, instance._resource, context);
        }

        public void PrepareCloningTarget(SharedContainer<T> instance, ref SharedContainer<T> target, SerializationContext context)
        {
            if (target != null)
            {
                target.Release();
            }

            target = instance; // needs to be set to the final object so that single-instancing works correctly
        }

        public void Clone(SharedContainer<T> instance, ref SharedContainer<T> target, SerializationContext context)
        {
            target.AddRef();
        }

        public void PrepareDeserializationTarget(BufferReader reader, ref SharedContainer<T> target, SerializationContext context)
        {
            SharedPool<T> sharedPool = null;
            T resource = default(T);

            if (target != null)
            {
                target.Release();
                sharedPool = target.SharedPool;
                sharedPool?.TryGet(out resource);
            }

            target = new SharedContainer<T>(resource, sharedPool);
        }

        public void Deserialize(BufferReader reader, ref SharedContainer<T> target, SerializationContext context)
        {
            handler.Deserialize(reader, ref target._resource, context);
        }

        public void Clear(ref SharedContainer<T> target, SerializationContext context)
        {
            // shared containers cannot be reused
            throw new InvalidOperationException();
        }
    }
}
