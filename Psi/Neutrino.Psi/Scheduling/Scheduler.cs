// <copyright file="Scheduler.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Threading;
using Neutrino.Psi.Common;
using Neutrino.Psi.Common.PerfCounters;

namespace Neutrino.Psi.Scheduling;

/// <summary>
/// Maintains a queue of workitems and schedules worker threads to empty them.
/// </summary>
public sealed class Scheduler : IDisposable
{
    private readonly string _name;
    private readonly SimpleSemaphore _threadSemaphore;
    private readonly Func<Exception, bool> _errorHandler;
    private readonly bool _allowSchedulingOnExternalThreads;
    private readonly ManualResetEvent _stopped = new(true);
    private readonly AutoResetEvent _futureQueuePulse = new(false);
    private readonly ThreadLocal<WorkItem?> _nextWorkitem = new();
    private readonly ThreadLocal<bool> _isSchedulerThread = new(() => false);
    private readonly ThreadLocal<DateTime> _currentWorkitemTime = new(() => DateTime.MaxValue);

    // the queue of pending workitems, ordered by start time
    private readonly WorkItemQueue _globalWorkitems;
    private readonly FutureWorkItemQueue _futureWorkitems;
    private Thread _futuresThread;
    private IPerfCounterCollection<SchedulerCounters> _counters;
    private bool _forcedShutdownRequested;
    private Clock _clock;
    private bool _delayFutureWorkItemsUntilDue;
    private bool _started = false;
    private bool _completed = false;

    /// <summary>
    /// Initializes a new instance of the <see cref="Scheduler"/> class.
    /// </summary>
    /// <param name="errorHandler">Error handler function.</param>
    /// <param name="threadCount">Scheduler thread count.</param>
    /// <param name="allowSchedulingOnExternalThreads">Allow scheduling on external threads.</param>
    /// <param name="name">Scheduler name.</param>
    /// <param name="clock">Optional clock to use (defaults to virtual time offset of DateTime.MinValue and dilation factor of 0).</param>
    public Scheduler(Func<Exception, bool> errorHandler, int threadCount = 0, bool allowSchedulingOnExternalThreads = false, string name = "default", Clock clock = null)
    {
        _name = name;
        _errorHandler = errorHandler;
        _threadSemaphore = new SimpleSemaphore((threadCount == 0) ? Environment.ProcessorCount * 2 : threadCount);
        _allowSchedulingOnExternalThreads = allowSchedulingOnExternalThreads;
        _globalWorkitems = new WorkItemQueue(name);
        _futureWorkitems = new FutureWorkItemQueue(name + "_future", this);

        // set virtual time such that any scheduled item appears to be in the future and gets queued in the future workitem queue
        // the time will change when the scheduler is started, and the future workitem queue will be drained then as appropriate
        _clock = clock ?? new Clock(DateTime.MinValue, 0);
        _delayFutureWorkItemsUntilDue = true;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Scheduler"/> class.
    /// </summary>
    /// <param name="parent">Parent scheduler from which to inherit settings.</param>
    internal Scheduler(Scheduler parent)
        : this(parent._errorHandler, parent._threadSemaphore.MaxThreadCount, parent._allowSchedulingOnExternalThreads, $"Sub{parent._name}", new Clock(parent._clock))
    {
    }

    /// <summary>
    /// Gets the scheduler clock.
    /// </summary>
    public Clock Clock => _clock;

    /// <summary>
    /// Gets a value indicating whether the scheduler should delay future work items until they're due.
    /// </summary>
    internal bool DelayFutureWorkItemsUntilDue => _delayFutureWorkItemsUntilDue;

    internal bool IsStarted => !_stopped.WaitOne(0);

    /// <inheritdoc/>
    public void Dispose()
    {
        _threadSemaphore.Dispose();
        _stopped.Dispose();
        _futureQueuePulse.Dispose();
        _globalWorkitems.Dispose();
        _futureWorkitems.Dispose();
        _nextWorkitem.Dispose();
        _isSchedulerThread.Dispose();
        _currentWorkitemTime.Dispose();
    }

    /// <summary>
    /// Attempts to execute the delegate immediately, on the calling thread, without scheduling.
    /// </summary>
    /// <typeparam name="T">Action argument type.</typeparam>
    /// <param name="synchronizationObject">Synchronization object.</param>
    /// <param name="action">Action to execute.</param>
    /// <param name="argument">Action argument.</param>
    /// <param name="startTime">Scheduled start time.</param>
    /// <param name="context">The scheduler context on which to execute the action.</param>
    /// <param name="outputActionTiming">Indicates whether to output action timing information.</param>
    /// <param name="actionStartTime">The time the runtime started executing the action.</param>
    /// <param name="actionEndTime">The time the runtime ended executing the action.</param>
    /// <returns>Success flag.</returns>
    public bool TryExecute<T>(
        SynchronizationLock synchronizationObject,
        Action<T> action,
        T argument,
        DateTime startTime,
        SchedulerContext context,
        bool outputActionTiming,
        out DateTime actionStartTime,
        out DateTime actionEndTime)
    {
        actionStartTime = DateTime.MinValue;
        actionEndTime = DateTime.MinValue;

        if (_forcedShutdownRequested || startTime > context.FinalizeTime)
        {
            actionStartTime = actionEndTime = _clock.GetCurrentTime();
            return true;
        }

        if (startTime > _clock.GetCurrentTime() && _delayFutureWorkItemsUntilDue)
        {
            return false;
        }

        if (!_isSchedulerThread.Value && !_allowSchedulingOnExternalThreads)
        {
            // this thread is not ours, so return
            return false;
        }

        // try to acquire a lock on the sync context
        // however, if this thread already has the lock, we have to give up to keep the no-reentrancy guarantee
        if (!synchronizationObject.TryLock())
        {
            return false;
        }

        try
        {
            // Unlike ExecuteAndRelease, which assumes that the context has already been entered (e.g.
            // when the work item was first created), we need to explicitly enter the context prior to
            // running the action. The context will be exited in the finally clause.
            context.Enter();

            _counters?.Increment(SchedulerCounters.WorkitemsPerSecond);
            _counters?.Increment(SchedulerCounters.ImmediateWorkitemsPerSecond);

            if (outputActionTiming)
            {
                actionStartTime = _clock.GetCurrentTime();
            }

            action(argument);
        }
        catch (Exception e) when (_errorHandler(e))
        {
        }
        finally
        {
            if (outputActionTiming)
            {
                actionEndTime = _clock.GetCurrentTime();
            }

            synchronizationObject.Release();
            context.Exit();
        }

        return true;
    }

    /// <summary>
    /// Attempts to execute the delegate immediately, on the calling thread, without scheduling.
    /// </summary>
    /// <param name="synchronizationObject">Synchronization object.</param>
    /// <param name="action">Action to execute.</param>
    /// <param name="startTime">Scheduled start time.</param>
    /// <param name="context">The scheduler context on which to execute the action.</param>
    /// <returns>Success flag.</returns>
    public bool TryExecute(SynchronizationLock synchronizationObject, Action action, DateTime startTime, SchedulerContext context)
    {
        if (_forcedShutdownRequested || startTime > context.FinalizeTime)
        {
            return true;
        }

        if (startTime > _clock.GetCurrentTime() && _delayFutureWorkItemsUntilDue)
        {
            return false;
        }

        if (!_isSchedulerThread.Value && !_allowSchedulingOnExternalThreads)
        {
            // this thread is not ours, so return
            return false;
        }

        // try to acquire a lock
        // however, if this thread already has the lock, we have to give up to keep the no-reentrancy guarantee
        if (!synchronizationObject.TryLock())
        {
            return false;
        }

        // ExecuteAndRelease assumes that the context has already been entered, so we must explicitly
        // enter it here. The context will be exited just prior to returning from ExecuteAndRelease.
        context.Enter();
        ExecuteAndRelease(synchronizationObject, action, context);
        _counters?.Increment(SchedulerCounters.ImmediateWorkitemsPerSecond);
        return true;
    }

    /// <summary>
    /// Enqueues a workitem and, if possible, kicks off a worker thread to pick it up.
    /// </summary>
    /// <param name="synchronizationObject">Synchronization object.</param>
    /// <param name="action">Action to execute.</param>
    /// <param name="startTime">Scheduled start time.</param>
    /// <param name="context">The scheduler context on which to schedule the action.</param>
    /// <param name="asContinuation">Flag whether to execute once current operation completes.</param>
    /// <param name="allowSchedulingPastFinalization">Allow scheduling past finalization time.</param>
    public void Schedule(SynchronizationLock synchronizationObject, Action action, DateTime startTime, SchedulerContext context, bool asContinuation = true, bool allowSchedulingPastFinalization = false)
    {
        if (synchronizationObject == null || action == null || context == null)
        {
            throw new ArgumentNullException();
        }

        if (!allowSchedulingPastFinalization && startTime > context.FinalizeTime)
        {
            return;
        }

        // Enter the context to track this new work item. The context will be exited
        // only after the work item has been successfully executed (or dropped).
        context.Enter();
        Schedule(new WorkItem() { SyncLock = synchronizationObject, Callback = action, StartTime = startTime, SchedulerContext = context }, asContinuation);
    }

    /// <summary>
    /// Prevent further scheduling.
    /// </summary>
    /// <param name="synchronizationObject">Synchronization object.</param>
    public void Freeze(SynchronizationLock synchronizationObject)
    {
        synchronizationObject.Hold();
    }

    /// <summary>
    /// Allow further scheduling.
    /// </summary>
    /// <param name="synchronizationObject">Synchronization object.</param>
    public void Thaw(SynchronizationLock synchronizationObject)
    {
        synchronizationObject.Release();
    }

    /// <summary>
    /// Starts the scheduler. This sets the scheduler clock and a flag indicating whether work items
    /// should be scheduled only when they are due based on the clock time or as soon as possible.
    /// </summary>
    /// <param name="clock">Scheduler clock.</param>
    /// <param name="delayFutureWorkitemsUntilDue">Delay future workitems until scheduled time.</param>
    public void Start(Clock clock, bool delayFutureWorkitemsUntilDue)
    {
        if (!_stopped.WaitOne(0))
        {
            throw new InvalidOperationException("The scheduler was already started.");
        }

        // if no clock is specified, schedule everything without delay
        _delayFutureWorkItemsUntilDue = delayFutureWorkitemsUntilDue;
        _clock = clock;
        _started = true;
        _stopped.Reset();
        StartFuturesThread();
    }

    /// <summary>
    /// Reject any new scheduling and block until running threads finish their current work.
    /// </summary>
    /// <remarks>Assumes source components have been shut down.</remarks>
    /// <param name="abandonPendingWorkitems">Whether to abandon queued/future workitems.</param>
    public void Stop(bool abandonPendingWorkitems = false)
    {
        if (_stopped.WaitOne(0))
        {
            return;
        }

        _forcedShutdownRequested = abandonPendingWorkitems;
        _stopped.Set();
        _futuresThread.Join();
        _clock = new Clock(DateTime.MinValue, 0);
        _completed = true;
        _delayFutureWorkItemsUntilDue = true;
    }

    /// <summary>
    /// Starts scheduling work on the given context.
    /// </summary>
    /// <param name="context">The context on which scheduling should start.</param>
    public void StartScheduling(SchedulerContext context)
    {
        context.Start(Clock);
    }

    /// <summary>
    /// Stops scheduling further work items on the given context.
    /// </summary>
    /// <param name="context">The context on which scheduling should cease.</param>
    public void StopScheduling(SchedulerContext context)
    {
        // wait for already scheduled work to finish
        PauseForQuiescence(context);
        context.Stop();
    }

    /// <summary>
    /// Enable performance counters.
    /// </summary>
    /// <param name="name">Instance name.</param>
    /// <param name="perf">Performance counters implementation (platform specific).</param>
    public void EnablePerfCounters(string name, IPerfCounters<SchedulerCounters> perf)
    {
        const string Category = "Microsoft Psi scheduler";

        if (_counters != null)
        {
            throw new InvalidOperationException("Perf counters are already enabled for this scheduler");
        }

#pragma warning disable SA1118 // Parameter must not span multiple lines
        perf.AddCounterDefinitions(
            Category,
            new Tuple<SchedulerCounters, string, string, PerfCounterType>[]
            {
                Tuple.Create(SchedulerCounters.LocalToGlobalPromotions, "Local-to-global promotions", "The percentage of workitems promoted to the global queue", PerfCounterType.AverageCount64),
                Tuple.Create(SchedulerCounters.LocalQueueCount, "Local workitem count", "The number of messages in the thread-local queues", PerfCounterType.AverageBase),
                Tuple.Create(SchedulerCounters.WorkitemsPerSecond, "Workitems / second", "The number of workitems executed per second", PerfCounterType.RateOfCountsPerSecond32),
                Tuple.Create(SchedulerCounters.GlobalWorkitemsPerSecond, "Global workitems / second", "The number of workitems from the global queue executed per second", PerfCounterType.RateOfCountsPerSecond32),
                Tuple.Create(SchedulerCounters.LocalWorkitemsPerSecond, "Local workitems / second", "The number of workitems from the thread-local queues executed per second", PerfCounterType.RateOfCountsPerSecond32),
                Tuple.Create(SchedulerCounters.ImmediateWorkitemsPerSecond, "Immediate workitems / second", "The number of workitems executed synchronously without enqueuing, per second", PerfCounterType.RateOfCountsPerSecond32),
                Tuple.Create(SchedulerCounters.ActiveThreads, "Active threads", "The count of active threads", PerfCounterType.NumberOfItems32),
            });
#pragma warning restore SA1118 // Parameter must not span multiple lines

        _counters = perf.Enable(Category, name);
    }

    /// <summary>
    /// Pause scheduler until all scheduled work items on the given context have completed.
    /// </summary>
    /// <param name="context">The scheduler context on which to wait.</param>
    internal void PauseForQuiescence(SchedulerContext context)
    {
        if (context.Empty.WaitOne(0) || !_started || _completed)
        {
            return;
        }

        // Pulse the future queue to process any work items that are either due or
        // non-reachable due to having a start time past the finalization time.
        _futureQueuePulse.Set();

        if (!_isSchedulerThread.Value && !_allowSchedulingOnExternalThreads)
        {
            // this is not a scheduler thread, so just wait for the context to empty and return
            context.Empty.WaitOne();
            return;
        }

        // continue executing work items on this scheduler thread while waiting for the context to empty
        while (!_forcedShutdownRequested && !context.Empty.WaitOne(0))
        {
            WorkItem wi;

            if (_nextWorkitem.Value != null)
            {
                // if there is a queued local work item, try to run it first
                wi = (WorkItem)_nextWorkitem.Value;
                _nextWorkitem.Value = null;
                _counters?.Decrement(SchedulerCounters.LocalQueueCount);
                if (wi.SyncLock.TryLock())
                {
                    _counters?.Increment(SchedulerCounters.LocalWorkitemsPerSecond);
                    goto ExecuteWorkItem;
                }
                else
                {
                    // it's locked, so let someone else have it
                    Schedule(wi, false);
                    _counters?.Increment(SchedulerCounters.LocalToGlobalPromotions);
                }
            }

            // try to get another item from the global queue
            if (!_globalWorkitems.TryDequeue(out wi))
            {
                // no work left
                continue;
            }

            _counters?.Increment(SchedulerCounters.GlobalWorkitemsPerSecond);

        ExecuteWorkItem:
            _currentWorkitemTime.Value = wi.StartTime;
            ExecuteAndRelease(wi.SyncLock, wi.Callback, wi.SchedulerContext);
        }
    }

    /// <summary>
    /// Enqueues a workitem and, if possible, kicks off a worker thread to pick it up.
    /// </summary>
    /// <param name="wi">The work item to schedule.</param>
    /// <param name="asContinuation">Flag whether to execute once current operation completes.</param>
    private void Schedule(WorkItem wi, bool asContinuation = true)
    {
        // Exit the context without executing the work item if:
        // (1) a forced shutdown has been initiated
        // (2) the scheduler has already been stopped (completed)
        if (_forcedShutdownRequested || _completed)
        {
            wi.SchedulerContext.Exit();
            return;
        }

        // if the work item is not yet due, add it to the future work item queue, as long as it is not after the finalize time
        if ((wi.StartTime > wi.SchedulerContext.Clock.GetCurrentTime() && wi.StartTime <= wi.SchedulerContext.FinalizeTime && _delayFutureWorkItemsUntilDue) ||
            !_started || !wi.SchedulerContext.Started)
        {
            _futureWorkitems.Enqueue(wi);
            _futureQueuePulse.Set();
            return;
        }

        // try to kick off another thread now, without going through the global queue
        if (wi.SyncLock.TryLock())
        {
            if (_threadSemaphore.TryEnter())
            {
                // there are threads available, which means the items in the global queue, if any, are locked
                // start a thread to do the work
                _counters?.Increment(SchedulerCounters.ImmediateWorkitemsPerSecond);
                ThreadPool.QueueUserWorkItem(Run, wi);
                return;
            }

            wi.SyncLock.Release();
        }

        // try to schedule on the local thread, to be executed once the current operation finishes, as long as the new workitem time is the same or less as the current one
        if (asContinuation && _isSchedulerThread.Value && _nextWorkitem.Value == null && wi.StartTime <= _currentWorkitemTime.Value)
        {
            // we own the thread, so schedule the work in the local queue
            _nextWorkitem.Value = wi;
            _counters?.Increment(SchedulerCounters.LocalQueueCount);
            return;
        }

        // last resort, add the workitem to the global queue
        _globalWorkitems.Enqueue(wi);

        // if a thread became available, it might have missed the workitem being enqueued, so retry to make sure
        if (_threadSemaphore.TryEnter())
        {
            if (_globalWorkitems.TryDequeue(out wi))
            {
                _counters?.Increment(SchedulerCounters.GlobalWorkitemsPerSecond);
                ThreadPool.QueueUserWorkItem(Run, wi);
            }
            else
            {
                _threadSemaphore.Exit();
            }
        }
    }

    private void StartFuturesThread()
    {
        _futuresThread = new Thread(new ThreadStart(ProcessFutureQueue)) { IsBackground = true };
        Platform.Specific.SetApartmentState(_futuresThread, ApartmentState.MTA);
        _futuresThread.Start();
    }

    // a thread enters Run as a result of a workitem being enqueued when the thread limit is not yet reached
    // a thread exits Run when it traverses the workitem queue and no workitem is ready to execute (which means other threads are executing them).
    private void Run(object workItem)
    {
        _counters?.Increment(SchedulerCounters.ActiveThreads);
        _isSchedulerThread.Value = true;
        string threadName = Thread.CurrentThread.Name;
        bool completed = false;

        try
        {
            WorkItem wi;
            if (workItem != null)
            {
                wi = (WorkItem)workItem;
            }
            else
            {
                // we got scheduled without a workitem, as a result of some other thread crashing
                // try to get another item from the global queue
                if (!_globalWorkitems.TryDequeue(out wi))
                {
                    // no work left
                    completed = true;
                    return;
                }
            }

            // unless asked to shut down, keep trying to get items until no other workitems are ready
            while (!_forcedShutdownRequested)
            {
                _currentWorkitemTime.Value = wi.StartTime;
                ExecuteAndRelease(wi.SyncLock, wi.Callback, wi.SchedulerContext);

                // process the next local wi, if one is present
                if (_nextWorkitem.Value != null)
                {
                    wi = (WorkItem)_nextWorkitem.Value;
                    _nextWorkitem.Value = null;
                    _counters?.Decrement(SchedulerCounters.LocalQueueCount);
                    if (wi.SyncLock.TryLock())
                    {
                        _counters?.Increment(SchedulerCounters.LocalWorkitemsPerSecond);
                        continue;
                    }

                    // it's locked, so let someone else have it
                    Schedule(wi, false);
                    _counters?.Increment(SchedulerCounters.LocalToGlobalPromotions);
                }

                // try to get another item from the global queue
                if (!_globalWorkitems.TryDequeue(out wi))
                {
                    // no work left
                    completed = true;
                    return;
                }

                _counters?.Increment(SchedulerCounters.GlobalWorkitemsPerSecond);
            }
        }
        finally
        {
            _isSchedulerThread.Value = false;
            _currentWorkitemTime.Value = DateTime.MaxValue;
            _counters?.Decrement(SchedulerCounters.ActiveThreads);

            if (completed)
            {
                _threadSemaphore.Exit();
            }
            else
            {
                // we got here because of an exception. let the exception bubble but start another thread.
                ThreadPool.QueueUserWorkItem(Run, null);
            }
        }
    }

    private void ExecuteAndRelease(SynchronizationLock synchronizationObject, Action action, SchedulerContext context)
    {
        try
        {
            action();
            _counters?.Increment(SchedulerCounters.WorkitemsPerSecond);
        }
        catch (Exception e) when (_errorHandler(e))
        {
        }
        finally
        {
            synchronizationObject.Release();
            context.Exit();
        }
    }

    // schedules workitems due from the future queue
    private void ProcessFutureQueue()
    {
        int waitTimeout = -1;

        EventWaitHandle[] workReadyHandles = new EventWaitHandle[] { _stopped, _futureQueuePulse };
        WaitHandle[] allHandles = new[] { _threadSemaphore.Empty, _globalWorkitems.Empty, _futureWorkitems.Empty };
        SpinWait spinWait = default;
        while (true)
        {
            int waitResult = WaitHandle.WaitAny(workReadyHandles, waitTimeout);
            if (waitResult == 0)
            {
                // waitResult == 0 means this.stopped is signaled
                if (_forcedShutdownRequested)
                {
                    // this.stopped is signaled and we need to exit right away.
                    return;
                }

                // Check that all workitems have been processed, keep processing otherwise
                if (WaitHandle.WaitAll(allHandles, 0))
                {
                    // all work is done
                    return;
                }

                // check if there are any queued future work items
                if (_futureWorkitems.Count == 0)
                {
                    // spin to ensure we yield to other scheduler threads
                    spinWait.SpinOnce();
                }
            }

            // get any items that are due
            WorkItem wi;
            while (_futureWorkitems.TryDequeue(out wi, false))
            {
                Schedule(wi, false);
            }

            if (_futureWorkitems.TryPeek(out wi))
            {
                // the result could be a negative value if some other thread captured "now" before us and added the item to the future queue after the while loop above exited
                waitTimeout = _delayFutureWorkItemsUntilDue ? (int)_clock.ToRealTime(wi.StartTime - _clock.GetCurrentTime()).TotalMilliseconds : 0;
                if (waitTimeout < 0)
                {
                    waitTimeout = 0;
                }
            }
            else
            {
                waitTimeout = -1;
            }
        }
    }
}
