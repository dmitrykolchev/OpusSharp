﻿// <copyright file="StreamMetadataBase.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using Neutrino.Psi.Common.Intervals;

namespace Neutrino.Psi.Common;


/// <summary>
/// Stream metadata base class.
/// </summary>
public abstract class StreamMetadataBase : IStreamMetadata
{
    /// <summary>
    /// Initializes a new instance of the <see cref="StreamMetadataBase"/> class.
    /// </summary>
    /// <param name="name">Stream name.</param>
    /// <param name="id">Stream ID.</param>
    /// <param name="typeName">Stream type name.</param>
    /// <param name="partitionName">Partition/file name.</param>
    /// <param name="partitionPath">Partition/file path.</param>
    /// <param name="first">First message time.</param>
    /// <param name="last">Last message time.</param>
    /// <param name="messageCount">Total message count.</param>
    /// <param name="averageMessageSize">Average message size (bytes).</param>
    /// <param name="averageLatencyMs">Average message latency (milliseconds).</param>
    public StreamMetadataBase(string name, int id, string typeName, string partitionName, string partitionPath, DateTime first, DateTime last, long messageCount, double averageMessageSize, double averageLatencyMs)
    {
        Name = name;
        Id = id;
        TypeName = typeName;
        StoreName = partitionName;
        StorePath = partitionPath;
        OpenedTime = FirstMessageCreationTime = FirstMessageOriginatingTime = first;
        ClosedTime = LastMessageCreationTime = LastMessageOriginatingTime = last;
        MessageCount = messageCount;
        AverageMessageSize = averageMessageSize;
        AverageMessageLatencyMs = averageLatencyMs;
    }

    /// <inheritdoc />
    public string Name { get; private set; }

    /// <inheritdoc />
    public int Id { get; private set; }

    /// <inheritdoc />
    public string TypeName { get; private set; }

    /// <inheritdoc />
    public string SupplementalMetadataTypeName => null;

    /// <inheritdoc />
    public string StoreName { get; private set; }

    /// <inheritdoc />
    public string StorePath { get; private set; }

    /// <inheritdoc />
    public DateTime OpenedTime { get; private set; }

    /// <inheritdoc />
    public DateTime ClosedTime { get; private set; }

    /// <inheritdoc />
    public bool IsClosed => true;

    /// <inheritdoc />
    public DateTime FirstMessageCreationTime { get; private set; }

    /// <inheritdoc />
    public DateTime LastMessageCreationTime { get; private set; }

    /// <inheritdoc />
    public DateTime FirstMessageOriginatingTime { get; private set; }

    /// <inheritdoc />
    public DateTime LastMessageOriginatingTime { get; private set; }

    /// <inheritdoc />
    public long MessageCount { get; private set; }

    /// <inheritdoc />
    public double AverageMessageSize { get; private set; }

    /// <inheritdoc />
    public double AverageMessageLatencyMs { get; private set; }

    /// <inheritdoc />
    public virtual T GetSupplementalMetadata<T>()
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public virtual void Update(Envelope envelope, int size)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public virtual void Update(TimeInterval messagesTimeInterval, TimeInterval messagesOriginatingTimeInterval)
    {
        throw new NotImplementedException();
    }
}
