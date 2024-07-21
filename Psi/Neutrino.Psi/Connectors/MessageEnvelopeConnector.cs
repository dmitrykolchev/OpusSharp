// <copyright file="MessageEnvelopeConnector.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using Microsoft.Psi.Data;

namespace Microsoft.Psi.Components;


/// <summary>
/// A pass-through component that connects two different pipelines, and sends the envelope of the message
/// with a 0 (int) payload from the source pipeline to the target pipeline. This connector is internal
/// for now, and used by the <see cref="Exporter"/> component, to write stream envelopes to store.
/// </summary>
/// <typeparam name="T">The message type.</typeparam>
internal sealed class MessageEnvelopeConnector<T> : IConsumer<T>, IProducer<Message<int>>, IConnector
{
    private readonly string _name;

    /// <summary>
    /// Initializes a new instance of the <see cref="MessageEnvelopeConnector{T}"/> class.
    /// </summary>
    /// <param name="from">The source pipeline to bridge from.</param>
    /// <param name="to">The target pipeline to bridge to.</param>
    /// <param name="name">The name of the connector.</param>
    /// <remarks>The `MessageEnvelope` to bridge `from` a source pipeline into a `target` pipeline, while carrying all
    /// the envelope information from the source into the target pipeline.</remarks>
    internal MessageEnvelopeConnector(Pipeline from, Pipeline to, string name = null)
    {
        _name = name ?? $"{from.Name}→{to.Name}";
        In = from.CreateReceiver<T>(this, (m, e) => Out.Post(Message.Create(0, e), e.OriginatingTime), name);
        Out = to.CreateEmitter<Message<int>>(this, _name);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MessageEnvelopeConnector{T}"/> class.
    /// </summary>
    /// <param name="pipeline">The pipeline to add the connector to.</param>
    /// <param name="name">The name of the connector.</param>
    internal MessageEnvelopeConnector(Pipeline pipeline, string name = null)
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
    public Emitter<Message<int>> Out { get; }

    /// <inheritdoc />
    public override string ToString()
    {
        return _name;
    }
}
