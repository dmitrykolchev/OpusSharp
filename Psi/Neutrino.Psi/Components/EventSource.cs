// <copyright file="EventSource.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using Neutrino.Psi.Executive;


namespace Neutrino.Psi.Components;

/// <summary>
/// A generator component that publishes messages of a specified type whenever an event is raised.
/// </summary>
/// <typeparam name="TEventHandler">The event handler delegate type.</typeparam>
/// <typeparam name="TOut">The output stream type.</typeparam>
public class EventSource<TEventHandler, TOut> : IProducer<TOut>, ISourceComponent
{
    private readonly Action<TEventHandler> _subscribe;
    private readonly Action<TEventHandler> _unsubscribe;
    private readonly TEventHandler _eventHandler;
    private readonly Pipeline _pipeline;
    private readonly string _name;

    /// <summary>
    /// Initializes a new instance of the <see cref="EventSource{TEventHandler, TOut}"/> class.
    /// The component will subscribe to an event on startup via the <paramref name="subscribe"/>
    /// delegate, using the supplied <paramref name="converter"/> function to transform the
    /// <see cref="Post"/> action delegate into an event handler compatible with the external
    /// event that is being subscribed to.
    /// </summary>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="subscribe">The delegate that subscribes to the external event.</param>
    /// <param name="unsubscribe">The delegate that unsubscribes from the external event.</param>
    /// <param name="converter">
    /// A function used to convert the <see cref="Post"/> action delegate into an event
    /// handler of type <typeparamref name="TEventHandler"/> that will be subscribed to the
    /// external event by the <paramref name="subscribe"/> delegate.
    /// </param>
    /// <param name="name">An optional name for the component.</param>
    public EventSource(
        Pipeline pipeline,
        Action<TEventHandler> subscribe,
        Action<TEventHandler> unsubscribe,
        Func<Action<TOut>, TEventHandler> converter,
        string name = nameof(EventSource<TEventHandler, TOut>))
    {
        _pipeline = pipeline;
        _name = name;
        Out = pipeline.CreateEmitter<TOut>(this, nameof(Out));
        _subscribe = subscribe;
        _unsubscribe = unsubscribe;

        // If the source event is triggered from the execution context of some other receiver, then because the
        // execution context flows all the way through to the event handler, the tracked state object (if tracking
        // is enabled) would represent the owner of the receiver, which would be inconsistent with posting from a
        // pure source (no tracked state object). In order to rectify this, we set the tracked state object to null
        // just prior to the call to this.Post by wrapping it in TrackStateObjectOnContext with a null state object.
        _eventHandler = converter(PipelineElement.TrackStateObjectOnContext<TOut>(Post, null, pipeline));
    }

    /// <summary>
    /// Gets the stream of output messages.
    /// </summary>
    public Emitter<TOut> Out { get; }

    /// <inheritdoc/>
    public void Start(Action<DateTime> notifyCompletionTime)
    {
        // notify that this is an infinite source component
        notifyCompletionTime(DateTime.MaxValue);

        _subscribe(_eventHandler);
    }

    /// <inheritdoc/>
    public void Stop(DateTime finalOriginatingTime, Action notifyCompleted)
    {
        _unsubscribe(_eventHandler);
        notifyCompleted();
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return _name;
    }

    /// <summary>
    /// Posts a value on the output stream.
    /// </summary>
    /// <param name="e">The value to post.</param>
    private void Post(TOut e)
    {
        Out.Post(e, _pipeline.GetCurrentTime());
    }
}
