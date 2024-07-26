// <copyright file="ParallelSparseSplitter.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Collections.Generic;
using System.Linq;
using Neutrino.Psi.Common;
using Neutrino.Psi.Connectors;
using Neutrino.Psi.Executive;
using Neutrino.Psi.Streams;

namespace Neutrino.Psi.Components;

/// <summary>
/// Implements the splitter for the <see cref="ParallelSparseDo{TIn, TBranchKey, TBranchIn}"/>
/// and <see cref="ParallelSparseSelect{TIn, TBranchKey, TBranchIn, TBranchOut, TOut}"/> components.
/// </summary>
/// <typeparam name="TIn">The input message type.</typeparam>
/// <typeparam name="TBranchKey">The key type.</typeparam>
/// <typeparam name="TBranchIn">The branch input message type.</typeparam>
/// <typeparam name="TBranchOut">The branch output message type.</typeparam>
public class ParallelSparseSplitter<TIn, TBranchKey, TBranchIn, TBranchOut> : IConsumer<TIn>
{
    private readonly Pipeline _pipeline;
    private readonly string _name;
    private readonly Dictionary<TBranchKey, Emitter<TBranchIn>> _branches = [];
    private readonly Dictionary<TBranchKey, int> _keyToBranchMapping = [];
    private readonly Func<TIn, Dictionary<TBranchKey, TBranchIn>> _splitterFunction;
    private readonly Func<TBranchKey, IProducer<TBranchIn>, IProducer<TBranchOut>> _parallelTransform;
    private readonly Action<TBranchKey, IProducer<TBranchIn>> _parallelAction;
    private readonly Func<TBranchKey, Dictionary<TBranchKey, TBranchIn>, DateTime, (bool, DateTime)> _branchTerminationPolicy;
    private readonly Action<IProducer<TBranchOut>> _connectToJoin;
    private readonly Dictionary<TBranchKey, int> _activeBranches;
    private int _branchKey;

    /// <summary>
    /// Initializes a new instance of the <see cref="ParallelSparseSplitter{TIn, TBranchKey, TBranchIn, TBranchOut}"/> class.
    /// </summary>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="splitter">A function that splits the input by generating a dictionary of key-value pairs for each given input message.</param>
    /// <param name="transform">Function mapping keyed input producers to output producers.</param>
    /// <param name="branchTerminationPolicy">Predicate function determining whether and when (originating time) to terminate branches (defaults to when key no longer present), given the current key.</param>
    /// <param name="connectToJoin">Action that connects the results of a parallel branch back to join.</param>
    /// <param name="name">An optional name for the component.</param>
    public ParallelSparseSplitter(
        Pipeline pipeline,
        Func<TIn, Dictionary<TBranchKey, TBranchIn>> splitter,
        Func<TBranchKey, IProducer<TBranchIn>, IProducer<TBranchOut>> transform,
        Func<TBranchKey, Dictionary<TBranchKey, TBranchIn>, DateTime, (bool, DateTime)> branchTerminationPolicy,
        Action<IProducer<TBranchOut>> connectToJoin,
        string name = nameof(ParallelSparseSplitter<TIn, TBranchKey, TBranchIn, TBranchOut>))
    {
        _pipeline = pipeline;
        _name = name;
        _splitterFunction = splitter;
        _parallelTransform = transform;
        _branchTerminationPolicy = branchTerminationPolicy ?? BranchTerminationPolicy<TBranchKey, TBranchIn>.WhenKeyNotPresent();
        _connectToJoin = connectToJoin;
        _activeBranches = new Dictionary<TBranchKey, int>();
        In = pipeline.CreateReceiver<TIn>(this, Receive, nameof(In));
        ActiveBranches = pipeline.CreateEmitter<Dictionary<TBranchKey, int>>(this, nameof(ActiveBranches));
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ParallelSparseSplitter{TIn, TBranchKey, TBranchIn, TBranchOut}"/> class.
    /// </summary>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="splitter">A function that generates a dictionary of key-value pairs for each given input message.</param>
    /// <param name="action">Action to perform in parallel.</param>
    /// <param name="branchTerminationPolicy">Predicate function determining whether and when (originating time) to terminate branches (defaults to when key no longer present), given the current key.</param>
    public ParallelSparseSplitter(
        Pipeline pipeline,
        Func<TIn, Dictionary<TBranchKey, TBranchIn>> splitter,
        Action<TBranchKey, IProducer<TBranchIn>> action,
        Func<TBranchKey, Dictionary<TBranchKey, TBranchIn>, DateTime, (bool, DateTime)> branchTerminationPolicy)
    {
        _pipeline = pipeline;
        _splitterFunction = splitter;
        _parallelAction = action;
        _branchTerminationPolicy = branchTerminationPolicy ?? BranchTerminationPolicy<TBranchKey, TBranchIn>.WhenKeyNotPresent();
        In = pipeline.CreateReceiver<TIn>(this, Receive, nameof(In));
    }

    /// <inheritdoc/>
    public Receiver<TIn> In { get; }

    /// <summary>
    /// Gets the active branches emitter.
    /// </summary>
    public Emitter<Dictionary<TBranchKey, int>> ActiveBranches { get; }

    /// <inheritdoc/>
    public override string ToString() => _name;

    private void Receive(TIn input, Envelope e)
    {
        Dictionary<TBranchKey, TBranchIn> keyedValues = _splitterFunction(input);
        foreach (KeyValuePair<TBranchKey, TBranchIn> pair in keyedValues)
        {
            if (!_branches.ContainsKey(pair.Key))
            {
                _keyToBranchMapping[pair.Key] = _branchKey++;
                Subpipeline subpipeline = Subpipeline.Create(_pipeline, $"subpipeline{pair.Key}");
                Connector<TBranchIn> connectorIn = subpipeline.CreateInputConnectorFrom<TBranchIn>(_pipeline, $"connectorIn{pair.Key}");
                Emitter<TBranchIn> branch = _pipeline.CreateEmitter<TBranchIn>(this, $"branch{pair.Key}-{Guid.NewGuid()}");
                _branches[pair.Key] = branch;
                branch.PipeTo(connectorIn, true); // allows connections in running pipelines
                connectorIn.In.Unsubscribed += time => subpipeline.Stop(time);
                if (_parallelTransform != null)
                {
                    IProducer<TBranchOut> branchResult = _parallelTransform(pair.Key, connectorIn.Out);
                    Connector<TBranchOut> connectorOut = subpipeline.CreateOutputConnectorTo<TBranchOut>(_pipeline, $"connectorOut{pair.Key}");
                    branchResult.PipeTo(connectorOut.In, true);
                    connectorOut.In.Unsubscribed += closeOriginatingTime => connectorOut.Out.Close(closeOriginatingTime);
                    _connectToJoin.Invoke(connectorOut.Out);
                }
                else
                {
                    _parallelAction(pair.Key, connectorIn.Out);
                }

                // run the subpipeline with a start time based on the message originating time
                subpipeline.RunAsync(
                    e.OriginatingTime,
                    _pipeline.ReplayDescriptor.End,
                    _pipeline.ReplayDescriptor.EnforceReplayClock);
            }

            _branches[pair.Key].Post(pair.Value, e.OriginatingTime);
        }

        foreach (KeyValuePair<TBranchKey, Emitter<TBranchIn>> branch in _branches.ToArray())
        {
            (bool terminate, DateTime originatingTime) = _branchTerminationPolicy(branch.Key, keyedValues, e.OriginatingTime);
            if (terminate)
            {
                branch.Value.Close(originatingTime);
                _branches.Remove(branch.Key);
            }
        }

        if (ActiveBranches != null)
        {
            _activeBranches.Clear();
            foreach (TBranchKey key in _branches.Keys)
            {
                _activeBranches.Add(key, _keyToBranchMapping[key]);
            }

            ActiveBranches.Post(_activeBranches, e.OriginatingTime);
        }
    }
}
