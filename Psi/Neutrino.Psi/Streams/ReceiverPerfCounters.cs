// <copyright file="ReceiverPerfCounters.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

namespace Neutrino.Psi.Streams;

/// <summary>
/// The counters we support.
/// </summary>
public enum ReceiverCounters
{
    /// <summary>
    /// The rate of received messages
    /// </summary>
    Total,

    /// <summary>
    /// The rate of dropped messages
    /// </summary>
    Dropped,

    /// <summary>
    /// The rate of processed messages
    /// </summary>
    Processed,

    /// <summary>
    /// The time it took to process the message
    /// </summary>
    ProcessingTime,

    /// <summary>
    /// The delta between the time the message was posted and the time the message was received.
    /// </summary>
    IngestTime,

    /// <summary>
    /// The delta between the originating time of the message and the time the message was received.
    /// </summary>
    PipelineExclusiveDelay,

    /// <summary>
    /// The time spent by messages waiting in the delivery queue
    /// </summary>
    TimeInQueue,

    /// <summary>
    /// The time elapsed between receiving the message and completing its processing.
    /// </summary>
    ProcessingDelay,

    /// <summary>
    /// The end-to-end delay, from originating time to the time when processing completed.
    /// </summary>
    PipelineInclusiveDelay,

    /// <summary>
    /// The number of messages in the queue
    /// </summary>
    QueueSize,

    /// <summary>
    /// The maximum number of messages in the queue at any time
    /// </summary>
    MaxQueueSize,

    /// <summary>
    /// The rate of throttling requests issued due to queue full
    /// </summary>
    ThrottlingRequests,

    /// <summary>
    /// The number of messages that are still in use by the component
    /// </summary>
    OutstandingUnrecycled,

    /// <summary>
    /// The number of messages that are available for recycling
    /// </summary>
    AvailableRecycled,
}
