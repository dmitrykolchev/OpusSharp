// <copyright file="MFTOutputDataBuffer.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System.Runtime.InteropServices;

namespace Neutrino.Psi.Audio.ComInterop;

/// <summary>
/// MFT_OUTPUT_DATA_BUFFER structure (defined in Mftransform.h).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct MFTOutputDataBuffer
{
    /// <summary>
    /// dwStreamID.
    /// </summary>
    internal int StreamID;

    /// <summary>
    /// pSample.
    /// </summary>
    internal IMFSample Sample;

    /// <summary>
    /// dwStatus.
    /// </summary>
    internal int Status;

    /// <summary>
    /// pEvents.
    /// </summary>
    internal IMFCollection Events;
}
