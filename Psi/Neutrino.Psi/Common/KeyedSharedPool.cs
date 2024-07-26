// <copyright file="KeyedSharedPool.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Collections.Concurrent;
using System.Linq;

namespace Neutrino.Psi.Common;

/// <summary>
/// Provides a pool of shared objects organized by a key. The key is used both to group
/// interchangeable objects as well as a parameter to the object allocation function.
/// </summary>
/// <typeparam name="T">The type of the objects managed by this pool.</typeparam>
/// <typeparam name="TKey">The type of the key.</typeparam>
public class KeyedSharedPool<T, TKey> : IDisposable
    where T : class
{
    private readonly ConcurrentDictionary<TKey, SharedPool<T>> _sharedPools = new();
    private readonly Func<TKey, T> _allocator;
    private readonly int _initialSize;

    /// <summary>
    /// Initializes a new instance of the <see cref="KeyedSharedPool{T, TKey}"/> class.
    /// </summary>
    /// <param name="allocator">The allocation function for constructing a new object.</param>
    /// <param name="initialSize">Initial size of each pool.</param>
    public KeyedSharedPool(Func<TKey, T> allocator, int initialSize = 10)
    {
        _allocator = allocator;
        _initialSize = initialSize;
    }

    /// <summary>
    /// Resets the keyed shared pool.
    /// </summary>
    /// <param name="clearLiveObjects">Indicates whether to clear any live objects.</param>
    /// <remarks>
    /// If the clearLiveObjects flag is false, an exception is thrown if a reset is attempted while the pool
    /// still contains live objects.
    /// </remarks>
    public void Reset(bool clearLiveObjects = false)
    {
        // Reset the individual shared pools
        foreach (SharedPool<T> pool in _sharedPools.Values)
        {
            pool.Reset(clearLiveObjects);
        }

        // And remove them
        _sharedPools.Clear();
    }

    /// <summary>
    /// Get or creates a shared object from the pool.
    /// </summary>
    /// <param name="key">The shared object key.</param>
    /// <returns>A shared object from the pool.</returns>
    public Shared<T> GetOrCreate(TKey key)
    {
        return GetSharedPool(key).GetOrCreate();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (SharedPool<T> sharedPool in _sharedPools.Values)
        {
            sharedPool.Dispose();
        }
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return $"{_sharedPools.Count} keys, {_sharedPools.Sum(kvp => kvp.Value.TotalCount)} objects.";
    }

    private SharedPool<T> GetSharedPool(TKey key)
    {
        return _sharedPools.GetOrAdd(key, new SharedPool<T>(() => _allocator(key), _initialSize));
    }
}
