// <copyright file="Program.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using DykBits.Audio.Codecs.Opus;

using NAudio.Wave;

namespace OpusSharp.Sample;

internal class Program
{
    static void Main(string[] args)
    {
        short[] input = new short[48000 * 30];

        OpusUtils.GenerateMusic(input);

        using OpusEncoder encoder = OpusEncoder.Create(OpusSamplingRate.Rate48K, OpusChannels.Two, OpusApplication.VoIP);
        using OpusDecoder decoder = OpusDecoder.Create(OpusSamplingRate.Rate48K, OpusChannels.Two);

        byte[] output = new byte[4096];

        int frameSize = 480;
        short[] pcmBuffer = new short[frameSize];

        for (int index = 0; ; ++index)
        {
            if ((index + 1) * frameSize * 2 > input.Length)
            {
                break;
            }
            int length = encoder.Encode(input, index * frameSize * 2, frameSize, output);

            int decodeLength = decoder.Decode(output, length, pcmBuffer, 0, frameSize);

            Console.WriteLine($"{index * 10}ms: {frameSize} => {length} => {decodeLength}");
        }
    }
}
