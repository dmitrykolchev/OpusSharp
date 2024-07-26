// <copyright file="SchedulerContext.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Threading;
using Neutrino.Psi.Common;

namespace Neutrino.Psi.Scheduling;

/// <summary>
/// Provides a context in which work items may be scheduled and tracked as a group.
/// Maintains a count of the number of work items currently in-flight and an event
/// to signal when there are no remaining work items in the context.
/// </summary>
public sealed class SchedulerContext : IDisposable
{
    private readonly object _syncLock = new();
    private readonly ManualResetEvent _empty = new(true);
    private int _workItemCount;

    /// <summary>
    /// Gets or sets the finalization time of the context after which no further work will be scheduled.
    /// This is initialized to <see cref="DateTime.MaxValue"/> when scheduling on the context is enabled.
    /// It may later be set to a finite time prior to terminating scheduling on the context once the
    /// final scheduling time on the context is known.
    /// </summary>
    public DateTime FinalizeTime { get; set; } = DateTime.MaxValue;

    /// <summary>
    /// Gets a wait handle that signals when there are no remaining work items in the context.
    /// </summary>
    public WaitHandle Empty => _empty;

    internal Clock Clock { get; private set; } = new Clock(DateTime.MinValue, 0);

    internal bool Started { get; private set; } = false;

    /// <inheritdoc/>
    public void Dispose()
    {
        _empty.Dispose();
    }

    /// <summary>
    /// Starts scheduling work on the context.
    /// </summary>
    /// <param name="clock">The scheduler clock.</param>
    public void Start(Clock clock)
    {
        Clock = clock;
        Started = true;
    }

    /// <summary>
    /// Stops scheduling work on the context.
    /// </summary>
    public void Stop()
    {
        Started = false;
        Clock = new Clock(DateTime.MinValue, 0);
    }

    /// <summary>
    /// Enters the context before scheduling a work item.
    /// </summary>
    internal void Enter()
    {
        lock (_syncLock)
        {
            if (++_workItemCount == 1)
            {
                _empty.Reset();
            }
        }
    }

    /// <summary>
    /// Exits the context after a work item has completed or been abandoned.
    /// </summary>
    internal void Exit()
    {
        lock (_syncLock)
        {
            if (--_workItemCount == 0)
            {
                _empty.Set();
            }
        }
    }
}
