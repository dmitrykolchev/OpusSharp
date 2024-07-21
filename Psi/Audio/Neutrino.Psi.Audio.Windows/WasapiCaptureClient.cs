// <copyright file="WasapiCaptureClient.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using Microsoft.Psi.Audio.ComInterop;

namespace Microsoft.Psi.Audio;

/// <summary>
/// The callback delegate that is invoked whenever new audio data is captured.
/// </summary>
/// <param name="data">A pointer to the audio buffer.</param>
/// <param name="length">The number of bytes of data available in the audio buffer.</param>
/// <param name="timestamp">The timestamp of the audio buffer.</param>
internal delegate void AudioDataAvailableCallback(IntPtr data, int length, long timestamp);

/// <summary>
/// The WASAPI capture class.
/// </summary>
[SupportedOSPlatform("windows")]
internal class WasapiCaptureClient : IDisposable
{
    private const int AudioClientBufferEmpty = 0x08890001;
    private static Guid audioClientIID = new(Guids.IAudioClientIIDString);

    // Core Audio Capture member variables.
    private readonly IMMDevice _endpoint;
    private readonly bool _isEventDriven;
    private IAudioClient _audioClient;
    private IAudioCaptureClient _captureClient;
    private IMFTransform _resampler;
    private AutoResetEvent _audioAvailableEvent;

    private AudioDataAvailableCallback _dataAvailableCallback;
    private Thread _captureThread;
    private ManualResetEvent _shutdownEvent;
    private int _engineLatencyInMs;
    private int _engineBufferInMs;
    private WaveFormat _mixFormat;
    private int _mixFrameSize;
    private float _gain;

    // Capture buffer member variables
    private int _inputBufferSize;
    private IMFMediaBuffer _inputBuffer;
    private IMFSample _inputSample;
    private int _outputBufferSize;
    private IMFMediaBuffer _outputBuffer;
    private IMFSample _outputSample;
    private int _bytesCaptured;

    /// <summary>
    /// Initializes a new instance of the <see cref="WasapiCaptureClient"/> class.
    /// </summary>
    /// <param name="endpoint">The audio endpoint device.</param>
    /// <param name="isEventDriven">If true, uses WASAPI event-driven audio capture.</param>
    public WasapiCaptureClient(IMMDevice endpoint, bool isEventDriven)
    {
        _endpoint = endpoint;
        _isEventDriven = isEventDriven;
    }

    /// <summary>
    /// Gets the mix format of the captured audio.
    /// </summary>
    public WaveFormat MixFormat => _mixFormat;

    /// <summary>
    /// Disposes the <see cref="WasapiCaptureClient"/> object.
    /// </summary>
    public void Dispose()
    {
        if (_captureThread != null)
        {
            _shutdownEvent.Set();
            _captureThread.Join();
            _captureThread = null;
        }

        if (_shutdownEvent != null)
        {
            _shutdownEvent.Close();
            _shutdownEvent = null;
        }

        if (_audioAvailableEvent != null)
        {
            _audioAvailableEvent.Close();
            _audioAvailableEvent = null;
        }

        if (_audioClient != null)
        {
            Marshal.ReleaseComObject(_audioClient);
            _audioClient = null;
        }

        if (_captureClient != null)
        {
            Marshal.ReleaseComObject(_captureClient);
            _captureClient = null;
        }

        if (_resampler != null)
        {
            Marshal.ReleaseComObject(_resampler);
            _resampler = null;
        }

        if (_inputBuffer != null)
        {
            Marshal.ReleaseComObject(_inputBuffer);
            _inputBuffer = null;
        }

        if (_inputSample != null)
        {
            Marshal.ReleaseComObject(_inputSample);
            _inputSample = null;
        }

        if (_outputBuffer != null)
        {
            Marshal.ReleaseComObject(_outputBuffer);
            _outputBuffer = null;
        }

        if (_outputSample != null)
        {
            Marshal.ReleaseComObject(_outputSample);
            _outputSample = null;
        }
    }

    /// <summary>
    /// Initialize the capturer.
    /// </summary>
    /// <param name="engineLatency">
    /// Number of milliseconds of acceptable lag between live sound being produced and recording operation.
    /// </param>
    /// <param name="engineBuffer">
    /// Number of milliseconds of audio that may be buffered between reads.
    /// </param>
    /// <param name="gain">
    /// The gain to be applied to the audio after capture.
    /// </param>
    /// <param name="outFormat">
    /// The format of the audio to be captured. If this is NULL, the default audio format of the
    /// capture device will be used.
    /// </param>
    /// <param name="callback">
    /// Callback function delegate which will handle the captured data.
    /// </param>
    /// <param name="speech">
    /// If true, sets the audio category to speech to optimize audio pipeline for speech recognition.
    /// </param>
    public void Initialize(int engineLatency, int engineBuffer, float gain, WaveFormat outFormat, AudioDataAvailableCallback callback, bool speech)
    {
        // Create our shutdown event - we want a manual reset event that starts in the not-signaled state.
        _shutdownEvent = new ManualResetEvent(false);

        // Now activate an IAudioClient object on our preferred endpoint and retrieve the mix format for that endpoint.
        object obj = _endpoint.Activate(ref audioClientIID, ClsCtx.INPROC_SERVER, IntPtr.Zero);
        _audioClient = (IAudioClient)obj;

        // The following block enables advanced mic array APO pipeline on Windows 10 RS2 builds >= 15004.
        // This must be called before the call to GetMixFormat() in LoadFormat().
        if (speech)
        {
            IAudioClient2 audioClient2 = (IAudioClient2)_audioClient;
            if (audioClient2 != null)
            {
                AudioClientProperties properties = new()
                {
                    Size = Marshal.SizeOf<AudioClientProperties>(),
                    Category = AudioStreamCategory.Speech,
                };

                int hr = audioClient2.SetClientProperties(ref properties);
                if (hr != 0)
                {
                    Console.WriteLine("Failed to set audio stream category to AudioCategory_Speech: {0}", hr);
                }
            }
            else
            {
                Console.WriteLine("Unable to get IAudioClient2 interface");
            }
        }

        // Load the MixFormat. This may differ depending on the shared mode used.
        LoadFormat();

        // Remember our configured latency and buffer size
        _engineLatencyInMs = engineLatency;
        _engineBufferInMs = engineBuffer;

        // Set the gain
        _gain = gain;

        // Determine whether or not we need a resampler
        _resampler = null;

        if (outFormat != null)
        {
            // Check if the desired format is supported
            IntPtr outFormatPtr = WaveFormat.MarshalToPtr(outFormat);
            int hr = _audioClient.IsFormatSupported(AudioClientShareMode.Shared, outFormatPtr, out nint closestMatchPtr);

            // Free outFormatPtr to prevent leaking memory
            Marshal.FreeHGlobal(outFormatPtr);

            if (hr == 0)
            {
                // Replace _MixFormat with outFormat. Since it is supported, we will initialize
                // the audio capture client with that format and capture without resampling.
                _mixFormat = outFormat;
                _mixFrameSize = _mixFormat.BitsPerSample / 8 * _mixFormat.Channels;

                InitializeAudioEngine();
            }
            else
            {
                // In all other cases, we need to resample to OutFormat
                if ((hr == 1) && (closestMatchPtr != IntPtr.Zero))
                {
                    // Use closest match suggested by IsFormatSupported() and resample
                    _mixFormat = WaveFormat.MarshalFromPtr(closestMatchPtr);
                    _mixFrameSize = _mixFormat.BitsPerSample / 8 * _mixFormat.Channels;

                    // Free closestMatchPtr to prevent leaking memory
                    Marshal.FreeCoTaskMem(closestMatchPtr);
                }

                // initialize the audio engine first as the engine latency may be modified after initialization
                InitializeAudioEngine();

                // initialize the resampler buffers
                _inputBufferSize = (int)(_engineBufferInMs * _mixFormat.AvgBytesPerSec / 1000);
                _outputBufferSize = (int)(_engineBufferInMs * outFormat.AvgBytesPerSec / 1000);

                DeviceUtil.CreateResamplerBuffer(_inputBufferSize, out _inputSample, out _inputBuffer);
                DeviceUtil.CreateResamplerBuffer(_outputBufferSize, out _outputSample, out _outputBuffer);

                // Create resampler object
                _resampler = DeviceUtil.CreateResampler(_mixFormat, outFormat);
            }
        }
        else
        {
            // initialize the audio engine with the default mix format
            InitializeAudioEngine();
        }

        // Set the callback function
        _dataAvailableCallback = callback;
    }

    /// <summary>
    ///  Start capturing audio data.
    /// </summary>
    public void Start()
    {
        _bytesCaptured = 0;

        // Now create the thread which is going to drive the capture.
        _captureThread = new Thread(DoCaptureThread);

        // We're ready to go, start capturing!
        _captureThread.Start();
        _audioClient.Start();
    }

    /// <summary>
    /// Stop the capturer.
    /// </summary>
    public void Stop()
    {
        // Tell the capture thread to shut down, wait for the thread to complete then clean up all the stuff we
        // allocated in Start().
        if (_shutdownEvent != null)
        {
            _shutdownEvent.Set();
        }

        _audioClient.Stop();

        if (_captureThread != null)
        {
            _captureThread.Join();
            _captureThread = null;
        }
    }

    /// <summary>
    /// Capture thread - captures audio from WASAPI, resampling it if necessary.
    /// </summary>
    private void DoCaptureThread()
    {
        bool stillPlaying = true;
        int mmcssHandle = 0;
        int mmcssTaskIndex = 0;

        mmcssHandle = NativeMethods.AvSetMmThreadCharacteristics("Audio", ref mmcssTaskIndex);

        WaitHandle[] waitArray = _isEventDriven ?
            new WaitHandle[] { _shutdownEvent, _audioAvailableEvent } :
            new WaitHandle[] { _shutdownEvent };

        int waitTimeout = _isEventDriven ? Timeout.Infinite : _engineLatencyInMs;

        while (stillPlaying)
        {
            // We want to wait for half the desired latency in milliseconds.
            // That way we'll wake up half way through the processing period to pull the
            // next set of samples from the engine.
            int waitResult = WaitHandle.WaitAny(waitArray, waitTimeout);

            switch (waitResult)
            {
                case 0:
                    // If shutdownEvent has been set, we're done and should exit the main capture loop.
                    stillPlaying = false;
                    break;

                default:
                    {
                        // We need to retrieve the next buffer of samples from the audio capturer.
                        bool isEmpty = false;
                        long lastQpcPosition = 0;

                        // Keep fetching audio in a tight loop as long as audio device still has data.
                        while (!isEmpty && !_shutdownEvent.WaitOne(0))
                        {
                            int hr = _captureClient.GetBuffer(out nint dataPointer, out int framesAvailable, out int flags, out long bufferPosition, out long qpcPosition);
                            if (hr >= 0)
                            {
                                if ((hr == AudioClientBufferEmpty) || (framesAvailable == 0))
                                {
                                    isEmpty = true;
                                }
                                else
                                {
                                    int bytesAvailable = framesAvailable * _mixFrameSize;

                                    unsafe
                                    {
                                        // The flags on capture tell us information about the data.
                                        // We only really care about the silent flag since we want to put frames of silence into the buffer
                                        // when we receive silence.  We rely on the fact that a logical bit 0 is silence for both float and int formats.
                                        if ((flags & (int)AudioClientBufferFlags.Silent) != 0)
                                        {
                                            // Fill 0s from the capture buffer to the output buffer.
                                            float* ptr = (float*)dataPointer.ToPointer();
                                            for (int i = 0; i < bytesAvailable / sizeof(float); i++)
                                            {
                                                *(ptr + i) = 0f;
                                            }
                                        }
                                        else if (_gain != 1.0f)
                                        {
                                            // Apply gain on the raw buffer if needed, before the resampler.
                                            // When we capture in shared mode the capture mix format is always 32-bit IEEE
                                            // floating point, so we can safely assume float samples in the buffer.
                                            float* ptr = (float*)dataPointer.ToPointer();
                                            for (int i = 0; i < bytesAvailable / sizeof(float); i++)
                                            {
                                                *(ptr + i) *= _gain;
                                            }
                                        }
                                    }

                                    // Check if we need to resample
                                    if (_resampler != null)
                                    {
                                        // Process input to resampler
                                        ProcessResamplerInput(dataPointer, bytesAvailable, flags, qpcPosition);

                                        // Process output from resampler
                                        int bytesWritten = ProcessResamplerOutput();

                                        // Audio capture was successful, so bump the capture buffer pointer.
                                        _bytesCaptured += bytesWritten;
                                    }
                                    else
                                    {
                                        // Invoke the callback directly to handle the captured samples
                                        if (_dataAvailableCallback != null)
                                        {
                                            if (qpcPosition > lastQpcPosition)
                                            {
                                                _dataAvailableCallback(dataPointer, bytesAvailable, qpcPosition);
                                                lastQpcPosition = qpcPosition;

                                                _bytesCaptured += bytesAvailable;
                                            }
                                            else
                                            {
                                                Console.WriteLine("QPC is less than last {0}", qpcPosition - lastQpcPosition);
                                            }
                                        }
                                    }
                                }

                                _captureClient.ReleaseBuffer(framesAvailable);
                            }
                        }
                    }

                    break;
            }
        }

        if (mmcssHandle != 0)
        {
            NativeMethods.AvRevertMmThreadCharacteristics(mmcssHandle);
        }
    }

    /// <summary>
    /// Take audio data captured from WASAPI and feed it as input to audio resampler.
    /// </summary>
    /// <param name="bufferPtr">
    /// [in] Buffer holding audio data from WASAPI.
    /// </param>
    /// <param name="bufferSize">
    /// [in] Number of bytes available in pBuffer.
    /// </param>
    /// <param name="flags">
    /// [in] Flags returned from WASAPI capture.
    /// </param>
    /// <param name="qpcPosition">
    /// [in] The value of the performance counter in 100-nanosecond ticks at the time
    /// the first audio frame in pBuffer was recorded.
    /// </param>
    private void ProcessResamplerInput(IntPtr bufferPtr, int bufferSize, int flags, long qpcPosition)
    {

        _inputBuffer.Lock(out nint ptrLocked, out int maxLength, out _);
        int dataToCopy = Math.Min(bufferSize, maxLength);

        // Copy data from the audio engine buffer to the output buffer.
        unsafe
        {
            Buffer.MemoryCopy(bufferPtr.ToPointer(), ptrLocked.ToPointer(), maxLength, dataToCopy);
        }

        // Set the sample timestamp and duration (use LL suffix to prevent INT32 overflow!)
        _inputSample.SetSampleTime(qpcPosition);
        _inputSample.SetSampleDuration(10000000L * dataToCopy / _mixFormat.AvgBytesPerSec);

        _inputBuffer.SetCurrentLength(dataToCopy);
        _resampler.ProcessInput(0, _inputSample, 0);

        _inputBuffer.Unlock();
    }

    /// <summary>
    /// Get data output from audio resampler and raises a callback.
    /// </summary>
    /// <returns>Number of bytes captured.</returns>
    private int ProcessResamplerOutput()
    {
        MFTOutputDataBuffer outBuffer;
        int lockedLength = 0;

        outBuffer.StreamID = 0;
        outBuffer.Sample = _outputSample;
        outBuffer.Status = 0;
        outBuffer.Events = null;

        int hr = _resampler.ProcessOutput(0, 1, ref outBuffer, out int outStatus);
        if (hr == 0)
        {
            _outputBuffer.Lock(out nint ptrLocked, out _, out _);

            lockedLength = _outputBuffer.GetCurrentLength();

            hr = _outputSample.GetSampleTime(out long sampleTime);
            if (hr < 0)
            {
                // Use zero to indicate that timestamp was not available
                sampleTime = 0;
            }

            // Raise the callback to handle the captured samples
            _dataAvailableCallback?.Invoke(ptrLocked, lockedLength, sampleTime);

            _outputBuffer.Unlock();
        }

        return lockedLength;
    }

    /// <summary>
    /// Initialize WASAPI in timer driven mode, and retrieve a capture client for the transport.
    /// </summary>
    private void InitializeAudioEngine()
    {
        AudioClientStreamFlags streamFlags = AudioClientStreamFlags.NoPersist;

        if (_isEventDriven)
        {
            streamFlags |= AudioClientStreamFlags.EventCallback;
            _audioAvailableEvent = new AutoResetEvent(false);
        }
        else
        {
            // ensure buffer is at least twice the latency (only in pull mode)
            if (_engineBufferInMs < 2 * _engineLatencyInMs)
            {
                _engineBufferInMs = 2 * _engineLatencyInMs;
            }
        }

        IntPtr mixFormatPtr = WaveFormat.MarshalToPtr(_mixFormat);
        _audioClient.Initialize(AudioClientShareMode.Shared, streamFlags, _engineBufferInMs * 10000, 0, mixFormatPtr, Guid.Empty);
        Marshal.FreeHGlobal(mixFormatPtr);

        if (_isEventDriven)
        {
            _audioClient.SetEventHandle(_audioAvailableEvent.SafeWaitHandle.DangerousGetHandle());
        }

        // get the actual audio engine buffer size
        int bufferFrames = _audioClient.GetBufferSize();
        _engineBufferInMs = (int)(bufferFrames * 1000L / _mixFormat.SamplesPerSec);

        object obj = _audioClient.GetService(new Guid(Guids.IAudioCaptureClientIIDString));
        _captureClient = (IAudioCaptureClient)obj;
    }

    /// <summary>
    /// Retrieve the format we'll use to capture samples.
    /// We use the Mix format since we're capturing in shared mode.
    /// </summary>
    private void LoadFormat()
    {
        IntPtr mixFormatPtr = _audioClient.GetMixFormat();
        _mixFormat = WaveFormat.MarshalFromPtr(mixFormatPtr);
        Marshal.FreeCoTaskMem(mixFormatPtr);
        _mixFrameSize = _mixFormat.BitsPerSample / 8 * _mixFormat.Channels;
    }
}
