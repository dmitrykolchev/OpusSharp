// <copyright file="DiagnosticsSampler.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using Neutrino.Psi.Common;
using Neutrino.Psi.Components;
using Neutrino.Psi.Executive;
using Neutrino.Psi.Streams;


namespace Neutrino.Psi.Diagnostics;

/// <summary>
/// Component that periodically samples and produces a stream of collected diagnostics information from a running pipeline; including graph structure and message flow statistics.
/// </summary>
internal class DiagnosticsSampler : ISourceComponent, IDisposable
{
    private readonly Pipeline _pipeline;
    private readonly string _name;
    private readonly DiagnosticsCollector _collector;
    private Time.TimerDelegate _timerDelegate;
    private bool _running;
    private Platform.ITimer _timer;

    /// <summary>
    /// Initializes a new instance of the <see cref="DiagnosticsSampler"/> class.
    /// </summary>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="collector">Diagnostics collector.</param>
    /// <param name="configuration">Diagnostics configuration.</param>
    /// <param name="name">An optional name for the component.</param>
    public DiagnosticsSampler(Pipeline pipeline, DiagnosticsCollector collector, DiagnosticsConfiguration configuration, string name = nameof(DiagnosticsSampler))
    {
        _pipeline = pipeline;
        _name = name;
        _collector = collector;
        Config = configuration;
        Diagnostics = pipeline.CreateEmitter<PipelineDiagnostics>(this, nameof(Diagnostics));
    }

    /// <summary>
    /// Finalizes an instance of the <see cref="DiagnosticsSampler"/> class.
    /// Releases underlying unmanaged timer.
    /// </summary>
    ~DiagnosticsSampler()
    {
        if (_running)
        {
            _timer.Stop();
        }
    }

    /// <summary>
    /// Gets emitter producing pipeline diagnostics information.
    /// </summary>
    public Emitter<PipelineDiagnostics> Diagnostics { get; private set; }

    /// <summary>
    /// Gets the diagnostics configuration.
    /// </summary>
    public DiagnosticsConfiguration Config { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        Stop();
    }

    /// <inheritdoc />
    public void Start(Action<DateTime> notifyCompletionTime)
    {
        // notify that this is an infinite source component
        notifyCompletionTime(DateTime.MaxValue);
        if (_collector != null)
        {
            _timerDelegate = new Time.TimerDelegate((i, m, c, d1, d2) => Update());
            _timer = Platform.Specific.TimerStart((uint)Config.SamplingInterval.TotalMilliseconds, _timerDelegate);
            _running = true;
        }
    }

    /// <inheritdoc />
    public void Stop(DateTime finalOriginatingTime, Action notifyCompleted)
    {
        Stop();
        notifyCompleted();
    }

    /// <inheritdoc />
    public override string ToString() => _name;

    private void Stop()
    {
        if (_running)
        {
            _timer.Stop();
            _running = false;
            Update(); // final update even if interval hasn't elapsed
        }

        GC.SuppressFinalize(this);
    }

    private void Update()
    {
        PipelineDiagnosticsInternal root = _collector.CurrentRoot;
        if (root != null)
        {
            DateTime currentTime = _pipeline.GetCurrentTime();
            root.CloseSample(currentTime - Config.AveragingTimeSpan);
            Diagnostics.Post(new PipelineDiagnostics(root, Config.IncludeStoppedPipelines, Config.IncludeStoppedPipelineElements), currentTime);
        }
    }
}
