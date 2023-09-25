// <copyright file="OpusErrorCode.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

namespace DykBits.Audio.Codecs.Opus;

public enum OpusErrorCode
{
    Ok = OpusApi.OPUS_OK,
    InvalidArguments = OpusApi.OPUS_BAD_ARG,
    BufferToSmall = OpusApi.OPUS_BUFFER_TOO_SMALL,
    InternalError = OpusApi.OPUS_INTERNAL_ERROR,
    InvalidPacket = OpusApi.OPUS_INVALID_PACKET,
    Unimplemented = OpusApi.OPUS_UNIMPLEMENTED,
    InvalidState = OpusApi.OPUS_INVALID_STATE,
    AllocationFailed = OpusApi.OPUS_ALLOC_FAIL,
}
