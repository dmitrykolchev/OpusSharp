// <copyright file="AsyncConsumerProducer.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Threading.Tasks;

namespace Microsoft.Psi.Components;


/// <summary>
/// A simple transform component.
/// </summary>
/// <typeparam name="TIn">The input message type.</typeparam>
/// <typeparam name="TOut">The output message type.</typeparam>
public abstract class AsyncConsumerProducer<TIn, TOut> : IConsumerProducer<TIn, TOut>
{
    private readonly string _name;

    /// <summary>
    /// Initializes a new instance of the <see cref="AsyncConsumerProducer{TIn, TOut}"/> class.
    /// </summary>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="name">An optional name for the component.</param>
    public AsyncConsumerProducer(Pipeline pipeline, string name = nameof(AsyncConsumerProducer<TIn, TOut>))
    {
        _name = name;
        Out = pipeline.CreateEmitter<TOut>(this, nameof(Out));
        In = pipeline.CreateAsyncReceiver<TIn>(this, ReceiveAsync, nameof(In));
    }

    /// <inheritdoc />
    public Receiver<TIn> In { get; }

    /// <inheritdoc />
    public Emitter<TOut> Out { get; }

    /// <inheritdoc/>
    public override string ToString()
    {
        return _name;
    }

    /// <summary>
    /// Async receiver to be implemented by subclass.
    /// </summary>
    /// <param name="value">Value received.</param>
    /// <param name="envelope">Message envelope.</param>
    /// <returns>Async task.</returns>
    protected virtual async Task ReceiveAsync(TIn value, Envelope envelope)
    {
        await Task.Run(() => throw new NotImplementedException());
    }
}
