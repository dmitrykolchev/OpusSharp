// <copyright file="AudioDataEventArgs.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;

namespace Neutrino.Psi.Audio;

/// <summary>
/// Defines the arguments for audio data events.
/// </summary>
internal class AudioDataEventArgs : EventArgs
{
    private long _timestamp;
    private readonly IntPtr _dataPtr;
    private int _dataLength;

    /// <summary>
    /// Initializes a new instance of the <see cref="AudioDataEventArgs"/> class.
    /// </summary>
    /// <param name="timestamp">
    /// The timestamp of the first audio sample contained in data.
    /// </param>
    /// <param name="dataPtr">
    /// A pointer to the captured audio samples.
    /// </param>
    /// <param name="dataLength">
    /// The number of bytes of data available.
    /// </param>
    internal AudioDataEventArgs(long timestamp, IntPtr dataPtr, int dataLength)
    {
        _timestamp = timestamp;
        _dataPtr = dataPtr;
        _dataLength = dataLength;
    }

    /// <summary>
    /// Gets or sets the timestamp (in 100-ns ticks since system boot) of the first audio sample contained in <see cref="Psi.Data"/>.
    /// </summary>
    public long Timestamp
    {
        get => _timestamp;

        set => _timestamp = value;
    }

    /// <summary>
    /// Gets a pointer to the captured audio samples.
    /// </summary>
    public IntPtr Data => _dataPtr;

    /// <summary>
    /// Gets or sets the number of bytes of data available.
    /// </summary>
    public int Length
    {
        get => _dataLength;

        set => _dataLength = value;
    }
}
