// <copyright file="SimpleSemaphore.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Threading;

namespace Neutrino.Psi.Scheduling;

/// <summary>
/// Implements a semaphore class that limits the number of threads entering a resource and provides an event when all threads finished.
/// </summary>
public class SimpleSemaphore : IDisposable
{
    private readonly ManualResetEvent _empty;
    private readonly ManualResetEvent _available;
    private readonly int _maxThreadCount;
    private int _count;

    /// <summary>
    /// Initializes a new instance of the <see cref="SimpleSemaphore"/> class.
    /// </summary>
    /// <param name="maxThreadCount">Maximum number of threads.</param>
    public SimpleSemaphore(int maxThreadCount)
    {
        _maxThreadCount = maxThreadCount;
        _empty = new ManualResetEvent(true);
        _available = new ManualResetEvent(true);
    }

    /// <summary>
    /// Gets empty state wait handle.
    /// </summary>
    public WaitHandle Empty => _empty;

    /// <summary>
    /// Gets availability wait handle.
    /// </summary>
    public WaitHandle Available => _available;

    /// <summary>
    /// Gets maximum number of threads.
    /// </summary>
    public int MaxThreadCount => _maxThreadCount;

    /// <inheritdoc/>
    public void Dispose()
    {
        _empty.Dispose();
        _available.Dispose();
    }

    /// <summary>
    /// Try to enter the semaphore.
    /// </summary>
    /// <returns>Success.</returns>
    public bool TryEnter()
    {
        _empty.Reset();
        int newCount = Interlocked.Increment(ref _count);
        if (newCount > _maxThreadCount)
        {
            Exit();
            return false;
        }

        return true;
    }

    /// <summary>
    /// Exit the semaphore.
    /// </summary>
    public void Exit()
    {
        int newCount = Interlocked.Decrement(ref _count);
        if (newCount == 0)
        {
            _empty.Set();
        }
    }
}
