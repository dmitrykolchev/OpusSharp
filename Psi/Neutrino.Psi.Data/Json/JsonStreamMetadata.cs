﻿// <copyright file="JsonStreamMetadata.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using Neutrino.Psi.Common;
using Neutrino.Psi.Common.Intervals;
using Newtonsoft.Json;

namespace Neutrino.Psi.Data.Json;

/// <summary>
/// Represents metadata used in storing stream data in a JSON store.
/// </summary>
public class JsonStreamMetadata : IStreamMetadata
{
    /// <summary>
    /// Initializes a new instance of the <see cref="JsonStreamMetadata"/> class.
    /// </summary>
    public JsonStreamMetadata()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonStreamMetadata"/> class.
    /// </summary>
    /// <param name="name">The name of the stream the metadata represents.</param>
    /// <param name="id">The id of the stream the metadata represents.</param>
    /// <param name="typeName">The name of the type of data contained in the stream the metadata represents.</param>
    /// <param name="supplementalMetadataTypeName">The name of the type of supplemental metadata for the stream the metadata represents.</param>
    /// <param name="storeName">The name of the store containing the stream.</param>
    /// <param name="storePath">The path of the store containing the stream.</param>
    public JsonStreamMetadata(string name, int id, string typeName, string supplementalMetadataTypeName, string storeName, string storePath)
        : this()
    {
        Name = name;
        Id = id;
        TypeName = typeName;
        SupplementalMetadataTypeName = supplementalMetadataTypeName;
        StoreName = storeName;
        StorePath = storePath;
    }

    /// <inheritdoc />
    [JsonProperty(Order = 1)]
    public string Name { get; set; }

    /// <inheritdoc />
    [JsonProperty(Order = 2)]
    public int Id { get; set; }

    /// <inheritdoc />
    [JsonProperty(Order = 3)]
    public string TypeName { get; set; }

    /// <inheritdoc />
    [JsonProperty(Order = 4)]
    public string StoreName { get; set; }

    /// <inheritdoc />
    [JsonProperty(Order = 5)]
    public string StorePath { get; set; }

    /// <inheritdoc />
    [JsonProperty(Order = 6)]
    public DateTime FirstMessageCreationTime { get; set; }

    /// <inheritdoc />
    [JsonProperty(Order = 7)]
    public DateTime LastMessageCreationTime { get; set; }

    /// <inheritdoc />
    [JsonProperty(Order = 8)]
    public DateTime FirstMessageOriginatingTime { get; set; }

    /// <inheritdoc />
    [JsonProperty(Order = 9)]
    public DateTime LastMessageOriginatingTime { get; set; }

    /// <inheritdoc />
    [JsonProperty(Order = 10)]
    public long MessageCount { get; set; }

    /// <summary>
    /// Gets or sets the total size (bytes) of messages in the stream.
    /// </summary>
    [JsonProperty(Order = 11)]
    public long MessageSizeCumulativeSum { get; set; }

    /// <summary>
    /// Gets or sets the cumulative sum of latencies of messages in the stream.
    /// </summary>
    [JsonProperty(Order = 12)]
    public long LatencyCumulativeSum { get; set; }

    /// <inheritdoc />
    [JsonIgnore]
    public double AverageMessageSize => MessageCount > 0 ? (double)MessageSizeCumulativeSum / MessageCount : 0;

    /// <inheritdoc />
    [JsonIgnore]
    public double AverageMessageLatencyMs => MessageCount > 0 ? (double)LatencyCumulativeSum / MessageCount / TimeSpan.TicksPerMillisecond : 0;

    /// <inheritdoc />
    [JsonProperty(Order = 13)]
    public string SupplementalMetadataTypeName { get; set; }

    /// <inheritdoc />
    [JsonProperty(Order = 14)]
    public DateTime OpenedTime => DateTime.MinValue;

    /// <inheritdoc />
    [JsonProperty(Order = 15)]
    public DateTime ClosedTime => DateTime.MaxValue;

    /// <inheritdoc />
    [JsonProperty(Order = 16)]
    public bool IsClosed => false;

    /// <inheritdoc />
    public T GetSupplementalMetadata<T>()
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public void Update(Envelope envelope, int size)
    {
        if (FirstMessageOriginatingTime == default)
        {
            FirstMessageOriginatingTime = envelope.OriginatingTime;
            FirstMessageCreationTime = envelope.CreationTime;
        }

        LastMessageOriginatingTime = envelope.OriginatingTime;
        LastMessageCreationTime = envelope.CreationTime;
        MessageCount++;
        MessageSizeCumulativeSum += size;
        LatencyCumulativeSum += (envelope.CreationTime - envelope.OriginatingTime).Ticks;
    }

    /// <inheritdoc />
    public void Update(TimeInterval messagesTimeInterval, TimeInterval messagesOriginatingTimeInterval)
    {
        throw new NotImplementedException();
    }
}
