// <copyright file="ReproducibleInterpolator{TIn,TResult}.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

namespace Neutrino.Psi;

/// <summary>
/// Defines a reproducible stream interpolator.
/// </summary>
/// <typeparam name="TIn">The type of the input messages.</typeparam>
/// <typeparam name="TResult">The type of the interpolation result.</typeparam>
/// <remarks>Reproducible interpolators produce results that do not depend on the wall-clock time of
/// message arrival on a stream, i.e., they are based on originating times of messages. As a result,
/// these interpolators might introduce extra delays as they might have to wait for enough messages on the
/// secondary stream to prove that the interpolation result is correct, irrespective of any other messages
/// that might arrive later.</remarks>
public abstract class ReproducibleInterpolator<TIn, TResult> : Interpolator<TIn, TResult>
{
}
