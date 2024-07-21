﻿// <copyright file="SchedulerPerfCounters.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

namespace Neutrino.Psi.Scheduling;

/// <summary>
/// The counters we support.
/// </summary>
public enum SchedulerCounters
{
    /// <summary>
    /// The rate of workitems that had to be promoted to the global queue.
    /// </summary>
    LocalToGlobalPromotions,

    /// <summary>
    /// The number of messages in the thread-local queues.
    /// </summary>
    LocalQueueCount,

    /// <summary>
    /// The rate of executed workitems.
    /// </summary>
    WorkitemsPerSecond,

    /// <summary>
    /// The rate of executed workitems from the global workitem queue.
    /// </summary>
    GlobalWorkitemsPerSecond,

    /// <summary>
    /// The rate of executed workitems from the thread-local workitem queues.
    /// </summary>
    LocalWorkitemsPerSecond,

    /// <summary>
    /// The rate of executed workitems executed synchronously, without queuing.
    /// </summary>
    ImmediateWorkitemsPerSecond,

    /// <summary>
    /// The count of active threads.
    /// </summary>
    ActiveThreads,
}
