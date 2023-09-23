// <copyright file="OpusEncoder.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System.Reflection.Emit;

namespace DykBits.Audio.Codecs.Opus;

public class OpusEncoder : IDisposable
{
    private IntPtr _encoder;

    private OpusEncoder(IntPtr encoder, OpusSamplingRate samplingRate, OpusChannels channels, OpusApplication application)
    {
        _encoder = encoder;
        SamplingRate = samplingRate;
        Channels = channels;
        Application = application;
    }

    ~OpusEncoder()
    {
        Dispose(false);
    }

    public OpusChannels Channels { get; }

    public OpusSamplingRate SamplingRate { get; }

    public OpusApplication Application { get; }

    public static OpusEncoder Create(OpusSamplingRate samplingRate, OpusChannels channels, OpusApplication application)
    {
        IntPtr encoder = Native.opus_encoder_create((int)samplingRate, (int)channels, (int)application, out int error);
        if (error != (int)OpusErrorCode.Ok)
        {
            throw new OpusException((OpusErrorCode)error);
        }
        return new OpusEncoder(encoder, samplingRate, channels, application);
    }

    public unsafe int Encode(short[] pcm, int offset, int frameSize, byte[] data)
    {
        fixed (short* pcmPtr = &pcm[offset])
        fixed (byte* dataPtr = data)
        {
            int length = Native.opus_encode(_encoder, new IntPtr(pcmPtr), frameSize, new IntPtr(dataPtr), data.Length);
            if (length < 0)
            {
                throw new OpusException((OpusErrorCode)length);
            }
            return length;
        }
    }

    /// <summary>
    /// Configures the encoder's computational complexity.
    /// </summary>
    /// <param name="level">The supported range is 0-10 inclusive with 10 representing the highest complexity</param>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    /// <exception cref="OpusException"></exception>
    public void SetComplexity(int level)
    {
        if (level < 0 || level > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(level));
        }
        int errorCode = Native.opus_encoder_ctl(_encoder, Native.OPUS_SET_COMPLEXITY_REQUEST, level);
        OpusException.HandleError(errorCode);
    }
    /// <summary>
    /// Configures the bitrate in the encoder
    /// </summary>
    /// <param name="value">Auto, Max</param>
    public void SetBitrate(OpusBitrate value)
    {
        int errorCode = Native.opus_encoder_ctl(_encoder, Native.OPUS_SET_BITRATE_REQUEST, (int)value);
        OpusException.HandleError(errorCode);
    }
    /// <summary>
    /// Configures the bitrate in the encoder.
    /// </summary>
    /// <param name="value">Rates from 500 to 512000 bits per second are meaningful</param>
    public void SetBitrate(int value)
    {
        int errorCode = Native.opus_encoder_ctl(_encoder, Native.OPUS_SET_BITRATE_REQUEST, value);
        OpusException.HandleError(errorCode);
    }

    public void Close()
    {
        if (_encoder != IntPtr.Zero)
        {
            Native.opus_encoder_destroy(_encoder);
            _encoder = IntPtr.Zero;
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        Close();
        if (disposing)
        {
            GC.SuppressFinalize(this);
        }
    }

    public void Dispose()
    {
        Dispose(true);
    }
}
