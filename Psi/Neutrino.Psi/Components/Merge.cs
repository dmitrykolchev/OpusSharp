// <copyright file="Merge.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

namespace Neutrino.Psi.Components;

/// <summary>
/// Merge one or more streams (T) into a single stream (Message{T}) interleaved in wall-clock time.
/// </summary>
/// <remarks>Messages are produced in the order they arrive, in wall-clock time; not necessarily in originating-time order.</remarks>
/// <typeparam name="T">The type of the messages.</typeparam>
public class Merge<T> : IProducer<Message<T>>
{
    private readonly Pipeline _pipeline;
    private readonly string _name;

    /// <summary>
    /// Initializes a new instance of the <see cref="Merge{T}"/> class.
    /// </summary>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="name">An optional name for this component.</param>
    public Merge(Pipeline pipeline, string name = nameof(Merge<T>))
    {
        _pipeline = pipeline;
        _name = name;
        Out = pipeline.CreateEmitter<Message<T>>(this, nameof(Out));
    }

    /// <summary>
    /// Gets the output emitter.
    /// </summary>
    public Emitter<Message<T>> Out { get; }

    /// <summary>
    /// Add input receiver.
    /// </summary>
    /// <param name="name">The unique debug name of the receiver.</param>
    /// <returns>Receiver.</returns>
    public Receiver<T> AddInput(string name)
    {
        return _pipeline.CreateReceiver<T>(this, Receive, name);
    }

    /// <inheritdoc/>
    public override string ToString() => _name;

    private void Receive(T message, Envelope e)
    {
        Out.Post(Message.Create(message, e), _pipeline.GetCurrentTime());
    }
}
