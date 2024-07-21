// <copyright file="AudioClientStreamFlags.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;

namespace Microsoft.Psi.Audio.ComInterop;

/// <summary>
/// AUDCLNT_STREAMFLAGS_XXX Constants (defined in Audiosessiontypes.h).
/// </summary>
[Flags]
internal enum AudioClientStreamFlags : uint
{
    /// <summary>
    /// None
    /// </summary>
    None = 0,

    /// <summary>
    /// AUDCLNT_STREAMFLAGS_CROSSPROCESS
    /// </summary>
    CrossProcess = 0x00010000,

    /// <summary>
    /// AUDCLNT_STREAMFLAGS_LOOPBACK
    /// </summary>
    Loopback = 0x00020000,

    /// <summary>
    /// AUDCLNT_STREAMFLAGS_EVENTCALLBACK
    /// </summary>
    EventCallback = 0x00040000,

    /// <summary>
    /// AUDCLNT_STREAMFLAGS_NOPERSIST
    /// </summary>
    NoPersist = 0x00080000,

    /// <summary>
    /// AUDCLNT_STREAMFLAGS_RATEADJUST
    /// </summary>
    RateAdjust = 0x00100000,

    /// <summary>
    /// AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM
    /// </summary>
    AutoConvertPcm = 0x80000000,

    /// <summary>
    /// AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY
    /// </summary>
    SourceDefaultQuality = 0x08000000,
}
