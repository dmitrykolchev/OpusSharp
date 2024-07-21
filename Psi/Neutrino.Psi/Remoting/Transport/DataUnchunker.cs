﻿// <copyright file="DataUnchunker.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.IO;

namespace Microsoft.Psi.Remoting;

/// <summary>
/// Chunked UDP datagram receiver.
/// </summary>
/// <remarks>
/// Meant to consume UDP datagrams off the wire, having been encoded by UdpChunkSender.
/// </remarks>
public class DataUnchunker
{
    /// <summary>
    /// Function called upon receiving each set of chunks (for reporting, performance counters, testing, ...)
    /// </summary>
    private readonly Action<long> _chunkset;

    /// <summary>
    /// Function called upon abandonment of a chunk set (for reporting, performance counters, testing, ...)
    /// </summary>
    private readonly Action<long> _abandoned;

    /// <summary>
    /// Maximum size of a datagram.
    /// </summary>
    private readonly int _maxDatagramSize;

    /// <summary>
    /// Current chunk ID being assembled.
    /// </summary>
    private long _currentId = 0;

    /// <summary>
    /// Number of received chunks for the current ID.
    /// </summary>
    private ushort _numReceived = 0;

    /// <summary>
    /// Flag indicating whether the current payload is still being assembled (waiting for chunks).
    /// </summary>
    private bool _unfinished;

    /// <summary>
    /// Initializes a new instance of the <see cref="DataUnchunker" /> class.
    /// </summary>
    /// <remarks>
    /// Datagrams may be dropped or received out of order over UDP. Chunk sets (different IDs) may be interleaved.
    /// As datagrams arrive, they are assembled - even when received out of order.
    /// Interleaving causes abandonment. Each time a new ID is seen, the previous one is abandoned. This means
    /// dropped datagrams are abandoned once followed by a full set.
    /// </remarks>
    /// <param name="maxDatagramSize">Maximum size of a datagram (as used when chunking).</param>
    /// <param name="chunksetFn">Function called upon receiving each set of chunks (for reporting, performance counters, testing, ...)</param>
    /// <param name="abandonedFn">Function called upon abandonment of a chunk set (for reporting, performance counters, testing, ...)</param>
    public DataUnchunker(int maxDatagramSize, Action<long> chunksetFn, Action<long> abandonedFn)
    {
        Payload = new byte[maxDatagramSize]; // initial size (grows as needed)
        _maxDatagramSize = maxDatagramSize;
        _chunkset = chunksetFn;
        _abandoned = abandonedFn;
    }

    /// <summary>
    /// Gets buffer for assembled payload.
    /// </summary>
    public byte[] Payload { get; private set; }

    /// <summary>
    /// Gets length of payload within buffer.
    /// </summary>
    public int Length { get; private set; }

    /// <summary>
    /// Receive chunk of data.
    /// </summary>
    /// <remarks>Decode header and unpack chunk into assembled Payload buffer.</remarks>
    /// <param name="chunk">Chunk of data (likely from UdpClient).</param>
    /// <returns>Flag indicating whether full payload has been assembles (or else waiting for more chunks).</returns>
    public bool Receive(byte[] chunk)
    {
        BinaryReader reader = new(new MemoryStream(chunk));
        long id = reader.ReadInt64();
        ushort count = reader.ReadUInt16();
        ushort num = reader.ReadUInt16();

        if (!_unfinished)
        {
            _currentId = -1; // reset after previous completion
        }

        if (id != _currentId)
        {
            if (_unfinished)
            {
                _abandoned(_currentId);
            }

            _chunkset(id);
            _currentId = id;
            _numReceived = 0;
            Length = 0;
        }

        int len = chunk.Length - DataChunker.HeaderSize;
        int offset = num * (_maxDatagramSize - DataChunker.HeaderSize);
        Length = Math.Max(Length, offset + len);

        if (Length > Payload.Length)
        {
            byte[] current = Payload;
            Payload = new byte[Length];
            Array.Copy(current, Payload, current.Length);
        }

        Array.Copy(chunk, DataChunker.HeaderSize, Payload, offset, len);

        return !(_unfinished = ++_numReceived < count);
    }
}
