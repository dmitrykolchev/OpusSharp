// <copyright file="WasapiRenderClient.cs">
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
/// The callback delegate that is invoked whenever more audio data is requested by the renderer.
/// </summary>
/// <param name="data">A pointer to the audio buffer.</param>
/// <param name="length">The length of the audio buffer.</param>
/// <param name="timestamp">The timestamp of the audio.</param>
/// <returns>The number of bytes that were copied into the audio buffer.</returns>
internal delegate int AudioDataRequestedCallback(IntPtr data, int length, out long timestamp);

/// <summary>
/// The WASAPI renderer class.
/// </summary>
[SupportedOSPlatform("windows")]
internal class WasapiRenderClient : IDisposable
{
    private static Guid s_audioClientIID = new(Guids.IAudioClientIIDString);

    // Core Audio Renderer member variables.
    private readonly IMMDevice _endpoint;
    private IAudioClient _audioClient;
    private IAudioRenderClient _renderClient;
    private IMFTransform _resampler;

    private AudioDataRequestedCallback _dataRequestedCallback;
    private Thread _renderThread;
    private ManualResetEvent _shutdownEvent;
    private int _engineLatencyInMs;
    private WaveFormat _mixFormat;
    private int _mixFrameSize;
    private float _gain;

    // Render buffer member variables
    private int _bufferFrameCount;
    private int _inputBufferSize;
    private IMFMediaBuffer _inputBuffer;
    private IMFSample _inputSample;
    private int _outputBufferSize;
    private IMFMediaBuffer _outputBuffer;
    private IMFSample _outputSample;
    private int _bytesRendered;

    /// <summary>
    /// Initializes a new instance of the <see cref="WasapiRenderClient"/> class.
    /// </summary>
    /// <param name="endpoint">The audio endpoint device.</param>
    public WasapiRenderClient(IMMDevice endpoint)
    {
        _endpoint = endpoint;
    }

    /// <summary>
    /// Gets the mix format of the audio renderer.
    /// </summary>
    public WaveFormat MixFormat => _mixFormat;

    /// <summary>
    /// Gets number of bytes of audio data rendered so far.
    /// </summary>
    public int BytesRendered => _bytesRendered;

    /// <summary>
    /// Disposes the <see cref="WasapiRenderClient"/> object.
    /// </summary>
    public void Dispose()
    {
        if (_renderThread != null)
        {
            _shutdownEvent.Set();
            _renderThread.Join();
            _renderThread = null;
        }

        if (_shutdownEvent != null)
        {
            _shutdownEvent.Close();
            _shutdownEvent = null;
        }

        if (_audioClient != null)
        {
            Marshal.ReleaseComObject(_audioClient);
            _audioClient = null;
        }

        if (_renderClient != null)
        {
            Marshal.ReleaseComObject(_renderClient);
            _renderClient = null;
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
    /// Initialize the renderer.
    /// </summary>
    /// <param name="engineLatency">
    /// Number of milliseconds of acceptable lag between playback of samples and live sound being produced.
    /// </param>
    /// <param name="gain">
    /// The gain to be applied to the audio before rendering.
    /// </param>
    /// <param name="inFormat">
    /// The format of the input audio samples to be rendered. If this is NULL, the current default audio
    /// format of the renderer device will be assumed.
    /// </param>
    /// <param name="callback">
    /// Callback function delegate which will supply the data to be rendered.
    /// </param>
    public void Initialize(int engineLatency, float gain, WaveFormat inFormat, AudioDataRequestedCallback callback)
    {
        // Create our shutdown event - we want a manual reset event that starts in the not-signaled state.
        _shutdownEvent = new ManualResetEvent(false);

        // Now activate an IAudioClient object on our preferred endpoint and retrieve the mix format for that endpoint.
        object obj = _endpoint.Activate(ref s_audioClientIID, ClsCtx.INPROC_SERVER, IntPtr.Zero);
        _audioClient = (IAudioClient)obj;

        // Load the MixFormat. This may differ depending on the shared mode used.
        LoadFormat();

        // Remember our configured latency
        _engineLatencyInMs = engineLatency;

        // Set the gain
        _gain = gain;

        // Check if the desired format is supported
        IntPtr inFormatPtr = WaveFormat.MarshalToPtr(inFormat);
        int hr = _audioClient.IsFormatSupported(AudioClientShareMode.Shared, inFormatPtr, out nint closestMatchPtr);

        // Free outFormatPtr to prevent leaking memory
        Marshal.FreeHGlobal(inFormatPtr);

        if (hr == 0)
        {
            // Replace _MixFormat with inFormat. Since it is supported, we will initialize
            // the audio render client with that format and render without resampling.
            _mixFormat = inFormat;
            _mixFrameSize = _mixFormat.BitsPerSample / 8 * _mixFormat.Channels;
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
        }

        _inputBufferSize = (int)(_engineLatencyInMs * inFormat.AvgBytesPerSec / 1000);
        _outputBufferSize = (int)(_engineLatencyInMs * _mixFormat.AvgBytesPerSec / 1000);

        DeviceUtil.CreateResamplerBuffer(_inputBufferSize, out _inputSample, out _inputBuffer);
        DeviceUtil.CreateResamplerBuffer(_outputBufferSize, out _outputSample, out _outputBuffer);

        // Create resampler object
        _resampler = DeviceUtil.CreateResampler(inFormat, _mixFormat);

        InitializeAudioEngine();

        // Set the callback function
        _dataRequestedCallback = callback;
    }

    /// <summary>
    ///  Start rendering audio data.
    /// </summary>
    public void Start()
    {
        _bytesRendered = 0;

        // Now create the thread which is going to drive the rendering.
        _renderThread = new Thread(DoRenderThread);

        // We're ready to go, start rendering!
        _renderThread.Start();

        _audioClient.Start();
    }

    /// <summary>
    /// Stop the renderer.
    /// </summary>
    public void Stop()
    {
        // Tell the render thread to shut down, wait for the thread to complete then clean up all the stuff we
        // allocated in Start().
        if (_shutdownEvent != null)
        {
            _shutdownEvent.Set();
        }

        _audioClient.Stop();

        if (_renderThread != null)
        {
            _renderThread.Join();
            _renderThread = null;
        }
    }

    /// <summary>
    /// Render thread - reads audio, processes it with a resampler and renders it to WASAPI.
    /// </summary>
    private void DoRenderThread()
    {
        bool stillPlaying = true;
        int mmcssHandle = 0;
        int mmcssTaskIndex = 0;

        mmcssHandle = NativeMethods.AvSetMmThreadCharacteristics("Audio", ref mmcssTaskIndex);

        while (stillPlaying)
        {
            // We want to wait for half the desired latency in milliseconds.
            // That way we'll wake up half way through the processing period to send the
            // next set of samples to the engine.
            bool waitResult = _shutdownEvent.WaitOne(_engineLatencyInMs / 2);

            if (waitResult)
            {
                // If shutdownEvent has been set, we're done and should exit the main render loop.
                stillPlaying = false;
            }
            else
            {
                // We need to send the next buffer of samples to the audio renderer.
                bool isEmpty = false;

                // Keep fetching audio in a tight loop as long as there is audio available.
                while (!isEmpty && !_shutdownEvent.WaitOne(0))
                {
                    // Process input to resampler
                    int bytesAvailable = ProcessResamplerInput();
                    if (bytesAvailable > 0)
                    {
                        // Process output from resampler
                        _bytesRendered += ProcessResamplerOutput();
                    }
                    else
                    {
                        isEmpty = true;
                        stillPlaying = !(bytesAvailable < 0);
                    }
                }
            }
        }

        if (mmcssHandle != 0)
        {
            NativeMethods.AvRevertMmThreadCharacteristics(mmcssHandle);
        }
    }

    /// <summary>
    /// Read audio data and feed it as input to audio resampler.
    /// </summary>
    /// <returns>The number of bytes read.</returns>
    private int ProcessResamplerInput()
    {

        _inputBuffer.Lock(out nint ptrLocked, out int maxLength, out _);
        int bytesRead = 0;
        long sampleTimestamp = 0;

        // Invoke the callback to fill the input buffer with more samples.
        if (_dataRequestedCallback != null)
        {
            bytesRead = _dataRequestedCallback(ptrLocked, maxLength, out sampleTimestamp);
        }

        if (bytesRead > 0)
        {
            // Process and resample the audio data
            _inputBuffer.SetCurrentLength(bytesRead);
            _resampler.ProcessInput(0, _inputSample, 0);
        }

        _inputBuffer.Unlock();

        return bytesRead;
    }

    /// <summary>
    /// Get data output from audio resampler and render it to WASAPI.
    /// </summary>
    /// <returns>The number of bytes rendered.</returns>
    private int ProcessResamplerOutput()
    {
        MFTOutputDataBuffer outBuffer;
        int totalBytesWritten = 0;

        outBuffer.StreamID = 0;
        outBuffer.Sample = _outputSample;
        outBuffer.Status = 0;
        outBuffer.Events = null;

        // Call resampler to generate resampled output audio data.
        int hr = _resampler.ProcessOutput(0, 1, ref outBuffer, out int outStatus);
        if (hr == 0)
        {
            // Grab (lock) the resampler output buffer.
            _outputBuffer.Lock(out nint ptrLocked, out _, out _);

            // How many bytes of audio data do we have?
            int lockedLength = _outputBuffer.GetCurrentLength();

            // Convert this to frames since the render client deals in frames, not bytes.
            int framesAvailable = lockedLength / _mixFrameSize;
            int framesRemaining = framesAvailable;

            // For as long as we have frames to write and we have not been told to shutdown.
            while ((framesRemaining > 0) && !_shutdownEvent.WaitOne(0))
            {
                // How many frames in the render buffer are still waiting to be processed?
                int numFramesPadding = _audioClient.GetCurrentPadding();

                // Render the smaller of all remaining output frames and the actual space in the render buffer.
                int numRenderFrames = Math.Min(_bufferFrameCount - numFramesPadding, framesRemaining);

                // numRenderFrames can be zero if the render buffer is still full, so we need
                // this check to avoid unnecessary calls to IAudioRenderClient::GetBuffer, etc.
                if (numRenderFrames > 0)
                {
                    IntPtr dataPointer = _renderClient.GetBuffer(numRenderFrames);
                    int numRenderBytes = numRenderFrames * _mixFrameSize;

                    // Copy data from the resampler output buffer to the audio engine buffer.
                    unsafe
                    {
                        // Apply gain on the raw buffer if needed, before rendering.
                        if (_gain != 1.0f)
                        {
                            // Assumes float samples in the buffer!
                            float* src = (float*)ptrLocked.ToPointer();
                            float* dest = (float*)dataPointer.ToPointer();
                            for (int i = 0; i < numRenderBytes / sizeof(float); i++)
                            {
                                *(dest + i) = *(src + i) * _gain;
                            }
                        }
                        else
                        {
                            Buffer.MemoryCopy(ptrLocked.ToPointer(), dataPointer.ToPointer(), numRenderBytes, numRenderBytes);
                        }
                    }

                    _renderClient.ReleaseBuffer(numRenderFrames, 0);

                    // Increment pLocked and decrement frames remaining
                    ptrLocked += numRenderBytes;
                    totalBytesWritten += numRenderBytes;
                    framesRemaining -= numRenderFrames;
                }
                else
                {
                    // Render buffer is full, so wait for half the latency to give it a chance to free up some space
                    _shutdownEvent.WaitOne(_engineLatencyInMs / 2);
                }
            }

            _outputBuffer.Unlock();
        }

        return totalBytesWritten;
    }

    /// <summary>
    /// Initialize WASAPI in timer driven mode, and retrieve a render client for the transport.
    /// </summary>
    private void InitializeAudioEngine()
    {
        IntPtr mixFormatPtr = WaveFormat.MarshalToPtr(_mixFormat);
        _audioClient.Initialize(AudioClientShareMode.Shared, AudioClientStreamFlags.NoPersist, _engineLatencyInMs * 10000, 0, mixFormatPtr, Guid.Empty);
        Marshal.FreeHGlobal(mixFormatPtr);

        _bufferFrameCount = _audioClient.GetBufferSize();

        object obj = _audioClient.GetService(new Guid(Guids.IAudioRenderClientIIDString));
        _renderClient = (IAudioRenderClient)obj;
    }

    /// <summary>
    /// Retrieve the format we'll use to render samples.
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
