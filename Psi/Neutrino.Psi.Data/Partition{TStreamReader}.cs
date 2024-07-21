// <copyright file="Partition{TStreamReader}.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using Microsoft.Psi.Data.Converters;
using Newtonsoft.Json;

namespace Microsoft.Psi.Data;

/// <summary>
/// Defines a base class of partitions that can be added to a session.
/// </summary>
/// <typeparam name="TStreamReader">Type of IStreamReader used to read partition.</typeparam>
[DataContract(Namespace = "http://www.microsoft.com/psi")]
public sealed class Partition<TStreamReader> : IPartition, IDisposable
    where TStreamReader : IStreamReader
{
    private static readonly IEnumerable<IStreamMetadata> emptyStreamMetadataCollection = new List<IStreamMetadata>();
    private TStreamReader streamReader;
    private string name;
    private readonly bool isStoreValid = true;

    /// <summary>
    /// Initializes a new instance of the <see cref="Partition{TStreamReader}"/> class.
    /// </summary>
    /// <param name="session">The session that this partition belongs to.</param>
    /// <param name="streamReader">Stream reader used to read partition.</param>
    /// <param name="name">The partition name.</param>
    public Partition(Session session, TStreamReader streamReader, string name)
    {
        Initialize(session, streamReader, name, streamReader.Name, streamReader.Path);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Partition{TStreamReader}"/> class.
    /// </summary>
    /// <param name="session">The session that this partition belongs to.</param>
    /// <param name="storeName">The store name of this partition.</param>
    /// <param name="storePath">The store path of this partition.</param>
    /// <param name="streamReaderTypeName">Stream reader used to read partition.</param>
    /// <param name="name">The partition name.</param>
    [JsonConstructor]
    private Partition(Session session, string storeName, string storePath, string streamReaderTypeName, string name)
    {
        TStreamReader streamReader = default;
        try
        {
            streamReader = (TStreamReader)Data.StreamReader.Create(storeName, storePath, streamReaderTypeName);
        }
        catch
        {
            // Any exception when trying to create the stream reader will mean the partition is unreadable.
            //
            // - TargetInvocationException wrapping FileNotFoundException will be thrown if catalog file is not found.
            // - TargetInvocationException wrapping InvalidOperationException will be thrown if no data files exist.
            isStoreValid = false;
        }

        Initialize(session, streamReader, name, storeName, storePath);
    }

    /// <inheritdoc />
    [DataMember]
    public string Name
    {
        get => name;
        set
        {
            if (Session != null && Session.Partitions.Any(p => p.Name == value))
            {
                // partition names must be unique
                throw new InvalidOperationException($"Session already contains a partition named {value}");
            }

            name = value;
        }
    }

    /// <inheritdoc />
    [IgnoreDataMember]
    public bool IsStoreValid => isStoreValid;

    /// <inheritdoc />
    [IgnoreDataMember]
    public TimeInterval MessageOriginatingTimeInterval { get; private set; } = TimeInterval.Empty;

    /// <inheritdoc />
    [IgnoreDataMember]
    public TimeInterval MessageCreationTimeInterval { get; private set; } = TimeInterval.Empty;

    /// <inheritdoc />
    [IgnoreDataMember]
    public TimeInterval TimeInterval { get; private set; } = TimeInterval.Empty;

    /// <inheritdoc />
    [IgnoreDataMember]
    public long? Size { get; private set; }

    /// <inheritdoc />
    [IgnoreDataMember]
    public int? StreamCount { get; private set; }

    /// <summary>
    /// Gets the data store reader for this partition.
    /// </summary>
    [IgnoreDataMember]
    public TStreamReader StreamReader
    {
        get => streamReader;
        private set
        {
            streamReader = value;
            if (streamReader != null)
            {
                // Set originating time interval from the reader metadata
                MessageOriginatingTimeInterval = streamReader.MessageOriginatingTimeInterval;
                MessageCreationTimeInterval = streamReader.MessageCreationTimeInterval;
                TimeInterval = streamReader.StreamTimeInterval;
                Size = streamReader.Size;
                StreamCount = streamReader.StreamCount;
            }
        }
    }

    /// <inheritdoc />
    [IgnoreDataMember]
    public Session Session { get; set; }

    /// <inheritdoc />
    [DataMember]
    public string StoreName { get; private set; }

    /// <inheritdoc />
    [DataMember]
    [JsonConverter(typeof(RelativePathConverter))]
    public string StorePath { get; private set; }

    /// <inheritdoc />
    [DataMember]
    public string StreamReaderTypeName { get; private set; }

    /// <inheritdoc />
    [IgnoreDataMember]
    public IEnumerable<IStreamMetadata> AvailableStreams => IsStoreValid ? StreamReader.AvailableStreams : emptyStreamMetadataCollection;

    /// <inheritdoc />
    public void Dispose()
    {
        streamReader?.Dispose();
    }

    private void Initialize(Session session, TStreamReader streamReader, string name, string storeName, string storePath)
    {
        Session = session;
        Name = name;
        StoreName = storeName;
        StorePath = storePath;

        if (streamReader != null)
        {
            StreamReaderTypeName = streamReader.GetType().AssemblyQualifiedName;
            StreamReader = streamReader;
            Name = name ?? streamReader.Name;
        }
    }
}
