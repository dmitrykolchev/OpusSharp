﻿// <copyright file="AudioPlayerConfiguration.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

namespace Neutrino.Psi.Audio;

/// <summary>
/// Represents the configuration for the <see cref="AudioPlayer"/> component.
/// </summary>
/// <remarks>
/// Use this class to configure a new instance of the <see cref="AudioPlayer"/> component.
/// Refer to the properties in this class for more information on the various configuration options.
/// </remarks>
public sealed class AudioPlayerConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AudioPlayerConfiguration"/> class.
    /// </summary>
    public AudioPlayerConfiguration()
    {
    }

    /// <summary>
    /// Gets or sets the name of the audio player device.
    /// </summary>
    /// <remarks>
    /// Use this to specify the name of the audio playback device on which to output audio.
    /// To obtain a list of available recording devices on the system, use the
    /// <see cref="AudioPlayer.GetAvailableDevices"/> static method. If not specified, the
    /// default playback device will be selected.
    /// </remarks>
    public string DeviceName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the target audio latency.
    /// </summary>
    /// <remarks>
    /// This parameter controls the amount of audio to send to the audio device for playback
    /// at a time. This in turn determines the latency of the audio output (i.e. the amount of lag
    /// between when the audio was available and when the corresponding sound is produced). For
    /// live audio playback, we normally want this to be small. By default, this value is set to
    /// 20 milliseconds. It is safe to leave this unchanged.
    /// </remarks>
    public int TargetLatencyInMs { get; set; } = 20;

    /// <summary>
    /// Gets or sets the maximum duration of audio that can be buffered for playback.
    /// </summary>
    /// <remarks>
    /// This controls the amount of audio that can be buffered while waiting for the playback
    /// device to be ready to render it. The default value is 0.1 seconds.
    /// </remarks>
    public double BufferLengthSeconds { get; set; } = 0.1;

    /// <summary>
    /// Gets or sets the audio output level.
    /// </summary>
    /// <remarks>
    /// This is the initial level to set the audio playback device to. Valid values range
    /// between 0.0 and 1.0 inclusive. If not specified, the current level of the selected
    /// playback device will be left unchanged.
    /// </remarks>
    public float AudioLevel { get; set; } = -1;

    /// <summary>
    /// Gets or sets the additional gain to be applied to the audio data.
    /// </summary>
    /// <remarks>
    /// This specifies an additional gain which may be applied computationally to the audio
    /// signal. Values greater than 1.0 boost the audio signal, while values in the range
    /// of 0.0 to 1.0 attenuate it. The default value is 1.0 (no additional gain).
    /// </remarks>
    public float Gain { get; set; } = 1.0f;

    /// <summary>
    /// Gets or sets the input format of the audio stream.
    /// </summary>
    /// <remarks>
    /// This specifies the expected format of the audio arriving on the input stream. If not
    /// set, the <see cref="AudioPlayer"/> component will attempt to infer the audio format
    /// from the <see cref="AudioBuffer"/> messages arriving on the input stream.
    /// </remarks>
    public WaveFormat Format { get; set; } = WaveFormat.Create16kHz1Channel16BitPcm();
}
