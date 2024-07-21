// <copyright file="MFResampler.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Neutrino.Psi.Audio.ComInterop;

namespace Neutrino.Psi.Audio;

/// <summary>
/// The Media Foundation audio resampler class.
/// </summary>
[SupportedOSPlatform("windows")]
internal class MFResampler : IDisposable
{
    private int _bufferLengthInMs;
    private int _inputBytesPerSecond;
    private IMFTransform _resampler;
    private int _inputBufferSize;
    private IMFMediaBuffer _inputBuffer;
    private IMFSample _inputSample;
    private int _outputBufferSize;
    private IMFMediaBuffer _outputBuffer;
    private IMFSample _outputSample;
    private AudioDataAvailableCallback _dataAvailableCallback;

    /// <summary>
    /// Initializes a new instance of the <see cref="MFResampler"/> class.
    /// </summary>
    public MFResampler()
    {
    }

    /// <summary>
    /// Disposes the <see cref="MFResampler"/> object.
    /// </summary>
    public void Dispose()
    {
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
    /// Initialize the resampler.
    /// </summary>
    /// <param name="targetLatencyInMs">
    /// The target maximum number of milliseconds of acceptable lag between
    /// input and resampled output audio samples.
    /// </param>
    /// <param name="inFormat">
    /// The input format of the audio to be resampled.
    /// </param>
    /// <param name="outFormat">
    /// The output format of the resampled audio.
    /// </param>
    /// <param name="callback">
    /// Callback delegate which will receive the resampled data.
    /// </param>
    public void Initialize(int targetLatencyInMs, WaveFormat inFormat, WaveFormat outFormat, AudioDataAvailableCallback callback)
    {
        // Buffer sizes are calculated from the target latency.
        _bufferLengthInMs = targetLatencyInMs;
        _inputBytesPerSecond = (int)inFormat.AvgBytesPerSec;
        _inputBufferSize = (int)(_bufferLengthInMs * inFormat.AvgBytesPerSec / 1000);
        _outputBufferSize = (int)(_bufferLengthInMs * outFormat.AvgBytesPerSec / 1000);

        Exception taskException = null;

        // Activate native Media Foundation COM objects on a thread-pool thread to ensure that they are in an MTA
        Task.Run(() =>
        {
            try
            {
                DeviceUtil.CreateResamplerBuffer(_inputBufferSize, out _inputSample, out _inputBuffer);
                DeviceUtil.CreateResamplerBuffer(_outputBufferSize, out _outputSample, out _outputBuffer);

                // Create resampler object
                _resampler = DeviceUtil.CreateResampler(inFormat, outFormat);
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

        // Set the callback function
        _dataAvailableCallback = callback;
    }

    /// <summary>
    /// Resamples audio data.
    /// </summary>
    /// <param name="dataPtr">
    /// Pointer to a buffer containing the audio data to be resampled.
    /// </param>
    /// <param name="length">
    /// The number of bytes in dataPtr.
    /// </param>
    /// <param name="timestamp">
    /// The timestamp in 100-ns ticks of the first sample in pbData.
    /// </param>
    /// <returns>
    /// The number of bytes in dataPtr that were processed.
    /// </returns>
    public int Resample(IntPtr dataPtr, int length, long timestamp)
    {
        int resampledBytes = 0;

        while (length > 0)
        {

            _inputBuffer.Lock(out nint ptrLocked, out int maxLength, out _);

            // Copy the next chunk into the input buffer
            int bytesToWrite = Math.Min(maxLength, length);
            unsafe
            {
                Buffer.MemoryCopy(dataPtr.ToPointer(), ptrLocked.ToPointer(), maxLength, bytesToWrite);
            }

            // Count the number of bytes processed
            resampledBytes += bytesToWrite;

            // Set the sample timestamp and duration
            long sampleDuration = 10000000L * bytesToWrite / _inputBytesPerSecond;
            _inputSample.SetSampleTime(timestamp);
            _inputSample.SetSampleDuration(sampleDuration);

            // Process and resample the audio data
            _inputBuffer.SetCurrentLength(bytesToWrite);
            int hr = _resampler.ProcessInput(0, _inputSample, 0);

            _inputBuffer.Unlock();

            if (hr == 0)
            {
                // Process output from resampler
                ProcessResamplerOutput();
            }

            // Advance the data pointer and timestamp
            dataPtr += bytesToWrite;
            length -= bytesToWrite;
            timestamp += sampleDuration;
        }

        return resampledBytes;
    }

    /// <summary>
    /// Get data output from audio resampler and raises a callback.
    /// </summary>
    /// <returns>The number of bytes of resampled data.</returns>
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
}
