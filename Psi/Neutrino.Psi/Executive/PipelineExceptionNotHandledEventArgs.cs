// <copyright file="PipelineExceptionNotHandledEventArgs.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;

namespace Neutrino.Psi;

/// <summary>
/// Provides data for the <see cref="Pipeline.PipelineExceptionNotHandled"/> event.
/// </summary>
public class PipelineExceptionNotHandledEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PipelineExceptionNotHandledEventArgs"/> class.
    /// </summary>
    /// <param name="exception">The exception thrown by the pipeline.</param>
    internal PipelineExceptionNotHandledEventArgs(Exception exception)
    {
        Exception = exception;
    }

    /// <summary>
    /// Gets the exception thrown by the pipeline.
    /// </summary>
    public Exception Exception { get; }
}
