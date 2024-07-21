// <copyright file="MessageConnector.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using Neutrino.Psi.Data;

namespace Neutrino.Psi.Components;


/// <summary>
/// A pass-through component that connects two different pipelines, and sends the entire message (including
/// envelope) from the source pipeline to the target pipeline. This connector is internal for now, and
/// used by the <see cref="Exporter"/> component, which requires the entire envelope.
/// </summary>
/// <typeparam name="T">The message type.</typeparam>
internal sealed class MessageConnector<T> : IConsumer<T>, IProducer<Message<T>>, IConnector
{
    private readonly string _name;

    /// <summary>
    /// Initializes a new instance of the <see cref="MessageConnector{T}"/> class.
    /// </summary>
    /// <param name="from">The source pipeline to bridge from.</param>
    /// <param name="to">The target pipeline to bridge to.</param>
    /// <param name="name">The name of the connector.</param>
    /// <remarks>The `MessageConnector` to bridge `from` a source pipeline into a `target` pipeline, while carrying all
    /// the envelope information from the source into the target pipeline.</remarks>
    internal MessageConnector(Pipeline from, Pipeline to, string name = null)
    {
        _name = name ?? $"{from.Name}→{to.Name}";
        In = from.CreateReceiver<T>(this, (m, e) => Out.Post(Message.Create(m, e), e.OriginatingTime), name);
        Out = to.CreateEmitter<Message<T>>(this, _name);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MessageConnector{T}"/> class.
    /// </summary>
    /// <param name="pipeline">The pipeline to add the connector to.</param>
    /// <param name="name">The name of the connector.</param>
    internal MessageConnector(Pipeline pipeline, string name = null)
        : this(pipeline, pipeline, name ?? $"Connector-{pipeline.Name}")
    {
    }

    /// <summary>
    /// Gets the connector input.
    /// </summary>
    public Receiver<T> In { get; }

    /// <summary>
    /// Gets the connector output.
    /// </summary>
    public Emitter<Message<T>> Out { get; }

    /// <inheritdoc />
    public override string ToString()
    {
        return _name;
    }
}
