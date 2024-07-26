// <copyright file="Message.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

#pragma warning disable SA1201 // ElementsMustAppearInTheCorrectOrder
using System;
using System.Collections.Generic;


namespace Neutrino.Psi.Common;

/// <summary>
/// Static methods that simplify <see cref="Message{T}"/> creation.
/// </summary>
public static class Message
{
    /// <summary>
    /// Creates a new instance of the <see cref="Message{T}"/> struct.
    /// </summary>
    /// <typeparam name="T">The payload of the message.</typeparam>
    /// <param name="data">The data to time-stamp.</param>
    /// <param name="originatingTime">The time of the real-world event that led to the creation of this message.</param>
    /// <param name="time">The time of this message.</param>
    /// <param name="sourceId">The source id of this message.</param>
    /// <param name="sequenceId">The sequence id of this message.</param>
    /// <returns>The newly created message.</returns>
    public static Message<T> Create<T>(T data, DateTime originatingTime, DateTime time, int sourceId, int sequenceId)
    {
        return new Message<T>(data, originatingTime, time, sourceId, sequenceId);
    }

    /// <summary>
    /// Creates a new instance of the <see cref="Message{T}"/> struct.
    /// </summary>
    /// <typeparam name="T">The payload of the message.</typeparam>
    /// <param name="data">The data to time-stamp.</param>
    /// <param name="envelope">The envelope of the message.</param>
    /// <returns>The newly created message.</returns>
    public static Message<T> Create<T>(T data, Envelope envelope)
    {
        return new Message<T>(data, envelope);
    }
}

/// <summary>
/// Represents a message that can be published to a data stream.
/// </summary>
/// <typeparam name="T">The payload of the message.</typeparam>
public struct Message<T>
{
    private Envelope _envelope;
    private T _data;

    /// <summary>
    /// Initializes a new instance of the <see cref="Message{T}"/> struct.
    /// </summary>
    /// <param name="data">The data to time-stamp.</param>
    /// <param name="originatingTime">The time of the real-world event that led to the creation of this message.</param>
    /// <param name="time">The time of this message.</param>
    /// <param name="sourceId">The source id of this message.</param>
    /// <param name="sequenceId">The sequence id of this message.</param>
    public Message(T data, DateTime originatingTime, DateTime time, int sourceId, int sequenceId)
        : this(data, new Envelope(originatingTime, time, sourceId, sequenceId))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Message{T}"/> struct.
    /// </summary>
    /// <param name="data">The data to time-stamp.</param>
    /// <param name="envelope">The envelope of the message.</param>
    internal Message(T data, Envelope envelope)
    {
        _data = data;
        _envelope = envelope;
    }

    /// <summary>
    /// Gets the payload of the message.
    /// </summary>
    public T Data
    {
        get => _data;
        internal set => _data = value;
    }

    /// <summary>
    /// Gets the time when the source message was created.
    /// </summary>
    public DateTime OriginatingTime => _envelope.OriginatingTime;

    /// <summary>
    /// Gets the time when the message was created and posted.
    /// </summary>
    public DateTime CreationTime => _envelope.CreationTime;

    /// <summary>
    /// Gets the sequence id of the message in the data stream.
    /// </summary>
    public int SequenceId => _envelope.SequenceId;

    /// <summary>
    /// Gets the ID of the stream that created the message.
    /// </summary>
    public int SourceId => _envelope.SourceId;

    /// <summary>
    /// Gets the message envelope.
    /// </summary>
    public Envelope Envelope => _envelope;

    /// <summary>
    /// Determines whether two instances are equal.
    /// </summary>
    /// <param name="first">The first object to compare.</param>
    /// <param name="second">The object to compare to.</param>
    /// <returns>True if the instances are equal.</returns>
    public static bool operator ==(Message<T> first, Message<T> second)
    {
        return (first._envelope == second._envelope) && EqualityComparer<T>.Default.Equals(first._data, second.Data);
    }

    /// <summary>
    /// Determines whether two instances are equal.
    /// </summary>
    /// <param name="first">The first object to compare.</param>
    /// <param name="second">The object to compare to.</param>
    /// <returns>True if the instances are equal.</returns>
    public static bool operator !=(Message<T> first, Message<T> second)
    {
        return !(first == second);
    }

    /// <summary>
    /// Provide a string representation of this Timestamped instance.
    /// </summary>
    /// <returns>Payload preceded by originating time.</returns>
    public override string ToString()
    {
        return string.Format("T[{0}]:{1}", _envelope, Data);
    }

    /// <summary>
    /// Determines whether two instances are equal.
    /// </summary>
    /// <param name="other">The object to compare to.</param>
    /// <returns>True if the instances are equal.</returns>
    public override bool Equals(object other)
    {
        if (!(other is Message<T>))
        {
            return false;
        }

        return this == (Message<T>)other;
    }

    /// <summary>
    /// Returns a hash code for this instance, obtained by combining the hash codes of the instance fields.
    /// </summary>
    /// <returns>A hashcode.</returns>
    public override int GetHashCode()
    {
        return _envelope.GetHashCode() ^ (EqualityComparer<T>.Default.Equals(default(T), _data) ? 0 : _data.GetHashCode());
    }
}

#pragma warning restore SA1201 // ElementsMustAppearInTheCorrectOrder
