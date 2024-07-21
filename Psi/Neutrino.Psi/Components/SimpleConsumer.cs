// <copyright file="SimpleConsumer.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

namespace Microsoft.Psi.Components;

/// <summary>
/// A simple consumer.
/// </summary>
/// <typeparam name="TIn">The input message type.</typeparam>
public abstract class SimpleConsumer<TIn> : IConsumer<TIn>
{
    private readonly string _name;

    /// <summary>
    /// Initializes a new instance of the <see cref="SimpleConsumer{TIn}"/> class.
    /// </summary>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="name">An optional name for this component.</param>
    public SimpleConsumer(Pipeline pipeline, string name = nameof(SimpleConsumer<TIn>))
    {
        _name = name;
        In = pipeline.CreateReceiver<TIn>(this, Receive, nameof(In));
    }

    /// <inheritdoc />
    public Receiver<TIn> In { get; }

    /// <summary>
    /// Message receiver.
    /// </summary>
    /// <param name="message">Message received.</param>
    public abstract void Receive(Message<TIn> message);

    /// <inheritdoc/>
    public override string ToString()
    {
        return _name;
    }
}
