// <copyright file="PipelineDiagnostics.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;


namespace Neutrino.Psi.Diagnostics;

/// <summary>
/// Represents diagnostic information about a pipeline.
/// </summary>
/// <remarks>
/// This is a summarized snapshot of the graph with aggregated message statistics which is posted to the
/// diagnostics stream. It has a much smaller memory footprint compared with PipelineDiagnosticsInternal.
/// </remarks>
public class PipelineDiagnostics
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PipelineDiagnostics"/> class.
    /// </summary>
    /// <param name="id">Pipeline ID.</param>
    /// <param name="name">Pipeline name.</param>
    /// <param name="isPipelineRunning">Whether the pipeline is running (after started, before stopped).</param>
    /// <param name="parentPipelineDiagnostics">Parent pipeline of this pipeline (it any).</param>
    /// <param name="subpipelineDiagnostics">Subpipelines of this pipeline.</param>
    /// <param name="pipelineElements">Elements in this pipeline.</param>
    public PipelineDiagnostics(
        int id,
        string name,
        bool isPipelineRunning,
        PipelineDiagnostics parentPipelineDiagnostics,
        PipelineDiagnostics[] subpipelineDiagnostics,
        PipelineElementDiagnostics[] pipelineElements)
    {
        Id = id;
        Name = name;
        IsPipelineRunning = isPipelineRunning;
        ParentPipelineDiagnostics = parentPipelineDiagnostics;
        SubpipelineDiagnostics = subpipelineDiagnostics ?? new PipelineDiagnostics[0];
        PipelineElements = pipelineElements ?? new PipelineElementDiagnostics[0];
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PipelineDiagnostics"/> class.
    /// </summary>
    /// <param name="pipelineDiagnosticsInternal">Internal pipeline diagnostics.</param>
    /// <param name="includeStoppedPipelines">Whether to include stopped pipelines.</param>
    /// <param name="includeStoppedPipelineElements">Whether to include stopped pipeline elements.</param>
    internal PipelineDiagnostics(PipelineDiagnosticsInternal pipelineDiagnosticsInternal, bool includeStoppedPipelines, bool includeStoppedPipelineElements)
    {
        // This is the constructor used when creating a summary snapshot to be posted.
        // The Builder is used to construct one-and-only-one instance of each part of the
        // graph and to initialize circular references (via delayed thunks).
        Builder builder = new();
        Initialize(pipelineDiagnosticsInternal, null, builder, includeStoppedPipelines, includeStoppedPipelineElements);
        builder.InvokeThunks();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PipelineDiagnostics"/> class.
    /// </summary>
    /// <param name="pipelineDiagnosticsInternal">Internal pipeline diagnostics.</param>
    /// <param name="parent">Parent pipeline diagnostics to this pipeline diagnostics.</param>
    /// <param name="builder">Builder of pipeline parts used during construction.</param>
    /// <param name="includeStoppedPipelines">Whether to include stopped pipelines.</param>
    /// <param name="includeStoppedPipelineElements">Whether to include stopped pipeline elements.</param>
    private PipelineDiagnostics(PipelineDiagnosticsInternal pipelineDiagnosticsInternal, PipelineDiagnostics parent, Builder builder, bool includeStoppedPipelines, bool includeStoppedPipelineElements)
    {
        Initialize(pipelineDiagnosticsInternal, parent, builder, includeStoppedPipelines, includeStoppedPipelineElements);
    }

    /// <summary>
    /// Gets pipeline ID.
    /// </summary>
    public int Id { get; private set; }

    /// <summary>
    /// Gets pipeline name.
    /// </summary>
    public string Name { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the pipeline is running (after started, before stopped).
    /// </summary>
    public bool IsPipelineRunning { get; private set; }

    /// <summary>
    /// Gets or sets elements in this pipeline.
    /// </summary>
    public PipelineElementDiagnostics[] PipelineElements { get; set; }

    /// <summary>
    /// Gets or sets parent pipeline of this pipeline (it any).
    /// </summary>
    public PipelineDiagnostics ParentPipelineDiagnostics { get; set; }

    /// <summary>
    /// Gets or sets subpipelines of this pipeline.
    /// </summary>
    public PipelineDiagnostics[] SubpipelineDiagnostics { get; set; }

    /// <summary>
    /// Gets ancestor pipeline diagnostics.
    /// </summary>
    public IEnumerable<PipelineDiagnostics> AncestorPipelines
    {
        get
        {
            PipelineDiagnostics parent = ParentPipelineDiagnostics;
            while (parent != null)
            {
                yield return parent;
                parent = parent.ParentPipelineDiagnostics;
            }
        }
    }

    /// <summary>
    /// Gets descendant pipeline diagnostics.
    /// </summary>
    public IEnumerable<PipelineDiagnostics> DescendantPipelines
    {
        get
        {
            foreach (PipelineDiagnostics sub in SubpipelineDiagnostics)
            {
                yield return sub;
                foreach (PipelineDiagnostics subsub in sub.DescendantPipelines)
                {
                    yield return subsub;
                }
            }
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PipelineDiagnostics"/> class.
    /// </summary>
    /// <param name="pipelineDiagnosticsInternal">Internal pipeline diagnostics.</param>
    /// <param name="parent">Parent pipeline diagnostics to this pipeline diagnostics.</param>
    /// <param name="builder">Builder of pipeline parts used during construction.</param>
    /// <param name="includeStoppedPipelines">Whether to include stopped pipelines.</param>
    /// <param name="includeStoppedPipelineElements">Whether to include stopped pipeline element .</param>
    private void Initialize(PipelineDiagnosticsInternal pipelineDiagnosticsInternal, PipelineDiagnostics parent, Builder builder, bool includeStoppedPipelines, bool includeStoppedPipelineElements)
    {
        Id = pipelineDiagnosticsInternal.Id;
        Name = pipelineDiagnosticsInternal.Name;
        IsPipelineRunning = pipelineDiagnosticsInternal.IsPipelineRunning;
        ParentPipelineDiagnostics = parent;
        SubpipelineDiagnostics =
            pipelineDiagnosticsInternal.Subpipelines.Values
                .Where(s => includeStoppedPipelines || s.IsPipelineRunning)
                .Select(s => builder.GetOrCreatePipelineDiagnostics(s, this, includeStoppedPipelines, includeStoppedPipelineElements))
                .ToArray();
        PipelineElements =
            pipelineDiagnosticsInternal.PipelineElements.Values
                .Where(pe => includeStoppedPipelineElements || (pe.IsRunning && (pe.RepresentsSubpipeline == null || pe.RepresentsSubpipeline.IsPipelineRunning)))
                .Select(pe => builder.GetOrCreatePipelineElementDiagnostics(pe))
                .ToArray();
    }

    /// <summary>
    /// Represents diagnostic information about a pipeline element.
    /// </summary>
    public class PipelineElementDiagnostics
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PipelineElementDiagnostics"/> class.
        /// </summary>
        /// <param name="id">Pipeline element ID.</param>
        /// <param name="name">Pipeline element name.</param>
        /// <param name="typeName">Pipeline element type name.</param>
        /// <param name="kind">Pipeline element kind.</param>
        /// <param name="isRunning">Whether the pipeline element is running (after started, before stopped).</param>
        /// <param name="finalized">Whether the pipeline element is finalized.</param>
        /// <param name="diagnosticState">Diagnostic state for the pipeline element.</param>
        /// <param name="pipelineId">ID of pipeline to which this element belongs.</param>
        /// <param name="emitters">Pipeline element emitters.</param>
        /// <param name="receivers">Pipeline element receivers.</param>
        /// <param name="representsSubpipeline">Pipeline which this element represents (e.g. Subpipeline).</param>
        /// <param name="connectorBridgeToPipelineElement">Bridge to pipeline element in another pipeline (e.g. Connectors).</param>
        public PipelineElementDiagnostics(
            int id,
            string name,
            string typeName,
            PipelineElementKind kind,
            bool isRunning,
            bool finalized,
            string diagnosticState,
            int pipelineId,
            EmitterDiagnostics[] emitters,
            ReceiverDiagnostics[] receivers,
            PipelineDiagnostics representsSubpipeline,
            PipelineElementDiagnostics connectorBridgeToPipelineElement)
        {
            Id = id;
            Name = name;
            TypeName = typeName;
            Kind = kind;
            IsRunning = isRunning;
            Finalized = finalized;
            DiagnosticState = diagnosticState;
            PipelineId = pipelineId;
            Emitters = emitters;
            Receivers = receivers;
            RepresentsSubpipeline = representsSubpipeline;
            ConnectorBridgeToPipelineElement = connectorBridgeToPipelineElement;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PipelineElementDiagnostics"/> class.
        /// </summary>
        /// <param name="pipelineElementDiagnosticsInternal">Internal pipeline element diagnostics.</param>
        /// <param name="builder">Builder of pipeline parts used during construction.</param>
        internal PipelineElementDiagnostics(PipelineDiagnosticsInternal.PipelineElementDiagnostics pipelineElementDiagnosticsInternal, Builder builder)
        {
            Id = pipelineElementDiagnosticsInternal.Id;
            Name = pipelineElementDiagnosticsInternal.Name;
            TypeName = pipelineElementDiagnosticsInternal.TypeName;
            Kind = pipelineElementDiagnosticsInternal.Kind;
            IsRunning = pipelineElementDiagnosticsInternal.IsRunning;
            Finalized = pipelineElementDiagnosticsInternal.Finalized;
            DiagnosticState = pipelineElementDiagnosticsInternal.DiagnosticState;
            Emitters = pipelineElementDiagnosticsInternal.Emitters.Values.Select(e => builder.GetOrCreateEmitterDiagnostics(e)).ToArray();
            Receivers = pipelineElementDiagnosticsInternal.Receivers.Values.Select(r => builder.GetOrCreateReceiverDiagnostics(r)).ToArray();
            PipelineId = pipelineElementDiagnosticsInternal.PipelineId;
            if (pipelineElementDiagnosticsInternal.RepresentsSubpipeline != null)
            {
                builder.EnqueueThunk(() =>
                {
                    if (builder.Pipelines.TryGetValue(pipelineElementDiagnosticsInternal.RepresentsSubpipeline.Id, out PipelineDiagnostics pipeline))
                    {
                        RepresentsSubpipeline = pipeline;
                    }
                });
            }

            if (pipelineElementDiagnosticsInternal.ConnectorBridgeToPipelineElement != null)
            {
                builder.EnqueueThunk(() =>
                {
                    if (builder.PipelineElements.TryGetValue(pipelineElementDiagnosticsInternal.ConnectorBridgeToPipelineElement.Id, out PipelineElementDiagnostics bridge))
                    {
                        ConnectorBridgeToPipelineElement = bridge;
                    }
                });
            }
        }

        /// <summary>
        /// Gets pipeline element ID.
        /// </summary>
        public int Id { get; }

        /// <summary>
        /// Gets pipeline element name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets pipeline element component type name.
        /// </summary>
        public string TypeName { get; private set; }

        /// <summary>
        /// Gets pipeline element kind.
        /// </summary>
        public PipelineElementKind Kind { get; }

        /// <summary>
        /// Gets a value indicating whether the pipeline element is running (after started, before stopped).
        /// </summary>
        public bool IsRunning { get; }

        /// <summary>
        /// Gets a value indicating whether the pipeline element is finalized.
        /// </summary>
        public bool Finalized { get; }

        /// <summary>
        /// Gets the diagnostic state for the pipeline element.
        /// </summary>
        public string DiagnosticState { get; }

        /// <summary>
        /// Gets ID of pipeline to which this element belongs.
        /// </summary>
        public int PipelineId { get; }

        /// <summary>
        /// Gets or sets pipeline element emitters.
        /// </summary>
        public EmitterDiagnostics[] Emitters { get; set; }

        /// <summary>
        /// Gets or sets pipeline element receivers.
        /// </summary>
        public ReceiverDiagnostics[] Receivers { get; set; }

        /// <summary>
        /// Gets or sets pipeline which this element represents (e.g. Subpipeline).
        /// </summary>
        /// <remarks>This is used when a pipeline element is a pipeline (e.g. Subpipeline).</remarks>
        public PipelineDiagnostics RepresentsSubpipeline { get; set; }

        /// <summary>
        /// Gets or sets bridge to pipeline element in another pipeline (e.g. Connectors).
        /// </summary>
        public PipelineElementDiagnostics ConnectorBridgeToPipelineElement { get; set; }
    }

    /// <summary>
    /// Represents diagnostic information about a pipeline element emitter.
    /// </summary>
    public class EmitterDiagnostics
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="EmitterDiagnostics"/> class.
        /// </summary>
        /// <param name="id">Emitter ID.</param>
        /// <param name="name">Emitter name.</param>
        /// <param name="type">Emitter type.</param>
        /// <param name="pipelineElement">Pipeline element to which emitter belongs.</param>
        /// <param name="targets">Emitter target receivers.</param>
        public EmitterDiagnostics(
            int id,
            string name,
            string type,
            PipelineElementDiagnostics pipelineElement,
            ReceiverDiagnostics[] targets)
        {
            Id = id;
            Name = name;
            Type = type;
            PipelineElement = pipelineElement;
            Targets = targets;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="EmitterDiagnostics"/> class.
        /// </summary>
        /// <param name="emitterDiagnostics">Internal emitter diagnostics.</param>
        /// <param name="builder">Builder of pipeline parts used during construction.</param>
        internal EmitterDiagnostics(PipelineDiagnosticsInternal.EmitterDiagnostics emitterDiagnostics, Builder builder)
        {
            Id = emitterDiagnostics.Id;
            Name = emitterDiagnostics.Name;
            Type = emitterDiagnostics.Type;
            builder.EnqueueThunk(() =>
            {
                if (builder.PipelineElements.TryGetValue(emitterDiagnostics.PipelineElement.Id, out PipelineElementDiagnostics element))
                {
                    PipelineElement = element;
                }
            });
            builder.EnqueueThunk(() => Targets = emitterDiagnostics.Targets.Values.Select(r => builder.Receivers.TryGetValue(r.Id, out ReceiverDiagnostics receiver) ? receiver : null).Where(r => r != null).ToArray());
        }

        /// <summary>
        /// Gets emitter ID.
        /// </summary>
        public int Id { get; }

        /// <summary>
        /// Gets emitter name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets emitter type.
        /// </summary>
        public string Type { get; }

        /// <summary>
        /// Gets or sets pipeline element to which emitter belongs.
        /// </summary>
        public PipelineElementDiagnostics PipelineElement { get; set; }

        /// <summary>
        /// Gets or sets emitter target receivers.
        /// </summary>
        public ReceiverDiagnostics[] Targets { get; set; }
    }

    /// <summary>
    /// Represents diagnostic information about a pipeline element receiver.
    /// </summary>
    public class ReceiverDiagnostics
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ReceiverDiagnostics"/> class.
        /// </summary>
        /// <param name="id">Receiver ID.</param>
        /// <param name="receiverName">Receiver name.</param>
        /// <param name="deliveryPolicyName">Name of delivery policy used by receiver.</param>
        /// <param name="typeName">Receiver type name.</param>
        /// <param name="receiverIsThrottled">Whether receiver is throttled.</param>
        /// <param name="lastDeliveryQueueSize">Delivery queue size at last message.</param>
        /// <param name="avgDeliveryQueueSize">Average delivery queue size.</param>
        /// <param name="totalMessageEmittedCount">Total count of emitted messages.</param>
        /// <param name="windowMessageEmittedCount">Count of emitted messages in last averaging time window.</param>
        /// <param name="totalMessageProcessedCount">Total count of processed messages.</param>
        /// <param name="windowMessageProcessedCount">Count of processed messages in last averaging time window.</param>
        /// <param name="totalMessageDroppedCount">Total count of dropped messages.</param>
        /// <param name="windowMessageDroppedCount">Count of dropped messages in last averaging time window.</param>
        /// <param name="lastMessageCreatedLatency">Latency with which the last message was created.</param>
        /// <param name="avgMessageCreatedLatency">Average message created latency in last averaging time window.</param>
        /// <param name="lastMessageEmittedLatency">Latency with which the last message was emitted.</param>
        /// <param name="avgMessageEmittedLatency">Average message emitted latency in last averaging time window.</param>
        /// <param name="lastMessageReceivedLatency">Latency with which the last message was received.</param>
        /// <param name="avgMessageReceivedLatency">Average message received latency in last averaging time window.</param>
        /// <param name="lastMessageProcessTime">Receiver processing time for the last message.</param>
        /// <param name="avgMessageProcessTime">Average receiver processing time in last averaging time window.</param>
        /// <param name="lastMessageSize">Message size for the last message.</param>
        /// <param name="avgMessageSize">Average message size over in last averaging time window.</param>
        /// <param name="pipelineElement">Pipeline element to which emitter belongs.</param>
        /// <param name="source">Receiver's source emitter.</param>
        public ReceiverDiagnostics(
            int id,
            string receiverName,
            string deliveryPolicyName,
            string typeName,
            bool receiverIsThrottled,
            double lastDeliveryQueueSize,
            double avgDeliveryQueueSize,
            int totalMessageEmittedCount,
            int windowMessageEmittedCount,
            int totalMessageProcessedCount,
            int windowMessageProcessedCount,
            int totalMessageDroppedCount,
            int windowMessageDroppedCount,
            double lastMessageCreatedLatency,
            double avgMessageCreatedLatency,
            double lastMessageEmittedLatency,
            double avgMessageEmittedLatency,
            double lastMessageReceivedLatency,
            double avgMessageReceivedLatency,
            double lastMessageProcessTime,
            double avgMessageProcessTime,
            double lastMessageSize,
            double avgMessageSize,
            PipelineElementDiagnostics pipelineElement,
            EmitterDiagnostics source)
        {
            Id = id;
            ReceiverName = receiverName;
            DeliveryPolicyName = deliveryPolicyName;
            TypeName = typeName;
            ReceiverIsThrottled = receiverIsThrottled;
            LastDeliveryQueueSize = lastDeliveryQueueSize;
            AvgDeliveryQueueSize = avgDeliveryQueueSize;
            TotalMessageEmittedCount = totalMessageEmittedCount;
            WindowMessageEmittedCount = windowMessageEmittedCount;
            TotalMessageProcessedCount = totalMessageProcessedCount;
            WindowMessageProcessedCount = windowMessageProcessedCount;
            TotalMessageDroppedCount = totalMessageDroppedCount;
            WindowMessageDroppedCount = windowMessageDroppedCount;
            LastMessageCreatedLatency = lastMessageCreatedLatency;
            AvgMessageCreatedLatency = avgMessageCreatedLatency;
            LastMessageEmittedLatency = lastMessageEmittedLatency;
            AvgMessageEmittedLatency = avgMessageEmittedLatency;
            LastMessageReceivedLatency = lastMessageReceivedLatency;
            AvgMessageReceivedLatency = avgMessageReceivedLatency;
            LastMessageProcessTime = lastMessageProcessTime;
            AvgMessageProcessTime = avgMessageProcessTime;
            LastMessageSize = lastMessageSize;
            AvgMessageSize = avgMessageSize;
            PipelineElement = pipelineElement;
            Source = source;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ReceiverDiagnostics"/> class.
        /// </summary>
        /// <param name="receiverDiagnostics">Internal receiver diagnostics.</param>
        /// <param name="builder">Builder of pipeline parts used during construction.</param>
        internal ReceiverDiagnostics(PipelineDiagnosticsInternal.ReceiverDiagnostics receiverDiagnostics, Builder builder)
        {
            Id = receiverDiagnostics.Id;
            ReceiverName = receiverDiagnostics.ReceiverName;
            DeliveryPolicyName = receiverDiagnostics.DeliveryPolicyName;
            TypeName = receiverDiagnostics.TypeName;
            ReceiverIsThrottled = receiverDiagnostics.ReceiverIsThrottled;
            LastDeliveryQueueSize = receiverDiagnostics.DeliveryQueueSizeHistory.LastOrDefault().Item1;
            AvgDeliveryQueueSize = receiverDiagnostics.DeliveryQueueSizeHistory.AverageSize();
            TotalMessageEmittedCount = receiverDiagnostics.TotalMessageEmittedCount;
            WindowMessageEmittedCount = receiverDiagnostics.MessageEmittedCountHistory.Count;
            TotalMessageProcessedCount = receiverDiagnostics.TotalMessageProcessedCount;
            WindowMessageProcessedCount = receiverDiagnostics.MessageProcessedCountHistory.Count;
            TotalMessageDroppedCount = receiverDiagnostics.TotalMessageDroppedCount;
            WindowMessageDroppedCount = receiverDiagnostics.MessageDroppedCountHistory.Count;
            LastMessageCreatedLatency = receiverDiagnostics.MessageCreatedLatencyHistory.LastOrDefault().Item1.TotalMilliseconds;
            AvgMessageCreatedLatency = receiverDiagnostics.MessageCreatedLatencyHistory.AverageTime().TotalMilliseconds;
            LastMessageEmittedLatency = receiverDiagnostics.MessageEmittedLatencyHistory.LastOrDefault().Item1.TotalMilliseconds;
            AvgMessageEmittedLatency = receiverDiagnostics.MessageEmittedLatencyHistory.AverageTime().TotalMilliseconds;
            LastMessageReceivedLatency = receiverDiagnostics.MessageReceivedLatencyHistory.LastOrDefault().Item1.TotalMilliseconds;
            AvgMessageReceivedLatency = receiverDiagnostics.MessageReceivedLatencyHistory.AverageTime().TotalMilliseconds;
            LastMessageProcessTime = receiverDiagnostics.MessageProcessTimeHistory.LastOrDefault().Item1.TotalMilliseconds;
            AvgMessageProcessTime = receiverDiagnostics.MessageProcessTimeHistory.AverageTime().TotalMilliseconds;
            LastMessageSize = receiverDiagnostics.MessageSizeHistory.LastOrDefault().Item1;
            AvgMessageSize = receiverDiagnostics.MessageSizeHistory.AverageSize();
            builder.EnqueueThunk(() =>
            {
                if (builder.PipelineElements.TryGetValue(receiverDiagnostics.PipelineElement.Id, out PipelineElementDiagnostics element))
                {
                    PipelineElement = element;
                }
            });
            if (receiverDiagnostics.Source != null)
            {
                builder.EnqueueThunk(() =>
                {
                    if (receiverDiagnostics.Source != null && builder.Emitters.TryGetValue(receiverDiagnostics.Source.Id, out EmitterDiagnostics source))
                    {
                        Source = source;
                    }
                });
            }
        }

        /// <summary>
        /// Gets the name of all the statistics.
        /// </summary>
        public static string[] AllStatistics => new string[]
        {
            nameof(AvgMessageEmittedLatency),
            nameof(AvgMessageCreatedLatency),
            nameof(AvgMessageProcessTime),
            nameof(AvgMessageReceivedLatency),
            nameof(AvgMessageSize),
            nameof(AvgDeliveryQueueSize),
            nameof(LastMessageEmittedLatency),
            nameof(LastMessageCreatedLatency),
            nameof(LastMessageProcessTime),
            nameof(LastMessageReceivedLatency),
            nameof(LastMessageSize),
            nameof(LastDeliveryQueueSize),
            nameof(ReceiverIsThrottled),
            nameof(TotalMessageDroppedCount),
            nameof(TotalMessageDroppedPercentage),
            nameof(TotalMessageEmittedCount),
            nameof(TotalMessageProcessedCount),
            nameof(TotalMessageProcessedPercentage),
            nameof(WindowMessageDroppedCount),
            nameof(WindowMessageDroppedPercentage),
            nameof(WindowMessageEmittedCount),
            nameof(WindowMessageProcessedCount),
            nameof(WindowMessageProcessedPercentage),
        };

        /// <summary>
        /// Gets receiver ID.
        /// </summary>
        public int Id { get; }

        /// <summary>
        /// Gets receiver name.
        /// </summary>
        public string ReceiverName { get; }

        /// <summary>
        /// Gets name of delivery policy used by receiver.
        /// </summary>
        public string DeliveryPolicyName { get; }

        /// <summary>
        /// Gets receiver type name.
        /// </summary>
        public string TypeName { get; }

        /// <summary>
        /// Gets a value indicating whether receiver is throttled.
        /// </summary>
        public bool ReceiverIsThrottled { get; }

        /// <summary>
        /// Gets delivery queue size at last message.
        /// </summary>
        public double LastDeliveryQueueSize { get; }

        /// <summary>
        /// Gets average delivery queue size.
        /// </summary>
        public double AvgDeliveryQueueSize { get; }

        /// <summary>
        /// Gets total count of emitted messages.
        /// </summary>
        public int TotalMessageEmittedCount { get; }

        /// <summary>
        /// Gets count of emitted messages in last averaging time window.
        /// </summary>
        public int WindowMessageEmittedCount { get; }

        /// <summary>
        /// Gets total count of processed messages.
        /// </summary>
        public int TotalMessageProcessedCount { get; }

        /// <summary>
        /// Gets the total message processed percentage.
        /// </summary>
        public double TotalMessageProcessedPercentage => TotalMessageEmittedCount != 0 ? 100d * TotalMessageProcessedCount / TotalMessageEmittedCount : double.NaN;

        /// <summary>
        /// Gets count of processed messages in last averaging time window.
        /// </summary>
        public int WindowMessageProcessedCount { get; }

        /// <summary>
        /// Gets the message processed percentage in last averaging time window.
        /// </summary>
        public double WindowMessageProcessedPercentage => WindowMessageEmittedCount != 0 ? 100d * WindowMessageProcessedCount / WindowMessageEmittedCount : double.NaN;

        /// <summary>
        /// Gets total count of dropped messages.
        /// </summary>
        public int TotalMessageDroppedCount { get; }

        /// <summary>
        /// Gets the total message dropped percentage.
        /// </summary>
        public double TotalMessageDroppedPercentage => TotalMessageEmittedCount != 0 ? 100d * TotalMessageDroppedCount / TotalMessageEmittedCount : double.NaN;

        /// <summary>
        /// Gets count of dropped messages in last averaging time window.
        /// </summary>
        public int WindowMessageDroppedCount { get; }

        /// <summary>
        /// Gets the message dropped percentage in last averaging time window.
        /// </summary>
        public double WindowMessageDroppedPercentage => WindowMessageEmittedCount != 0 ? 100d * WindowMessageDroppedCount / WindowMessageEmittedCount : double.NaN;

        /// <summary>
        /// Gets latency with which the last message was created.
        /// </summary>
        public double LastMessageCreatedLatency { get; }

        /// <summary>
        /// Gets average message created latency in last averaging time window.
        /// </summary>
        public double AvgMessageCreatedLatency { get; }

        /// <summary>
        /// Gets latency with which the last message was emitted.
        /// </summary>
        public double LastMessageEmittedLatency { get; }

        /// <summary>
        /// Gets average message emitted latency in last averaging time window.
        /// </summary>
        public double AvgMessageEmittedLatency { get; }

        /// <summary>
        /// Gets latency with which the last message was received.
        /// </summary>
        public double LastMessageReceivedLatency { get; }

        /// <summary>
        /// Gets average message received latency in last averaging time window.
        /// </summary>
        public double AvgMessageReceivedLatency { get; }

        /// <summary>
        /// Gets receiver processing time for the last message.
        /// </summary>
        public double LastMessageProcessTime { get; }

        /// <summary>
        /// Gets average receiver processing time in last averaging time window.
        /// </summary>
        public double AvgMessageProcessTime { get; }

        /// <summary>
        /// Gets message size for the last message.
        /// </summary>
        public double LastMessageSize { get; }

        /// <summary>
        /// Gets average message size over in last averaging time window.
        /// </summary>
        public double AvgMessageSize { get; }

        /// <summary>
        /// Gets or sets pipeline element to which emitter belongs.
        /// </summary>
        public PipelineElementDiagnostics PipelineElement { get; set; }

        /// <summary>
        /// Gets or sets receiver's source emitter.
        /// </summary>
        public EmitterDiagnostics Source { get; set; }
    }

    /// <summary>
    /// Builder of graph elements used during construction.
    /// </summary>
    internal class Builder
    {
        private readonly ConcurrentQueue<Action> thunks = new();

        public Builder()
        {
            Pipelines = new ConcurrentDictionary<int, PipelineDiagnostics>();
            PipelineElements = new ConcurrentDictionary<int, PipelineElementDiagnostics>();
            Emitters = new ConcurrentDictionary<int, EmitterDiagnostics>();
            Receivers = new ConcurrentDictionary<int, ReceiverDiagnostics>();
        }

        public ConcurrentDictionary<int, PipelineDiagnostics> Pipelines { get; }

        public ConcurrentDictionary<int, PipelineElementDiagnostics> PipelineElements { get; }

        public ConcurrentDictionary<int, EmitterDiagnostics> Emitters { get; }

        public ConcurrentDictionary<int, ReceiverDiagnostics> Receivers { get; }

        /// <summary>
        /// Enqueue thunk to be executed once all pipelines, elements, emitters and receivers are created.
        /// </summary>
        /// <param name="thunk">Thunk to be executed.</param>
        public void EnqueueThunk(Action thunk)
        {
            thunks.Enqueue(thunk);
        }

        /// <summary>
        /// Execute enqueued thunks.
        /// </summary>
        public void InvokeThunks()
        {
            foreach (Action t in thunks)
            {
                t();
            }
        }

        /// <summary>
        /// Get or create external pipeline diagnostics representation.
        /// </summary>
        /// <param name="pipelineDiagnosticsInternal">Internal pipeline diagnostics representation.</param>
        /// <param name="parentPipelineDiagnostics">Parent pipeline diagnostics.</param>
        /// <param name="includeStoppedPipelines">Whether to include stopped pipelines.</param>
        /// <param name="includeStoppedPipelineElements">Whether to include stopped pipeline element .</param>
        /// <returns>External pipeline diagnostics representation.</returns>
        public PipelineDiagnostics GetOrCreatePipelineDiagnostics(PipelineDiagnosticsInternal pipelineDiagnosticsInternal, PipelineDiagnostics parentPipelineDiagnostics, bool includeStoppedPipelines, bool includeStoppedPipelineElements)
        {
            return GetOrCreate(Pipelines, pipelineDiagnosticsInternal, p => p.Id, (p, c) => new PipelineDiagnostics(p, parentPipelineDiagnostics, c, includeStoppedPipelines, includeStoppedPipelineElements));
        }

        /// <summary>
        /// Get or create external pipeline element diagnostics representation.
        /// </summary>
        /// <param name="pipelineElementDiagnosticsInternal">Internal pipeline element diagnostics representation.</param>
        /// <returns>External pipeline element diagnostics representation.</returns>
        public PipelineElementDiagnostics GetOrCreatePipelineElementDiagnostics(PipelineDiagnosticsInternal.PipelineElementDiagnostics pipelineElementDiagnosticsInternal)
        {
            return GetOrCreate(PipelineElements, pipelineElementDiagnosticsInternal, e => e.Id, (e, c) => new PipelineElementDiagnostics(e, c));
        }

        /// <summary>
        /// Get or create external emitter diagnostics representation.
        /// </summary>
        /// <param name="emitterDiagnosticsInternal">Internal pipeline element diagnostics representation.</param>
        /// <returns>External pipeline element diagnostics representation.</returns>
        public EmitterDiagnostics GetOrCreateEmitterDiagnostics(PipelineDiagnosticsInternal.EmitterDiagnostics emitterDiagnosticsInternal)
        {
            return GetOrCreate(Emitters, emitterDiagnosticsInternal, e => e.Id, (e, c) => new EmitterDiagnostics(e, c));
        }

        /// <summary>
        /// Get or create external receiver diagnostics representation.
        /// </summary>
        /// <param name="receiverDiagnosticsInternal">Internal pipeline element diagnostics representation.</param>
        /// <returns>External pipeline element diagnostics representation.</returns>
        public ReceiverDiagnostics GetOrCreateReceiverDiagnostics(PipelineDiagnosticsInternal.ReceiverDiagnostics receiverDiagnosticsInternal)
        {
            return GetOrCreate(Receivers, receiverDiagnosticsInternal, r => r.Id, (r, c) => new ReceiverDiagnostics(r, c));
        }

        private TExternal GetOrCreate<TExternal, TInternal>(ConcurrentDictionary<int, TExternal> dict, TInternal intern, Func<TInternal, int> getId, Func<TInternal, Builder, TExternal> ctor)
        {
            int id = getId(intern);
            if (!dict.TryGetValue(id, out TExternal ext))
            {
                ext = ctor(intern, this);
                dict.TryAdd(id, ext);
            }

            return ext;
        }
    }
}
