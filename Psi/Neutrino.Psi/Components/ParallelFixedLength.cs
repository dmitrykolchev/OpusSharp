// <copyright file="ParallelFixedLength.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;

namespace Microsoft.Psi.Components;


/// <summary>
/// Creates and applies a sub-pipeline to each element in the input array. The input array must have the same length across all messages.
/// The sub-pipelines have index affinity, meaning the same sub-pipeline is re-used across multiple messages for the entry with the same index.
/// </summary>
/// <typeparam name="TIn">The input message type.</typeparam>
/// <typeparam name="TOut">The result type.</typeparam>
public class ParallelFixedLength<TIn, TOut> : Subpipeline, IConsumer<TIn[]>, IProducer<TOut[]>
{
    private readonly Connector<TIn[]> _inConnector;
    private readonly Connector<TOut[]> _outConnector;
    private readonly Receiver<TIn[]> _splitter;
    private readonly Emitter<TIn>[] _branches;
    private readonly IProducer<TOut[]> _join;

    /// <summary>
    /// Initializes a new instance of the <see cref="ParallelFixedLength{TIn, TOut}"/> class.
    /// </summary>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="vectorSize">Vector size.</param>
    /// <param name="action">Action to apply to output producers.</param>
    /// <param name="name">Name for this component (defaults to ParallelFixedLength).</param>
    /// <param name="defaultDeliveryPolicy">Pipeline-level default delivery policy to be used by this component (defaults to <see cref="DeliveryPolicy.Unlimited"/> if unspecified).</param>
    public ParallelFixedLength(Pipeline pipeline, int vectorSize, Action<int, IProducer<TIn>> action, string name = null, DeliveryPolicy defaultDeliveryPolicy = null)
        : base(pipeline, name ?? nameof(ParallelFixedLength<TIn, TOut>), defaultDeliveryPolicy)
    {
        _inConnector = CreateInputConnectorFrom<TIn[]>(pipeline, nameof(_inConnector));
        _splitter = CreateReceiver<TIn[]>(this, Receive, nameof(_splitter));
        _inConnector.PipeTo(_splitter);
        _branches = new Emitter<TIn>[vectorSize];
        for (int i = 0; i < vectorSize; i++)
        {
            Subpipeline subpipeline = Subpipeline.Create(pipeline, $"subpipeline{i}");
            Connector<TIn> connector = new(pipeline, subpipeline, $"connector{i}");
            _branches[i] = pipeline.CreateEmitter<TIn>(this, $"branch{i}");
            _branches[i].PipeTo(connector);
            action(i, connector.Out);
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ParallelFixedLength{TIn, TOut}"/> class.
    /// </summary>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="vectorSize">Vector size.</param>
    /// <param name="transform">Function mapping keyed input producers to output producers.</param>
    /// <param name="outputDefaultIfDropped">When true, a result is produced even if a message is dropped in processing one of the input elements. In this case the corresponding output element is set to a default value.</param>
    /// <param name="defaultValue">Default value to use when messages are dropped in processing one of the input elements.</param>
    /// <param name="name">Name for this component (defaults to ParallelFixedLength).</param>
    /// <param name="defaultDeliveryPolicy">Pipeline-level default delivery policy to be used by this component (defaults to <see cref="DeliveryPolicy.Unlimited"/> if unspecified).</param>
    public ParallelFixedLength(Pipeline pipeline, int vectorSize, Func<int, IProducer<TIn>, IProducer<TOut>> transform, bool outputDefaultIfDropped, TOut defaultValue = default, string name = null, DeliveryPolicy defaultDeliveryPolicy = null)
        : base(pipeline, name ?? nameof(ParallelFixedLength<TIn, TOut>), defaultDeliveryPolicy)
    {
        _inConnector = CreateInputConnectorFrom<TIn[]>(pipeline, nameof(_inConnector));
        _splitter = CreateReceiver<TIn[]>(this, Receive, nameof(_splitter));
        _inConnector.PipeTo(_splitter);
        _branches = new Emitter<TIn>[vectorSize];
        IProducer<TOut>[] branchResults = new IProducer<TOut>[vectorSize];
        for (int i = 0; i < vectorSize; i++)
        {
            Subpipeline subpipeline = Subpipeline.Create(this, $"subpipeline{i}");
            Connector<TIn> connectorIn = new(this, subpipeline, $"connectorIn{i}");
            Connector<TOut> connectorOut = new(subpipeline, this, $"connectorOut{i}");
            _branches[i] = CreateEmitter<TIn>(this, $"branch{i}");
            _branches[i].PipeTo(connectorIn);
            transform(i, connectorIn.Out).PipeTo(connectorOut.In);
            branchResults[i] = connectorOut;
        }

        ReproducibleInterpolator<TOut> interpolator = outputDefaultIfDropped ? Reproducible.ExactOrDefault<TOut>(defaultValue) : Reproducible.Exact<TOut>();
        _join = Operators.Join(branchResults, interpolator);
        _outConnector = CreateOutputConnectorTo<TOut[]>(pipeline, nameof(_outConnector));
        _join.PipeTo(_outConnector);
    }

    /// <inheritdoc />
    public Receiver<TIn[]> In => _inConnector.In;

    /// <inheritdoc />
    public Emitter<TOut[]> Out => _outConnector.Out;

    /// <inheritdoc />
    public override void Dispose()
    {
        _splitter.Dispose();
        base.Dispose();
    }

    private void Receive(TIn[] message, Envelope e)
    {
        if (message.Length != _branches.Length)
        {
            throw new InvalidOperationException("The Parallel operator has encountered a stream message that does not match the specified size of the input vector.");
        }

        for (int i = 0; i < message.Length; i++)
        {
            _branches[i].Post(message[i], e.OriginatingTime);
        }
    }
}
