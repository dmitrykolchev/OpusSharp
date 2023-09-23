// <copyright file="OpusUtils.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

namespace DykBits.Audio.Codecs.Opus;

public class OpusUtils
{
    private static uint s_rz, s_rw;

    static OpusUtils()
    {
        s_rz = s_rw = unchecked((uint)DateTime.Now.Ticks);
    }

    public static uint FastRand()
    {
        s_rz = 36969 * (s_rz & 65535) + (s_rz >> 16);
        s_rw = 18000 * (s_rw & 65535) + (s_rw >> 16);
        return (s_rz << 16) + s_rw;
    }

    public static void GenerateMusic(short[] buf)
    {
        int a1, b1, a2, b2;
        int c1, c2, d1, d2;
        int i, j;
        a1 = b1 = a2 = b2 = 0;
        c1 = c2 = d1 = d2 = 0;
        j = 0;
        /*60ms silence*/
        for (i = 0; i < 2880; i++)
        {
            buf[i * 2] = buf[i * 2 + 1] = 0;
        }
        for (i = 2880; i < buf.Length / 2; i++)
        {
            uint r;
            int v1, v2;
            v1 = v2 = (((j * ((j >> 12) ^ ((j >> 10 | j >> 12) & 26 & j >> 7))) & 128) + 128) << 15;
            r = FastRand();
            v1 += (int)(r & 0xFFFFu);
            v1 -= (int)(r >> 16);
            r = FastRand();
            v2 += (int)(r & 0xFFFFu);
            v2 -= (int)(r >> 16);
            b1 = v1 - a1 + ((b1 * 61 + 32) >> 6);
            a1 = v1;
            b2 = v2 - a2 + ((b2 * 61 + 32) >> 6);
            a2 = v2;
            c1 = (30 * (c1 + b1 + d1) + 32) >> 6;
            d1 = b1;
            c2 = (30 * (c2 + b2 + d2) + 32) >> 6;
            d2 = b2;
            v1 = (c1 + 128) >> 8;
            v2 = (c2 + 128) >> 8;
            buf[i * 2] = (short)Math.Min(short.MaxValue, Math.Max(short.MinValue, v1));
            buf[i * 2 + 1] = (short)Math.Min(short.MaxValue, Math.Max(short.MinValue, v2));
            if (i % 6 == 0)
            {
                j++;
            }
        }
    }
}
