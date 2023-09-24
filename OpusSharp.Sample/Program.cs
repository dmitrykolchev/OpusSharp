// <copyright file="Program.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using DykBits.Audio.Codecs.Opus;

using NAudio.Wave;

namespace OpusSharp.Sample;

internal class Program
{
    static unsafe void Main(string[] args)
    {
        short[] input = new short[48000 * 60 * 2];

        OpusUtils.GenerateMusic(input);

        WaveFileReader reader = new WaveFileReader("music.wav");
        fixed (short* ptr = input)
        {
            reader.Read(new Span<byte>((void*)ptr, input.Length * 2));
        }

        WaveOut waveOut = new WaveOut(WaveCallbackInfo.FunctionCallback());
        BufferedWaveProvider playBuffer = new BufferedWaveProvider(new WaveFormat(48000, 16, 2));
        playBuffer.BufferLength = 48000 * 60 * 2 * 2;
        waveOut.DeviceNumber = 0;
        waveOut.Init(playBuffer);
        waveOut.Play();

        using OpusEncoder encoder = OpusEncoder.Create(OpusSamplingRate.Rate48K, OpusChannels.Two, OpusApplication.Audio);
        //encoder.SetComplexity(0);
        using OpusDecoder decoder = OpusDecoder.Create(OpusSamplingRate.Rate48K, OpusChannels.Two);

        byte[] output = new byte[4096];

        int frameSize = 480;
        byte[] pcmBuffer = new byte[4096];

        for (int index = 0; ; ++index)
        {
            if ((index + 1) * frameSize * 2 > input.Length)
            {
                break;
            }
            int length = encoder.Encode(input, index * frameSize * 2, frameSize, output);

            int decodeLength = decoder.Decode(output, length, pcmBuffer, frameSize);
            playBuffer.AddSamples(pcmBuffer, 0, 480 * 2 * 2);
            Console.WriteLine($"{index * 10}ms: {frameSize} => {length} => {decodeLength}");
        }
        Console.WriteLine("Press ENTER to exit...");
        Console.ReadLine();
        waveOut.Dispose();
    }
}
