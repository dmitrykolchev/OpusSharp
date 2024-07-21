// <copyright file="Splitter.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Collections.Generic;


namespace Neutrino.Psi.Components;

/// <summary>
/// Sends the input message to at most one of the dynamic outputs, selected using the specified output selector.
/// </summary>
/// <typeparam name="TIn">The input message type.</typeparam>
/// <typeparam name="TKey">The type of key to use when identifying the correct output.</typeparam>
public class Splitter<TIn, TKey> : IConsumer<TIn>
{
    private readonly Dictionary<TKey, Emitter<TIn>> _outputs = new();
    private readonly Func<TIn, Envelope, TKey> _outputSelector;
    private readonly Pipeline _pipeline;
    private readonly string _name;
    private readonly Receiver<TIn> _input;

    /// <summary>
    /// Initializes a new instance of the <see cref="Splitter{TIn, TKey}"/> class.
    /// </summary>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="outputSelector">Selector function identifying the output.</param>
    /// <param name="name">An optional name for the component.</param>
    public Splitter(Pipeline pipeline, Func<TIn, Envelope, TKey> outputSelector, string name = nameof(Splitter<TIn, TKey>))
    {
        _pipeline = pipeline;
        _name = name;
        _outputSelector = outputSelector;
        _input = pipeline.CreateReceiver<TIn>(this, Receive, nameof(In));
    }

    /// <inheritdoc />
    public Receiver<TIn> In => _input;

    /// <summary>
    /// Add emitter mapping.
    /// </summary>
    /// <param name="key">Key to which to map emitter.</param>
    /// <returns>Emitter having been mapped.</returns>
    public Emitter<TIn> Add(TKey key)
    {
        if (_outputs.ContainsKey(key))
        {
            throw new InvalidOperationException($"An output for this key {key} has already been added.");
        }

        return _outputs[key] = _pipeline.CreateEmitter<TIn>(this, key.ToString());
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return _name;
    }

    private void Receive(TIn message, Envelope e)
    {
        TKey key = _outputSelector(message, e);
        if (_outputs.ContainsKey(key))
        {
            _outputs[key].Post(message, e.OriginatingTime);
        }
    }
}
