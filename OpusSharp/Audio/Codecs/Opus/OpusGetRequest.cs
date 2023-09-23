// <copyright file="OpusGetRequest.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

namespace DykBits.Audio.Codecs.Opus;
public enum OpusGetRequest : int
{
    Application = 4001,
    Bitrate = 4003,
    MaxBandwidth = 4005,
    Vbr = 4007,
    Bandwidth = 4009,
    Complexity = 4011,
    InbandFec = 4013,
    PacketLossPerc = 4015,
    Dtx = 4017,
    VbrConstraint = 4021,
    ForceChannels = 4023,
    Signal = 4025,
    Lookahead = 4027,
    SampleRate = 4029,
    FinalRange = 4031,
    Pitch = 4033,
    Gain = 4045,
    LsbDepth = 4037,
    LastPacketDuration = 4039,
    ExpertFrameDuration = 4041,
    PredictionDisabled = 4043,
    PhaseInversionDisabled = 4047,
    InDtx = 4049,
}
