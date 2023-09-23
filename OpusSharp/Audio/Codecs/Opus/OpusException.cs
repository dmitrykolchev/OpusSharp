// <copyright file="OpusException.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System.Runtime.CompilerServices;

namespace DykBits.Audio.Codecs.Opus;

public class OpusException : Exception
{
    public OpusException(OpusErrorCode errorCode) : base(CodeToMessage(errorCode))
    {
        ErrorCode = errorCode;
    }

    public OpusException(OpusErrorCode errorCode, string message) : base(message)
    {
        ErrorCode = errorCode;
    }

    public OpusErrorCode ErrorCode { get; }

    private static string CodeToMessage(OpusErrorCode errorCode)
    {
        return errorCode switch
        {
            OpusErrorCode.Ok => "No error",
            OpusErrorCode.InvalidArguments => "One or more invalid/out of range arguments",
            OpusErrorCode.BufferToSmall => "Not enough bytes allocated in the buffer",
            OpusErrorCode.InternalError => "An internal error was detected",
            OpusErrorCode.InvalidPacket => "The compressed data passed is corrupted",
            OpusErrorCode.Unimplemented => "Invalid/unsupported request number",
            OpusErrorCode.InvalidState => "An encoder or decoder structure is invalid or already freed",
            OpusErrorCode.AllocationFailed => "Memory allocation has failed",
            _ => "Unexpected error code"
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void HandleError(int errorCode)
    {
        if (errorCode < (int)OpusErrorCode.Ok)
        {
            throw new OpusException((OpusErrorCode)errorCode);
        }
    }
}
