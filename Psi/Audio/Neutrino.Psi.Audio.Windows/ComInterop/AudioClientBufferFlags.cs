// <copyright file="AudioClientBufferFlags.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;

namespace Microsoft.Psi.Audio.ComInterop;

/// <summary>
/// _AUDCLNT_BUFFERFLAGS enumeration (defined in AudioClient.h).
/// </summary>
[Flags]
internal enum AudioClientBufferFlags
{
    /// <summary>
    /// None
    /// </summary>
    None = 0,

    /// <summary>
    /// AUDCLNT_BUFFERFLAGS_DATA_DISCONTINUITY
    /// </summary>
    DataDiscontinuity = 0x1,

    /// <summary>
    /// AUDCLNT_BUFFERFLAGS_SILENT
    /// </summary>
    Silent = 0x2,

    /// <summary>
    /// AUDCLNT_BUFFERFLAGS_TIMESTAMP_ERROR
    /// </summary>
    TimestampError = 0x4,
}
