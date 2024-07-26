﻿// <copyright file="Pipeline.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Neutrino.Psi.Common;
using Neutrino.Psi.Common.Intervals;
using Neutrino.Psi.Components;
using Neutrino.Psi.Connectors;
using Neutrino.Psi.Diagnostics;
using Neutrino.Psi.Scheduling;
using Neutrino.Psi.Streams;

namespace Neutrino.Psi.Executive;

/// <summary>
/// Represents a graph of components and controls scheduling and message passing.
/// </summary>
public class Pipeline : IDisposable
{
    private static int s_lastStreamId = 0;
    private static int s_lastReceiverId = -1;
    private static int s_lastElementId = -1;
    private static int s_lastPipelineId = -1;

    private readonly int _id;
    private readonly string _name;

    /// <summary>
    /// This event becomes set once the pipeline is done.
    /// </summary>
    private readonly ManualResetEvent _completed = new(false);

    private readonly KeyValueStore _configStore = new();

    private readonly DeliveryPolicy _defaultDeliveryPolicy;

    /// <summary>
    /// The list of completable components.
    /// </summary>
    private readonly List<PipelineElement> _completableComponents = [];

    private readonly Scheduler _scheduler;

    // the context on which message delivery is scheduled
    private readonly SchedulerContext _schedulerContext;

    // the context used exclusively for activating components
    private readonly SchedulerContext _activationContext;

    private readonly List<Exception> _errors = [];

    /// <summary>
    /// The list of components.
    /// </summary>
    private ConcurrentQueue<PipelineElement> _components = new();

    /// <summary>
    /// If set, indicates that the pipeline is in replay mode.
    /// </summary>
    private ReplayDescriptor _replayDescriptor;

    private TimeInterval _proposedOriginatingTimeInterval;

    private State _state;

    private bool _enableExceptionHandling;

    private Emitter<PipelineDiagnostics> _diagnosticsEmitter;

    private IProgress<double> _progressReporter;
    private Time.TimerDelegate _progressDelegate;
    private Platform.ITimer _progressTimer;
    private bool _pipelineRunEventHandled = false;
    private int _isPipelineDisposed = 0;

    /// <summary>
    /// Initializes a new instance of the <see cref="Pipeline"/> class.
    /// </summary>
    /// <param name="name">Pipeline name.</param>
    /// <param name="defaultDeliveryPolicy">Pipeline-level default delivery policy (defaults to <see cref="DeliveryPolicy.Unlimited"/> if unspecified).</param>
    /// <param name="threadCount">Number of threads.</param>
    /// <param name="allowSchedulingOnExternalThreads">Whether to allow scheduling on external threads.</param>
    /// <param name="enableDiagnostics">Whether to enable collecting and publishing diagnostics information on the Pipeline.Diagnostics stream.</param>
    /// <param name="diagnosticsConfiguration">Optional diagnostics configuration information.</param>
    public Pipeline(
        string name,
        DeliveryPolicy defaultDeliveryPolicy,
        int threadCount,
        bool allowSchedulingOnExternalThreads,
        bool enableDiagnostics = false,
        DiagnosticsConfiguration diagnosticsConfiguration = null)
        : this(name, defaultDeliveryPolicy, enableDiagnostics ? new DiagnosticsCollector(diagnosticsConfiguration) : null, diagnosticsConfiguration)
    {
        _scheduler = new Scheduler(ErrorHandler, threadCount, allowSchedulingOnExternalThreads, name);
        _schedulerContext = new SchedulerContext();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Pipeline"/> class.
    /// </summary>
    /// <param name="name">Pipeline name.</param>
    /// <param name="defaultDeliveryPolicy">Pipeline-level default delivery policy (defaults to <see cref="DeliveryPolicy.Unlimited"/> if unspecified).</param>
    /// <param name="scheduler">Scheduler to be used.</param>
    /// <param name="schedulerContext">The scheduler context.</param>
    /// <param name="diagnosticsCollector">Collector with which to gather diagnostic information.</param>
    /// <param name="diagnosticsConfig">Optional diagnostics configuration information.</param>
    internal Pipeline(
        string name,
        DeliveryPolicy defaultDeliveryPolicy,
        Scheduler scheduler,
        SchedulerContext schedulerContext,
        DiagnosticsCollector diagnosticsCollector,
        DiagnosticsConfiguration diagnosticsConfig)
        : this(name, defaultDeliveryPolicy, diagnosticsCollector, diagnosticsConfig)
    {
        _scheduler = scheduler;
        _schedulerContext = schedulerContext;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Pipeline"/> class.
    /// </summary>
    /// <param name="name">Pipeline name.</param>
    /// <param name="defaultDeliveryPolicy">Pipeline-level default delivery policy (defaults to <see cref="DeliveryPolicy.Unlimited"/> if unspecified).</param>
    /// <param name="diagnosticsCollector">Collector with which to gather diagnostic information.</param>
    /// <param name="diagnosticsConfig">Optional diagnostics configuration information.</param>
    private Pipeline(
        string name,
        DeliveryPolicy defaultDeliveryPolicy,
        DiagnosticsCollector diagnosticsCollector,
        DiagnosticsConfiguration diagnosticsConfig)
    {
        _id = Interlocked.Increment(ref s_lastPipelineId);
        _name = name ?? "default";
        _defaultDeliveryPolicy = defaultDeliveryPolicy ?? DeliveryPolicy.Unlimited;
        _enableExceptionHandling = false;
        FinalOriginatingTime = DateTime.MinValue;
        _state = State.Initial;
        _activationContext = new SchedulerContext();
        if (diagnosticsCollector != null)
        {
            DiagnosticsConfiguration = diagnosticsConfig ?? DiagnosticsConfiguration.Default;
            DiagnosticsCollector = diagnosticsCollector;
            DiagnosticsCollector.PipelineCreate(this);
            if (this is not Subpipeline)
            {
                Diagnostics = new DiagnosticsSampler(this, DiagnosticsCollector, DiagnosticsConfiguration).Diagnostics;
            }
        }
    }

    /// <summary>
    /// Event that is raised when the pipeline starts running.
    /// </summary>
    public event EventHandler<PipelineRunEventArgs> PipelineRun;

    /// <summary>
    /// Event that is raised upon pipeline completion.
    /// </summary>
    public event EventHandler<PipelineCompletedEventArgs> PipelineCompleted;

    /// <summary>
    /// Event that is raised upon component completion.
    /// </summary>
    public event EventHandler<ComponentCompletedEventArgs> ComponentCompleted;

    /// <summary>
    /// Event that is raised when one or more unhandled exceptions occur in the pipeline. If a handler is attached
    /// to this event, any unhandled exceptions during pipeline execution will not be thrown, and will instead
    /// be handled by the attached handler. If no handler is attached, unhandled exceptions will be thrown within
    /// the execution context in which the exception occurred if the pipeline was run asynchronously via one of the
    /// RunAsync methods. This could cause the application to terminate abruptly. If the pipeline was run synchronously
    /// via one of the Run methods, an AggregateException will be thrown from the Run method (which may be caught).
    /// </summary>
    public event EventHandler<PipelineExceptionNotHandledEventArgs> PipelineExceptionNotHandled;

    /// <summary>
    /// Enumeration of pipeline states.
    /// </summary>
    private enum State
    {
        Initial,
        Starting,
        Running,
        Stopping,
        Completed,
    }

    /// <summary>
    /// Gets pipeline ID.
    /// </summary>
    public int Id => _id;

    /// <summary>
    /// Gets pipeline name.
    /// </summary>
    public string Name => _name;

    /// <summary>
    /// Gets replay descriptor.
    /// </summary>
    public ReplayDescriptor ReplayDescriptor => _replayDescriptor;

    /// <summary>
    /// Gets emitter producing diagnostics information (must be enabled when running pipeline).
    /// </summary>
    public Emitter<PipelineDiagnostics> Diagnostics
    {
        get
        {
            if (this is Subpipeline)
            {
                throw new InvalidOperationException("Diagnostics is not supported directly on Subpipelines.");
            }

            return _diagnosticsEmitter;
        }

        private set
        {
            _diagnosticsEmitter = value;
        }
    }

    /// <summary>
    /// Gets the pipeline start time based on the pipeline clock.
    /// </summary>
    public DateTime StartTime { get; private set; }

    /// <summary>
    /// Gets or sets the progress reporting time interval.
    /// </summary>
    public TimeSpan ProgressReportInterval { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Gets or sets virtual time offset.
    /// </summary>
    internal virtual TimeSpan VirtualTimeOffset { get; set; } = TimeSpan.Zero;

    internal bool IsInitial => _state == State.Initial;

    internal bool IsStarting => _state == State.Starting;

    internal bool IsRunning => _state == State.Running;

    internal bool IsStopping => _state == State.Stopping;

    internal bool IsCompleted => _state == State.Completed;

    internal Scheduler Scheduler => _scheduler;

    internal SchedulerContext ActivationContext => _activationContext;

    internal SchedulerContext SchedulerContext => _schedulerContext;

    internal Clock Clock => _scheduler.Clock;

    public KeyValueStore ConfigurationStore => _configStore;

    internal ConcurrentQueue<PipelineElement> Components => _components;

    internal DiagnosticsCollector DiagnosticsCollector { get; set; }

    internal DiagnosticsConfiguration DiagnosticsConfiguration { get; set; }

    /// <summary>
    /// Gets or sets originating time of final message scheduled.
    /// </summary>
    internal DateTime FinalOriginatingTime { get; set; }

    /// <summary>
    /// Gets or sets the completion time of the latest completed finite source component.
    /// </summary>
    protected DateTime? LatestFiniteSourceCompletionTime { get; set; }

    /// <summary>
    /// Gets an <see cref="AutoResetEvent"/> that signals when there are no remaining completable components.
    /// </summary>
    /// <remarks>
    /// This is an <see cref="AutoResetEvent"/> rather than a <see cref="ManualResetEvent"/> as we need the
    /// event to trigger one and only one action when signaled.
    /// </remarks>
    protected AutoResetEvent NoRemainingCompletableComponents { get; } = new AutoResetEvent(false);

    /// <summary>
    /// Create pipeline.
    /// </summary>
    /// <param name="name">Pipeline name.</param>
    /// <param name="deliveryPolicy">Pipeline-level delivery policy.</param>
    /// <param name="threadCount">Number of threads.</param>
    /// <param name="allowSchedulingOnExternalThreads">Whether to allow scheduling on external threads.</param>
    /// <param name="enableDiagnostics">Indicates whether to enable collecting and publishing diagnostics information on the Pipeline.Diagnostics stream.</param>
    /// <param name="diagnosticsConfiguration">Optional diagnostics configuration information.</param>
    /// <returns>Created pipeline.</returns>
    public static Pipeline Create(
        string name = null,
        DeliveryPolicy deliveryPolicy = null,
        int threadCount = 0,
        bool allowSchedulingOnExternalThreads = false,
        bool enableDiagnostics = false,
        DiagnosticsConfiguration diagnosticsConfiguration = null)
    {
        return new Pipeline(name, deliveryPolicy, threadCount, allowSchedulingOnExternalThreads, enableDiagnostics, diagnosticsConfiguration);
    }

    /// <summary>
    /// Propose replay time.
    /// </summary>
    /// <param name="originatingTimeInterval">Originating time interval.</param>
    public virtual void ProposeReplayTime(TimeInterval originatingTimeInterval)
    {
        if (!originatingTimeInterval.LeftEndpoint.Bounded)
        {
            throw new ArgumentException(nameof(originatingTimeInterval), "Replay time intervals must have a valid start time.");
        }

        _proposedOriginatingTimeInterval = (_proposedOriginatingTimeInterval == null) ? originatingTimeInterval : TimeInterval.Coverage(new[] { _proposedOriginatingTimeInterval, originatingTimeInterval });
    }

    /// <summary>
    /// Gets the default delivery policy for a stream of given type.
    /// </summary>
    /// <typeparam name="T">The type of the stream.</typeparam>
    /// <returns>The default delivery policy to use for that stream.</returns>
    /// <remarks>The default delivery policy is used when no delivery policy is specified when wiring the stream.</remarks>
    public virtual DeliveryPolicy<T> GetDefaultDeliveryPolicy<T>()
    {
        return _defaultDeliveryPolicy;
    }

    /// <summary>
    /// Gets the default message validator for a stream of a given type.
    /// </summary>
    /// <typeparam name="T">The type of the stream.</typeparam>
    /// <returns>The default validator to use for that stream.</returns>
    public virtual Emitter<T>.ValidateMessageHandler GetDefaultMessageValidator<T>()
    {
        return null;
    }

    /// <summary>
    /// Creates an input receiver associated with the specified component object.
    /// </summary>
    /// <typeparam name="T">The type of messages accepted by this receiver.</typeparam>
    /// <param name="owner">The component that owns the receiver. This is usually the state object that the receiver operates on.
    /// The receivers associated with the same owner are never executed concurrently.</param>
    /// <param name="action">The action to execute when a message is delivered to this receiver.</param>
    /// <param name="name">The debug name of the receiver.</param>
    /// <returns>A new receiver.</returns>
    public Receiver<T> CreateReceiver<T>(object owner, Action<T, Envelope> action, string name)
    {
        return CreateReceiver<T>(owner, m => action(m.Data, m.Envelope), name);
    }

    /// <summary>
    /// Creates an input receiver associated with the specified component object.
    /// </summary>
    /// <typeparam name="T">The type of messages accepted by this receiver.</typeparam>
    /// <param name="owner">The component that owns the receiver. This is usually the state object that the receiver operates on.
    /// The receivers associated with the same owner are never executed concurrently.</param>
    /// <param name="action">The action to execute when a message is delivered to this receiver.</param>
    /// <param name="name">The debug name of the receiver.</param>
    /// <returns>A new receiver.</returns>
    public Receiver<T> CreateReceiver<T>(object owner, Action<T> action, string name)
    {
        return CreateReceiver<T>(owner, m => action(m.Data), name);
    }

    /// <summary>
    /// Creates an input receiver associated with the specified component object.
    /// </summary>
    /// <typeparam name="T">The type of messages accepted by this receiver.</typeparam>
    /// <param name="owner">The component that owns the receiver. This is usually the state object that the receiver operates on.
    /// The receivers associated with the same owner are never executed concurrently.</param>
    /// <param name="action">The action to execute when a message is delivered to this receiver.</param>
    /// <param name="name">The debug name of the receiver.</param>
    /// <returns>A new receiver.</returns>
    public Receiver<T> CreateReceiver<T>(object owner, Action<Message<T>> action, string name)
    {
        PipelineElement node = GetOrCreateNode(owner);
        Receiver<T> receiver = new(Interlocked.Increment(ref s_lastReceiverId), name, node, owner, action, node.SyncContext, this);
        node.AddInput(name, receiver);
        return receiver;
    }

    /// <summary>
    /// Creates an input receiver associated with the specified component object, connected to an async message processing function.
    /// The expected signature of the message processing delegate is: <code>async void Receive(<typeparamref name="T"/> message, Envelope env);</code>
    /// </summary>
    /// <typeparam name="T">The type of messages accepted by this receiver.</typeparam>
    /// <param name="owner">The component that owns the receiver. This is usually the state object that the receiver operates on.
    /// The receivers associated with the same owner are never executed concurrently.</param>
    /// <param name="action">The action to execute when a message is delivered to this receiver.</param>
    /// <param name="name">The debug name of the receiver.</param>
    /// <returns>A new receiver.</returns>
    public Receiver<T> CreateAsyncReceiver<T>(object owner, Func<T, Envelope, Task> action, string name)
    {
        return CreateReceiver<T>(owner, m => action(m.Data, m.Envelope).Wait(), name);
    }

    /// <summary>
    /// Creates an input receiver associated with the specified component object, connected to an async message processing function.
    /// The expected signature of the message processing delegate is: <code>async void Receive(<typeparamref name="T"/> message);</code>
    /// </summary>
    /// <typeparam name="T">The type of messages accepted by this receiver.</typeparam>
    /// <param name="owner">The component that owns the receiver. This is usually the state object that the receiver operates on.
    /// The receivers associated with the same owner are never executed concurrently.</param>
    /// <param name="action">The action to execute when a message is delivered to this receiver.</param>
    /// <param name="name">The debug name of the receiver.</param>
    /// <returns>A new receiver.</returns>
    public Receiver<T> CreateAsyncReceiver<T>(object owner, Func<T, Task> action, string name)
    {
        return CreateReceiver<T>(owner, m => action(m.Data).Wait(), name);
    }

    /// <summary>
    /// Creates an input receiver associated with the specified component object, connected to an async message processing function.
    /// The expected signature of the message processing delegate is: <code>async void Receive(Message{<typeparamref name="T"/>} message);</code>
    /// </summary>
    /// <typeparam name="T">The type of messages accepted by this receiver.</typeparam>
    /// <param name="owner">The component that owns the receiver. This is usually the state object that the receiver operates on.
    /// The receivers associated with the same owner are never executed concurrently.</param>
    /// <param name="action">The action to execute when a message is delivered to this receiver.</param>
    /// <param name="name">The debug name of the receiver.</param>
    /// <returns>A new receiver.</returns>
    public Receiver<T> CreateAsyncReceiver<T>(object owner, Func<Message<T>, Task> action, string name)
    {
        return CreateReceiver<T>(owner, m => action(m).Wait(), name);
    }

    /// <summary>
    /// Create emitter.
    /// </summary>
    /// <typeparam name="T">Type of emitted messages.</typeparam>
    /// <param name="owner">Owner of emitter.</param>
    /// <param name="name">Name of emitter.</param>
    /// <param name="messageValidator">An optional message validator.</param>
    /// <returns>Created emitter.</returns>
    public Emitter<T> CreateEmitter<T>(object owner, string name, Emitter<T>.ValidateMessageHandler messageValidator = null)
    {
        return CreateEmitterWithFixedStreamId(owner, name, Interlocked.Increment(ref s_lastStreamId), messageValidator);
    }

    /// <summary>
    /// Wait for all components to complete.
    /// </summary>
    /// <param name="millisecondsTimeout">Timeout (milliseconds).</param>
    /// <returns>Success.</returns>
    public bool WaitAll(int millisecondsTimeout = Timeout.Infinite)
    {
        return _completed.WaitOne(millisecondsTimeout);
    }

    /// <summary>
    /// Wait for all components to complete.
    /// </summary>
    /// <param name="timeout">Timeout.</param>
    /// <returns>Success.</returns>
    public bool WaitAll(TimeSpan timeout)
    {
        return _completed.WaitOne(timeout);
    }

    /// <inheritdoc />
    public virtual void Dispose()
    {
        Dispose(false);
    }

    /// <summary>
    /// Runs the pipeline synchronously.
    /// </summary>
    /// <param name="descriptor">An optional replay descriptor to apply when replaying data from a store.</param>
    /// <param name="progress">An optional progress reporter for progress updates.</param>
    public void Run(ReplayDescriptor descriptor = null, IProgress<double> progress = null)
    {
        _enableExceptionHandling = true; // suppress exceptions while running
        RunAsync(descriptor, progress);
        WaitAll();
        _enableExceptionHandling = false;

        // throw any exceptions if running synchronously and there is no PipelineException handler
        ThrowIfError();
    }

    /// <summary>
    /// Runs the pipeline synchronously in replay mode. This method may be used when replaying data from a store.
    /// </summary>
    /// <param name="replayInterval">
    /// The time interval within which to replay the data. The pipeline will commence playback at the start time of
    /// this interval, and only messages bearing an originating time within this interval will be retrieved from the
    /// store(s) contained in the pipeline and delivered. Pipeline execution will stop once all messages within this
    /// interval have been processed.
    /// </param>
    /// <param name="enforceReplayClock">
    /// Whether to enforce the replay clock. If true, messages retrieved from the store(s) will be delivered according
    /// to their originating times, as though they were being generated in real-time. If false, messages retrieved from
    /// store(s) will be delivered as soon as possible irrespective of their originating times.
    /// </param>
    /// <param name="progress">An optional progress reporter for progress updates.</param>
    public void Run(TimeInterval replayInterval, bool enforceReplayClock = true, IProgress<double> progress = null)
    {
        Run(new ReplayDescriptor(replayInterval, enforceReplayClock), progress);
    }

    /// <summary>
    /// Runs the pipeline synchronously in replay mode. This method may be used when replaying data from a store.
    /// </summary>
    /// <param name="replayStartTime">The time at which to start replaying.</param>
    /// <param name="replayEndTime">The time at which to end replaying.</param>
    /// <param name="enforceReplayClock">
    /// Whether to enforce the replay clock. If true, messages retrieved from the store(s) will be delivered according
    /// to their originating times, as though they were being generated in real-time. If false, messages retrieved from
    /// store(s) will be delivered as soon as possible irrespective of their originating times.
    /// </param>
    /// <param name="progress">An optional progress reporter for progress updates.</param>
    public void Run(DateTime replayStartTime, DateTime replayEndTime, bool enforceReplayClock = true, IProgress<double> progress = null)
    {
        Run(new ReplayDescriptor(replayStartTime, replayEndTime, enforceReplayClock), progress);
    }

    /// <summary>
    /// Runs the pipeline synchronously in replay mode. This method may be used when replaying data from a store.
    /// </summary>
    /// <param name="replayStartTime">Time at which to start replaying.</param>
    /// <param name="enforceReplayClock">
    /// Whether to enforce the replay clock. If true, messages retrieved from the store(s) will be delivered according
    /// to their originating times, as though they were being generated in real-time. If false, messages retrieved from
    /// store(s) will be delivered as soon as possible irrespective of their originating times.
    /// </param>
    /// <param name="progress">An optional progress reporter for progress updates.</param>
    public void Run(DateTime replayStartTime, bool enforceReplayClock = true, IProgress<double> progress = null)
    {
        Run(new ReplayDescriptor(replayStartTime, DateTime.MaxValue, enforceReplayClock), progress);
    }

    /// <summary>
    /// Runs the pipeline asynchronously.
    /// </summary>
    /// <param name="descriptor">An optional replay descriptor to apply when replaying data from a store.</param>
    /// <param name="progress">An optional progress reporter for progress updates.</param>
    /// <returns>An IDisposable instance which may be used to terminate the pipeline.</returns>
    public IDisposable RunAsync(ReplayDescriptor descriptor = null, IProgress<double> progress = null)
    {
        return RunAsync(descriptor, null, progress);
    }

    /// <summary>
    /// Runs the pipeline asynchronously in replay mode. This method may be used when replaying data from a store.
    /// </summary>
    /// <param name="replayInterval">
    /// The time interval within which to replay the data. The pipeline will commence playback at the start time of
    /// this interval, and only messages bearing an originating time within this interval will be retrieved from the
    /// store(s) contained in the pipeline and delivered. Pipeline execution will stop once all messages within this
    /// interval have been processed.
    /// </param>
    /// <param name="enforceReplayClock">
    /// Whether to enforce the replay clock. If true, messages retrieved from the store(s) will be delivered according
    /// to their originating times, as though they were being generated in real-time. If false, messages retrieved from
    /// store(s) will be delivered as soon as possible irrespective of their originating times.
    /// </param>
    /// <param name="progress">An optional progress reporter for progress updates.</param>
    /// <returns>An IDisposable instance which may be used to terminate the pipeline.</returns>
    public IDisposable RunAsync(TimeInterval replayInterval, bool enforceReplayClock = true, IProgress<double> progress = null)
    {
        return RunAsync(new ReplayDescriptor(replayInterval, enforceReplayClock), progress);
    }

    /// <summary>
    /// Runs the pipeline asynchronously in replay mode. This method may be used when replaying data from a store.
    /// </summary>
    /// <param name="replayStartTime">Time at which to start replaying.</param>
    /// <param name="replayEndTime">Time at which to end replaying.</param>
    /// <param name="enforceReplayClock">
    /// Whether to enforce the replay clock. If true, messages retrieved from the store(s) will be delivered according
    /// to their originating times, as though they were being generated in real-time. If false, messages retrieved from
    /// store(s) will be delivered as soon as possible irrespective of their originating times.
    /// </param>
    /// <param name="progress">An optional progress reporter for progress updates.</param>
    /// <returns>An IDisposable instance which may be used to terminate the pipeline.</returns>
    public IDisposable RunAsync(DateTime replayStartTime, DateTime replayEndTime, bool enforceReplayClock = true, IProgress<double> progress = null)
    {
        return RunAsync(new ReplayDescriptor(replayStartTime, replayEndTime, enforceReplayClock), progress);
    }

    /// <summary>
    /// Runs the pipeline asynchronously in replay mode. This method may be used when replaying data from a store.
    /// </summary>
    /// <param name="replayStartTime">Time at which to start replaying.</param>
    /// <param name="enforceReplayClock">
    /// Whether to enforce the replay clock. If true, messages retrieved from the store(s) will be delivered according
    /// to their originating times, as though they were being generated in real-time. If false, messages retrieved from
    /// store(s) will be delivered as soon as possible irrespective of their originating times.
    /// </param>
    /// <param name="progress">An optional progress reporter for progress updates.</param>
    /// <returns>An IDisposable instance which may be used to terminate the pipeline.</returns>
    public IDisposable RunAsync(DateTime replayStartTime, bool enforceReplayClock = true, IProgress<double> progress = null)
    {
        return RunAsync(new ReplayDescriptor(replayStartTime, DateTime.MaxValue, enforceReplayClock), progress);
    }

    /// <summary>
    /// Get current clock time.
    /// </summary>
    /// <returns>Current clock time.</returns>
    public DateTime GetCurrentTime()
    {
        return Clock.GetCurrentTime();
    }

    /// <summary>
    /// Get current time, given elapsed ticks.
    /// </summary>
    /// <param name="ticksFromSystemBoot">Ticks elapsed since system boot.</param>
    /// <returns>Current time.</returns>
    public DateTime GetCurrentTimeFromElapsedTicks(long ticksFromSystemBoot)
    {
        return Clock.GetTimeFromElapsedTicks(ticksFromSystemBoot);
    }

    /// <summary>
    /// Convert virtual duration to real time.
    /// </summary>
    /// <param name="duration">Duration to convert.</param>
    /// <returns>Converted time span.</returns>
    public TimeSpan ConvertToRealTime(TimeSpan duration)
    {
        return Clock.ToRealTime(duration);
    }

    /// <summary>
    /// Convert virtual datetime to real time.
    /// </summary>
    /// <param name="time">Datetime to convert.</param>
    /// <returns>Converted datetime.</returns>
    public DateTime ConvertToRealTime(DateTime time)
    {
        return Clock.ToRealTime(time);
    }

    /// <summary>
    /// Convert real timespan to virtual.
    /// </summary>
    /// <param name="duration">Duration to convert.</param>
    /// <returns>Converted time span.</returns>
    public TimeSpan ConvertFromRealTime(TimeSpan duration)
    {
        return Clock.ToVirtualTime(duration);
    }

    /// <summary>
    /// Convert real datetime to virtual.
    /// </summary>
    /// <param name="time">Datetime to convert.</param>
    /// <returns>Converted datetime.</returns>
    public DateTime ConvertFromRealTime(DateTime time)
    {
        return Clock.ToVirtualTime(time);
    }

    /// <summary>
    /// Set last stream ID.
    /// </summary>
    internal static void SetLastStreamId(int lastStreamId)
    {
        Pipeline.s_lastStreamId = Math.Max(Pipeline.s_lastStreamId, lastStreamId);
    }

    internal Emitter<T> CreateEmitterWithFixedStreamId<T>(object owner, string name, int streamId, Emitter<T>.ValidateMessageHandler messageValidator)
    {
        PipelineElement node = GetOrCreateNode(owner);
        Emitter<T> emitter = new(streamId, name, owner, node.SyncContext, this, messageValidator ?? GetDefaultMessageValidator<T>());
        node.AddOutput(name, emitter);
        return emitter;
    }

    /// <summary>
    /// Stops the pipeline and removes all connectivity (pipes).
    /// </summary>
    /// <param name="abandonPendingWorkItems">Abandons pending work items.</param>
    internal void Dispose(bool abandonPendingWorkItems)
    {
        if (Interlocked.CompareExchange(ref _isPipelineDisposed, 1, 0) != 0)
        {
            // we've already been disposed
            return;
        }

        // Subpipelines may have already been notified of the final originating time via a call to NotifyPipelineFinalizing
        // by the parent pipeline. If so, the FinalOriginatingTime will have been set to a value other than DateTime.MinValue
        // and we should use that as the finalOriginatingTime in the call to Stop() instead of GetCurrentTime().
        Stop(FinalOriginatingTime != DateTime.MinValue ? FinalOriginatingTime : GetCurrentTime(), abandonPendingWorkItems);
        DisposeComponents();
        _components = null;
        DiagnosticsCollector?.PipelineDisposed(this);
        _completed.Dispose();
        _schedulerContext.Dispose();
        _activationContext.Dispose();

        // Dispose the scheduler if this is the main pipeline
        if (this is not Subpipeline)
        {
            _scheduler.Dispose();
        }
    }

    internal void AddComponent(PipelineElement pe)
    {
        pe.Initialize(this);
        if (pe.StateObject != this)
        {
            _components.Enqueue(pe);
        }
    }

    /// <summary>
    /// Notify pipeline of component completion along with originating time of final message.
    /// </summary>
    /// <param name="component">Component which has completed.</param>
    /// <param name="finalOriginatingTime">Originating time of final message.</param>
    internal virtual void NotifyCompletionTime(PipelineElement component, DateTime finalOriginatingTime)
    {
        CompleteComponent(component, finalOriginatingTime);

        if (NoRemainingCompletableComponents.WaitOne(0))
        {
            // stop the pipeline once all finite sources have completed
            if (LatestFiniteSourceCompletionTime.HasValue)
            {
                ThreadPool.QueueUserWorkItem(_ => Stop(LatestFiniteSourceCompletionTime.Value));
            }
        }
    }

    /// <summary>
    /// Mark the component as completed and update the final message originating time of the pipeline.
    /// </summary>
    /// <param name="component">Component which has completed.</param>
    /// <param name="finalOriginatingTime">Originating time of final message.</param>
    internal void CompleteComponent(PipelineElement component, DateTime finalOriginatingTime)
    {
        if (!component.IsSource)
        {
            return;
        }

        // MaxValue is special; meaning the component was *never* a finite source
        // we simply remove it from the list and continue.
        //
        // An example of such a component is the Subpipeline, which is declared as
        // a finite source, but only truly is if it contains a finite source (which
        // can't be known until runtime).
        bool finiteCompletion = finalOriginatingTime != DateTime.MaxValue;

        bool lastRemainingCompletable = false;

        lock (_completableComponents)
        {
            if (!_completableComponents.Contains(component))
            {
                // the component was already removed (e.g. because the pipeline is stopping)
                return;
            }

            _completableComponents.Remove(component);
            lastRemainingCompletable = _completableComponents.Count == 0;

            if (finiteCompletion && (LatestFiniteSourceCompletionTime == null || finalOriginatingTime > LatestFiniteSourceCompletionTime))
            {
                // keep track of the latest finite source component completion time
                LatestFiniteSourceCompletionTime = finalOriginatingTime;
            }
        }

        if (finiteCompletion)
        {
            ComponentCompleted?.Invoke(this, new ComponentCompletedEventArgs(component.Name, finalOriginatingTime));
        }

        if (lastRemainingCompletable)
        {
            // signal completion of all completable components
            NoRemainingCompletableComponents.Set();
        }
    }

    /// <summary>
    /// Apply action to this pipeline and to all descendant Subpipelines.
    /// </summary>
    /// <param name="action">Action to apply.</param>
    internal void ForThisPipelineAndAllDescendentSubpipelines(Action<Pipeline> action)
    {
        action(this);
        foreach (Subpipeline sub in GetSubpipelines())
        {
            sub.ForThisPipelineAndAllDescendentSubpipelines(action);
        }
    }

    /// <summary>
    /// Pause pipeline (and all subpipeline) scheduler for quiescence.
    /// </summary>
    internal void PauseForQuiescence()
    {
        ForThisPipelineAndAllDescendentSubpipelines(p => p._scheduler.PauseForQuiescence(p._schedulerContext));
    }

    internal PipelineElement GetOrCreateNode(object component)
    {
        PipelineElement node = _components.FirstOrDefault(c => c.StateObject == component);
        if (node == null)
        {
            int id = Interlocked.Increment(ref s_lastElementId);
            string name = component.GetType().Name;
            string fullName = $"{id}.{name}";
            node = new PipelineElement(id, fullName, component);
            if (IsRunning && node.IsSource && component is not Subpipeline)
            {
                throw new InvalidOperationException($"Source component added when pipeline already running. Consider using Subpipeline.");
            }

            AddComponent(node);
            DiagnosticsCollector?.PipelineElementCreate(this, node, component);
        }

        return node;
    }

    /// <summary>
    /// Stops the pipeline by disabling message passing between the pipeline components.
    /// The pipeline configuration is not changed and the pipeline can be restarted later.
    /// </summary>
    /// <param name="finalOriginatingTime">
    /// The final originating time of the pipeline. Delivery of messages with originating times
    /// later than the final originating time will no longer be guaranteed.
    /// </param>
    /// <param name="abandonPendingWorkitems">Abandons the pending work items.</param>
    internal virtual void Stop(DateTime finalOriginatingTime, bool abandonPendingWorkitems = false)
    {
        if (IsCompleted)
        {
            return;
        }

        if (IsStopping)
        {
            _completed.WaitOne();
            return;
        }

        _state = State.Stopping;

        // use the supplied final originating time, unless the pipeline has already seen a later finite source completion time
        NotifyPipelineFinalizing((LatestFiniteSourceCompletionTime == null || finalOriginatingTime > LatestFiniteSourceCompletionTime) ? finalOriginatingTime : LatestFiniteSourceCompletionTime.Value);

        // deactivate all components, to disable the generation of new messages from source components
        int count;
        do
        {
            count = 0;
            ForThisPipelineAndAllDescendentSubpipelines(p => count += p.DeactivateComponents());
            PauseForQuiescence();
        }
        while (count > 0);

        // final call for components to post and cease
        FinalizeComponents();
        DiagnosticsCollector?.PipelineStopped(this);

        // block until all messages in the pipeline are fully processed
        Scheduler.StopScheduling(SchedulerContext);

        // wait for progress timer delegate to finish
        if (_progressTimer != null)
        {
            _progressTimer.Stop();
            ReportProgress(); // ensure that final progress is reported
        }

        // stop the scheduler if this is the main pipeline
        if (this is not Subpipeline)
        {
            _scheduler.Stop(abandonPendingWorkitems);
        }

        // pipeline has completed
        _state = State.Completed;

        // raise the pipeline completed event
        OnPipelineCompleted(
            new PipelineCompletedEventArgs(
                FinalOriginatingTime,
                abandonPendingWorkitems,
                _errors));

        // signal all threads waiting on pipeline completion
        _completed.Set();
    }

    /// <summary>
    /// Run pipeline (asynchronously).
    /// </summary>
    /// <param name="descriptor">Replay descriptor.</param>
    /// <param name="clock">Clock to use (in the case of a shared scheduler - e.g. subpipeline).</param>
    /// <param name="progress">Progress reporter.</param>
    /// <returns>Disposable used to terminate pipeline.</returns>
    protected virtual IDisposable RunAsync(ReplayDescriptor descriptor, Clock clock, IProgress<double> progress = null)
    {
        _state = State.Starting;
        descriptor ??= ReplayDescriptor.ReplayAllRealTime;
        _replayDescriptor = descriptor.Intersect(_proposedOriginatingTimeInterval);

        _completed.Reset();
        if (clock == null)
        {
            // this is the main pipeline (subpipelines inherit the parent clock)
            clock =
                _replayDescriptor.Interval.Left != DateTime.MinValue ?
                new Clock(_replayDescriptor.Start + VirtualTimeOffset) :
                new Clock(VirtualTimeOffset);

            // start the scheduler
            _scheduler.Start(clock, _replayDescriptor.EnforceReplayClock);
        }

        // The pipeline start time reflects either the replay start time if one was specified,
        // otherwise the clock origin, which is the time the pipeline scheduler was started.
        StartTime = _replayDescriptor.Start != DateTime.MinValue ? _replayDescriptor.Start : clock.Origin;

        DiagnosticsCollector?.PipelineStart(this);

        // raise the event prior to starting the components
        OnPipelineRun(new PipelineRunEventArgs(StartTime));

        // keep track of completable source components
        foreach (PipelineElement component in _components)
        {
            if (component.IsSource)
            {
                lock (_completableComponents)
                {
                    _completableComponents.Add(component);
                }
            }
        }

        // Start scheduling for component activation only - startup of source components will be scheduled, but
        // any other work (e.g. delivery of messages) will be deferred until the schedulerContext is started.
        _scheduler.StartScheduling(_activationContext);

        foreach (PipelineElement component in _components)
        {
            component.Activate();
        }

        // wait for component activation to finish
        _scheduler.PauseForQuiescence(_activationContext);

        // all components started - pipeline is now running
        _state = State.Running;

        // now start scheduling work on the main scheduler context
        _scheduler.StartScheduling(_schedulerContext);

        // start a progress reporting timer if a progress reporter is supplied
        if (progress != null)
        {
            _progressReporter = progress;
            _progressDelegate = new Time.TimerDelegate((i, m, c, d1, d2) => ReportProgress());
            _progressTimer = Platform.Specific.TimerStart((uint)ProgressReportInterval.TotalMilliseconds, _progressDelegate);
        }

        return this;
    }

    /// <summary>
    /// Gather all nodes within this pipeline and recursively within subpipelines.
    /// </summary>
    /// <param name="pipeline">Pipeline (or Subpipeline) from which to gather nodes.</param>
    /// <returns>Active nodes.</returns>
    private static IEnumerable<PipelineElement> GatherActiveNodes(Pipeline pipeline)
    {
        foreach (PipelineElement node in pipeline._components.Where(c => !c.IsFinalized))
        {
            if (node.StateObject is Subpipeline subpipeline)
            {
                IEnumerable<PipelineElement> subnodes = GatherActiveNodes(subpipeline);
                if (subnodes.Count() > 0)
                {
                    foreach (PipelineElement sub in subnodes)
                    {
                        yield return sub;
                    }
                }
                else
                {
                    yield return node; // include subpipeline itself once no more active children
                }
            }
            else
            {
                yield return node;
            }
        }
    }

    /// <summary>
    /// Determine whether a node is a Connector component.
    /// </summary>
    /// <param name="node">Element for which to determine whether it represents a Connector component.</param>
    /// <returns>Indication of whether the element represents a Connector component.</returns>
    private static bool IsConnector(PipelineElement node) => node.StateObject is IConnector;

    /// <summary>
    /// Pipeline-bridging Connectors create two nodes; one with inputs in one pipeline and one with outputs in the other.
    /// Here we attempt to find the input side, given a Connector node.
    /// </summary>
    /// <param name="node">Node (representing the output side of a Connector) for which to try to find the matching input node.</param>
    /// <param name="inputConnectors">Known nodes (with inputs) representing Connectors.</param>
    /// <param name="bridge">Populated with input side of Connector bridge if found.</param>
    /// <returns>Indication of whether a bridge has been found.</returns>
    private static bool TryGetConnectorBridge(PipelineElement node, Dictionary<object, PipelineElement> inputConnectors, out PipelineElement bridge)
    {
        bridge = null;
        return node.IsConnector && inputConnectors.TryGetValue(node.StateObject, out bridge);
    }

    /// <summary>
    /// Determine whether a node has *only* cyclic inputs (back to origin; not including upstream independent cycles).
    /// </summary>
    /// <param name="node">Node for which to determine whether it has only cyclic inputs.</param>
    /// <param name="origin">Node from which a potential cycle may originate.</param>
    /// <param name="emitterNodes">Mapping of emitter IDs to corresponding nodes.</param>
    /// <param name="inputConnectors">Known nodes (with inputs) representing Connectors.</param>
    /// <param name="visitedNodes">Used to mark visited nodes; preventing infinitely exploring cycles (upstream from the origin).</param>
    /// <returns>An indication of whether the node has only cyclic inputs.</returns>
    private static bool OnlyCyclicInputs(PipelineElement node, PipelineElement origin, Dictionary<int, PipelineElement> emitterNodes, Dictionary<object, PipelineElement> inputConnectors, HashSet<PipelineElement> visitedNodes)
    {
        if (node.IsFinalized)
        {
            return false;
        }

        if (node == origin)
        {
            return true; // cycle back to origin detected
        }

        if (visitedNodes.Contains(node))
        {
            return false; // upstream cycle
        }

        visitedNodes.Add(node);

        if (node.Inputs.Count > 0)
        {
            bool hasCycle = false; // begin assuming no cycles at all (e.g. no actually wired inputs)
            foreach (KeyValuePair<string, IReceiver> receiver in node.Inputs)
            {
                IEmitter emitter = receiver.Value.Source;
                if (emitter != null)
                {
                    if (emitterNodes.TryGetValue(emitter.Id, out PipelineElement parent))
                    {
                        if (OnlyCyclicInputs(parent, origin, emitterNodes, inputConnectors, visitedNodes))
                        {
                            hasCycle = true;
                        }
                        else
                        {
                            return false;
                        }
                    }
                }
            }

            return hasCycle;
        }
        else
        {
            // no inputs? perhaps it's the output side of a Connector pair
            // try to get the input side and check it for cycles
            if (TryGetConnectorBridge(node, inputConnectors, out PipelineElement bridge))
            {
                return OnlyCyclicInputs(bridge, origin, emitterNodes, inputConnectors, visitedNodes);
            }
        }

        return false;
    }

    /// <summary>
    /// Determine whether a node is eligible for finalization (has no inputs from unfinalized node or else only cycles back to self).
    /// </summary>
    /// <param name="node">Node for which to determine eligibility.</param>
    /// <param name="emitterNodes">Mapping of emitter IDs to corresponding nodes.</param>
    /// <param name="inputConnectors">Known nodes (with inputs) representing Connectors.</param>
    /// <param name="includeCycles">Whether to consider nodes that are members of a pure cycle to be finalizable.</param>
    /// <param name="onlySelfCycles">Whether to consider only self-cycles (node is it's own parent).</param>
    /// <returns>An indication of eligibility for finalization.</returns>
    private static bool IsNodeFinalizable(PipelineElement node, Dictionary<int, PipelineElement> emitterNodes, Dictionary<object, PipelineElement> inputConnectors, bool includeCycles, bool onlySelfCycles)
    {
        if (node.IsFinalized)
        {
            return false; // already done
        }

        if (node.Inputs.Count > 0)
        {
            foreach (KeyValuePair<string, IReceiver> receiver in node.Inputs)
            {
                IEmitter emitter = receiver.Value.Source;
                if (emitter != null)
                {
                    if (!includeCycles)
                    {
                        return false; // has active source (and irrelevant whether cyclic)
                    }

                    if (emitterNodes.TryGetValue(emitter.Id, out PipelineElement parent))
                    {
                        if (onlySelfCycles)
                        {
                            if (parent != node)
                            {
                                return false;
                            }
                        }
                        else if (!OnlyCyclicInputs(parent, node, emitterNodes, inputConnectors, new HashSet<PipelineElement>()))
                        {
                            return false; // has as least one active non-cyclic source
                        }
                    }
                    else
                    {
                        return false; // has inactive source but has not finished unsubscribing
                    }
                }
            }
        }
        else
        {
            if (TryGetConnectorBridge(node, inputConnectors, out PipelineElement bridge))
            {
                return IsNodeFinalizable(bridge, emitterNodes, inputConnectors, includeCycles, onlySelfCycles);
            }
        }

        return true;
    }

    /// <summary>
    /// Gets child subpipeline components.
    /// </summary>
    /// <returns>Child subpipelines.</returns>
    private IEnumerable<Subpipeline> GetSubpipelines()
    {
        return _components.Where(c => c.StateObject is Subpipeline && !c.IsFinalized).Select(c => c.StateObject as Subpipeline);
    }

    /// <summary>
    /// Raises the <see cref="PipelineRun"/> event.
    /// </summary>
    /// <param name="e">A <see cref="PipelineRunEventArgs"/> that contains the event data.</param>
    private void OnPipelineRun(PipelineRunEventArgs e)
    {
        // ensure that event will only be raised once
        if (!_pipelineRunEventHandled)
        {
            // raise the PipelineRun event for all subpipelines
            ForThisPipelineAndAllDescendentSubpipelines(p =>
            {
                // repeatedly invoke PipelineRun when invocation itself adds subscribers
                while (p.PipelineRun != null)
                {
                    EventHandler<PipelineRunEventArgs> handler = p.PipelineRun;
                    p.PipelineRun = null;
                    handler?.Invoke(p, e);
                }

                p._pipelineRunEventHandled = true;
            });
        }
    }

    /// <summary>
    /// Raises the <see cref="PipelineCompleted"/> event.
    /// </summary>
    /// <param name="e">A <see cref="PipelineCompletedEventArgs"/> that contains the event data.</param>
    private void OnPipelineCompleted(PipelineCompletedEventArgs e)
    {
        PipelineCompleted?.Invoke(this, e);
    }

    /// <summary>
    /// Deactivates all active components.
    /// </summary>
    /// <returns>Number of components deactivated.</returns>
    private int DeactivateComponents()
    {
        // to avoid deadlocks resulting from component calls to NotifyCompleted, copy the list before calling each source component
        PipelineElement[] components;
        lock (_components)
        {
            components = _components.ToArray();
        }

        int count = 0;
        foreach (PipelineElement component in components)
        {
            if (component.IsActivated)
            {
                component.Deactivate(FinalOriginatingTime);
                count++;
            }
            else if (component.IsDeactivating)
            {
                // continue to hold the pipeline open pending deactivation
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Finalizes all components within the pipeline and all subpipelines. The graph of the pipeline is iteratively
    /// inspected for nodes (representing components) that are finalizable. On each pass, nodes with no active
    /// inputs (i.e. whose receivers are all unsubscribed) are finalized immediately and their emitters are closed.
    /// The act of finalizing a node and closing its emitters may in turn cause downstream nodes to also become
    /// finalizable if all of their receivers become unsubscribed. This process is repeated until no finalizable
    /// nodes are found. Remaining active nodes are then inspected for their participation in cycles.
    ///
    /// We identify three kinds of nodes, depending on their participation in various types of cycles:
    ///  - A node in a self-cycle whose active inputs are all directly connected to its outputs.
    ///  - A node participating in only simple (or pure) cycles where every one of its active inputs (and those of
    ///    its predecessor nodes) cycle back to itself.
    ///  - A node which has at least one active input on a directed path that is not a simple cycle back to itself.
    ///
    /// Once there are no immediately finalizable nodes found, any nodes containing self cycles are finalized next.
    /// This may cause direct successor nodes to become finalizable once they have no active inputs. When there are
    /// again no more finalizable nodes, the graph is inspected for nodes in pure cycles. A node in the cycle is
    /// chosen arbitrarily and finalized to break the cycle. This process is iterated over until all that remains
    /// are nodes which have inputs that are not exclusively simple cycles (e.g. a cyclic node with a predecessor
    /// that is also on a different cycle), or nodes with such cycles upstream. The node with the most number of
    /// outputs (used as a heuristic to finalize terminal nodes last) is finalized, then the remaining nodes are
    /// evaluated for finalization using the same order of criteria as before (nodes with no active inputs, nodes
    /// with only self-cycles, nodes in only simple cycles, etc.) until all nodes have been finalized.
    /// </summary>
    /// <remarks>
    /// Prior to calling this method, all source components should first be deactivated such that they are no
    /// longer producing new source messages. The act of finalizing a node may cause it to post new messages to
    /// downstream components. It is therefore important to allow the pipeline to drain once there are no longer
    /// any nodes without active inputs remaining (these are always safe to finalize immediately).
    /// </remarks>
    private void FinalizeComponents()
    {
        IList<PipelineElement> nodes = GatherActiveNodes(this).ToList(); // all non-finalized node within pipeline and subpipelines
        while (nodes.Count() > 0)
        {
            // build emitter ID -> node and connector node mappings
            Dictionary<int, PipelineElement> emitterNodes = new(); // used to traverse up emitter edge
            Dictionary<object, PipelineElement> inputConnectors = new(); // used to find the input side of a pipeline-bridging Connector (two nodes; one in each pipeline)
            foreach (PipelineElement node in nodes)
            {
                foreach (KeyValuePair<string, IEmitter> output in node.Outputs)
                {
                    // used for looking up source nodes from active receiver inputs when testing for cycles
                    emitterNodes.Add(output.Value.Id, node);
                }

                if (node.Inputs.Count > 0 && IsConnector(node))
                {
                    // Used for looking up the input-side of a pipeline-bridging connector (these
                    // have an input node and an output node on each side of the bridge).
                    inputConnectors.Add(node.StateObject, node);
                }
            }

            // initially we exclude nodes which are in a cycle
            bool includeCycles = false;
            bool onlySelfCycles = false;

            // This is a LINQ expression which queries the list of all active nodes for nodes which are eligible to
            // be finalized immediately (i.e. without waiting for messages currently in the pipeline to drain).
            // Initially, only nodes which have no active receivers are considered finalizable. If there are no
            // such nodes found, messages in the pipeline are allowed to drain. We then progressively loosen the
            // requirements by including nodes with self-cycles, followed by nodes which are part of pure cycles
            // (nodes which have *only* cyclic inputs). Once a node has been finalized, more nodes may then become
            // finalizable as their receivers unsubscribe from the emitters of finalized node. The predicate
            // function IsNodeFinalizable() in the LINQ expression is evaluated for each node in the finalization
            // loop to determine its eligibility for finalization. If true, then the node may be finalized safely.
            IEnumerable<PipelineElement> finalizable = nodes.Where(n => IsNodeFinalizable(n, emitterNodes, inputConnectors, includeCycles, onlySelfCycles));

            // if we cannot find any nodes to finalize
            if (!finalizable.Any())
            {
                // try letting messages drain, then look for finalizable nodes again (there may have been closing messages in-flight)
                PauseForQuiescence();

                // if we still cannot find any nodes to finalize
                if (!finalizable.Any())
                {
                    // include nodes containing self-cycles (nodes receiving only from themselves - these are completely safe to finalize)
                    includeCycles = true;
                    onlySelfCycles = true;

                    // if there are no nodes containing self-cycles
                    if (!finalizable.Any())
                    {
                        // include nodes in pure cycles (nodes receiving indirectly from themselves - these finalize in arbitrary order!)
                        onlySelfCycles = false;
#if DEBUG
                        if (finalizable.Any())
                        {
                            Debug.WriteLine("FINALIZING SIMPLE CYCLES (UNSAFE ARBITRARY FINALIZATION ORDER)");
                            foreach (PipelineElement node in finalizable)
                            {
                                Debug.WriteLine($"  FINALIZING {node.Name} {node.StateObject} {node.StateObject.GetType()}");
                            }
                        }
#endif

                        // no finalizable nodes found (including self and simple cycles)
                        if (!finalizable.Any())
                        {
                            // All remaining nodes are either nodes which are part of a non-pure cycle (i.e. not all of its inputs
                            // and those of its predecessors cycle back to itself), or nodes with such cycles upstream. Pick the
                            // node with the most number of active inputs to finalize first, then search the graph again for
                            // finalizable nodes with no active inputs.
                            PipelineElement node = nodes.OrderBy(n => -n.Outputs.Where(o => o.Value.HasSubscribers).Count()).FirstOrDefault();
                            if (node != null)
                            {
#if DEBUG
                                Debug.WriteLine("ONLY NON-SIMPLE CYCLES REMAINING (UNSAFE ARBITRARY FINALIZATION ORDER)");
                                Debug.WriteLine($"  FINALIZING {node.Name} {node.StateObject} {node.StateObject.GetType()}");
#endif

                                // finalize the first node (i.e. with the most number of active inputs)
                                node.Final(FinalOriginatingTime);
                            }

                            // revert to searching for finalizable nodes with no active inputs
                            includeCycles = false;
                            onlySelfCycles = false;
                        }
                    }
                }
            }

            foreach (PipelineElement node in finalizable)
            {
                node.Final(FinalOriginatingTime);
            }

            nodes = GatherActiveNodes(this).ToList(); // all non-finalized node within pipeline and subpipelines
        }
    }

    /// <summary>
    /// Error handler function.
    /// </summary>
    /// <param name="e">Exception to handle.</param>
    /// <returns>Whether exception handled.</returns>
    private bool ErrorHandler(Exception e)
    {
        lock (_errors)
        {
            _errors.Add(e);
            if (!IsStopping)
            {
                ThreadPool.QueueUserWorkItem(_ => Stop(GetCurrentTime()));
            }
        }

        // raise the exception event
        PipelineExceptionNotHandled?.Invoke(this, new PipelineExceptionNotHandledEventArgs(e));

        // suppress the exception if there is a handler attached or enableExceptionHandling flag is set
        return PipelineExceptionNotHandled != null || _enableExceptionHandling;
    }

    private void NotifyPipelineFinalizing(DateTime finalOriginatingTime)
    {
        FinalOriginatingTime = finalOriginatingTime;
        _schedulerContext.FinalizeTime = finalOriginatingTime;

        // propagate the final originating time to all descendant subpipelines and their respective scheduler contexts
        foreach (Subpipeline sub in GetSubpipelines())
        {
            // if subpipeline is already stopping then don't override its final originating time
            if (!sub.IsStopping)
            {
                sub.FinalOriginatingTime = finalOriginatingTime;
                sub._schedulerContext.FinalizeTime = finalOriginatingTime;
            }
        }
    }

    private void DisposeComponents()
    {
        foreach (PipelineElement component in _components)
        {
            DiagnosticsCollector?.PipelineElementDisposed(this, component);
            component.Dispose();
        }
    }

    private void ThrowIfError()
    {
        if (PipelineExceptionNotHandled != null)
        {
            // if exception event is hooked, do not throw the exception
            return;
        }

        lock (_errors)
        {
            if (_errors.Count > 0)
            {
                AggregateException error = new($"Pipeline '{_name}' was terminated because of one or more unexpected errors", _errors);
                _errors.Clear();
                throw error;
            }
        }
    }

    private double EstimateProgress()
    {
        long startTicks = _replayDescriptor.Start.Ticks;
        long endTicks = _replayDescriptor.End.Ticks;
        double totalProgress = 0.0;
        int componentCount = _components.Count;

        foreach (PipelineElement component in _components)
        {
            double componentProgress = 0.0;
            if (component.IsFinalized)
            {
                // a finalized component is by definition 100% done
                componentProgress = 1.0;
            }
            else if (component.StateObject is Subpipeline sub)
            {
                // ensures dynamically-added subpipelines are actually running
                if (sub.IsRunning && sub.Components.Count > 0)
                {
                    // recursively estimate progress for subpipelines
                    componentProgress = sub.EstimateProgress();
                }
                else
                {
                    componentCount--; // disregard stopped/empty subpipelines entirely
                }
            }
            else if (component.Outputs.Count > 0 || component.Inputs.Count > 0)
            {
                long ticksSinceStart = 0;

                // use average originating time across all outputs and inputs to estimate percent completion
                int streamCount = component.Outputs.Count + component.Inputs.Count;

                if (streamCount > 0)
                {
                    foreach (KeyValuePair<string, IReceiver> input in component.Inputs)
                    {
                        ticksSinceStart += (input.Value.LastEnvelope.OriginatingTime.Ticks - startTicks) / streamCount;
                    }

                    foreach (KeyValuePair<string, IEmitter> output in component.Outputs)
                    {
                        ticksSinceStart += (output.Value.LastEnvelope.OriginatingTime.Ticks - startTicks) / streamCount;
                    }
                }
                else
                {
                    ticksSinceStart = 0;
                }

                // if we have seen a message with maximum originating time, call it done
                componentProgress = Math.Min(Math.Max(ticksSinceStart / (double)(endTicks - startTicks), 0), 1);
            }
            else if (component.IsActivated)
            {
                // if we cannot estimate progress, default to 50%
                componentProgress = 0.5;
            }
            else if (component.IsDeactivating)
            {
                // once pipeline has told the component to deactivate, it is 90% done
                componentProgress = 0.9;
            }
            else if (component.IsDeactivated)
            {
                // when component has been deactivated, it is 99% done (just needs to be finalized)
                componentProgress = 0.99;
            }

            // sum of all components progress
            totalProgress += componentProgress;
        }

        // use the average as a rough estimate of overall pipeline progress
        return totalProgress / componentCount;
    }

    private void ReportProgress()
    {
        double progress = EstimateProgress();
        _progressReporter.Report(progress);
    }
}
