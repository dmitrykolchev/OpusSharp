// <copyright file="PriorityQueue.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Threading;

namespace Microsoft.Psi.Scheduling;

/// <summary>
/// A generic ordered queue that sorts items based on the specified Comparer.
/// </summary>
/// <typeparam name="T">Type of item in the list.</typeparam>
public abstract class PriorityQueue<T> : IDisposable
{
    // the head of the ordered work item list is always empty
    private readonly PriorityQueueNode _head = new(0);
    private readonly PriorityQueueNode _emptyHead = new(0);
    private readonly Comparison<T> _comparer;
    private readonly ManualResetEvent _empty = new(true);
    private IPerfCounterCollection<PriorityQueueCounters> _counters;
    private int _count;
    private int _nextId;

    /// <summary>
    /// Initializes a new instance of the <see cref="PriorityQueue{T}"/> class.
    /// </summary>
    /// <param name="name">Priority queue name.</param>
    /// <param name="comparer">Comparison function.</param>
    public PriorityQueue(string name, Comparison<T> comparer)
    {
        _comparer = comparer;
    }

    /// <summary>
    /// Gets count of items in queue.
    /// </summary>
    public int Count => _count;

    internal WaitHandle Empty => _empty;

    /// <inheritdoc/>
    public void Dispose()
    {
        _empty.Dispose();
    }

    /// <summary>
    /// Try peeking at first item; returning indication of success.
    /// </summary>
    /// <param name="workitem">Work item populated if successful.</param>
    /// <returns>Indication of success.</returns>
    public bool TryPeek(out T workitem)
    {
        workitem = default;
        if (_count == 0)
        {
            return false;
        }

        // lock the head and the first work item
        PriorityQueueNode previous = _head;
        int retries = previous.Lock();
        PriorityQueueNode current = previous.Next;
        if (current == null)
        {
            return false;
        }

        current.Lock();
        workitem = current.Workitem;
        current.Release();
        previous.Release();

        return true;
    }

    /// <summary>
    /// Try to dequeue work item; returning indication of success.
    /// </summary>
    /// <param name="workitem">Work item populated if successful.</param>
    /// <param name="getAnyMatchingItem">Whether to match any item (or only first).</param>
    /// <returns>Indication of success.</returns>
    public bool TryDequeue(out T workitem, bool getAnyMatchingItem = true)
    {
        // keep looping until we either get a work item, or the list changed under us
        workitem = default;
        bool found = false;
        if (_count == 0)
        {
            return false;
        }

        // as we traverse the list of nodes, we use two locks, on previous and current, to ensure consistency
        // start by taking a lock on the head first, which is immutable (not an actual work item)
        PriorityQueueNode previous = _head;
        int retries = previous.Lock();
        PriorityQueueNode current = previous.Next;
        while (current != null)
        {
            retries += current.Lock();

            // we got the node, now see if it's ready
            if (DequeueCondition(current.Workitem))
            {
                // save the work item
                workitem = current.Workitem;
                found = true;

                // release the list
                previous.Next = current.Next;

                // add the node to the empty list
                current.Workitem = default;
                retries += _emptyHead.Lock();
                current.Next = _emptyHead.Next;
                _emptyHead.Next = current;
                _emptyHead.Release();
                current.Release();

                _counters?.Increment(PriorityQueueCounters.DequeueingCount);
                _counters?.Decrement(PriorityQueueCounters.WorkitemCount);
                break;
            }

            // keep going through the list
            previous.Release();
            previous = current;
            current = current.Next;

            if (!getAnyMatchingItem)
            {
                break;
            }
        }

        previous.Release();

        if (found && Interlocked.Decrement(ref _count) == 0)
        {
            _empty.Set();
        }

        _counters?.IncrementBy(PriorityQueueCounters.DequeuingRetries, retries);
        return found;
    }

    /// <summary>
    /// Enqueue work item.
    /// </summary>
    /// <remarks>
    /// Enqueuing is O(n), but since we re-enqueue the oldest originating time many times as it is processed by the pipeline,
    /// the dominant operation is to enqueue at the beginning of the queue.
    /// </remarks>
    /// <param name="workitem">Work item to enqueue.</param>
    public void Enqueue(T workitem)
    {
        // reset the empty signal as needed
        if (_count == 0)
        {
            _empty.Reset();
        }

        // take the head of the empty list
        int retries = _emptyHead.Lock();
        PriorityQueueNode newNode = _emptyHead.Next;
        if (newNode != null)
        {
            _emptyHead.Next = newNode.Next;
        }
        else
        {
            newNode = new PriorityQueueNode(Interlocked.Increment(ref _nextId));
        }

        _emptyHead.Release();

        newNode.Workitem = workitem;

        // insert it in the right place
        retries += Enqueue(newNode);
        _counters?.Increment(PriorityQueueCounters.EnqueueingCount);
        _counters?.Increment(PriorityQueueCounters.WorkitemCount);
        _counters?.IncrementBy(PriorityQueueCounters.EnqueueingRetries, retries);
    }

    /// <summary>
    /// Enable performance counters.
    /// </summary>
    /// <param name="name">Instance name.</param>
    /// <param name="perf">Performance counters implementation (platform specific).</param>
    public void EnablePerfCounters(string name, IPerfCounters<PriorityQueueCounters> perf)
    {
        const string Category = "Microsoft Psi scheduler queue";

        if (_counters != null)
        {
            throw new InvalidOperationException("Perf counters are already enabled for this scheduler");
        }

#pragma warning disable SA1118 // Parameter must not span multiple lines
        perf.AddCounterDefinitions(
            Category,
            new Tuple<PriorityQueueCounters, string, string, PerfCounterType>[]
            {
                Tuple.Create(PriorityQueueCounters.WorkitemCount, "Workitem queue count", "The number of work items in the global queue", PerfCounterType.NumberOfItems32),
                Tuple.Create(PriorityQueueCounters.EnqueuingTime, "Enqueuing time", "The time to enqueue a work item", PerfCounterType.NumberOfItems32),
                Tuple.Create(PriorityQueueCounters.DequeueingTime, "Dequeuing time", "The time to dequeuing a work item", PerfCounterType.NumberOfItems32),
                Tuple.Create(PriorityQueueCounters.EnqueueingRetries, "Enqueuing retry average", "The number of retries per work item enqueue operation.", PerfCounterType.AverageCount64),
                Tuple.Create(PriorityQueueCounters.EnqueueingCount, "Enqueue count", "The base counter for computing the work item enqueuing retry count.", PerfCounterType.AverageBase),
                Tuple.Create(PriorityQueueCounters.DequeuingRetries, "Dequeuing retry average", "The number of retries per work item dequeue operation.", PerfCounterType.AverageCount64),
                Tuple.Create(PriorityQueueCounters.DequeueingCount, "Dequeue count", "The base counter for computing the work item enqueuing retry count.", PerfCounterType.AverageBase),
            });
#pragma warning restore SA1118 // Parameter must not span multiple lines

        _counters = perf.Enable(Category, name);
    }

    /// <summary>
    /// Predicate function condition under which to dequeue.
    /// </summary>
    /// <param name="item">Candidate item.</param>
    /// <returns>Whether to dequeue.</returns>
    protected abstract bool DequeueCondition(T item);

    private int Enqueue(PriorityQueueNode node)
    {
        // we'll insert the node between "previous" and "next"
        PriorityQueueNode previous = _head;
        int retries = previous.Lock();
        PriorityQueueNode next = _head.Next;
        while (next != null && _comparer(node.Workitem, next.Workitem) > 0)
        {
            retries += next.Lock();
            previous.Release();
            previous = next;
            next = previous.Next;
        }

        node.Next = previous.Next;
        previous.Next = node;

        // increment the count and signal the empty queue if needed, before releasing the previous node
        // If we didn't and this was a 0-1 transition of this.count, another thread could dequeue and go to -1,
        // we would still bring it back to 0, but we would miss signaling the empty queue.
        if (Interlocked.Increment(ref _count) == 1)
        {
            _empty.Reset();
        }

        previous.Release();
        return retries;
    }

#pragma warning disable SA1401 // Fields must be private
    private class PriorityQueueNode
    {
        public T Workitem;

        private readonly SynchronizationLock _simpleLock;
        private PriorityQueueNode _next;
        private readonly int _id;

        public PriorityQueueNode(int id)
        {
            _id = id;
            _simpleLock = new SynchronizationLock(this, false);
        }

        public PriorityQueueNode Next
        {
            get => _next;

            set
            {
                if (value != null && value._id == _id)
                {
                    throw new InvalidOperationException("A node is pointing to itself.");
                }

                _next = value;
            }
        }

        public int Lock()
        {
            return _simpleLock.Lock();
        }

        public void Release()
        {
            _simpleLock.Release();
        }
    }
#pragma warning restore SA1401 // Fields must be private
}
