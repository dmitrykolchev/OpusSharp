// <copyright file="AudioPlayer.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using Microsoft.Psi.Components;

namespace Microsoft.Psi.Audio;

/// <summary>
/// Component that plays back an audio stream to an output device such as the speakers.
/// </summary>
/// <remarks>
/// This output component renders an audio input stream of type <see cref="AudioBuffer"/> to the
/// default or other specified audio output device for playback. The audio device on which to
/// playback the output may be specified by name via the <see cref="AudioPlayerConfiguration.DeviceName"/>
/// configuration parameter.
/// </remarks>
public sealed class AudioPlayer : SimpleConsumer<AudioBuffer>, IDisposable
{
    private readonly Pipeline _pipeline;

    /// <summary>
    /// The configuration for this component.
    /// </summary>
    private readonly AudioPlayerConfiguration _configuration;

    /// <summary>
    /// Number of bytes per audio frame.
    /// </summary>
    private readonly int _frameSize;

    /// <summary>
    /// The audio capture device.
    /// </summary>
    private LinuxAudioInterop.AudioDevice _audioDevice;

    /// <summary>
    /// Initializes a new instance of the <see cref="AudioPlayer"/> class.
    /// </summary>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="configuration">The component configuration.</param>
    /// <param name="name">An optional name for this component.</param>
    public AudioPlayer(Pipeline pipeline, AudioPlayerConfiguration configuration, string name = nameof(AudioPlayer))
        : base(pipeline, name)
    {
        pipeline.PipelineRun += (s, e) => OnPipelineRun();
        In.Unsubscribed += _ => OnUnsubscribed();
        _pipeline = pipeline;
        _configuration = configuration;
        _frameSize = _configuration.Format.Channels * configuration.Format.BitsPerSample / 8;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AudioPlayer"/> class.
    /// </summary>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="configurationFilename">The component configuration file.</param>
    public AudioPlayer(Pipeline pipeline, string configurationFilename = null)
        : this(pipeline, ConfigurationHelper.ReadFromFileOrDefault(configurationFilename, new AudioPlayerConfiguration(), true))
    {
    }

    /// <summary>
    /// Receiver for the audio data.
    /// </summary>
    /// <param name="audioData">A buffer containing the next chunk of audio data.</param>
    public override void Receive(Message<AudioBuffer> audioData)
    {
        // take action only if format is different
        if (_audioDevice != null && audioData.Data.HasValidData)
        {
            AudioBuffer data = audioData.Data;
            LinuxAudioInterop.Write(_audioDevice, data.Data, data.Length / _frameSize);
        }
    }

    /// <summary>
    /// Disposes the <see cref="AudioPlayer"/> object.
    /// </summary>
    public void Dispose()
    {
        OnUnsubscribed();
    }

    /// <summary>
    /// Starts playing back audio.
    /// </summary>
    private void OnPipelineRun()
    {
        _audioDevice = LinuxAudioInterop.Open(
            _configuration.DeviceName,
            LinuxAudioInterop.Mode.Playback,
            (int)_configuration.Format.SamplesPerSec,
            _configuration.Format.Channels,
            LinuxAudioInterop.ConvertFormat(_configuration.Format));
    }

    /// <summary>
    /// Stops playing back audio.
    /// </summary>
    private void OnUnsubscribed()
    {
        if (_audioDevice != null)
        {
            LinuxAudioInterop.Close(_audioDevice);
            _audioDevice = null;
        }
    }
}
