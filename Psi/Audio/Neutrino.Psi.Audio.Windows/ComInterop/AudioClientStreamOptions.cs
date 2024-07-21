// <copyright file="AudioClientStreamOptions.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

namespace Microsoft.Psi.Audio.ComInterop;

/// <summary>
/// AudioClientStreamOptions enumeration (defined in AudioClient.h).
/// </summary>
internal enum AudioClientStreamOptions
{
    /// <summary>
    /// AUDCLNT_STREAMOPTIONS_NONE
    /// </summary>
    None = 0,

    /// <summary>
    /// AUDCLNT_STREAMOPTIONS_RAW
    /// </summary>
    Raw = 0x1,

    /// <summary>
    /// AUDCLNT_STREAMOPTIONS_MATCH_FORMAT
    /// </summary>
    MatchFormat = 0x2,
}
