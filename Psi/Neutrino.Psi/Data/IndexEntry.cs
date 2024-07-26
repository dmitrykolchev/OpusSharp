// <copyright file="IndexEntry.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Runtime.InteropServices;
using Neutrino.Psi.Common;


namespace Neutrino.Psi.Data;

/// <summary>
/// Structure describing a position in a data file.
/// </summary>
/// <remarks>
/// This structure is used in two places: the index file and the large data file.
/// To facilitate seeking, each data file is accompanied by an index file containing records of this type.
/// Each record indicates the largest time and originating time values seen up to the specified position.
/// The position is a composite value, consisting of the extent and the relative position within the extent.
/// These records allow seeking close to (but guaranteed before) a given time.
/// Reading from the position provided by the index entry guarantees that all the messages with the
/// time specified by the index entry will be read.
///
/// To enable efficient reading of streams, the Store breaks streams in two categories: small and large.
/// When writing large messages, an index entry is written into the main data file,
/// pointing to a location in the large data file where the actual message resides.
/// </remarks>
[StructLayout(LayoutKind.Explicit)]
public struct IndexEntry
{
    /// <summary>
    /// The id of the extent this index entry refers to.
    /// A negative extentId indicates an entry in the large file for \psi stores.
    /// </summary>
    [FieldOffset(0)]
    public int ExtentId;

    /// <summary>
    /// The position within the extent to which this index entry points.
    /// </summary>
    [FieldOffset(4)]
    public int Position;

    /// <summary>
    /// The largest time value seen up to the position specified by this entry.
    /// </summary>
    [FieldOffset(8)]
    public DateTime CreationTime;

    /// <summary>
    /// The largest originating time value seen up to the position specified by this entry.
    /// </summary>
    [FieldOffset(16)]
    public DateTime OriginatingTime;

    /// <summary>
    /// Initializes a new instance of the <see cref="IndexEntry"/> struct.
    /// </summary>
    /// <param name="creationTIme">The largest creation time value seen up to the position specified by this entry.</param>
    /// <param name="originatingTime">The largest originating time value seen up to the position specified by this entry.</param>
    /// <param name="extentId">The id of the extent this index entry refers to.</param>
    /// <param name="position">The position within the extent to which this index entry points.</param>
    public IndexEntry(DateTime creationTIme, DateTime originatingTime, int extentId, int position)
    {
        CreationTime = creationTIme;
        OriginatingTime = originatingTime;
        ExtentId = extentId;
        Position = position;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IndexEntry"/> struct.
    /// </summary>
    /// <param name="envelope">Envelope from which to get Time and OriginatingTime.</param>
    /// <param name="extentId">The id of the extent this index entry refers to.</param>
    /// <param name="position">The position within the extent to which this index entry points.</param>
    public IndexEntry(Envelope envelope, int extentId, int position)
        : this(envelope.CreationTime, envelope.OriginatingTime, extentId, position)
    {
        OriginatingTime = envelope.OriginatingTime;
        CreationTime = envelope.CreationTime;
    }
}
