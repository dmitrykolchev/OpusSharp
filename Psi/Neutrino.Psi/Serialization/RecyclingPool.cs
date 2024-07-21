// <copyright file="RecyclingPool.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Neutrino.Psi.Serialization;

namespace Neutrino.Psi;

/// <summary>
/// Message recycling pool class.
/// </summary>
public class RecyclingPool
{
    /// <summary>
    /// Creates an appropriate recycling pool for the specified type.
    /// </summary>
    /// <param name="debugTrace">An optional debug trace to capture for debugging purposes.</param>
    /// <typeparam name="T">The type of objects to store in the recycling pool.</typeparam>
    /// <returns>A new recycling pool.</returns>
    public static IRecyclingPool<T> Create<T>(StackTrace debugTrace = null)
    {
        if (!Serializer.IsImmutableType<T>())
        {
            return new Cloner<T>(debugTrace);
        }

        return FakeCloner<T>.Default;
    }

    /// <summary>
    /// Maintains a cache of unused instances that can be use as cloning or deserialization targets.
    /// This class is not thread safe.
    /// </summary>
    /// <typeparam name="T">The type of instances that can be cached by this cloner.</typeparam>
    private class Cloner<T> : IRecyclingPool<T>
    {
        private const int MaxAllocationsWithoutRecycling = 100;
        private readonly SerializationContext _serializationContext = new();
        private readonly Stack<T> _free = new(); // not ConcurrentStack because ConcurrentStack performs an allocation for each Push. We want to be allocation free.
#if TRACKLEAKS
        private readonly StackTrace _debugTrace;
        private bool _recycledOnce;
#endif

        private int _outstandingAllocationCount;

        public Cloner(StackTrace debugTrace = null)
        {
#if TRACKLEAKS
            _debugTrace = debugTrace ?? new StackTrace(true);
#endif
        }

        /// <summary>
        /// Gets the number of available allocations that have been already returned to the pool.
        /// </summary>
        public int AvailableAllocationCount => _free.Count;

        /// <summary>
        /// Gets the number of allocations that have not yet been returned to the pool.
        /// </summary>
        public int OutstandingAllocationCount => _outstandingAllocationCount;

        /// <summary>
        /// Returns the next available cached object.
        /// </summary>
        /// <returns>An unused cached object that can be reused as a target for cloning or deserialization.</returns>
        public T Get()
        {
            T clone;
            lock (_free)
            {
                if (_free.Count > 0)
                {
                    clone = _free.Pop();
                }
                else
                {
                    clone = default;
                }

                _outstandingAllocationCount++;
            }
#if TRACKLEAKS
            // alert if the component is not recycling messages
            if (!_recycledOnce && _outstandingAllocationCount == MaxAllocationsWithoutRecycling && _debugTrace != null)
            {
                StringBuilder sb = new("\\psi output **********************************************");
                sb.AppendLine($"This component is not recycling messages {typeof(T)} (no recycling after {_outstandingAllocationCount} allocations). Constructor stack trace below:");
                foreach (StackFrame frame in _debugTrace.GetFrames())
                {
                    sb.AppendLine($"{frame.GetFileName()}({frame.GetFileLineNumber()}): {frame.GetMethod().DeclaringType}.{frame.GetMethod().Name}");
                }

                sb.AppendLine("**********************************************************");
                Debug.WriteLine(sb.ToString());
            }
#endif
            return clone;
        }

        /// <summary>
        /// Returns an unused object back to the pool.
        /// The caller must guarantee that the entire object tree (the object and any of the objects it references) are not in use anymore.
        /// </summary>
        /// <param name="freeInstance">The object to return to the pool.</param>
        public void Recycle(T freeInstance)
        {
            lock (_free)
            {
                Serializer.Clear(ref freeInstance, _serializationContext);
                _serializationContext.Reset();

                _free.Push(freeInstance);
                _outstandingAllocationCount--;
#if TRACKLEAKS
                _recycledOnce = true;
#endif
            }
        }
    }

    /// <summary>
    /// Used for immutable types.
    /// </summary>
    /// <typeparam name="T">The immutable type.</typeparam>
    private class FakeCloner<T> : IRecyclingPool<T>
    {
        public static readonly IRecyclingPool<T> Default = new FakeCloner<T>();

        public int OutstandingAllocationCount => 0;

        public int AvailableAllocationCount => 0;

        public T Get()
        {
            return default;
        }

        public void Recycle(T freeInstance)
        {
        }
    }
}
