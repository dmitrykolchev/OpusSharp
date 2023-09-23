// <copyright file="OpusErrorCode.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DykBits.Audio.Codecs.Opus;

public enum OpusErrorCode
{
    Ok = Native.OPUS_OK,
    InvalidArguments = Native.OPUS_BAD_ARG,
    BufferToSmall = Native.OPUS_BUFFER_TOO_SMALL,
    InternalError = Native.OPUS_INTERNAL_ERROR,
    InvalidPacket = Native.OPUS_INVALID_PACKET,
    Unimplemented = Native.OPUS_UNIMPLEMENTED,
    InvalidState = Native.OPUS_INVALID_STATE,
    AllocationFailed = Native.OPUS_ALLOC_FAIL,
}
