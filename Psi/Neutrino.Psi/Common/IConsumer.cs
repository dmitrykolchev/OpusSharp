// <copyright file="IConsumer.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using Neutrino.Psi.Streams;

namespace Neutrino.Psi.Common;

/// <summary>
/// Components that implement this interface are simple, single input consumers.
/// </summary>
/// <typeparam name="TIn">The type of message input.</typeparam>
public interface IConsumer<TIn>
{
    /// <summary>
    /// Gets the input we receive messages on.
    /// </summary>
    Receiver<TIn> In { get; }
}
