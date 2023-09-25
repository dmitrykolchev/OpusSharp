// <copyright file="OpusApiTable.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

namespace DykBits.Audio.Codecs.Opus;

internal unsafe partial struct OpusApiTable
{
    internal delegate* unmanaged[Cdecl]<int, int> opus_encoder_get_size;

    internal delegate* unmanaged[Cdecl]<int, int> opus_decoder_get_size;

    internal delegate* unmanaged[Cdecl]<int, int, int, int*, IntPtr> opus_encoder_create;

    internal delegate* unmanaged[Cdecl]<int, int, int*, IntPtr> opus_decoder_create;

    internal delegate* unmanaged[Cdecl]<IntPtr, int, int, int, int> opus_encoder_init;

    internal delegate* unmanaged[Cdecl]<IntPtr, IntPtr, int, IntPtr, int, int> opus_encode;

    internal delegate* unmanaged[Cdecl]<IntPtr, IntPtr, int, IntPtr, int, int, int> opus_decode;

    internal delegate* unmanaged[Cdecl]<IntPtr, void> opus_encoder_destroy;

    internal delegate* unmanaged[Cdecl]<IntPtr, void> opus_decoder_destroy;

    internal delegate* unmanaged[Cdecl]<IntPtr, int, IntPtr, int> opus_encoder_ctl;
}
