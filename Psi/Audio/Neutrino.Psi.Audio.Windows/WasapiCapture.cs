// <copyright file="WasapiCapture.cs">
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
/// Implements the services required to acquire audio from audio capture devices.
/// </summary>
[SupportedOSPlatform("windows")]
internal class WasapiCapture : IDisposable
{
    private static Guid s_guidEventContext = new(0x65717dc8, 0xe74c, 0x4087, 0x90, 0x1, 0xdb, 0xc5, 0xdd, 0x5c, 0x9e, 0x19);

    private IMMDevice _audioDevice;
    private IAudioEndpointVolume _volume;
    private AudioEndpointVolumeCallback _volumeCallback;
    private WasapiCaptureClient _wasapiCaptureClient;
    private AudioDataAvailableCallback _callbackDelegate;

    /// <summary>
    /// Initializes a new instance of the <see cref="WasapiCapture"/> class.
    /// </summary>
    public WasapiCapture()
    {
    }

    /// <summary>
    /// An event that is raised whenever new audio samples have been captured.
    /// </summary>
    public event EventHandler<AudioDataEventArgs> AudioDataAvailableEvent;

    /// <summary>
    /// An event that is raised whenever there is a change to the audio input level.
    /// </summary>
    public event EventHandler<AudioVolumeEventArgs> AudioVolumeNotification;

    /// <summary>
    /// Gets the captured output format. This property will only be valid after StartCapture has been called
    /// and will return the output format of the captured audio data from the selected audio capture device.
    /// </summary>
    public WaveFormat MixFormat => _wasapiCaptureClient?.MixFormat;

    /// <summary>
    /// Gets or sets the audio capture level.
    /// </summary>
    public double AudioLevel
    {
        get => _volume?.GetMasterVolumeLevelScalar() ?? 0;

        set => _volume?.SetMasterVolumeLevelScalar((float)value, ref s_guidEventContext);
    }

    /// <summary>
    /// Gets the friendly name of the audio capture device.
    /// </summary>
    public string Name => DeviceUtil.GetDeviceFriendlyName(_audioDevice);

    /// <summary>
    /// Gets a list of available audio capture devices.
    /// </summary>
    /// <returns>
    /// An array of available capture device names.
    /// </returns>
    public static string[] GetAvailableCaptureDevices()
    {
        // Get the collection of available capture devices
        IMMDeviceCollection deviceCollection = DeviceUtil.GetAvailableDevices(EDataFlow.Capture);

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
    /// Disposes an instance of the <see cref="WasapiCapture"/> class.
    /// </summary>
    public void Dispose()
    {
        StopCapture();

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
    /// Initializes the audio capture device.
    /// </summary>
    /// <param name="deviceDescription">
    /// The friendly name description of the device to capture from. This is usually
    /// something like "Microphone Array (USB Audio)". To capture from
    /// the default device, pass in NULL or an empty string.
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
                    _audioDevice = DeviceUtil.GetDefaultDevice(EDataFlow.Capture, ERole.Console);
                }
                else
                {
                    _audioDevice = DeviceUtil.GetDeviceByName(EDataFlow.Capture, deviceDescription);
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
                "No default audio capture device found." :
                $"Audio capture device {deviceDescription} not found.");
        }
    }

    /// <summary>
    /// Starts capturing audio data.
    /// </summary>
    /// <param name="targetLatencyInMs">
    /// The target maximum number of milliseconds of acceptable lag between
    /// live sound being produced and capture operation.
    /// </param>
    /// <param name="audioEngineBufferInMs">
    /// The amount of audio that may be buffered by the audio engine between
    /// reads.
    /// </param>
    /// <param name="gain">
    /// The gain to be applied to the captured audio.
    /// </param>
    /// <param name="outFormat">
    /// The desired output format of the captured audio.
    /// </param>
    /// <param name="speech">
    /// If true, optimizes the audio capture pipeline for speech recognition.
    /// </param>
    /// <param name="eventDrivenCapture">
    /// If true, initialize Windows audio capture in event-driven mode. The audio capture engine will call
    /// the <see cref="AudioDataAvailableCallback"/> handler as soon as data is available, at intervals
    /// determined by the audio engine (which may be less than the <paramref name="targetLatencyInMs"/>).
    /// This captures audio with the lowest possible latency while still allowing for buffering up to the
    /// amount of time specified by <paramref name="targetLatencyInMs"/> (when for example the system is
    /// under heavy load and the capture callback is unable to service audio packets at the rate at which
    /// the audio engine returns captured audio packets).
    /// </param>
    public void StartCapture(int targetLatencyInMs, int audioEngineBufferInMs, float gain, WaveFormat outFormat, bool speech, bool eventDrivenCapture)
    {
        if (_wasapiCaptureClient != null)
        {
            StopCapture();
        }

        _wasapiCaptureClient = new WasapiCaptureClient(_audioDevice, eventDrivenCapture);

        // Create a callback delegate and marshal it to a function pointer. Keep a
        // reference to the delegate as a class field to prevent it from being GC'd.
        _callbackDelegate = new AudioDataAvailableCallback(AudioDataAvailableCallback);

        // initialize the capture with the desired parameters
        _wasapiCaptureClient.Initialize(targetLatencyInMs, audioEngineBufferInMs, gain, outFormat, _callbackDelegate, speech);

        // tell WASAPI to start capturing
        _wasapiCaptureClient.Start();
    }

    /// <summary>
    /// Stops capturing audio data.
    /// </summary>
    public void StopCapture()
    {
        if (_wasapiCaptureClient != null)
        {
            _wasapiCaptureClient.Dispose();
            _wasapiCaptureClient = null;
        }
    }

    /// <summary>
    /// Callback function that is passed to WASAPI to call whenever it has
    /// new audio samples ready and waiting to be read.
    /// </summary>
    /// <param name="data">
    /// Pointer to the native buffer containing the new audio data.
    /// </param>
    /// <param name="length">
    /// The number of bytes of audio data available to be read.
    /// </param>
    /// <param name="timestamp">
    /// The timestamp in 100-ns ticks of the first sample in data.
    /// </param>
    private void AudioDataAvailableCallback(IntPtr data, int length, long timestamp)
    {
        // raise the event, passing the new data in the event args
        AudioDataAvailableEvent?.Invoke(this, new AudioDataEventArgs(timestamp, data, length));
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
