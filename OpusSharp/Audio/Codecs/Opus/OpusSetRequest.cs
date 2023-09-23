// <copyright file="OpusSetRequest.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

namespace DykBits.Audio.Codecs.Opus;
public enum OpusSetRequest : int
{
    Application = 4000,
    Bitrate = 4002,
    MaxBandwidth = 4004,
    Vbr = 4006,
    Bandwidth = 4008,
    Complexity = 4010,
    InbandFec = 4012,
    PacketLossPerc = 4014,
    Dtx = 4016,
    VbrConstraint = 4020,
    ForceChannels = 4022,
    Signal = 4024,
    Gain = 4034,
    LsbDepth = 4036,
    ExpertFrameDuration = 4040,
    PredictionDisabled = 4042,
    PhaseInversionDisabled = 4046
}

