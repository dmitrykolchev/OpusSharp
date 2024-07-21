// <copyright file="AudioVolumeEventArgs.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;

namespace Microsoft.Psi.Audio;

/// <summary>
/// Defines the arguments for audio volume events.
/// </summary>
internal class AudioVolumeEventArgs : EventArgs
{
    private readonly bool _muted;
    private readonly float _masterVolume;
    private readonly float[] _channelVolume;

    /// <summary>
    /// Initializes a new instance of the <see cref="AudioVolumeEventArgs"/> class.
    /// </summary>
    /// <param name="muted">A flag indicating whether the volume is muted.</param>
    /// <param name="masterVolume">The master volume level.</param>
    /// <param name="channelVolume">An array of channel volume levels.</param>
    internal AudioVolumeEventArgs(bool muted, float masterVolume, float[] channelVolume)
    {
        _muted = muted;
        _masterVolume = masterVolume;
        _channelVolume = channelVolume;
    }

    /// <summary>
    /// Gets a value indicating whether the volume is muted.
    /// </summary>
    public bool Muted => _muted;

    /// <summary>
    /// Gets the master volume level.
    /// </summary>
    public float MasterVolume => _masterVolume;

    /// <summary>
    /// Gets an array of channel volume levels.
    /// </summary>
    public float[] ChannelVolume => _channelVolume;
}
