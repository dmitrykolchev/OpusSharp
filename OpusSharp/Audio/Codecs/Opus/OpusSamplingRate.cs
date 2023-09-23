// <copyright file="OpusSamplingRate.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

namespace DykBits.Audio.Codecs.Opus;

/// <summary>
/// Sampling rate of input signal (Hz) 
/// 8000, 12000, 16000, 24000, or 48000
/// </summary>
public enum OpusSamplingRate : int
{
    Rate8K = 8000,
    Rate12K = 12000,
    Rate16K = 16000,
    Rate24K = 24000,
    Rate48K = 48000,
}
