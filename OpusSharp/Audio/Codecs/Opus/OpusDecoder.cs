// <copyright file="OpusDecoder.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System.Reflection.Emit;

namespace DykBits.Audio.Codecs.Opus;

public class OpusDecoder : IDisposable
{
    private IntPtr _decoder;

    private OpusDecoder(IntPtr decoder, OpusSamplingRate samplingRate, OpusChannels channels)
    {
        _decoder = decoder;
        SamplingRate = samplingRate;
        Channels = channels;
    }

    ~OpusDecoder()
    {
        Dispose(false);
    }

    public OpusChannels Channels { get; }

    public OpusSamplingRate SamplingRate { get; }

    public static OpusDecoder Create(OpusSamplingRate samplingRate, OpusChannels channels)
    {
        IntPtr encoder = Native.opus_decoder_create((int)samplingRate, (int)channels, out int error);
        OpusException.HandleError(error);
        return new OpusDecoder(encoder, samplingRate, channels);
    }

    public unsafe int Decode(byte[] data, int dataLength, short[] pcm, int offset, int frameSize, bool fec = false)
    {
        fixed (short* pcmPtr = &pcm[offset])
        fixed (byte* dataPtr = data)
        {
            int length = Native.opus_decode(_decoder, new IntPtr(dataPtr), dataLength, new IntPtr(pcmPtr), frameSize, fec ? 1 : 0);
            OpusException.HandleError(length);
            return length;
        }
    }

    public void Close()
    {
        if (_decoder != IntPtr.Zero)
        {
            Native.opus_encoder_destroy(_decoder);
            _decoder = IntPtr.Zero;
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
