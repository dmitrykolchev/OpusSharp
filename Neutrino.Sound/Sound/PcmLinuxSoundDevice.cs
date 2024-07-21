// <copyright file="PcmLinuxSoundDevice.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Xml.Serialization;
using Neutrino.Sound.Native;
using Neutrino.Sound.Native.Linux;

namespace Neutrino.Sound;

internal unsafe class PcmLinuxSoundDevice : PcmSoundDevice
{
    private readonly AlsaApiTable _api;
    private AlsaApiTable.snd_pcm_t* _handle;

    public PcmLinuxSoundDevice(PcmSoundDeviceOptions options) : base(options)
    {
        _api = AlsaApiTable.Load("libasound.so.2");
    }

    public override void Open(string name = "default")
    {
        AlsaApiTable.snd_pcm_t* handle = null;
        int count = Encoding.ASCII.GetByteCount(name);
        Span<byte> buffer = stackalloc byte[count + 1];
        Encoding.ASCII.GetBytes(name.AsSpan(), buffer);
        fixed (byte* ptr = buffer)
        fixed (AlsaApiTable.snd_pcm_t** handlePtr = &_handle)
        {
            int errorCode = _api.snd_pcm_open(handlePtr,
                ptr,
                AlsaApiTable.snd_pcm_stream_t.SND_PCM_STREAM_PLAYBACK, 0);
                //Alsa.SND_PCM_NONBLOCK);
            AlsaException.ThrowOnError(errorCode);
        }
    }

    public override void Close(bool drop)
    {
        if (_handle != null)
        {
            int errorCode;
            if (drop)
            {
                errorCode = _api.snd_pcm_drop(_handle);
            }
            else
            {
                errorCode = _api.snd_pcm_drain(_handle);
            }
            AlsaException.ThrowOnError(errorCode);
            errorCode = _api.snd_pcm_close(_handle);
            AlsaException.ThrowOnError(errorCode);
            _handle = null;
        }
    }

    public override void Play(Stream wavSream)
    {
        Span<WaveFile.WaveFileHeader> header = stackalloc WaveFile.WaveFileHeader[1];
        wavSream.ReadExactly(MemoryMarshal.AsBytes(header));
        nint hwParams = nint.Zero;
        int dir = 0;
        Initialize(header[0], ref hwParams, ref dir);
        WriteStream(wavSream, header[0], hwParams, ref dir);
    }

    public void WriteStream(Stream wavStream, WaveFile.WaveFileHeader header, nint hwParams, ref int dir)
    {
        ulong frames;
        int d;
        int errorCode = _api.snd_pcm_hw_params_get_period_size(hwParams, &frames, &d);
        AlsaException.ThrowOnError(errorCode);
        dir = d;

        ulong bufferSize = frames * header.BlockAlign;
        // In Interop, the frames is defined as ulong. But actucally, the value of bufferSize won't be too big.
        byte[] readBuffer = ArrayPool<byte>.Shared.Rent((int)bufferSize);
        try
        {
            // Jump wav header.
            //wavStream.Position = 44;

            fixed (byte* buffer = readBuffer)
            {
                while (wavStream.Read(readBuffer) != 0)
                {
                    long writtenFrames = _api.snd_pcm_writei(_handle, buffer, frames);
                    if (writtenFrames < 0)
                    {
                        AlsaException.ThrowOnError((int)writtenFrames);
                    }
                }
            }
            _api.snd_pcm_drain(_handle);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(readBuffer);
        }
    }

    public void Initialize(WaveFile.WaveFileHeader header, ref nint hwParams, ref int dir)
    {
        nint p;
        int errorCode = _api.snd_pcm_hw_params_malloc(&p);
        AlsaException.ThrowOnError(errorCode);
        hwParams = p;

        errorCode = _api.snd_pcm_hw_params_any(_handle, hwParams);
        AlsaException.ThrowOnError(errorCode);

        errorCode = _api.snd_pcm_hw_params_set_access(_handle, 
            hwParams, 
            AlsaApiTable.snd_pcm_access_t.SND_PCM_ACCESS_RW_INTERLEAVED);
        AlsaException.ThrowOnError(errorCode);

        errorCode = (header.BitsPerSample / 8) switch
        {
            1 => _api.snd_pcm_hw_params_set_format(_handle, hwParams, AlsaApiTable.snd_pcm_format_t.SND_PCM_FORMAT_U8),
            2 => _api.snd_pcm_hw_params_set_format(_handle, hwParams, AlsaApiTable.snd_pcm_format_t.SND_PCM_FORMAT_S16_LE),
            3 => _api.snd_pcm_hw_params_set_format(_handle, hwParams, AlsaApiTable.snd_pcm_format_t.SND_PCM_FORMAT_S24_LE),
            _ => throw new InvalidOperationException($"unsupported bits per sample value: {header.BitsPerSample}")
        };
        AlsaException.ThrowOnError(errorCode);

        errorCode = _api.snd_pcm_hw_params_set_channels(_handle, hwParams, header.Channels);
        AlsaException.ThrowOnError(errorCode);

        uint sampleRate = header.SampleRate;
        fixed (int* dirPtr = &dir)
        {
            errorCode = _api.snd_pcm_hw_params_set_rate_near(_handle, hwParams, &sampleRate, dirPtr);
            AlsaException.ThrowOnError(errorCode);
        }

        errorCode = _api.snd_pcm_hw_params(_handle, hwParams);
        AlsaException.ThrowOnError(errorCode);
    }
}
