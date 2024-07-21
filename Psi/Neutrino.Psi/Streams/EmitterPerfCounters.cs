// <copyright file="EmitterPerfCounters.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

namespace Microsoft.Psi;

/// <summary>
/// The counters supported by all Emitters.
/// </summary>
public enum EmitterCounters
{
    /// <summary>
    /// The rate of received messages.
    /// </summary>
    MessageCount,

    /// <summary>
    /// Total latency, from beginning of the pipeline.
    /// </summary>
    MessageLatency,
}
