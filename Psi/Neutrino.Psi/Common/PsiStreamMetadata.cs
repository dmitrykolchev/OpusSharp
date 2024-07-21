// <copyright file="PsiStreamMetadata.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Collections.Generic;
using Microsoft.Psi.Common;
using Microsoft.Psi.Serialization;

namespace Microsoft.Psi;


/// <summary>
/// Specifies custom flags for Psi data streams.
/// </summary>
public enum StreamMetadataFlags : ushort
{
    /// <summary>
    /// Flag indicating stream is being persisted.
    /// </summary>
    NotPersisted = 0x01,

    /// <summary>
    /// Flag indicating stream has been closed.
    /// </summary>
    Closed = 0x02,

    /// <summary>
    /// Flag indicating stream is indexed.
    /// </summary>
    Indexed = 0x04,

    /// <summary>
    /// Flag indicating stream contains polymorphic types.
    /// </summary>
    Polymorphic = 0x08,
}

/// <summary>
/// Represents metadata used in storing stream data in a Psi store.
/// </summary>
public sealed class PsiStreamMetadata : Metadata, IStreamMetadata
{
    private const int LatestVersion = 2;
    private byte[] _supplementalMetadataBytes = Array.Empty<byte>();

    internal PsiStreamMetadata(
        string name,
        int id,
        string typeName,
        int version = LatestVersion,
        int serializationSystemVersion = RuntimeInfo.LatestSerializationSystemVersion,
        ushort customFlags = 0)
        : base(MetadataKind.StreamMetadata, name, id, version, serializationSystemVersion)
    {
        TypeName = typeName;
        CustomFlags = customFlags;
    }

    /// <summary>
    /// Gets the name of the type of data contained in the stream.
    /// </summary>
    public string TypeName { get; }

    /// <summary>
    /// Gets the custom flags implemented in derived types.
    /// </summary>
    public ushort CustomFlags { get; internal set; }

    /// <summary>
    /// Gets the time when the stream was opened.
    /// </summary>
    public DateTime OpenedTime { get; internal set; } = DateTime.MinValue;

    /// <summary>
    /// Gets the time when the stream was closed.
    /// </summary>
    public DateTime ClosedTime { get; internal set; } = DateTime.MaxValue;

    /// <inheritdoc />
    public string StoreName { get; internal set; }

    /// <inheritdoc />
    public string StorePath { get; internal set; }

    /// <inheritdoc />
    public DateTime FirstMessageCreationTime { get; internal set; }

    /// <inheritdoc />
    public DateTime LastMessageCreationTime { get; internal set; }

    /// <inheritdoc />
    public DateTime FirstMessageOriginatingTime { get; internal set; }

    /// <inheritdoc />
    public DateTime LastMessageOriginatingTime { get; internal set; }

    /// <inheritdoc />
    public long MessageCount { get; internal set; }

    /// <summary>
    /// Gets the total size (bytes) of messages in the stream.
    /// </summary>
    public long MessageSizeCumulativeSum { get; private set; }

    /// <summary>
    /// Gets the cumulative sum of latencies of messages in the stream.
    /// </summary>
    public long LatencyCumulativeSum { get; private set; }

    /// <inheritdoc />
    public double AverageMessageSize => MessageCount > 0 ? (double)MessageSizeCumulativeSum / MessageCount : 0;

    /// <inheritdoc />
    public double AverageMessageLatencyMs => MessageCount > 0 ? (double)LatencyCumulativeSum / MessageCount / TimeSpan.TicksPerMillisecond : 0;

    /// <summary>
    /// Gets a dictionary of runtime type names referenced in stream.
    /// </summary>
    public Dictionary<int, string> RuntimeTypes { get; internal set; }

    /// <summary>
    /// Gets a value indicating whether the stream has been closed.
    /// </summary>
    public bool IsClosed
    {
        get => GetFlag(StreamMetadataFlags.Closed);
        internal set => SetFlag(StreamMetadataFlags.Closed, value);
    }

    /// <summary>
    /// Gets a value indicating whether the stream is persisted.
    /// </summary>
    public bool IsPersisted
    {
        get => !GetFlag(StreamMetadataFlags.NotPersisted);
        internal set => SetFlag(StreamMetadataFlags.NotPersisted, !value);
    }

    /// <summary>
    /// Gets a value indicating whether the stream is indexed.
    /// </summary>
    public bool IsIndexed
    {
        get => GetFlag(StreamMetadataFlags.Indexed);
        internal set => SetFlag(StreamMetadataFlags.Indexed, value);
    }

    /// <summary>
    /// Gets a value indicating whether the stream is persisted.
    /// </summary>
    public bool IsPolymorphic
    {
        get => GetFlag(StreamMetadataFlags.Polymorphic);

        internal set
        {
            RuntimeTypes ??= new Dictionary<int, string>();
            SetFlag(StreamMetadataFlags.Polymorphic, value);
        }
    }

    /// <inheritdoc />
    public string SupplementalMetadataTypeName { get; private set; }

    /// <summary>
    /// Gets the time interval this stream was in existence (from open to close).
    /// </summary>
    public TimeInterval StreamTimeInterval => new(OpenedTime, ClosedTime);

    /// <summary>
    /// Gets the interval between the creation times of the first and last messages written to this stream.
    /// If the stream contains no messages, an empty interval is returned.
    /// </summary>
    public TimeInterval MessageCreationTimeInterval => MessageCount == 0 ? TimeInterval.Empty : new TimeInterval(FirstMessageCreationTime, LastMessageCreationTime);

    /// <summary>
    /// Gets the interval between the originating times of the first and last messages written to this stream.
    /// If the stream contains no messages, an empty interval is returned.
    /// </summary>
    public TimeInterval MessageOriginatingTimeInterval => MessageCount == 0 ? TimeInterval.Empty : new TimeInterval(FirstMessageOriginatingTime, LastMessageOriginatingTime);

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
        FirstMessageCreationTime = messagesTimeInterval.Left;
        LastMessageCreationTime = messagesTimeInterval.Right;

        FirstMessageOriginatingTime = messagesOriginatingTimeInterval.Left;
        LastMessageOriginatingTime = messagesOriginatingTimeInterval.Right;
    }

    /// <summary>
    /// Gets supplemental stream metadata.
    /// </summary>
    /// <typeparam name="T">Type of supplemental metadata.</typeparam>
    /// <param name="serializers">Known serializers.</param>
    /// <returns>Supplemental metadata.</returns>
    public T GetSupplementalMetadata<T>(KnownSerializers serializers)
    {
        if (string.IsNullOrEmpty(SupplementalMetadataTypeName))
        {
            throw new InvalidOperationException("Stream does not contain supplemental metadata.");
        }

        if (typeof(T) != TypeResolutionHelper.GetVerifiedType(SupplementalMetadataTypeName))
        {
            throw new InvalidCastException($"Supplemental metadata type mismatch ({SupplementalMetadataTypeName}).");
        }

        SerializationHandler<T> handler = serializers.GetHandler<T>();
        BufferReader reader = new BufferReader(_supplementalMetadataBytes);
        T target = default(T);
        handler.Deserialize(reader, ref target, new SerializationContext(serializers));
        return target;
    }

    /// <inheritdoc />
    public T GetSupplementalMetadata<T>()
    {
        return GetSupplementalMetadata<T>(KnownSerializers.Default);
    }

    /// <summary>
    /// Sets supplemental stream metadata.
    /// </summary>
    /// <param name="supplementalMetadataTypeName">The serialized supplemental metadata bytes.</param>
    /// <param name="supplementalMetadataBytes">The supplemental metadata type name.</param>
    internal void SetSupplementalMetadata(string supplementalMetadataTypeName, byte[] supplementalMetadataBytes)
    {
        SupplementalMetadataTypeName = supplementalMetadataTypeName;
        _supplementalMetadataBytes = new byte[supplementalMetadataBytes.Length];
        Array.Copy(supplementalMetadataBytes, _supplementalMetadataBytes, supplementalMetadataBytes.Length);
    }

    /// <summary>
    /// Update supplemental stream metadata from another stream metadata.
    /// </summary>
    /// <param name="other">Other stream metadata from which to copy supplemental metadata.</param>
    /// <returns>Updated stream metadata.</returns>
    internal PsiStreamMetadata UpdateSupplementalMetadataFrom(PsiStreamMetadata other)
    {
        SupplementalMetadataTypeName = other.SupplementalMetadataTypeName;
        _supplementalMetadataBytes = other._supplementalMetadataBytes;
        return this;
    }

    // custom deserializer with no dependency on the Serializer subsystem
    // order of fields is important for backwards compat and must be the same as the order in Serialize, don't change!
    internal new void Deserialize(BufferReader metadataBuffer)
    {
        OpenedTime = metadataBuffer.ReadDateTime();
        ClosedTime = metadataBuffer.ReadDateTime();

        if (Version >= 2)
        {
            MessageCount = metadataBuffer.ReadInt64(); // long in v2+
            MessageSizeCumulativeSum = metadataBuffer.ReadInt64(); // added in v2
            LatencyCumulativeSum = metadataBuffer.ReadInt64(); // added in v2
        }
        else
        {
            MessageCount = metadataBuffer.ReadInt32(); // < v1 int
            //// MessageSizeCumulativeSum computed below for old versions
            //// LatencyCumulativeSum computed below for old versions
        }

        FirstMessageCreationTime = metadataBuffer.ReadDateTime();
        LastMessageCreationTime = metadataBuffer.ReadDateTime();
        FirstMessageOriginatingTime = metadataBuffer.ReadDateTime();
        LastMessageOriginatingTime = metadataBuffer.ReadDateTime();
        if (Version < 2)
        {
            // AverageMessageSize/Latency migrated in v2+ to cumulative sums
            int avgMessageSize = metadataBuffer.ReadInt32();
            MessageSizeCumulativeSum = avgMessageSize * MessageCount;

            int avgLatency = metadataBuffer.ReadInt32() * 10; // convert microseconds to ticks
            LatencyCumulativeSum = avgLatency * MessageCount;
        }

        if (IsPolymorphic)
        {
            int typeCount = metadataBuffer.ReadInt32();
            RuntimeTypes ??= new Dictionary<int, string>(typeCount);
            for (int i = 0; i < typeCount; i++)
            {
                RuntimeTypes.Add(metadataBuffer.ReadInt32(), metadataBuffer.ReadString());
            }
        }

        if (Version >= 1)
        {
            // supplemental metadata added in v1
            SupplementalMetadataTypeName = metadataBuffer.ReadString();
            int len = metadataBuffer.ReadInt32();
            _supplementalMetadataBytes = new byte[len];
            metadataBuffer.Read(_supplementalMetadataBytes, len);
        }

        Version = LatestVersion; // upgrade to current version format
    }

    internal override void Serialize(BufferWriter metadataBuffer)
    {
        // Serialization follows a legacy pattern of fields, as described
        // in the comments at the top of the Metadata.Deserialize method.
        metadataBuffer.Write(Name);
        metadataBuffer.Write(Id);
        metadataBuffer.Write(TypeName);
        metadataBuffer.Write(Version);
        metadataBuffer.Write(default(string));      // this metadata field is not used by PsiStreamMetadata
        metadataBuffer.Write(SerializationSystemVersion);
        metadataBuffer.Write(CustomFlags);
        metadataBuffer.Write((ushort)Kind);

        metadataBuffer.Write(OpenedTime);
        metadataBuffer.Write(ClosedTime);
        metadataBuffer.Write(MessageCount);
        metadataBuffer.Write(MessageSizeCumulativeSum);
        metadataBuffer.Write(LatencyCumulativeSum);
        metadataBuffer.Write(FirstMessageCreationTime);
        metadataBuffer.Write(LastMessageCreationTime);
        metadataBuffer.Write(FirstMessageOriginatingTime);
        metadataBuffer.Write(LastMessageOriginatingTime);
        if (IsPolymorphic)
        {
            metadataBuffer.Write(RuntimeTypes.Count);
            foreach (var pair in RuntimeTypes)
            {
                metadataBuffer.Write(pair.Key);
                metadataBuffer.Write(pair.Value);
            }
        }

        metadataBuffer.Write(SupplementalMetadataTypeName);
        metadataBuffer.Write(_supplementalMetadataBytes.Length);
        if (_supplementalMetadataBytes.Length > 0)
        {
            metadataBuffer.Write(_supplementalMetadataBytes);
        }
    }

    private bool GetFlag(StreamMetadataFlags smflag)
    {
        ushort flag = (ushort)smflag;
        return (CustomFlags & flag) != 0;
    }

    private void SetFlag(StreamMetadataFlags smflag, bool value)
    {
        ushort flag = (ushort)smflag;
        CustomFlags = (ushort)((CustomFlags & ~flag) | (value ? flag : 0));
    }
}
