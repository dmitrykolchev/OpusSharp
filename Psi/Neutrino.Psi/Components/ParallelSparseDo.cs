// <copyright file="ParallelSparseDo.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Collections.Generic;
using Neutrino.Psi.Common;
using Neutrino.Psi.Connectors;
using Neutrino.Psi.Executive;
using Neutrino.Psi.Streams;

namespace Neutrino.Psi.Components;


/// <summary>
/// Creates and executes parallel subpipelines based on an input stream and a splitter function.
/// </summary>
/// <typeparam name="TIn">The input message type.</typeparam>
/// <typeparam name="TBranchKey">The branch key type.</typeparam>
/// <typeparam name="TBranchIn">The branch input message type.</typeparam>
/// <remarks>A splitter function is applied to each input message to generate a dictionary, and
/// a subpipeline is created and executed for every key in the dictionary. A branch termination
/// policy function governs when branches are terminated.</remarks>
public class ParallelSparseDo<TIn, TBranchKey, TBranchIn> : Subpipeline, IConsumer<TIn>
{
    private readonly Connector<TIn> _inConnector;

    /// <summary>
    /// Initializes a new instance of the <see cref="ParallelSparseDo{TIn, TBranchKey, TBranchIn}"/> class.
    /// </summary>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="splitter">A function that generates a dictionary of key-value pairs for each given input message.</param>
    /// <param name="action">Action to perform in parallel.</param>
    /// <param name="branchTerminationPolicy">Predicate function determining whether and when (originating time) to terminate branches (defaults to when key no longer present), given the current key, message payload (dictionary) and originating time.</param>
    /// <param name="name">An optional name for the component.</param>
    /// <param name="defaultDeliveryPolicy">Pipeline-level default delivery policy to be used by this component (defaults to <see cref="DeliveryPolicy.Unlimited"/> if unspecified).</param>
    public ParallelSparseDo(
        Pipeline pipeline,
        Func<TIn, Dictionary<TBranchKey, TBranchIn>> splitter,
        Action<TBranchKey, IProducer<TBranchIn>> action,
        Func<TBranchKey, Dictionary<TBranchKey, TBranchIn>, DateTime, (bool, DateTime)> branchTerminationPolicy = null,
        string name = nameof(ParallelSparseDo<TIn, TBranchKey, TBranchIn>),
        DeliveryPolicy defaultDeliveryPolicy = null)
        : base(pipeline, name, defaultDeliveryPolicy)
    {
        _inConnector = CreateInputConnectorFrom<TIn>(pipeline, nameof(_inConnector));
        ParallelSparseSplitter<TIn, TBranchKey, TBranchIn, TBranchIn> parallelSparseSplitter 
            = new(this, splitter, action, branchTerminationPolicy);
        _inConnector.PipeTo(parallelSparseSplitter);
    }

    /// <inheritdoc />
    public Receiver<TIn> In => _inConnector.In;
}
