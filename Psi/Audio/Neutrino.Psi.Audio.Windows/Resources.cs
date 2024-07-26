// <copyright file="Resources.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Runtime.Versioning;
using Neutrino.Psi.Configuration;
using Neutrino.Psi.Executive;

namespace Neutrino.Psi.Audio;

/// <summary>
/// Implements methods for registering platform specific resources.
/// </summary>
[SupportedOSPlatform("windows")]
public static class Resources
{
    /// <summary>
    /// Registers platform specific resources.
    /// </summary>
    public static void RegisterPlatformResources()
    {
        PlatformResources.RegisterDefault<Func<Pipeline, IAudioResampler>>(p => new AudioResampler(p));
        PlatformResources.RegisterDefault<Func<Pipeline, WaveFormat, IAudioResampler>>((p, outFormat) => new AudioResampler(p, new AudioResamplerConfiguration { OutputFormat = outFormat }));
        PlatformResources.Register<Func<Pipeline, IAudioResampler>>(nameof(AudioResampler), p => new AudioResampler(p));
    }
}
