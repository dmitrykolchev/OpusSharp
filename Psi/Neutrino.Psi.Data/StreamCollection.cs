// <copyright file="StreamCollection.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Psi.Data;

/// <summary>
/// Represents a collection of streams.
/// </summary>
public class StreamCollection
{
    private readonly Dictionary<string, (Type Type, IEmitter Emitter)> _emitters = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="StreamCollection"/> class.
    /// </summary>
    public StreamCollection()
    {
    }

    private StreamCollection(Dictionary<string, (Type Type, IEmitter Emitter)> emitters)
    {
        _emitters = emitters;
    }

    /// <summary>
    /// Gets the stream with the specified name from the collection, or null if it does not exist.
    /// </summary>
    /// <typeparam name="T">The type of the stream data.</typeparam>
    /// <param name="name">The stream name.</param>
    /// <returns>The stream with the specified name, or null if it does not exist.</returns>
    public IProducer<T> GetOrDefault<T>(string name)
    {
        return _emitters.ContainsKey(name) ? _emitters[name].Emitter as IProducer<T> : null;
    }

    /// <summary>
    /// Adds a stream to the collection.
    /// </summary>
    /// <typeparam name="T">The type of the stream data.</typeparam>
    /// <param name="stream">The stream to add.</param>
    /// <param name="name">The name of the stream.</param>
    public void Add<T>(IProducer<T> stream, string name)
    {
        _emitters.Add(name, (typeof(T), stream.Out));
    }

    /// <summary>
    /// Writes the streams in the collection to the specified exporter.
    /// </summary>
    /// <param name="exporter">The exporter to write to.</param>
    /// <returns>The stream collection.</returns>
    public StreamCollection Write(Exporter exporter)
    {
        return Write(null, exporter);
    }

    /// <summary>
    /// Writes the streams in the collection to the specified exporter with the specified prefix.
    /// </summary>
    /// <param name="prefix">The prefix to prepend to the stream names.</param>
    /// <param name="exporter">The exporter to write to.</param>
    /// <returns>The stream collection.</returns>
    public StreamCollection Write(string prefix, Exporter exporter)
    {
        prefix = string.IsNullOrEmpty(prefix) ? string.Empty : $"{prefix}.";
        System.Reflection.MethodInfo writeMethod = typeof(Exporter).GetMethods().Where(m => m.Name == nameof(Exporter.Write) && m.GetGenericArguments().Count() == 1).First();
        foreach (string name in _emitters.Keys)
        {
            writeMethod
                .MakeGenericMethod([_emitters[name].Type])
                .Invoke(exporter, new object[] { _emitters[name].Emitter, $"{prefix}{name}", false, null });
        }

        return this;
    }

    /// <summary>
    /// Writes the streams in the collection to another stream collection.
    /// </summary>
    /// <param name="streamCollection">The stream collection to write to.</param>
    /// <returns>The stream collection.</returns>
    public StreamCollection Write(StreamCollection streamCollection)
    {
        return Write(null, streamCollection);
    }

    /// <summary>
    /// Writes the streams in the collection to another stream collection with the specified prefix.
    /// </summary>
    /// <param name="prefix">The prefix to prepend to the stream names.</param>
    /// <param name="streamCollection">The stream collection to write to.</param>
    /// <returns>The stream collection.</returns>
    public StreamCollection Write(string prefix, StreamCollection streamCollection)
    {
        prefix = string.IsNullOrEmpty(prefix) ? string.Empty : $"{prefix}.";
        foreach (string name in _emitters.Keys)
        {
            streamCollection._emitters.Add($"{prefix}{name}", _emitters[name]);
        }

        return this;
    }

    /// <summary>
    /// Bridges the streams in the collection to another pipeline.
    /// </summary>
    /// <param name="pipeline">The target pipeline.</param>
    /// <returns>The bridged stream collection.</returns>
    public StreamCollection BridgeTo(Pipeline pipeline)
    {
        Dictionary<string, (Type Type, IEmitter Emitter)> bridgedEmitters = new();
        System.Reflection.MethodInfo bridgeToMethod = typeof(Psi.Operators).GetMethod(nameof(Operators.BridgeTo));
        foreach (string name in _emitters.Keys)
        {
            // Construct the bridged emitter via reflection
            IEmitter bridgedEmitter = (bridgeToMethod
                .MakeGenericMethod([_emitters[name].Type])
                .Invoke(null, new object[] { _emitters[name].Emitter, pipeline, null, null }) as dynamic).Out as IEmitter;

            bridgedEmitters.Add(name, (_emitters[name].Type, bridgedEmitter));
        }

        return new(bridgedEmitters);
    }
}
