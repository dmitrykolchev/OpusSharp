// <copyright file="SynchronizationLock.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Threading;

namespace Microsoft.Psi.Scheduling;

/// <summary>
/// Implements a simple lock. Unlike Monitor, this class doesn't enforce thread ownership.
/// </summary>
public sealed class SynchronizationLock
{
    private int _counter;
    private readonly object _owner;

    /// <summary>
    /// Initializes a new instance of the <see cref="SynchronizationLock"/> class.
    /// </summary>
    /// <param name="owner">Owner object.</param>
    /// <param name="locked">Locked flag.</param>
    public SynchronizationLock(object owner, bool locked = false)
    {
        _owner = owner;
        _counter = locked ? 1 : 0;
    }

    /// <summary>
    /// Prevents anybody else from locking the lock, regardless of current state (i.e. NOT exclusive).
    /// </summary>
    public void Hold()
    {
        Interlocked.Increment(ref _counter);
    }

    /// <summary>
    /// Attempts to take exclusive hold of the lock.
    /// </summary>
    /// <returns>True if no one else was holding the lock.</returns>
    public bool TryLock()
    {
        int v = Interlocked.CompareExchange(ref _counter, 1, 0);
        return v == 0;
    }

    /// <summary>
    /// Spins until the lock is acquired, with no back-off.
    /// </summary>
    /// <returns>Number of spins before the lock was acquired.</returns>
    public int Lock()
    {
        SpinWait sw = default;
        while (!TryLock())
        {
            sw.SpinOnce();
        }

        return sw.Count;
    }

    /// <summary>
    /// Releases the hold on the lock.
    /// </summary>
    public void Release()
    {
        int v = Interlocked.Decrement(ref _counter);
        if (v < 0)
        {
            throw new InvalidOperationException("The lock hold was released too many times.");
        }
    }
}
