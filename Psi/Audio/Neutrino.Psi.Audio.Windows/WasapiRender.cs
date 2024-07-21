// <copyright file="WasapiRender.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Microsoft.Psi.Audio.ComInterop;

namespace Microsoft.Psi.Audio;

/// <summary>
/// Implements the services required to play audio to audio renderer devices.
/// </summary>
[SupportedOSPlatform("windows")]
internal class WasapiRender : IDisposable
{
    private static Guid s_guidEventContext = new(0x65717dc8, 0xe74c, 0x4087, 0x90, 0x1, 0xdb, 0xc5, 0xdd, 0x5c, 0x9e, 0x19);

    private IMMDevice _audioDevice;
    private IAudioEndpointVolume _volume;
    private AudioEndpointVolumeCallback _volumeCallback;
    private WasapiRenderClient _wasapiRenderClient;
    private AudioDataRequestedCallback _callbackDelegate;
    private CircularBufferStream _audioBufferStream;

    /// <summary>
    /// Initializes a new instance of the <see cref="WasapiRender"/> class.
    /// </summary>
    public WasapiRender()
    {
    }

    /// <summary>
    /// An event that is raised whenever there is a change to the audio volume.
    /// </summary>
    public event EventHandler<AudioVolumeEventArgs> AudioVolumeNotification;

    /// <summary>
    /// Gets the expected audio format. This property will only be valid after StartRendering has been called
    /// and will return the expected format of the audio being rendered on the selected audio renderer device.
    /// </summary>
    public WaveFormat MixFormat => _wasapiRenderClient?.MixFormat;

    /// <summary>
    /// Gets or sets the audio output level.
    /// </summary>
    public float AudioLevel
    {
        get => _volume?.GetMasterVolumeLevelScalar() ?? 0;

        set => _volume?.SetMasterVolumeLevelScalar(value, ref s_guidEventContext);
    }

    /// <summary>
    /// Gets the friendly name of the audio renderer device.
    /// </summary>
    public string Name => DeviceUtil.GetDeviceFriendlyName(_audioDevice);

    /// <summary>
    /// Gets a list of available audio render devices.
    /// </summary>
    /// <returns>
    /// An array of available render device names.
    /// </returns>
    public static string[] GetAvailableRenderDevices()
    {
        // Get the collection of available render devices
        IMMDeviceCollection deviceCollection = DeviceUtil.GetAvailableDevices(EDataFlow.Render);

        string[] devices = null;
        int deviceCount = deviceCollection.GetCount();

        devices = new string[deviceCount];

        // Iterate over the collection to get the device names
        for (int i = 0; i < deviceCount; i++)
        {
            IMMDevice device = deviceCollection.Item(i);

            // Get the friendly name of the device
            devices[i] = DeviceUtil.GetDeviceFriendlyName(device);

            // Done with the device so release it
            Marshal.ReleaseComObject(device);
        }

        // Release the collection when done
        Marshal.ReleaseComObject(deviceCollection);

        return devices;
    }

    /// <summary>
    /// Disposes an instance of the <see cref="WasapiRender"/> class.
    /// </summary>
    public void Dispose()
    {
        StopRendering();

        if (_volume != null)
        {
            if (_volumeCallback != null)
            {
                // Unregister the callback before releasing.
                _volume.UnregisterControlChangeNotify(_volumeCallback);
                _volumeCallback = null;
            }

            Marshal.ReleaseComObject(_volume);
            _volume = null;
        }

        if (_audioDevice != null)
        {
            Marshal.ReleaseComObject(_audioDevice);
            _audioDevice = null;
        }
    }

    /// <summary>
    /// Initializes the audio renderer device.
    /// </summary>
    /// <param name="deviceDescription">
    /// The friendly name description of the device to render to. This is usually
    /// something like "Speakers (High Definition Audio)". To just use the
    /// default device, pass in NULL or an empty string.
    /// </param>
    public void Initialize(string deviceDescription)
    {
        Exception taskException = null;

        // Activate native audio COM objects on a thread-pool thread to ensure that they are in an MTA
        Task.Run(() =>
        {
            try
            {
                if (string.IsNullOrEmpty(deviceDescription))
                {
                    // use the default console device
                    _audioDevice = DeviceUtil.GetDefaultDevice(EDataFlow.Render, ERole.Console);
                }
                else
                {
                    _audioDevice = DeviceUtil.GetDeviceByName(EDataFlow.Render, deviceDescription);
                }

                if (_audioDevice != null)
                {
                    // Try to get the volume control
                    object obj = _audioDevice.Activate(new Guid(Guids.IAudioEndpointVolumeIIDString), ClsCtx.ALL, IntPtr.Zero);
                    _volume = (IAudioEndpointVolume)obj;

                    // Now create an IAudioEndpointVolumeCallback object that wraps the callback and register it with the endpoint.
                    _volumeCallback = new AudioEndpointVolumeCallback(AudioVolumeCallback);
                    _volume.RegisterControlChangeNotify(_volumeCallback);
                }
            }
            catch (Exception e)
            {
                taskException = e;
            }
        }).Wait();

        // do error checking on the main thread
        if (taskException != null)
        {
            // rethrow exception
            throw taskException;
        }
        else if (_audioDevice == null)
        {
            throw new IOException(string.IsNullOrEmpty(deviceDescription) ?
                "No default audio playback device found." :
                $"Audio playback device {deviceDescription} not found.");
        }
    }

    /// <summary>
    /// Starts rendering audio data.
    /// </summary>
    /// <param name="maxBufferSeconds">
    /// The maximum duration of audio that can be buffered for playback.
    /// </param>
    /// <param name="targetLatencyInMs">
    /// The target maximum number of milliseconds of acceptable lag between
    /// playback of samples and live sound being produced.
    /// </param>
    /// <param name="gain">
    /// The gain to be applied prior to rendering the audio.
    /// </param>
    /// <param name="inFormat">
    /// The input audio format.
    /// </param>
    public void StartRendering(double maxBufferSeconds, int targetLatencyInMs, float gain, WaveFormat inFormat)
    {
        if (_wasapiRenderClient != null)
        {
            StopRendering();
        }

        // Create an audio buffer to buffer audio awaiting playback.
        _audioBufferStream = new CircularBufferStream((long)Math.Ceiling(maxBufferSeconds * inFormat.AvgBytesPerSec), false);

        _wasapiRenderClient = new WasapiRenderClient(_audioDevice);

        // Create a callback delegate and marshal it to a function pointer. Keep a
        // reference to the delegate as a class field to prevent it from being GC'd.
        _callbackDelegate = new AudioDataRequestedCallback(AudioDataRequestedCallback);

        // initialize the renderer with the desired parameters
        _wasapiRenderClient.Initialize(targetLatencyInMs, gain, inFormat, _callbackDelegate);

        // tell WASAPI to start rendering
        _wasapiRenderClient.Start();
    }

    /// <summary>
    /// Appends audio buffers to the render queue. Audio will be rendered as soon as possible
    /// if <see cref="StartRendering"/> has previously been called.
    /// </summary>
    /// <param name="audioBuffer">The audio buffer to be rendered.</param>
    /// <param name="overwritePending">
    /// If true, then the internal buffer of audio pending rendering may be overwritten. If false,
    /// the call will block until there is sufficient space in the buffer to accommodate the audio
    /// data. Default is false.
    /// </param>
    public void AppendAudio(byte[] audioBuffer, bool overwritePending = false)
    {
        if (_audioBufferStream == null)
        {
            // component has been stopped
            return;
        }

        if (overwritePending)
        {
            _audioBufferStream.Write(audioBuffer, 0, audioBuffer.Length);
        }
        else
        {
            int bytesRemaining = audioBuffer.Length;
            while (bytesRemaining > 0)
            {
                bytesRemaining -= _audioBufferStream.WriteNoOverrun(audioBuffer, audioBuffer.Length - bytesRemaining, bytesRemaining);
            }
        }
    }

    /// <summary>
    /// Stops rendering audio data.
    /// </summary>
    public void StopRendering()
    {
        if (_wasapiRenderClient != null)
        {
            _wasapiRenderClient.Dispose();
            _wasapiRenderClient = null;
        }

        if (_audioBufferStream != null)
        {
            _audioBufferStream.Dispose();
            _audioBufferStream = null;
        }
    }

    /// <summary>
    /// Callback function that is passed to WASAPI to call whenever it is
    /// ready to receive new audio samples for rendering.
    /// </summary>
    /// <param name="dataPtr">
    /// Pointer to the native buffer that will receive the new audio data.
    /// </param>
    /// <param name="length">
    /// The maximum number of bytes of audio data that may be copied into pbData.
    /// </param>
    /// <param name="timestamp">
    /// The timestamp in 100-ns ticks of the first sample in data.
    /// </param>
    /// <returns>
    /// Returns the actual number of bytes copied into dataPtr.
    /// </returns>
    private int AudioDataRequestedCallback(IntPtr dataPtr, int length, out long timestamp)
    {
        // Timestamp is unnecessary when rendering so just set it to zero
        timestamp = 0;

        unsafe
        {
            // Read buffered audio directly into dataPtr
            return _audioBufferStream.Read(dataPtr, length, length);
        }
    }

    /// <summary>
    /// Callback function that is passed to the audio endpoint to call whenever
    /// there is a new audio volume notification.
    /// </summary>
    /// <param name="data">
    /// The audio volume notification data.
    /// </param>
    private void AudioVolumeCallback(AudioVolumeNotificationData data)
    {
        // Only raise event notification if we didn't initiate the volume change
        if (data.EventContext != s_guidEventContext)
        {
            // Raise the event, passing the audio volume notification data in the event args
            AudioVolumeNotification?.Invoke(this, new AudioVolumeEventArgs(data.Muted, data.MasterVolume, data.ChannelVolume));
        }
    }
}
