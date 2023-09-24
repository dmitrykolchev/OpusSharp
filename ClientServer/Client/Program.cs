// <copyright file="Program.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using DykBits.Audio.Codecs.Opus;
using NAudio.Wave;

namespace Client;

internal class Program
{
    private static async Task Main(string[] args)
    {
        Console.WriteLine("Press enter to start...");
        Console.ReadLine();

        short[] input = new short[48000 * 60 * 2];
        WaveFileReader reader = new("music.wav");
        int read = reader.Read(MemoryMarshal.Cast<short, byte>(input.AsSpan()));

        int frameSize = 240;
        using OpusEncoder encoder = OpusEncoder.Create(OpusSamplingRate.Rate48K, OpusChannels.Two, OpusApplication.Audio);
        byte[] output = new byte[1024];
        using Socket udpSocket = new (SocketType.Dgram, ProtocolType.Udp);
        IPEndPoint serverEndPoint = new (IPAddress.Parse("192.168.175.2"), 50000);

        for (int index = 0; ; ++index)
        {
            if ((index + 1) * frameSize * 2 > input.Length)
            {
                break;
            }
            int length = encoder.Encode(input, index * frameSize * 2, frameSize, output);
            await udpSocket.SendToAsync(output.AsMemory(0, length), SocketFlags.None, serverEndPoint);
        }
    }
}
