// <copyright file="PipelineElement.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Neutrino.Psi.Components;
using Neutrino.Psi.Scheduling;

namespace Neutrino.Psi.Executive;

/// <summary>
/// Class that encapsulates the execution context of a component (the state object, the sync object, the component wiring etc.)
/// </summary>
internal class PipelineElement
{
#if DEBUG
    /// <summary>
    /// Slot for execution context local tracking of state object (receiver's owner or start/stop/final state object).
    /// </summary>
    private static readonly AsyncLocal<object> ExecutionContextStateObjectSlot = new();

    /// <summary>
    /// Slot for execution context local tracking of pipeline instance.
    /// </summary>
    private static readonly AsyncLocal<Pipeline> ExecutionContextPipelineSlot = new();
#endif

    private static readonly ConcurrentDictionary<object, SynchronizationLock> Locks = new();

    private readonly int _id;
    private readonly ConcurrentDictionary<string, IEmitter> _outputs = new();
    private readonly ConcurrentDictionary<string, IReceiver> _inputs = new();
    private readonly SynchronizationLock _syncContext;
    private readonly string _name;

    private object _stateObject;
    private Pipeline _pipeline;
    private State _state = State.Initial;
    private DateTime _finalOriginatingTime;

    /// <summary>
    /// Initializes a new instance of the <see cref="PipelineElement"/> class.
    /// </summary>
    /// <param name="id">The ID of the instance.</param>
    /// <param name="name">The name of the instance.</param>
    /// <param name="stateObject">The state object wrapped by this node.</param>
    public PipelineElement(int id, string name, object stateObject)
    {
        _id = id;
        _name = name;
        _stateObject = stateObject;
        IsSource = stateObject is ISourceComponent;
        _syncContext = Locks.GetOrAdd(stateObject, state => new SynchronizationLock(state));
    }

    private enum State
    {
        Initial,
        Activated,
        Deactivating,
        Deactivated,
        Finalized,
    }

    /// <summary>
    /// Gets pipeline element ID.
    /// </summary>
    public int Id => _id;

    /// <summary>
    /// Gets the name of the entity.
    /// </summary>
    public string Name => _name;

    public SynchronizationLock SyncContext => _syncContext;

    /// <summary>
    /// Gets a value indicating whether the component has been activated.
    /// </summary>
    internal bool IsActivated => _state == State.Activated;

    /// <summary>
    /// Gets a value indicating whether the component is deactivating - meaning that it will cease producing non-reactive source messages past the final originating time.
    /// </summary>
    internal bool IsDeactivating => _state == State.Deactivating;

    /// <summary>
    /// Gets a value indicating whether the component has been deactivated - meaning that it has ceased producing non-reactive source messages (from timers, sensors, etc.)
    /// </summary>
    internal bool IsDeactivated => _state == State.Deactivated;

    /// <summary>
    /// Gets a value indicating whether the component has been finalized - meaning that it should no longer be producing messages for any reason.
    /// </summary>
    internal bool IsFinalized => _state == State.Finalized;

    /// <summary>
    /// Gets a value indicating whether the entity is a source component
    /// Generally this means it produces messages on its own thread rather than in response to other messages.
    /// </summary>
    internal bool IsSource { get; private set; }

    internal object StateObject => _stateObject;

    internal ConcurrentDictionary<string, IEmitter> Outputs => _outputs;

    internal ConcurrentDictionary<string, IReceiver> Inputs => _inputs;

    internal IEnumerable<string> InputNames => _inputs.Keys;

    /// <summary>
    /// Gets a value indicating whether node is a <see cref="Connector{T}"/> component.
    /// </summary>
    internal bool IsConnector
    {
        get
        {
            return typeof(IConnector).IsAssignableFrom(StateObject.GetType());
        }
    }

    /// <summary>
    /// Gets the envelope of the last message posted on any of this node's outputs.
    /// </summary>
    internal Envelope LastOutputEnvelope
    {
        get
        {
            Envelope lastEnvelope = default;
            foreach (IEmitter emitter in _outputs.Values)
            {
                // we define "last" as being the envelope with the latest originating time seen so far
                if (emitter.LastEnvelope.OriginatingTime > lastEnvelope.OriginatingTime)
                {
                    lastEnvelope = emitter.LastEnvelope;
                }
            }

            return lastEnvelope;
        }
    }

    /// <summary>
    /// Track state object in the execution context (in DEBUG only).
    /// </summary>
    /// <remarks>This allows checking that emitter post calls are from expected sources.</remarks>
    /// <param name="action">Action around which tracking will be instrumented.</param>
    /// <param name="owner">Owner/state object.</param>
    /// <param name="pipeline">Pipeline instance.</param>
    /// <returns>Action with tracking.</returns>
    internal static Action TrackStateObjectOnContext(Action action, object owner, Pipeline pipeline)
    {
#if DEBUG
        return () =>
        {
            // save any previous tracked state in order to restore it after the action
            (object prevOwner, Pipeline prevPipeline) = (ExecutionContextStateObjectSlot.Value, ExecutionContextPipelineSlot.Value);
            ExecutionContextStateObjectSlot.Value = owner;
            ExecutionContextPipelineSlot.Value = pipeline;
            action();
            ExecutionContextStateObjectSlot.Value = prevOwner;
            ExecutionContextPipelineSlot.Value = prevPipeline;
        };
#else
        return action; // no tracking
#endif
    }

    /// <summary>
    /// Track state object in the execution context.
    /// </summary>
    /// <remarks>This allows checking that emitter post calls are from expected sources.</remarks>
    /// <typeparam name="T">Type of action.</typeparam>
    /// <param name="action">Action around which tracking will be instrumented.</param>
    /// <param name="owner">Owner/state object.</param>
    /// <param name="pipeline">Pipeline instance.</param>
    /// <returns>Action with tracking.</returns>
    internal static Action<T> TrackStateObjectOnContext<T>(Action<T> action, object owner, Pipeline pipeline)
    {
        return m =>
        {
            TrackStateObjectOnContext(() => action(m), owner, pipeline)();
        };
    }

#if DEBUG
    /// <summary>
    /// Check that the current state object being tracked on the execution context is the expected owner (exception for source components).
    /// </summary>
    /// <param name="owner">Owner/state object.</param>
    /// <param name="pipeline">Pipeline instance.</param>
    internal static void CheckStateObjectOnContext(object owner, Pipeline pipeline)
    {
        object trackedOwner = ExecutionContextStateObjectSlot.Value;
        if (trackedOwner == null)
        {
            // this component should be a source
            if (!(owner is ISourceComponent))
            {
                throw new InvalidOperationException($"Emitter unexpectedly posted to from outside of a receiver (consider implementing {nameof(ISourceComponent)} if this is intentional - {owner}).");
            }
        }
        else
        {
            // this post is the result of a receiver (reactive component) - owners should match
            if (trackedOwner != owner)
            {
                throw new InvalidOperationException($"Emitter of one component unexpectedly received post from a receiver of another component ({trackedOwner} -> {owner}).");
            }

            // pipelines should match with the single exception of Connector bridging pipelines
            Pipeline trackedPipeline = ExecutionContextPipelineSlot.Value;
            if (trackedPipeline != pipeline && !(owner is IConnector))
            {
                throw new InvalidOperationException($"Emitter created in one pipeline unexpectedly received post from a receiver in another pipeline (consider using a Connector to construct such bridges).");
            }
        }
    }
#endif

    /// <summary>
    /// Delayed initialization of the state object. Note that we don't have a Scheduler yet.
    /// </summary>
    /// <param name="pipeline">The parent pipeline.</param>
    internal void Initialize(Pipeline pipeline)
    {
        _pipeline = pipeline;
        if (_state != State.Initial)
        {
            throw new InvalidOperationException($"Initialize was called on component {Name}, which is has already started (state={_state}).");
        }
    }

    /// <summary>
    /// Disposes the state object and turns off the receivers.
    /// </summary>
    internal void Dispose()
    {
        // disable the inputs
        foreach (IReceiver input in Inputs.Values)
        {
            input.Dispose();
        }

        if (_stateObject is IDisposable disposable)
        {
            disposable.Dispose();
        }

        Locks.TryRemove(_stateObject, out SynchronizationLock _);
        _stateObject = null;
    }

    /// <summary>
    /// Activates the entity - source components may begin producing non-reactive source messages. However, no messages
    /// will be delivered by the runtime to their recipients until all components in the pipeline have been activated.
    /// </summary>
    internal void Activate()
    {
        if (_state != State.Initial)
        {
            throw new InvalidOperationException($"Start was called on component {Name}, which is has already started (state={_state}).");
        }

        // tell the component it's being activated
        _state = State.Activated;

        // in addition, source components will be started upon activation
        if (IsSource)
        {
            // Start through the Scheduler to ensure exclusive execution of Start with respect to any receivers.
            // Use the ActivationContext so that the pipeline can wait for activation to complete before scheduling
            // delivery of messages.
            _pipeline.Scheduler.Schedule(
                _syncContext,
                TrackStateObjectOnContext(() => ((ISourceComponent)_stateObject).Start(OnNotifyCompletionTime), _stateObject, _pipeline),
                DateTime.MinValue,
                _pipeline.ActivationContext);
        }

        _pipeline.DiagnosticsCollector?.PipelineElementStart(_pipeline, this);
    }

    /// <summary>
    /// Deactivates the entity - meaning that it should cease producing non-reactive source messages (from timers, sensors, etc.)
    /// </summary>
    /// <param name="finalOriginatingTime">The final originating time.</param>
    internal void Deactivate(DateTime finalOriginatingTime)
    {
        if (_state == State.Activated)
        {
            // tell the component it is being deactivated, let any exception bubble up
            if (IsSource)
            {
                // this indicates that the component is stopping, but will not be considered deactivated until the component confirms via OnNotifyCompletionTime
                _state = State.Deactivating;

                // Stop through the Scheduler to ensure exclusive execution of Stop with respect to any receivers.
                // Schedule this on the pipeline's main scheduler context so that the pipeline will wait for component
                // deactivation and message delivery to complete during shutdown.
                _pipeline.Scheduler.Schedule(
                    _syncContext,
                    TrackStateObjectOnContext(() => ((ISourceComponent)_stateObject).Stop(finalOriginatingTime, OnNotifyCompleted), _stateObject, _pipeline),
                    DateTime.MinValue,
                    _pipeline.SchedulerContext);
            }
            else
            {
                // transition immediately to the deactivated state
                _state = State.Deactivated;
            }
        }
        else
        {
            // This is an early bug avoidance measure. Outside of component infrastructure, nothing should call stop
            throw new InvalidOperationException($"Stop was called on component {Name}, which is not active (state={_state}).");
        }

        _pipeline.DiagnosticsCollector?.PipelineElementStop(_pipeline, this);
    }

    /// <summary>
    /// Finalize the entity - meaning that it may produce final messages now and then should no longer be producing messages for any reason.
    /// </summary>
    /// <param name="finalOriginatingTime">The final originating time.</param>
    internal void Final(DateTime finalOriginatingTime)
    {
        if (_state != State.Finalized)
        {
            // close emitters unless this component was never started
            if (_state != State.Initial)
            {
                foreach (IEmitter emitter in Outputs.Values)
                {
                    emitter.Close(finalOriginatingTime);
                }
            }

            _state = State.Finalized;
        }

        _pipeline.DiagnosticsCollector?.PipelineElementFinal(_pipeline, this);
    }

    internal void OnNotifyCompletionTime(DateTime finalOriginatingTime)
    {
        _finalOriginatingTime = finalOriginatingTime;
        _pipeline.NotifyCompletionTime(this, finalOriginatingTime);
    }

    internal void OnNotifyCompleted()
    {
        _pipeline.NotifyCompletionTime(this, _finalOriginatingTime);

        // this is confirmation that an ISourceComponent has stopped producing non-reactive source messages
        _state = State.Deactivated;
    }

    internal IEmitter GetOutput(string name)
    {
        return _outputs[name];
    }

    internal IReceiver GetInput(string name)
    {
        return _inputs[name];
    }

    internal void AddOutput(string name, IEmitter output)
    {
        name ??= $"{Name}<{output.GetType().GetGenericArguments()[0].Name}> {output.GetHashCode()}";

        if (!_outputs.TryAdd(name, output))
        {
            throw new ArgumentException($"Cannot add another output named {name} because one already exists!");
        }

        _pipeline.DiagnosticsCollector?.PipelineElementAddEmitter(_pipeline, this, output);
    }

    internal void AddInput(string name, IReceiver input)
    {
        name ??= $"{Name}[{input.GetType().GetGenericArguments()[0].Name}] {input.GetHashCode()}";

        if (!_inputs.TryAdd(name, input))
        {
            throw new ArgumentException($"Cannot add another input named {name} because one already exists!");
        }

        _pipeline.DiagnosticsCollector?.PipelineElementAddReceiver(_pipeline, this, input);
    }
}
