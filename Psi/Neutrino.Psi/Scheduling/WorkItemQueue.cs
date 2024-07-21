// <copyright file="WorkItemQueue.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

namespace Neutrino.Psi.Scheduling;

/// <summary>
/// A workitem priority queue that locks workitems before dequeueing.
/// </summary>
internal class WorkItemQueue : PriorityQueue<WorkItem>
{
    public WorkItemQueue(string name)
        : base(name, WorkItem.PriorityCompare)
    {
    }

    protected override bool DequeueCondition(WorkItem item)
    {
        return item.TryLock();
    }
}
