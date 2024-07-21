﻿// <copyright file="Shared.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Globalization;
#if TRACKLEAKS
using System.Diagnostics;
using System.Text;
#endif
using Microsoft.Psi.Common;
using Microsoft.Psi.Serialization;


namespace Microsoft.Psi;

#pragma warning disable SA1402

/// <summary>
/// Shared resource utility.
/// </summary>
public static class Shared
{
    /// <summary>
    /// Creates a <see cref="Shared{T}"/> instance wrapping the specified resource.
    /// The returned instance must be disposed.
    /// </summary>
    /// <remarks>
    /// The reference count of the resource is incremented before this method returns.
    /// When the returned <see cref="Shared{T}"/> is disposed, resource's reference count is decremented.
    /// When the reference count reaches 0, the wrapped resource is released and disposed.
    /// </remarks>
    /// <typeparam name="T">The type of resource being wrapped.</typeparam>
    /// <param name="resource">The resource to wrap. Usually a large memory allocation.</param>
    /// <returns>A <see cref="Shared{T}"/> instance wrapping the specified resource.</returns>
    public static Shared<T> Create<T>(T resource)
        where T : class
    {
        return new Shared<T>(resource, null);
    }
}

/// <summary>
/// Provides a container that tracks the usage of a shared resource (such as a large memory allocation).
/// Follow the Cloning pattern and use the Shared.DeepClone extension method instead of direct assignment
/// to create long-lived references to the same shared resource.
/// </summary>
/// <typeparam name="T">The type of data held by this container.</typeparam>
/// <remarks>
/// The .Net model of delayed memory management via garbage collection is ill suited for frequent large allocations.
/// The memory allocated for an object can only reused after the object is garbage collected.
/// Since garbage collection is relatively infrequent, and independent of the lifespan of the allocated objects,
/// the time interval between the object becoming available for garbage collection
/// (that is, when nobody alive references it anymore) and the memory becoming available for reuse
/// can be quite large. More importantly, the memory is only reclaimed as a
/// result of a garbage collection pass, with a cost proportional to the number of objects allocated since
/// the last garbage collection pass. The generational model of the .Net garbage collector mitigates this issue
/// to some extent, but not enough to avoid large garbage collection pauses in memory-intensive streaming systems,
/// e.g. when allocating video frames at frame rate.
/// The Shared class is designed to provide full control over memory allocations,
/// to make it possible to exchange buffers between concurrent components but also reuse them once they are not needed anymore.
/// It uses explicit reference counting to decide when the memory can be released back to an allocation pool,
/// without having to rely on the garbage collector.
/// The behavior of Shared is as follows:
/// - when a Shared instance is instantiated, the reference count of the resource is set to 1.
/// - when a Shared instance is cloned (via Shared.DeepClone or Shared.AddRef), the reference count is incremented by 1.
/// - when a Shared instance is disposed (via Shared.Dispose or by the "using" keyword) the reference count is decremented by 1.
/// - the ref count is not affected when using the assignment operator (e.g. var copy = shared).
/// Thus, for it to function properly, the handling of Shared instances requires following special rules:
/// - avoid using the assignment operator to capture references to a Shared instance beyond local scope.
/// - call myShared.AddRef() to create a new reference to the underlying resource, and remember to call Dispose when done with it.
/// - use myShared.DeepClone(ref this.myStoredCopy) to store a long-lived reference to a Shared instance in a class field.
/// DeepClone will Dispose the target object first if needed, before cloning.
/// - there is no need to clone or AddRef when posting a Shared instance to a message stream.
/// - never store a long-lived reference to the underlying resource.
/// Note that Shared doesn't provide any facilities for concurrent access to the underlying resource.
/// Once a resource is wrapped in a Shared object, it should be considered read-only.
/// </remarks>
[Serializer(typeof(Shared<>.CustomSerializer))]
public class Shared<T> : IDisposable, IFormattable
    where T : class
{
    private SharedContainer<T> _inner;
#if TRACKLEAKS
    private StackTrace _constructorStackTrace;
    private StackTrace _disposeStackTrace;
#endif

    /// <summary>
    /// Initializes a new instance of the <see cref="Shared{T}"/> class.
    /// </summary>
    /// <param name="resource">The shared resource.</param>
    /// <param name="sharedPool">The shared object pool.</param>
    internal Shared(T resource, SharedPool<T> sharedPool)
        : this()
    {
        _inner = new SharedContainer<T>(resource, sharedPool);
    }

    // serialization support
    private Shared()
    {
#if TRACKLEAKS
        _constructorStackTrace = new StackTrace(true);
#endif
    }

    /// <summary>
    /// Finalizes an instance of the <see cref="Shared{T}"/> class.
    /// </summary>
    ~Shared()
    {
        if (_inner == null)
        {
            return;
        }

        _inner.Release();
#if TRACKLEAKS
        StringBuilder sb = new StringBuilder("\\psi output **********************************************");
        sb.AppendLine();
        sb.AppendLine($"A shared resource of type {typeof(T).FullName} was not explicitly released and has been garbage-collected. It should be released by calling Dispose instead.");
        if (_constructorStackTrace != null)
        {
            foreach (var frame in _constructorStackTrace.GetFrames())
            {
                sb.AppendLine($"{frame.GetFileName()}({frame.GetFileLineNumber()}): {frame.GetMethod().DeclaringType}.{frame.GetMethod().Name}");
            }
        }

        sb.AppendLine("**********************************************************");
        Debug.WriteLine(sb.ToString());
#endif
    }

    /// <summary>
    /// Gets underlying resource.
    /// </summary>
    public T Resource => _inner?.Resource;

    /// <summary>
    /// Gets the shared object pool.
    /// </summary>
    public SharedPool<T> SharedPool => _inner?.SharedPool;

    internal SharedContainer<T> Inner => _inner;

    /// <inheritdoc />
    public void Dispose()
    {
        if (_inner == null)
        {
            ObjectDisposedWithHistoryException e = new ObjectDisposedWithHistoryException(GetType().FullName);
#if TRACKLEAKS
            e.AddHistory("Instance created", _constructorStackTrace);
            e.AddHistory("Instance disposed", _disposeStackTrace);
#endif
#pragma warning disable CA1065 // Do not raise exceptions in unexpected locations
            throw e;
#pragma warning restore CA1065 // Do not raise exceptions in unexpected locations
        }
#if TRACKLEAKS
        else
        {
            _disposeStackTrace = new StackTrace(true);
        }
#endif

        _inner.Release();
        _inner = null;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Add reference.
    /// </summary>
    /// <returns>Shared resource.</returns>
    public Shared<T> AddRef()
    {
        Shared<T> shared = new Shared<T>
        {
            _inner = _inner,
        };
        _inner.AddRef();
        return shared;
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return ToString(string.Empty, CultureInfo.CurrentCulture);
    }

    /// <inheritdoc/>
    public string ToString(string format, IFormatProvider formatProvider)
    {
        string value;
        if (_inner != null && _inner.Resource is IFormattable formattableResource)
        {
            value = formattableResource.ToString(format, formatProvider);
        }
        else
        {
            value = _inner == null ? "<null>" : _inner.Resource.ToString();
        }

        return $"Shared({value})";
    }

    // The custom serializer delegates everything to the inner SharedContainer<>
    // The only reason we have a custom serializer is because of the Clear behavior, which
    // in addition to calling Clear on the SharedContainer<>, it sets the field to null.
    // This is done to avoid keeping a reference to an object that is still in use.
    private class CustomSerializer : ISerializer<Shared<T>>
    {
        public const int LatestSchemaVersion = 2;
        private SerializationHandler<SharedContainer<T>> handler;

        /// <inheritdoc />
        public bool? IsClearRequired => true;

        public TypeSchema Initialize(KnownSerializers serializers, TypeSchema targetSchema)
        {
            handler = serializers.GetHandler<SharedContainer<T>>();
            Type type = typeof(Shared<T>);
            string name = TypeSchema.GetContractName(type, serializers.RuntimeInfo.SerializationSystemVersion);
            TypeMemberSchema innerMember = new TypeMemberSchema("inner", typeof(SharedContainer<T>).AssemblyQualifiedName, true);
            TypeSchema schema = new TypeSchema(
                type.AssemblyQualifiedName,
                TypeFlags.IsClass,
                new TypeMemberSchema[] { innerMember },
                name,
                TypeSchema.GetId(name),
                LatestSchemaVersion,
                GetType().AssemblyQualifiedName,
                serializers.RuntimeInfo.SerializationSystemVersion);
            return targetSchema ?? schema;
        }

        public void Serialize(BufferWriter writer, Shared<T> instance, SerializationContext context)
        {
            handler.Serialize(writer, instance._inner, context);
        }

        public void PrepareCloningTarget(Shared<T> instance, ref Shared<T> target, SerializationContext context)
        {
            if (target == null)
            {
                target = new Shared<T>();
            }
            else
            {
#if TRACKLEAKS
                target._constructorStackTrace = instance._constructorStackTrace;
                target._disposeStackTrace = null;
#endif
            }
        }

        public void Clone(Shared<T> instance, ref Shared<T> target, SerializationContext context)
        {
            handler.Clone(instance._inner, ref target._inner, context);
        }

        public void PrepareDeserializationTarget(BufferReader reader, ref Shared<T> target, SerializationContext context)
        {
            if (target == null)
            {
                target = new Shared<T>();
            }
            else
            {
#if TRACKLEAKS
                target._constructorStackTrace = new StackTrace(true);
                target._disposeStackTrace = null;
#endif
            }
        }

        public void Deserialize(BufferReader reader, ref Shared<T> target, SerializationContext context)
        {
            handler.Deserialize(reader, ref target._inner, context);
        }

        public void Clear(ref Shared<T> target, SerializationContext context)
        {
            if (target != null && target._inner != null)
            {
                target._inner.Release();
                target._inner = null;
            }
        }
    }
}
