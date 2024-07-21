// <copyright file="Merger.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Collections.Generic;


namespace Neutrino.Psi.Components;

/// <summary>
/// Combines the input messages from multiple inputs; invoking given lambda for each.
/// </summary>
/// <typeparam name="TIn">The message type.</typeparam>
/// <typeparam name="TKey">The key type to use to identify the inputs.</typeparam>
public class Merger<TIn, TKey>
{
    private readonly Dictionary<TKey, Receiver<TIn>> _inputs = [];
    private readonly Pipeline _pipeline;
    private readonly string _name;
    private readonly Action<TKey, Message<TIn>> _action;
    private readonly object _syncRoot = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="Merger{TIn, TKey}"/> class.
    /// </summary>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="action">Action invoked for each key/message.</param>
    /// <param name="name">An optional name for the component.</param>
    public Merger(Pipeline pipeline, Action<TKey, Message<TIn>> action, string name = nameof(Merger<TIn, TKey>))
    {
        _pipeline = pipeline;
        _name = name;
        _action = action;
    }

    /// <summary>
    /// Add a key to which a receiver will be mapped.
    /// </summary>
    /// <param name="key">Key to which to map a receiver.</param>
    /// <returns>Receiver having been mapped.</returns>
    public Receiver<TIn> Add(TKey key)
    {
        // lock access to the inputs so Merger works concurrently
        lock (_syncRoot)
        {
            if (_inputs.ContainsKey(key))
            {
                throw new InvalidOperationException($"An input for this key {key} has already been added.");
            }

            return _inputs[key] = _pipeline.CreateReceiver<TIn>(this, m => _action(key, m), key.ToString());
        }
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return _name;
    }
}
