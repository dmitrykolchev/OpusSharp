// <copyright file="Envelope.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Runtime.InteropServices;


namespace Neutrino.Psi;

/// <summary>
/// Represents the envelope of a message published to a data stream.
/// See <see cref="Message{T}"/> for details.
/// </summary>
[StructLayout(LayoutKind.Explicit)]
public struct Envelope
{
    /// <summary>
    /// The id of the stream that generated the message.
    /// </summary>
    [FieldOffset(0)]
    public int SourceId;

    /// <summary>
    /// The sequence number of this message, unique within the stream identified by <see cref="SourceId"/>.
    /// </summary>
    [FieldOffset(4)]
    public int SequenceId;

    /// <summary>
    /// The originating time of the message, representing the time of the real-world event that led to the creation of this message.
    /// This value is used as a key when synchronizing messages across streams.
    /// This value must be propagated with any message derived from this message.
    /// </summary>
    [FieldOffset(8)]
    public DateTime OriginatingTime;

    /// <summary>
    /// The message creation time.
    /// </summary>
    [FieldOffset(16)]
    public DateTime CreationTime;

    /// <summary>
    /// Initializes a new instance of the <see cref="Envelope"/> struct.
    /// </summary>
    /// <param name="originatingTime">The <see cref="OriginatingTime"/> of this message.</param>
    /// <param name="creationTime">The <see cref="CreationTime"/> of the message.</param>
    /// <param name="sourceId">The <see cref="SourceId"/> of the message.</param>
    /// <param name="sequenceId">The unique <see cref="SequenceId"/> of the message.</param>
    public Envelope(DateTime originatingTime, DateTime creationTime, int sourceId, int sequenceId)
    {
        SourceId = sourceId;
        SequenceId = sequenceId;
        OriginatingTime = originatingTime;
        CreationTime = creationTime;
    }

    /// <summary>
    /// Determines whether two instances are equal.
    /// </summary>
    /// <param name="first">The first object to compare.</param>
    /// <param name="second">The object to compare to.</param>
    /// <returns>True if the instances are equal.</returns>
    public static bool operator ==(Envelope first, Envelope second)
    {
        return
            first.SourceId == second.SourceId &&
            first.SequenceId == second.SequenceId &&
            first.CreationTime == second.CreationTime &&
            first.OriginatingTime == second.OriginatingTime;
    }

    /// <summary>
    /// Determines whether two instances are equal.
    /// </summary>
    /// <param name="first">The first object to compare.</param>
    /// <param name="second">The object to compare to.</param>
    /// <returns>True if the instances are equal.</returns>
    public static bool operator !=(Envelope first, Envelope second)
    {
        return !(first == second);
    }

    /// <summary>
    /// Provide a string representation of this Timestamped instance.
    /// </summary>
    /// <returns>Payload preceded by originating time.</returns>
    public override string ToString()
    {
        return string.Format("{0}.{1} ({2})", SourceId, SequenceId, OriginatingTime);
    }

    /// <summary>
    /// Determines whether two instances are equal.
    /// </summary>
    /// <param name="other">The object to compare to.</param>
    /// <returns>True if the instances are equal.</returns>
    public override bool Equals(object other)
    {
        if (other is not Envelope)
        {
            return false;
        }

        return this == (Envelope)other;
    }

    /// <summary>
    /// Returns a hash code for this instance, obtained by combining the hash codes of the instance fields.
    /// </summary>
    /// <returns>A hashcode.</returns>
    public override int GetHashCode()
    {
        return SourceId ^ SequenceId ^ CreationTime.GetHashCode() ^ OriginatingTime.GetHashCode();
    }
}
