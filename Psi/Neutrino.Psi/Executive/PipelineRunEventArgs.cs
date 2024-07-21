// <copyright file="PipelineRunEventArgs.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;

namespace Microsoft.Psi;

/// <summary>
/// Class encapsulating the event arguments provided by the <see cref="Pipeline.PipelineRun"/> event.
/// </summary>
public class PipelineRunEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PipelineRunEventArgs"/> class.
    /// </summary>
    /// <param name="startOriginatingTime">The time the pipeline started running.</param>
    internal PipelineRunEventArgs(DateTime startOriginatingTime)
    {
        StartOriginatingTime = startOriginatingTime;
    }

    /// <summary>
    /// Gets the time when the pipeline started running.
    /// </summary>
    public DateTime StartOriginatingTime { get; private set; }
}
