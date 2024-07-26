// <copyright file="DiagnosticsCollector.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Collections.Concurrent;
using Neutrino.Psi.Common;
using Neutrino.Psi.Executive;
using Neutrino.Psi.Streams;


namespace Neutrino.Psi.Diagnostics;

/// <summary>
/// Class that collects diagnostics information from a running pipeline; including graph structure changes and message flow statistics.
/// </summary>
internal class DiagnosticsCollector
{
    private readonly DiagnosticsConfiguration _diagnosticsConfig;

    private readonly ConcurrentDictionary<int, PipelineDiagnosticsInternal> _graphs = new();
    private readonly ConcurrentDictionary<int, PipelineDiagnosticsInternal.EmitterDiagnostics> _outputs = new();
    private readonly ConcurrentDictionary<object, PipelineDiagnosticsInternal.PipelineElementDiagnostics> _connectors = new();

    public DiagnosticsCollector(DiagnosticsConfiguration diagnosticsConfig)
    {
        _diagnosticsConfig = diagnosticsConfig ?? DiagnosticsConfiguration.Default;
    }

    /// <summary>
    /// Gets current root graph (if any).
    /// </summary>
    public PipelineDiagnosticsInternal CurrentRoot { get; private set; }

    /// <summary>
    /// Pipeline creation.
    /// </summary>
    /// <remarks>Called upon pipeline construction.</remarks>
    /// <param name="pipeline">Pipeline being created.</param>
    public void PipelineCreate(Pipeline pipeline)
    {
        PipelineDiagnosticsInternal graph = new(pipeline.Id, pipeline.Name);
        if (!_graphs.TryAdd(pipeline.Id, graph))
        {
            throw new InvalidOperationException("Failed to add created graph");
        }

        if (CurrentRoot == null && pipeline is not Subpipeline)
        {
            CurrentRoot = graph;
        }
    }

    /// <summary>
    /// Pipeline start.
    /// </summary>
    /// <remarks>Called upon pipeline run, before child components started.</remarks>
    /// <param name="pipeline">Pipeline being started.</param>
    public void PipelineStart(Pipeline pipeline)
    {
        _graphs[pipeline.Id].IsPipelineRunning = true;
    }

    /// <summary>
    /// Pipeline stopped.
    /// </summary>
    /// <remarks>Called after child components finalized, before scheduler stopped.</remarks>
    /// <param name="pipeline">Pipeline being stopped.</param>
    public void PipelineStopped(Pipeline pipeline)
    {
        _graphs[pipeline.Id].IsPipelineRunning = false;
    }

    /// <summary>
    /// Pipeline disposal.
    /// </summary>
    /// <remarks>Called after pipeline disposal.</remarks>
    /// <param name="pipeline">Pipeline being disposed.</param>
    public void PipelineDisposed(Pipeline pipeline)
    {
        if (!_graphs.TryRemove(pipeline.Id, out _))
        {
            throw new InvalidOperationException("Failed to remove disposed graph");
        }
    }

    /// <summary>
    /// Element (representing component) created.
    /// </summary>
    /// <remarks>Called upon element construction (first moment component becomes a pipeline element).</remarks>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="element">Element being created.</param>
    /// <param name="component">Component associated with this pipeline element.</param>
    public void PipelineElementCreate(Pipeline pipeline, PipelineElement element, object component)
    {
        PipelineDiagnosticsInternal.PipelineElementDiagnostics node = new(element, pipeline.Id);
        if (node.Kind == PipelineElementKind.Subpipeline)
        {
            node.RepresentsSubpipeline = _graphs[((Pipeline)component).Id];
            _graphs[pipeline.Id].Subpipelines.TryAdd(node.RepresentsSubpipeline.Id, node.RepresentsSubpipeline);
        }
        else if (node.Kind == PipelineElementKind.Connector)
        {
            if (_connectors.TryGetValue(component, out PipelineDiagnosticsInternal.PipelineElementDiagnostics bridge))
            {
                node.ConnectorBridgeToPipelineElement = bridge;
                bridge.ConnectorBridgeToPipelineElement = node;
            }
            else
            {
                if (!_connectors.TryAdd(component, node))
                {
                    throw new InvalidOperationException("Failed to add connector");
                }
            }
        }

        _graphs[pipeline.Id].PipelineElements.TryAdd(element.Id, node);
    }

    /// <summary>
    /// Element (representing component) being started.
    /// </summary>
    /// <remarks>Called after scheduling calls to start handler.</remarks>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="element">Element being started.</param>
    public void PipelineElementStart(Pipeline pipeline, PipelineElement element)
    {
        _graphs[pipeline.Id].PipelineElements[element.Id].IsRunning = true;
        _graphs[pipeline.Id].PipelineElements[element.Id].DiagnosticState = element.StateObject.ToString();
    }

    /// <summary>
    /// Element (representing component) being stopped.
    /// </summary>
    /// <remarks>Called after scheduling calls to stop handler.</remarks>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="element">Element being stopped.</param>
    public void PipelineElementStop(Pipeline pipeline, PipelineElement element)
    {
        _graphs[pipeline.Id].PipelineElements[element.Id].IsRunning = false;
        _graphs[pipeline.Id].PipelineElements[element.Id].DiagnosticState = element.StateObject.ToString();
    }

    /// <summary>
    /// Element (representing component) being finalized.
    /// </summary>
    /// <remarks>Called after scheduling calls to final handler.</remarks>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="element">Element being finalized.</param>
    public void PipelineElementFinal(Pipeline pipeline, PipelineElement element)
    {
        _graphs[pipeline.Id].PipelineElements[element.Id].Finalized = true;
    }

    /// <summary>
    /// Element (representing component) created.
    /// </summary>
    /// <remarks>Called upon element disposal.</remarks>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="element">Element being created.</param>
    public void PipelineElementDisposed(Pipeline pipeline, PipelineElement element)
    {
        _graphs[pipeline.Id].PipelineElements.TryRemove(element.Id, out PipelineDiagnosticsInternal.PipelineElementDiagnostics _);
    }

    /// <summary>
    /// Output (emitter) added to element.
    /// </summary>
    /// <remarks>Called just after element start (or dynamically if added once pipeline running).</remarks>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="element">Element to which emitter is being added.</param>
    /// <param name="emitter">Emitter being added.</param>
    public void PipelineElementAddEmitter(Pipeline pipeline, PipelineElement element, IEmitter emitter)
    {
        PipelineDiagnosticsInternal.PipelineElementDiagnostics node = _graphs[pipeline.Id].PipelineElements[element.Id];
        PipelineDiagnosticsInternal.EmitterDiagnostics output = new(emitter.Id, emitter.Name, emitter.Type.FullName, node);
        node.Emitters.TryAdd(output.Id, output);
        if (!_outputs.TryAdd(output.Id, output))
        {
            throw new InvalidOperationException("Failed to add emitter/output");
        }
    }

    /// <summary>
    /// Emitter had been renamed.
    /// </summary>
    /// <remarks>Called when IEmitter.Name property set post-construction.</remarks>
    /// <param name="emitter">Emitter being renamed.</param>
    public void EmitterRenamed(IEmitter emitter)
    {
        _outputs[emitter.Id].Name = emitter.Name;
    }

    /// <summary>
    /// Input (receiver) added to element.
    /// </summary>
    /// <remarks>Called just after element start (or dynamically if added once pipeline running).</remarks>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="element">Element to which receiver is being added.</param>
    /// <param name="receiver">Receiver being added.</param>
    public void PipelineElementAddReceiver(Pipeline pipeline, PipelineElement element, IReceiver receiver)
    {
        PipelineDiagnosticsInternal.PipelineElementDiagnostics node = _graphs[pipeline.Id].PipelineElements[element.Id];
        node.Receivers.TryAdd(receiver.Id, new PipelineDiagnosticsInternal.ReceiverDiagnostics(receiver.Id, receiver.Name, receiver.Type.FullName, node));
    }

    /// <summary>
    /// Input subscribed to input.
    /// </summary>
    /// <remarks>Called just after element start (or dynamically if subscribed once pipeline running).</remarks>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="element">Element to which receiver belongs.</param>
    /// <param name="receiver">Receiver subscribing to emitter.</param>
    /// <param name="emitter">Emitter to which receiver is subscribing.</param>
    /// <param name="deliveryPolicyName">The name of the delivery policy used.</param>
    public void PipelineElementReceiverSubscribe(Pipeline pipeline, PipelineElement element, IReceiver receiver, IEmitter emitter, string deliveryPolicyName)
    {
        PipelineDiagnosticsInternal.ReceiverDiagnostics input = _graphs[pipeline.Id].PipelineElements[element.Id].Receivers[receiver.Id];
        PipelineDiagnosticsInternal.EmitterDiagnostics output = _outputs[emitter.Id];
        input.Source = output;
        input.DeliveryPolicyName = deliveryPolicyName;
        output.Targets.TryAdd(input.Id, input);
    }

    /// <summary>
    /// Input unsubscribed to input.
    /// </summary>
    /// <remarks>Called upon unsubscribe (only if pipeline running).</remarks>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="element">Element to which receiver belongs.</param>
    /// <param name="receiver">Receiver unsubscribing to emitter.</param>
    /// <param name="emitter">Emitter from which receiver is unsubscribing.</param>
    public void PipelineElementReceiverUnsubscribe(Pipeline pipeline, PipelineElement element, IReceiver receiver, IEmitter emitter)
    {
        _outputs[emitter.Id].Targets.TryRemove(receiver.Id, out PipelineDiagnosticsInternal.ReceiverDiagnostics _);
        _graphs[pipeline.Id].PipelineElements[element.Id].Receivers[receiver.Id].Source = null;
    }

    /// <summary>
    /// Get collector of diagnostics message flow statistics for a single receiver.
    /// </summary>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="element">Element to which receiver belongs.</param>
    /// <param name="receiver">Receiver having completed processing.</param>
    public ReceiverCollector GetReceiverDiagnosticsCollector(Pipeline pipeline, PipelineElement element, IReceiver receiver)
    {
        return new(_graphs[pipeline.Id].PipelineElements[element.Id], receiver.Id, _diagnosticsConfig);
    }

    /// <summary>
    /// Class that collects diagnostics message flow statistics for a single receiver.
    /// </summary>
    public class ReceiverCollector
    {
        private readonly PipelineDiagnosticsInternal.PipelineElementDiagnostics _pipelineElementDiagnostics;
        private readonly PipelineDiagnosticsInternal.ReceiverDiagnostics _receiverDiagnostics;
        private readonly DiagnosticsConfiguration _diagnosticsConfig;

        /// <summary>
        /// Initializes a new instance of the <see cref="ReceiverCollector"/> class.
        /// </summary>
        /// <param name="pipelineElementDiagnostics">Pipeline element diagnostics instance associated with this receiver.</param>
        /// <param name="receiverId">The id for the receiver to collect diagnostics about.</param>
        /// <param name="diagnosticsConfig">Diagnostics configuration.</param>
        internal ReceiverCollector(
            PipelineDiagnosticsInternal.PipelineElementDiagnostics pipelineElementDiagnostics,
            int receiverId,
            DiagnosticsConfiguration diagnosticsConfig)
        {
            _pipelineElementDiagnostics = pipelineElementDiagnostics;
            _receiverDiagnostics = _pipelineElementDiagnostics.Receivers[receiverId];
            _diagnosticsConfig = diagnosticsConfig;
        }

        /// <summary>
        /// Update of the pipeline element diagnostic state.
        /// </summary>
        /// <param name="diagnosticState">The new diagnostic state.</param>
        public void UpdateDiagnosticState(string diagnosticState)
        {
            _pipelineElementDiagnostics.DiagnosticState = diagnosticState;
        }

        /// <summary>
        /// Message was emitted towards a receiver.
        /// </summary>
        /// <param name="envelope">Message envelope.</param>
        /// <param name="diagnosticsTime">Time at which to record the diagnostic information.</param>
        public void MessageEmitted(Envelope envelope, DateTime diagnosticsTime)
        {
            _receiverDiagnostics.AddMessageEmitted(diagnosticsTime);
            _receiverDiagnostics.AddMessageCreatedLatency(envelope.CreationTime - envelope.OriginatingTime, diagnosticsTime);
            _receiverDiagnostics.AddMessageEmittedLatency(diagnosticsTime - envelope.OriginatingTime, diagnosticsTime);
        }

        /// <summary>
        /// Capture a queue size update.
        /// </summary>
        /// <param name="queueSize">Awaiting delivery queue size.</param>
        /// <param name="diagnosticsTime">Time at which to record the diagnostic information.</param>
        public void QueueSizeUpdate(int queueSize, DateTime diagnosticsTime)
        {
            _receiverDiagnostics.AddDeliveryQueueSize(queueSize, diagnosticsTime);
        }

        /// <summary>
        /// Message was dropped by receiver.
        /// </summary>
        /// <param name="diagnosticsTime">Time at which to record the diagnostic information.</param>
        public void MessageDropped(DateTime diagnosticsTime)
        {
            _receiverDiagnostics.AddMessageDropped(diagnosticsTime);
        }

        /// <summary>
        /// Capture throttle status update.
        /// </summary>
        /// <param name="receiverIsThrottled">Whether input is throttled.</param>
        public void PipelineElementReceiverThrottle(bool receiverIsThrottled)
        {
            _receiverDiagnostics.ReceiverIsThrottled = receiverIsThrottled;
        }

        /// <summary>
        /// Message was processed by component.
        /// </summary>
        /// <param name="envelope">Message envelope.</param>
        /// <param name="receiverStartTime">The time the runtime started executing the receiver for the message.</param>
        /// <param name="receiverEndTime">The time the runtime finished executing the receiver for the message.</param>
        /// <param name="messageSize">Message size (bytes).</param>
        /// <param name="diagnosticsTime">Time at which to record the diagnostic information.</param>
        public void MessageProcessed(Envelope envelope, DateTime receiverStartTime, DateTime receiverEndTime, int messageSize, DateTime diagnosticsTime)
        {
            _receiverDiagnostics.AddMessageProcessed(diagnosticsTime);
            _receiverDiagnostics.AddMessageSize(messageSize, diagnosticsTime);
            _receiverDiagnostics.AddMessageReceivedLatency(receiverStartTime - envelope.OriginatingTime, diagnosticsTime);
            _receiverDiagnostics.AddMessageProcessTime(receiverEndTime - receiverStartTime, diagnosticsTime);
        }
    }
}
