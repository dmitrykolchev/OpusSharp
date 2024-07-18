// <copyright file="WaveFile.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System.Runtime.InteropServices;

namespace Neutrino.Sound.Native;

internal unsafe class WaveFile
{
    [StructLayout(LayoutKind.Sequential)]
    public struct WaveFileHeader
    {
        public fixed byte Riff[4];      // 'RIFF' Magic header
        public uint FileSize;           // RIFF chunk size
        public fixed byte Wave[4];      // 'WAVE' header
        public fixed byte Fmt0[4];      // 'fmt\0' header
        public uint Subchunk1Size;      // 16 - size of the fmt chunk
        public short Format;            // 1 - PCM, 6 - mulaw, 7 - alaw, 257=IBM Mu-Law, 258=IBM A-Law, 259=ADPCM
        public ushort Channels;         // Number of channels, 1 - mono, 2 - stereo
        public uint SampleRate;         // Sample rate (sampling frequency in Hz)
        public uint BytesRate;          // bytes per second
        public ushort BlockAlign;        // 2 - 16-bit mono, 4 - 16-bit stereo
        public ushort BitsPerSample;     // Number of bits per sample
        public fixed byte Data[4];      // 'data'
        public int DataSize;            // Size of data section
    }
}
