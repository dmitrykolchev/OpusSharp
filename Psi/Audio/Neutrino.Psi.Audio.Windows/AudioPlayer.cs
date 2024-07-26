// <copyright file="AudioPlayer.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Runtime.Versioning;
using Neutrino.Psi.Common;
using Neutrino.Psi.Components;
using Neutrino.Psi.Configuration;
using Neutrino.Psi.Executive;
using Neutrino.Psi.Streams;

namespace Neutrino.Psi.Audio;

/// <summary>
/// Component that plays back an audio stream to an output device such as the speakers.
/// </summary>
/// <remarks>
/// This output component renders an audio input stream of type <see cref="AudioBuffer"/> to the
/// default or other specified audio output device for playback. The audio device on which to
/// playback the output may be specified by name via the <see cref="AudioPlayerConfiguration.DeviceName"/>
/// configuration parameter. The <see cref="GetAvailableDevices"/> static method may be used to
/// enumerate the names of audio output devices currently available on the system.
/// <br/>
/// **Please note**: This component uses Audio APIs that are available on Windows only.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class AudioPlayer : SimpleConsumer<AudioBuffer>, ISourceComponent, IDisposable
{
    private readonly Pipeline _pipeline;
    private readonly AudioPlayerConfiguration _configuration;
    private WaveFormat _currentInputFormat;

    /// <summary>
    /// The audio render device.
    /// </summary>
    private WasapiRender _wasapiRender;

    /// <summary>
    /// Initializes a new instance of the <see cref="AudioPlayer"/> class.
    /// </summary>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="configuration">The component configuration.</param>
    /// <param name="name">An optional name for the component.</param>
    public AudioPlayer(Pipeline pipeline, AudioPlayerConfiguration configuration, string name = nameof(AudioPlayer))
        : base(pipeline, name)
    {
        _pipeline = pipeline;
        _configuration = configuration;
        _currentInputFormat = configuration.Format;
        AudioLevelInput = pipeline.CreateReceiver<double>(this, SetAudioLevel, nameof(AudioLevelInput));
        AudioLevel = pipeline.CreateEmitter<double>(this, nameof(AudioLevel));

        _wasapiRender = new WasapiRender();
        _wasapiRender.Initialize(configuration.DeviceName);

        if (configuration.AudioLevel >= 0)
        {
            _wasapiRender.AudioLevel = configuration.AudioLevel;
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AudioPlayer"/> class.
    /// </summary>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="configurationFilename">The component configuration file.</param>
    /// <param name="name">An optional name for the component.</param>
    public AudioPlayer(Pipeline pipeline, string configurationFilename = null, string name = nameof(AudioPlayer))
        : this(
            pipeline,
            ConfigurationHelper.ReadFromFileOrDefault(configurationFilename, new AudioPlayerConfiguration(), true),
            name)
    {
    }

    /// <summary>
    /// Gets the receiver for the audio level stream which controls the volume of the output.
    /// </summary>
    public Receiver<double> AudioLevelInput { get; }

    /// <summary>
    /// Gets the stream containing the output audio level.
    /// </summary>
    public Emitter<double> AudioLevel { get; }

    /// <summary>
    /// Gets a list of available audio render devices.
    /// </summary>
    /// <returns>
    /// An array of available render device names.
    /// </returns>
    public static string[] GetAvailableDevices()
    {
        return WasapiRender.GetAvailableRenderDevices();
    }

    /// <summary>
    /// Sets the audio output level.
    /// </summary>
    /// <param name="level">The audio level.</param>
    public void SetAudioLevel(Message<double> level)
    {
        _wasapiRender.AudioLevel = (float)level.Data;
    }

    /// <summary>
    /// Receiver for the audio data.
    /// </summary>
    /// <param name="audioData">A buffer containing the next chunk of audio data.</param>
    public override void Receive(Message<AudioBuffer> audioData)
    {
        // take action only if format is different
        if (audioData.Data.HasValidData)
        {
            if (!WaveFormat.Equals(audioData.Data.Format, _currentInputFormat))
            {
                // Make a copy of the new input format (don't just use a direct reference,
                // as the object graph of the Message.Data will be reclaimed by the runtime).
                audioData.Data.Format.DeepClone(ref _currentInputFormat);
                _configuration.Format = _currentInputFormat;

                // stop and restart the renderer to switch formats
                _wasapiRender.StopRendering();
                _wasapiRender.StartRendering(
                    _configuration.BufferLengthSeconds,
                    _configuration.TargetLatencyInMs,
                    _configuration.Gain,
                    _configuration.Format);
            }

            // Append the audio buffer to the audio renderer
            _wasapiRender.AppendAudio(audioData.Data.Data, false);
        }
    }

    /// <summary>
    /// Disposes the <see cref="AudioPlayer"/> object.
    /// </summary>
    public void Dispose()
    {
        StopRendering();
        _wasapiRender.Dispose();
        _wasapiRender = null;
    }

    /// <inheritdoc/>
    public void Start(Action<DateTime> notifyCompletionTime)
    {
        // Notify that this is an infinite source component. It is a source because it
        // posts changes to the volume level of the output audio device to the AudioLevel
        // stream for as long as the component is active.
        notifyCompletionTime(DateTime.MaxValue);

        // publish initial volume level at startup
        AudioLevel.Post(_wasapiRender.AudioLevel, _pipeline.GetCurrentTime());

        // register the volume changed notification event handler
        _wasapiRender.AudioVolumeNotification += HandleVolumeChangedNotification;

        // start the audio renderer
        _wasapiRender.StartRendering(
            _configuration.BufferLengthSeconds,
            _configuration.TargetLatencyInMs,
            _configuration.Gain,
            _configuration.Format);
    }

    /// <inheritdoc/>
    public void Stop(DateTime finalOriginatingTime, Action notifyCompleted)
    {
        StopRendering();
        notifyCompleted();
    }

    private void StopRendering()
    {
        _wasapiRender.StopRendering();

        // Unsubscribe from volume change notifications. This will cause the component to
        // stop posting changes to the volume level on the AudioLevel stream.
        _wasapiRender.AudioVolumeNotification -= HandleVolumeChangedNotification;
    }

    /// <summary>
    /// Handles volume changed notification events.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="e">A <see cref="AudioVolumeEventArgs"/> that contains the event data.</param>
    private void HandleVolumeChangedNotification(object sender, AudioVolumeEventArgs e)
    {
        AudioLevel.Post(e.MasterVolume, _pipeline.GetCurrentTime());
    }
}
