// <copyright file="ParallelVariableLength.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Collections.Generic;
using System.Linq;

namespace Neutrino.Psi.Components;

/// <summary>
/// Creates and applies a sub-pipeline to each element in the input array. The input array can have variable length.
/// The sub-pipelines have index affinity, meaning the same sub-pipeline is re-used across multiple messages for the entry with the same index in the array.
/// </summary>
/// <typeparam name="TIn">The input message type.</typeparam>
/// <typeparam name="TOut">The result type.</typeparam>
public class ParallelVariableLength<TIn, TOut> : Subpipeline, IConsumer<TIn[]>, IProducer<TOut[]>
{
    private readonly Connector<TIn[]> _inConnector;
    private readonly Connector<TOut[]> _outConnector;
    private readonly Receiver<TIn[]> _splitter;
    private readonly List<Emitter<TIn>> _branches = new();
    private readonly Join<int, TOut, TOut[]> _join;
    private readonly Emitter<int> _activeBranchesEmitter;
    private readonly Func<int, IProducer<TIn>, IProducer<TOut>> _parallelTransform;
    private readonly Action<int, IProducer<TIn>> _parallelAction;

    /// <summary>
    /// Initializes a new instance of the <see cref="ParallelVariableLength{TIn, TOut}"/> class.
    /// </summary>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="action">Function mapping keyed input producers to output producers.</param>
    /// <param name="name">An optional name for the component.</param>
    /// <param name="defaultDeliveryPolicy">Pipeline-level default delivery policy to be used by this component (defaults to <see cref="DeliveryPolicy.Unlimited"/> if unspecified).</param>
    public ParallelVariableLength(Pipeline pipeline, Action<int, IProducer<TIn>> action, string name = nameof(ParallelVariableLength<TIn, TOut>), DeliveryPolicy defaultDeliveryPolicy = null)
        : base(pipeline, name, defaultDeliveryPolicy)
    {
        _parallelAction = action;
        _inConnector = CreateInputConnectorFrom<TIn[]>(pipeline, nameof(_inConnector));
        _splitter = CreateReceiver<TIn[]>(this, Receive, nameof(_splitter));
        _inConnector.PipeTo(_splitter);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ParallelVariableLength{TIn, TOut}"/> class.
    /// </summary>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="transform">Function mapping keyed input producers to output producers.</param>
    /// <param name="outputDefaultIfDropped">When true, a result is produced even if a message is dropped in processing one of the input elements. In this case the corresponding output element is set to a default value.</param>
    /// <param name="defaultValue">Default value to use when messages are dropped in processing one of the input elements.</param>
    /// <param name="name">An optional name for the component.</param>
    /// <param name="defaultDeliveryPolicy">Pipeline-level default delivery policy to be used by this component (defaults to <see cref="DeliveryPolicy.Unlimited"/> if unspecified).</param>
    public ParallelVariableLength(Pipeline pipeline, Func<int, IProducer<TIn>, IProducer<TOut>> transform, bool outputDefaultIfDropped = false, TOut defaultValue = default, string name = nameof(ParallelVariableLength<TIn, TOut>), DeliveryPolicy defaultDeliveryPolicy = null)
        : base(pipeline, name, defaultDeliveryPolicy)
    {
        _parallelTransform = transform;
        _inConnector = CreateInputConnectorFrom<TIn[]>(pipeline, nameof(_inConnector));
        _splitter = CreateReceiver<TIn[]>(this, Receive, nameof(_splitter));
        _inConnector.PipeTo(_splitter);
        _activeBranchesEmitter = CreateEmitter<int>(this, nameof(_activeBranchesEmitter));
        ReproducibleInterpolator<TOut> interpolator = outputDefaultIfDropped ? Reproducible.ExactOrDefault<TOut>(defaultValue) : Reproducible.Exact<TOut>();

        _join = new Join<int, TOut, TOut[]>(
            this,
            interpolator,
            (count, values) => values,
            0,
            count => Enumerable.Range(0, count));

        _activeBranchesEmitter.PipeTo(_join.InPrimary);
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
        for (int i = 0; i < message.Length; i++)
        {
            if (_branches.Count == i)
            {
                Subpipeline subpipeline = Subpipeline.Create(this, $"subpipeline{i}");
                Emitter<TIn> branch = CreateEmitter<TIn>(this, $"branch{i}");
                Connector<TIn> connectorIn = new(this, subpipeline, $"connectorIn{i}");
                branch.PipeTo(connectorIn, true); // allows connections in running pipelines

                _branches.Add(branch);

                if (_parallelTransform != null)
                {
                    IProducer<TOut> branchResult = _parallelTransform(i, connectorIn.Out);
                    Connector<TOut> connectorOut = new(subpipeline, this, $"connectorOut{i}");
                    branchResult.PipeTo(connectorOut, true);
                    connectorOut.Out.PipeTo(_join.AddInput(), true);
                }
                else
                {
                    _parallelAction(i, connectorIn.Out);
                }

                // run the subpipeline with a start time based on the message originating time
                subpipeline.RunAsync(
                    e.OriginatingTime,
                    ReplayDescriptor.End,
                    ReplayDescriptor.EnforceReplayClock);
            }

            _branches[i].Post(message[i], e.OriginatingTime);
        }

        _activeBranchesEmitter?.Post(message.Length, e.OriginatingTime);
    }
}
