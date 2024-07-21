// <copyright file="IAudioResampler.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using Neutrino.Psi.Components;

namespace Neutrino.Psi.Audio;

/// <summary>
/// Defines an interface for audio resampler components.
/// </summary>
public interface IAudioResampler : IConsumerProducer<AudioBuffer, AudioBuffer>
{
}
