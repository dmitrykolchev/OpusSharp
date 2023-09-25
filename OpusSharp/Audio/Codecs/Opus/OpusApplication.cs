// <copyright file="OpusApplication.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

namespace DykBits.Audio.Codecs.Opus;
public enum OpusApplication
{
    VoIP = OpusApi.OPUS_APPLICATION_VOIP,
    Audio = OpusApi.OPUS_APPLICATION_AUDIO,
    RestrictedLowDelay = OpusApi.OPUS_APPLICATION_RESTRICTED_LOWDELAY
}
