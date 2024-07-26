// <copyright file="AudioCapture.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Neutrino.Psi.Common;
using Neutrino.Psi.Components;
using Neutrino.Psi.Configuration;
using Neutrino.Psi.Executive;
using Neutrino.Psi.Streams;

namespace Neutrino.Psi.Audio;

/// <summary>
/// Component that captures and streams audio from an input device such as a microphone.
/// </summary>
/// <remarks>
/// This sensor component produces an audio output stream of type <see cref="AudioBuffer"/> which may be piped to
/// downstream components for further processing and optionally saved to a data store. The audio input device from
/// which to capture may be specified via the <see cref="AudioCaptureConfiguration.DeviceName"/> configuration
/// parameter. The <see cref="GetAvailableDevices"/> static method may be used to enumerate the names of audio
/// input devices currently available on the system.
/// <br/>
/// **Please note**: This component uses Audio APIs that are available on Windows only.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class AudioCapture : IProducer<AudioBuffer>, ISourceComponent, IDisposable
{
    private readonly Pipeline _pipeline;
    private readonly string _name;

    /// <summary>
    /// The configuration for this component.
    /// </summary>
    private readonly AudioCaptureConfiguration _configuration;

    /// <summary>
    /// The output stream of audio buffers.
    /// </summary>
    private readonly Emitter<AudioBuffer> _audioBuffers;

    /// <summary>
    /// The audio capture device.
    /// </summary>
    private WasapiCapture _wasapiCapture;

    /// <summary>
    /// The current audio capture buffer.
    /// </summary>
    private AudioBuffer _buffer;

    /// <summary>
    /// The current source audio format.
    /// </summary>
    private WaveFormat _sourceFormat = null;

    /// <summary>
    /// Keep track of the timestamp of the last audio buffer (computed from the value reported to us by the capture driver).
    /// </summary>
    private DateTime _lastPostedAudioTime = DateTime.MinValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="AudioCapture"/> class.
    /// </summary>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="configuration">The component configuration.</param>
    /// <param name="name">An optional name for the component.</param>
    public AudioCapture(Pipeline pipeline, AudioCaptureConfiguration configuration, string name = nameof(AudioCapture))
    {
        _pipeline = pipeline;
        _name = name;
        _configuration = configuration;
        _audioBuffers = pipeline.CreateEmitter<AudioBuffer>(this, "AudioBuffers");
        AudioLevelInput = pipeline.CreateReceiver<double>(this, SetAudioLevel, nameof(AudioLevelInput));
        AudioLevel = pipeline.CreateEmitter<double>(this, nameof(AudioLevel));

        _wasapiCapture = new WasapiCapture();
        _wasapiCapture.Initialize(Configuration.DeviceName);

        if (Configuration.AudioLevel >= 0)
        {
            _wasapiCapture.AudioLevel = Configuration.AudioLevel;
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AudioCapture"/> class.
    /// </summary>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="configurationFilename">The component configuration file.</param>
    /// <param name="name">An optional name for the component.</param>
    public AudioCapture(Pipeline pipeline, string configurationFilename = null, string name = nameof(AudioCapture))
        : this(
            pipeline,
            ConfigurationHelper.ReadFromFileOrDefault(configurationFilename, new AudioCaptureConfiguration(), true),
            name)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AudioCapture"/> class with a specified output format and device name.
    /// </summary>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="outputFormat">The output format to use.</param>
    /// <param name="deviceName">The name of the audio device.</param>
    /// <param name="name">An optional name for the component.</param>
    public AudioCapture(Pipeline pipeline, WaveFormat outputFormat, string deviceName = null, string name = nameof(AudioCapture))
        : this(pipeline, new AudioCaptureConfiguration() { Format = outputFormat, DeviceName = deviceName }, name)
    {
    }

    /// <summary>
    /// Gets the output stream of audio buffers.
    /// </summary>
    public Emitter<AudioBuffer> Out => _audioBuffers;

    /// <summary>
    /// Gets the level control input.
    /// </summary>
    public Receiver<double> AudioLevelInput { get; }

    /// <summary>
    /// Gets the output stream of audio level data.
    /// </summary>
    public Emitter<double> AudioLevel { get; }

    /// <summary>
    /// Gets the name of the audio device.
    /// </summary>
    public string AudioDeviceName => _wasapiCapture.Name;

    /// <summary>
    /// Gets the configuration for this component.
    /// </summary>
    private AudioCaptureConfiguration Configuration => _configuration;

    /// <summary>
    /// Static method to get the available audio capture devices.
    /// </summary>
    /// <returns>
    /// An array of available capture device names.
    /// </returns>
    public static string[] GetAvailableDevices()
    {
        return WasapiCapture.GetAvailableCaptureDevices();
    }

    /// <summary>
    /// Sets the audio level.
    /// </summary>
    /// <param name="level">The audio level.</param>
    public void SetAudioLevel(double level)
    {
        if (_wasapiCapture != null)
        {
            _wasapiCapture.AudioLevel = level;
        }
    }

    /// <summary>
    /// Dispose method.
    /// </summary>
    public void Dispose()
    {
        if (_wasapiCapture != null)
        {
            _wasapiCapture.Dispose();
            _wasapiCapture = null;
        }
    }

    /// <inheritdoc/>
    public void Start(Action<DateTime> notifyCompletionTime)
    {
        // notify that this is an infinite source component
        notifyCompletionTime(DateTime.MaxValue);

        // publish initial values at startup
        AudioLevel.Post(_wasapiCapture.AudioLevel, _pipeline.GetCurrentTime());

        // register the event handler which will post new captured samples on the output stream
        _wasapiCapture.AudioDataAvailableEvent += HandleAudioDataAvailableEvent;

        // register the volume notification event handler
        _wasapiCapture.AudioVolumeNotification += HandleAudioVolumeNotification;

        // tell the audio device to start capturing audio
        _wasapiCapture.StartCapture(Configuration.TargetLatencyInMs, Configuration.AudioEngineBufferInMs, Configuration.Gain, Configuration.Format, Configuration.OptimizeForSpeech, Configuration.UseEventDrivenCapture);

        // Get the actual capture format. This should normally match the configured output format,
        // unless that was null in which case the native device capture format is returned.
        _sourceFormat = Configuration.Format ?? _wasapiCapture.MixFormat;
    }

    /// <inheritdoc/>
    public void Stop(DateTime finalOriginatingTime, Action notifyCompleted)
    {
        notifyCompleted();
        _sourceFormat = null;
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return _name;
    }

    /// <summary>
    /// The event handler that processes new audio data packets.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="e">A <see cref="AudioDataEventArgs"/> that contains the event data.</param>
    private void HandleAudioDataAvailableEvent(object sender, AudioDataEventArgs e)
    {
        if ((e.Length > 0) && (_sourceFormat != null))
        {
            // use the end of the last sample in the packet as the originating time
            DateTime originatingTime = _pipeline.GetCurrentTimeFromElapsedTicks(e.Timestamp +
                (10000000L * e.Length / _sourceFormat.AvgBytesPerSec));

            // Detect out of order originating times
            if (originatingTime <= _lastPostedAudioTime)
            {
                if (_configuration.DropOutOfOrderPackets)
                {
                    // Ignore this packet with an out of order timestamp and return.
                    return;
                }
                else
                {
                    throw new InvalidOperationException(
                        $"The most recently captured audio buffer has a timestamp ({originatingTime.TimeOfDay}) which is before " +
                        $"that of the last posted audio buffer ({_lastPostedAudioTime.TimeOfDay}), as reported by the driver. This could " +
                        $"be due to a timing glitch in the audio stream. Set the 'DropOutOfOrderPackets' " +
                        $"{nameof(AudioCaptureConfiguration)} flag to true to handle this condition by dropping " +
                        $"packets with out of order timestamps.");
                }
            }

            _lastPostedAudioTime = originatingTime;

            // Only create a new buffer if necessary.
            if ((_buffer.Data == null) || (_buffer.Length != e.Length))
            {
                _buffer = new AudioBuffer(e.Length, _sourceFormat);
            }

            // Copy the data.
            Marshal.Copy(e.Data, _buffer.Data, 0, e.Length);

            // post the data to the output stream
            _audioBuffers.Post(_buffer, originatingTime);
        }
    }

    /// <summary>
    /// Handles volume notifications.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="e">A <see cref="AudioVolumeEventArgs"/> that contains the event data.</param>
    private void HandleAudioVolumeNotification(object sender, AudioVolumeEventArgs e)
    {
        AudioLevel.Post(e.MasterVolume, _pipeline.GetCurrentTime());
    }
}
