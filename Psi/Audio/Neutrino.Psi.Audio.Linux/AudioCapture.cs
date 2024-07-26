// <copyright file="AudioCapture.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Threading;
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
/// parameter (e.g. "plughw:0,0").
/// </remarks>
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
    private LinuxAudioInterop.AudioDevice _audioDevice;

    /// <summary>
    /// The current audio capture buffer.
    /// </summary>
    private AudioBuffer _buffer;

    private Thread _background;
    private volatile bool _isStopping;

    /// <summary>
    /// Initializes a new instance of the <see cref="AudioCapture"/> class.
    /// </summary>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="configuration">The component configuration.</param>
    /// <param name="name">An optional name for this component.</param>
    public AudioCapture(Pipeline pipeline, AudioCaptureConfiguration configuration, string name = nameof(AudioCapture))
    {
        _pipeline = pipeline;
        _name = name;
        _configuration = configuration;
        _audioBuffers = pipeline.CreateEmitter<AudioBuffer>(this, "AudioBuffers");
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AudioCapture"/> class.
    /// </summary>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="configurationFilename">The component configuration file.</param>
    public AudioCapture(Pipeline pipeline, string configurationFilename = null)
        : this(pipeline, ConfigurationHelper.ReadFromFileOrDefault(configurationFilename, new AudioCaptureConfiguration(), true))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AudioCapture"/> class with a specified output format and device name.
    /// </summary>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="outputFormat">The output format to use.</param>
    /// <param name="deviceName">The name of the audio device.</param>
    public AudioCapture(Pipeline pipeline, WaveFormat outputFormat, string deviceName = "plughw:0,0")
        : this(pipeline, new AudioCaptureConfiguration() { Format = outputFormat, DeviceName = deviceName })
    {
    }

    /// <summary>
    /// Gets the output stream of audio buffers.
    /// </summary>
    public Emitter<AudioBuffer> Out => _audioBuffers;

    /// <summary>
    /// Gets the name of the audio device.
    /// </summary>
    public string AudioDeviceName => _configuration.DeviceName;

    /// <summary>
    /// Gets the configuration for this component.
    /// </summary>
    private AudioCaptureConfiguration Configuration => _configuration;

    /// <summary>
    /// Dispose method.
    /// </summary>
    public void Dispose()
    {
        Stop();
    }

    /// <inheritdoc/>
    public void Start(Action<DateTime> notifyCompletionTime)
    {
        // notify that this is an infinite source component
        notifyCompletionTime(DateTime.MaxValue);

        _audioDevice = LinuxAudioInterop.Open(
            _configuration.DeviceName,
            LinuxAudioInterop.Mode.Capture,
            (int)_configuration.Format.SamplesPerSec,
            _configuration.Format.Channels,
            LinuxAudioInterop.ConvertFormat(_configuration.Format));

        _background = new Thread(new ThreadStart(() =>
        {
            const int blockSize = 256;
            WaveFormat format = _configuration.Format;
            int length = blockSize * format.BitsPerSample / 8;
            byte[] buf = new byte[length];

            while (!_isStopping)
            {
                try
                {
                    LinuxAudioInterop.Read(_audioDevice, buf, blockSize);
                }
                catch
                {
                    if (_audioDevice != null)
                    {
                        throw;
                    }
                }

                // Only create a new buffer if necessary
                if ((_buffer.Data == null) || (_buffer.Length != length))
                {
                    _buffer = new AudioBuffer(length, format);
                }

                // Copy the data
                Array.Copy(buf, _buffer.Data, length);

                // use the end of the last sample in the packet as the originating time
                DateTime originatingTime = _pipeline.GetCurrentTime().AddSeconds((double)length / format.AvgBytesPerSec);

                // post the data to the output stream
                _audioBuffers.Post(_buffer, originatingTime);
            }
        }))
        { IsBackground = true };

        _isStopping = false;
        _background.Start();
    }

    /// <inheritdoc/>
    public void Stop(DateTime finalOriginatingTime, Action notifyCompleted)
    {
        Stop();
        notifyCompleted();
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return _name;
    }

    private void Stop()
    {
        // stop any running background thread and wait for it to terminate
        _isStopping = true;
        _background?.Join();

        LinuxAudioInterop.AudioDevice audioDevice = Interlocked.Exchange(ref _audioDevice, null);
        if (audioDevice != null)
        {
            LinuxAudioInterop.Close(audioDevice);
        }
    }
}
