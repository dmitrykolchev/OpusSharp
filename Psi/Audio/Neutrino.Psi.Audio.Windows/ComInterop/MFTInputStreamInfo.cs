// <copyright file="MFTInputStreamInfo.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System.Runtime.InteropServices;

namespace Microsoft.Psi.Audio.ComInterop;

/// <summary>
/// MFT_INPUT_STREAM_INFO structure (defined in Mftransform.h).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct MFTInputStreamInfo
{
    /// <summary>
    /// hnsMaxLatency.
    /// </summary>
    internal long MaxLatency;

    /// <summary>
    /// dwFlags.
    /// </summary>
    internal int Flags;

    /// <summary>
    /// cbSize.
    /// </summary>
    internal int Size;

    /// <summary>
    /// cbMaxLookahead.
    /// </summary>
    internal int MaxLookahead;

    /// <summary>
    /// cbAlignment.
    /// </summary>
    internal int Alignment;
}
