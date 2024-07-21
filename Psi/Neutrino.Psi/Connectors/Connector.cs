// <copyright file="Connector.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

namespace Neutrino.Psi.Components;

/// <summary>
/// A pass-through component, that can relay messages from one pipeline to another and can be used when
/// writing composite components via subpipelines. The composite component can create input and output
/// connectors instead of receivers.
/// </summary>
/// <typeparam name="T">The message type.</typeparam>
public sealed class Connector<T> : IProducer<T>, IConsumer<T>, IConnector
{
    private readonly string _name;

    /// <summary>
    /// Initializes a new instance of the <see cref="Connector{T}"/> class.
    /// </summary>
    /// <param name="from">The source pipeline.</param>
    /// <param name="to">The target pipeline.</param>
    /// <param name="name">An optional name for the connector.</param>
    /// <param name="preserveEnvelope">An optional parameter that specifies whether or not the source message envelopes should be preserved.</param>
    public Connector(Pipeline from, Pipeline to, string name = null, bool preserveEnvelope = false)
    {
        _name = name ?? $"{from.Name}→{to.Name}";
        Out = to.CreateEmitter<T>(this, _name);
        In = preserveEnvelope ?
            from.CreateReceiver<T>(this, (m, e) => Out.Deliver(m, e), name) :
            from.CreateReceiver<T>(this, (m, e) => Out.Post(m, e.OriginatingTime), name);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Connector{T}"/> class.
    /// </summary>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="name">An optional name for the connector.</param>
    /// <param name="preserveEnvelope">An optional parameter that specifies whether or not the source message envelopes should be preserved.</param>
    public Connector(Pipeline pipeline, string name = null, bool preserveEnvelope = false)
        : this(pipeline, pipeline, name ?? $"Connector-{pipeline.Name}", preserveEnvelope)
    {
    }

    /// <summary>
    /// Gets the connector input.
    /// </summary>
    public Receiver<T> In { get; }

    /// <summary>
    /// Gets the connector output.
    /// </summary>
    public Emitter<T> Out { get; }

    /// <inheritdoc />
    public override string ToString()
    {
        return _name;
    }
}
