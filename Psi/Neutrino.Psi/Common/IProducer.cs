﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.

using Neutrino.Psi.Streams;

namespace Neutrino.Psi.Common;

/// <summary>
/// Components that implement this interface are simple, single output generators.
/// </summary>
/// <typeparam name="TOut">The type of the component output.</typeparam>
public interface IProducer<TOut>
{
    /// <summary>
    /// Gets the stream to write messages to.
    /// </summary>
    Emitter<TOut> Out { get; }
}
