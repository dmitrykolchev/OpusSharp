// <copyright file="AudioServer.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using DykBits.Audio.Codecs.Opus;
using NAudio.Wave;

namespace Server;

public class AudioServer
{
    private static readonly IPEndPoint Any = new (IPAddress.Any, 0);

    private Socket? _socket;
    private OpusDecoder? _decoder;
    private WaveOut? _waveOut;
    private BufferedWaveProvider? _playBuffer;

    public AudioServer()
    {
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_socket != null)
        {
            throw new InvalidOperationException("already started");
        }
        Initialize();

        byte[] buffer = GC.AllocateUninitializedArray<byte>(4096);
        Memory<byte> memory = buffer.AsMemory();
        byte[] pcmBuffer = GC.AllocateUninitializedArray<byte>(4096);
        int frameSize = 240;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                ValueTask<SocketReceiveFromResult> task = _socket!.ReceiveFromAsync(memory, SocketFlags.None, Any, cancellationToken);
                SocketReceiveFromResult result = task.IsCompleted ? task.Result : await task;
                Console.WriteLine($"{result.RemoteEndPoint}, {result.ReceivedBytes}");
                if (result.ReceivedBytes > 0)
                {
                    int length = Decode(memory, result.ReceivedBytes, pcmBuffer, frameSize);
                    _playBuffer!.AddSamples(pcmBuffer, 0, frameSize * 2 * 2);
                }
            }
            catch (SocketException ex)
            {
                Console.WriteLine(ex.ToString());
                break;
            }
        }
    }

    private int Decode(ReadOnlyMemory<byte> data, int length, byte[] pcm, int frameSize)
    {
        return _decoder!.Decode(data, length, MemoryMarshal.Cast<byte, short>(pcm.AsSpan()), 0, frameSize);
    }

    private void Initialize()
    {
        _socket = new(SocketType.Dgram, ProtocolType.Udp);

        _socket.Bind(new IPEndPoint(IPAddress.Any, 50000));
        Console.WriteLine($"Listening on 0.0.0.0:50000");

        _decoder = OpusDecoder.Create(OpusSamplingRate.Rate48K, OpusChannels.Two);

        _playBuffer = new(new WaveFormat(48000, 16, 2))
        {
            BufferLength = 48000 * 60 * 2 * 2
        };
        _waveOut = new(WaveCallbackInfo.FunctionCallback())
        {
            DeviceNumber = 0
        };
        _waveOut.Init(_playBuffer);
        _waveOut.Play();
    }
}
