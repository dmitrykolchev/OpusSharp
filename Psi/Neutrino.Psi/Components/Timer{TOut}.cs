﻿// <copyright file="Timer{TOut}.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using Neutrino.Psi.Common;
using Neutrino.Psi.Executive;
using Neutrino.Psi.Streams;

namespace Neutrino.Psi.Components;


/// <summary>
/// A simple producer component that wakes up on a predefined interval and publishes one message.
/// </summary>
/// <typeparam name="TOut">The type of messages published by the generator.</typeparam>
public class Timer<TOut> : Timer, IProducer<TOut>
{
    private readonly Func<DateTime, TimeSpan, TOut> _generator;

    /// <summary>
    /// Initializes a new instance of the <see cref="Timer{TOut}"/> class.
    /// </summary>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="timerInterval">Time interval with which to produce messages.</param>
    /// <param name="generator">Message generation function.</param>
    /// <param name="name">An optional name for the component.</param>
    public Timer(Pipeline pipeline, uint timerInterval, Func<DateTime, TimeSpan, TOut> generator, string name = nameof(Timer))
        : base(pipeline, timerInterval, name)
    {
        Out = pipeline.CreateEmitter<TOut>(this, nameof(Out));
        _generator = generator;
    }

    /// <inheritdoc />
    public Emitter<TOut> Out { get; }

    /// <summary>
    /// Generate timer message from current and elapsed time.
    /// </summary>
    /// <param name="absoluteTime">The current (virtual) time.</param>
    /// <param name="relativeTime">The time elapsed since the generator was started.</param>
    protected override void Generate(DateTime absoluteTime, TimeSpan relativeTime)
    {
        TOut value = _generator(absoluteTime, relativeTime);
        Out.Post(value, absoluteTime);
    }
}
