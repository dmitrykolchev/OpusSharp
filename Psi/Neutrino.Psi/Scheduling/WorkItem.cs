// <copyright file="WorkItem.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;

namespace Neutrino.Psi.Scheduling;

/// <summary>
/// A workitem that can be scheduled for execution by the scheduler.
/// </summary>
internal struct WorkItem
{
    public SchedulerContext SchedulerContext;
    public SynchronizationLock SyncLock;
    public DateTime StartTime;
    public Action Callback;

    public static int PriorityCompare(WorkItem w1, WorkItem w2)
    {
        return DateTime.Compare(w1.StartTime, w2.StartTime);
    }

    public bool TryLock()
    {
        return SyncLock.TryLock();
    }
}
