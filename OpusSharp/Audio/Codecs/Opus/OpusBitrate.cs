// <copyright file="OpusBitrate.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

namespace DykBits.Audio.Codecs.Opus;

public enum OpusBitrate : int
{
    /// <summary>
    /// Auto/default setting 
    /// </summary>
    Auto = -1000,
    /// <summary>
    /// Maximum bitrate
    /// </summary>
    Max = -1
}
