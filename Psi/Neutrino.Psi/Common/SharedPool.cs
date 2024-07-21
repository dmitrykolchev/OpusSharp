// <copyright file="SharedPool.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Collections.Generic;
using Microsoft.Psi.Serialization;


namespace Microsoft.Psi;

/// <summary>
/// Provides a pool of shared objects.
/// Use this class in conjunction with <see cref="Shared{T}"/>.
/// </summary>
/// <typeparam name="T">The type of the objects managed by this pool.</typeparam>
public class SharedPool<T> : IDisposable
    where T : class
{
    private readonly Func<T> _allocator;
    private readonly KnownSerializers _serializers;
    private readonly object _availableLock = new();
    private List<T> _pool;
    private Queue<T> _available;

    /// <summary>
    /// Initializes a new instance of the <see cref="SharedPool{T}"/> class.
    /// </summary>
    /// <param name="allocator">The allocation function for constructing a new object.</param>
    /// <param name="initialSize">The initial size of the pool. The size will be adjusted up as needed, but never down.</param>
    /// <param name="knownSerializers">An optional set of known serializers. Only required if the pool holds objects that are deserialized from an older store.</param>
    public SharedPool(Func<T> allocator, int initialSize = 10, KnownSerializers knownSerializers = null)
    {
        _allocator = allocator;
        _available = new Queue<T>(initialSize);
        _pool = new List<T>();
        _serializers = knownSerializers;
    }

    /// <summary>
    /// Gets the number of objects available, i.e., that are not live, in the pool.
    /// </summary>
    public int AvailableCount
    {
        get
        {
            lock (_availableLock)
            {
                return _available != null ? _available.Count : 0;
            }
        }
    }

    /// <summary>
    /// Gets the total number of objects managed by this pool.
    /// </summary>
    public int TotalCount
    {
        get
        {
            lock (_pool)
            {
                return _pool.Count;
            }
        }
    }

    /// <summary>
    /// Resets the shared pool.
    /// </summary>
    /// <param name="clearLiveObjects">Indicates whether to clear any live objects.</param>
    /// <remarks>
    /// If the clearLiveObjects flag is false, an exception is thrown if a reset is attempted while the pool
    /// still contains live objects.
    /// </remarks>
    public void Reset(bool clearLiveObjects = false)
    {
        lock (_availableLock)
        {
            lock (_pool)
            {
                // If no object is still alive, then reset the pool
                if (clearLiveObjects || (_available.Count == _pool.Count))
                {
                    // Dispose all the objects in the pool
                    if (typeof(IDisposable).IsAssignableFrom(typeof(T)))
                    {
                        foreach (var entry in _available)
                        {
                            ((IDisposable)entry).Dispose();
                        }
                    }

                    // Re-initialize
                    _available = new();
                    _pool = new();
                }
                else
                {
                    throw new InvalidOperationException("Cannot reset a shared pool that contains live objects.");
                }
            }
        }
    }

    /// <summary>
    /// Attempts to retrieve an unused object from the pool.
    /// </summary>
    /// <param name="recyclable">An unused object from the pool, if there is one.</param>
    /// <returns>True if an unused object was available, false otherwise.</returns>
    public bool TryGet(out T recyclable)
    {
        lock (_availableLock)
        {
            if (_available.Count > 0)
            {
                recyclable = _available.Dequeue();
                return true;
            }
        }

        recyclable = null;
        return false;
    }

    /// <summary>
    /// Attempts to retrieve an unused object from the pool.
    /// </summary>
    /// <param name="recyclable">An unused object, wrapped in a ref-counted <see cref="Shared{T}"/> instance.</param>
    /// <returns>True if an unused object was available, false otherwise.</returns>
    public bool TryGet(out Shared<T> recyclable)
    {
        recyclable = null;
        bool success = TryGet(out T recycled);
        if (success)
        {
            recyclable = new Shared<T>(recycled, this);
        }

        return success;
    }

    /// <summary>
    /// Attempts to retrieve an unused object from the pool if one is available, otherwise creates and returns a new instance.
    /// </summary>
    /// <returns>An unused object, wrapped in a ref-counted <see cref="Shared{T}"/> instance.</returns>
    public Shared<T> GetOrCreate()
    {
        if (!TryGet(out T recycled))
        {
            recycled = _allocator();
            lock (_pool)
            {
                _pool.Add(recycled);
            }
        }

        return new Shared<T>(recycled, this);
    }

    /// <summary>
    /// Releases all unused objects in the pool.
    /// </summary>
    public void Dispose()
    {
        if (typeof(IDisposable).IsAssignableFrom(typeof(T)))
        {
            lock (_availableLock)
            {
                foreach (T entry in _available)
                {
                    ((IDisposable)entry).Dispose();
                }

                _available = null;
            }
        }
    }

    /// <summary>
    /// Returns an object to the pool.
    /// This method is meant for internal use. Use <see cref="Shared{T}.Dispose"/> instead.
    /// </summary>
    /// <param name="recyclable">The object to return to the pool.</param>
    internal void Recycle(T recyclable)
    {
        SerializationContext context = new(_serializers);
        Serializer.Clear(ref recyclable, context);
        lock (_availableLock)
        {
            if (_available != null)
            {
                _available.Enqueue(recyclable);
            }
            else
            {
                // dispose the recycled object if it is disposable
                if (recyclable is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }
    }
}
