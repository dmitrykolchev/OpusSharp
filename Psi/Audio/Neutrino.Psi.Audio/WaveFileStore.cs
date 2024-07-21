﻿// <copyright file="WaveFileStore.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;

namespace Microsoft.Psi.Audio;

/// <summary>
/// Provides static methods to access WAVE file stores.
/// </summary>
public static class WaveFileStore
{
    /// <summary>
    /// Opens a WAVE file store for read and returns a <see cref="WaveFileImporter"/> instance
    /// which can be used to inspect the store and open the audio stream.
    /// </summary>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="name">The name of the application that generated the persisted files, or the root name of the files.</param>
    /// <param name="path">The directory in which the main persisted file resides or will reside, or null to open a volatile data store.</param>
    /// <param name="startTime">Starting time for streams of data..</param>
    /// <param name="audioBufferSizeMs">The size of each data buffer in milliseconds.</param>
    /// <returns>A <see cref="WaveFileImporter"/> instance.</returns>
    public static WaveFileImporter Open(Pipeline pipeline, string name, string path, DateTime startTime, int audioBufferSizeMs = WaveFileStreamReader.DefaultAudioBufferSizeMs)
    {
        return new(pipeline, name, path, startTime, audioBufferSizeMs);
    }
}
