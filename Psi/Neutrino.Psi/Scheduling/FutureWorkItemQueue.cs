// <copyright file="FutureWorkItemQueue.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

namespace Microsoft.Psi.Scheduling;

/// <summary>
/// A workitem priority queue that dequeues workitems based on the scheduler's clock.
/// </summary>
internal class FutureWorkItemQueue : PriorityQueue<WorkItem>
{
    private readonly Scheduler _scheduler;

    public FutureWorkItemQueue(string name, Scheduler scheduler)
        : base(name, WorkItem.PriorityCompare)
    {
        _scheduler = scheduler;
    }

    protected override bool DequeueCondition(WorkItem item)
    {
        // Dequeue work item if:
        // (1) it is due for execution, or
        // (2) the scheduler does not need to delay future work items, or
        // (3) it will never be executed due to the scheduler context being
        // finalized before its execution time.
        return item.StartTime <= _scheduler.Clock.GetCurrentTime() ||
            !_scheduler.DelayFutureWorkItemsUntilDue ||
            item.StartTime > item.SchedulerContext.FinalizeTime;
    }
}
