// <copyright file="AudioClientShareMode.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

namespace Neutrino.Psi.Audio.ComInterop;

/// <summary>
/// AudioClientShareMode enumeration (defined in AudioClient.h).
/// </summary>
internal enum AudioClientShareMode
{
    /// <summary>
    /// AUDCLNT_SHAREMODE_SHARED
    /// </summary>
    Shared,

    /// <summary>
    /// AUDCLNT_SHAREMODE_EXCLUSIVE
    /// </summary>
    Exclusive,
}
