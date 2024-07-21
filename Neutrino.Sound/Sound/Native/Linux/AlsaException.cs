// <copyright file="AlsaException.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System.Diagnostics.CodeAnalysis;

namespace Neutrino.Sound.Native.Linux;

internal class AlsaException : Exception
{
    private readonly int _errorCode;

    public AlsaException(int errorCode)
    {
        _errorCode = errorCode;
    }
    public AlsaException(int errorCode, string message) : base(message)
    {
        _errorCode = errorCode;
    }

    public int ErrorCode => _errorCode;

    public static void ThrowOnError(int errorCode)
    {
        if (errorCode < 0)
        {
            Console.WriteLine($"ALSA Error code: {errorCode}");
            throw new AlsaException(errorCode);
        }
    }
}
