// <copyright file="Program.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>


//using NAudio.Wave;
using Neutrino.Psi;
using Neutrino.Psi.Audio;
using Neutrino.Sound;

namespace OpusSharp.Sample;

internal class Program
{
    private static unsafe void Main(string[] args)
    {
        using (Pipeline pipeline = Pipeline.Create())
        {
            WaveFileAudioSource source = new(pipeline, "./music.wav");
            AudioPlayer player = new(pipeline, new AudioPlayerConfiguration
            {
                DeviceName = "default",
                Format = WaveFormat.Create(WaveFormatTag.WAVE_FORMAT_PCM,
                48000, 16, 2, 4, 192000)
            });
            source.PipeTo(player);
            pipeline.Run();
        }

        using PcmSoundDevice soundDevice = PcmSoundDevice.Create(PcmSoundDeviceOptions.Default);
        soundDevice.Open("default");
        soundDevice.Play("./music.wav");
        soundDevice.Close(false);

        //short[] input = new short[48000 * 60 * 2];

        //OpusUtils.GenerateMusic(input);

        //WaveFileReader reader = new WaveFileReader("music.wav");
        //fixed (short* ptr = input)
        //{
        //    reader.Read(new Span<byte>((void*)ptr, input.Length * 2));
        //}

        //WaveOut waveOut = new WaveOut(WaveCallbackInfo.FunctionCallback());
        //BufferedWaveProvider playBuffer = new BufferedWaveProvider(new WaveFormat(48000, 16, 2));
        //playBuffer.BufferLength = 48000 * 60 * 2 * 2;
        //waveOut.DeviceNumber = 0;
        //waveOut.Init(playBuffer);
        //waveOut.Play();

        //using OpusEncoder encoder = OpusEncoder.Create(OpusSamplingRate.Rate48K, OpusChannels.Two, OpusApplication.Audio);
        ////encoder.SetComplexity(0);
        //using OpusDecoder decoder = OpusDecoder.Create(OpusSamplingRate.Rate48K, OpusChannels.Two);

        //byte[] output = new byte[4096];

        //int frameSize = 480;
        //byte[] pcmBuffer = new byte[4096];

        //for (int index = 0; ; ++index)
        //{
        //    if ((index + 1) * frameSize * 2 > input.Length)
        //    {
        //        break;
        //    }
        //    int length = encoder.Encode(input, index * frameSize * 2, frameSize, output);

        //    int decodeLength = decoder.Decode(output, length, pcmBuffer, frameSize);
        //    playBuffer.AddSamples(pcmBuffer, 0, 480 * 2 * 2);
        //    Console.WriteLine($"{index * 10}ms: {frameSize} => {length} => {decodeLength}");
        //}
        //Console.WriteLine("Press ENTER to exit...");
        //Console.ReadLine();
        //waveOut.Dispose();
    }
}
