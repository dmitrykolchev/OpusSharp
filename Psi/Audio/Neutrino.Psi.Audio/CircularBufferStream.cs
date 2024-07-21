// <copyright file="CircularBufferStream.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.IO;
using System.Threading;

namespace Neutrino.Psi.Audio;

/// <summary>
/// Provides an in-memory stream using a circular buffer.
/// </summary>
public class CircularBufferStream : Stream
{
    /// <summary>
    /// Lock object to synchronize access to the internal buffer.
    /// </summary>
    private readonly object _bufferLock = new();

    /// <summary>
    /// The internal buffer.
    /// </summary>
    private readonly byte[] _buffer;

    /// <summary>
    /// Flag to indicate that the stream has been closed.
    /// </summary>
    private bool _isClosed;

    /// <summary>
    /// The size in bytes of the internal buffer.
    /// </summary>
    private readonly long _capacity;

    /// <summary>
    /// Index of the next read location in the internal buffer.
    /// </summary>
    private long _readIndex;

    /// <summary>
    /// Index of the next write location in the internal buffer.
    /// </summary>
    private long _writeIndex;

    /// <summary>
    /// The number of bytes available in the internal buffer.
    /// </summary>
    private long _bytesAvailable;

    /// <summary>
    /// The total number of bytes written to the stream.
    /// </summary>
    private long _bytesWritten;

    /// <summary>
    /// The total number of bytes read from the stream.
    /// </summary>
    private long _bytesRead;

    /// <summary>
    /// The total number of bytes lost to buffer overruns.
    /// </summary>
    private long _bytesOverrun;

    /// <summary>
    /// Whether reads should block if no data is currently available.
    /// </summary>
    private readonly bool _blockingReads = true;

    /// <summary>
    /// Initializes a new instance of the <see cref="CircularBufferStream"/> class.
    /// </summary>
    /// <param name="capacity">The capacity of the circular buffer.</param>
    /// <param name="blockingReads">
    /// A flag indicating whether the stream blocks until data is available for reading.
    /// </param>
    public CircularBufferStream(long capacity, bool blockingReads = true)
    {
        // set the capacity of the circular buffer
        _capacity = capacity;
        _buffer = new byte[_capacity];
        _blockingReads = blockingReads;
    }

    /// <summary>
    /// Gets a value indicating whether the current stream supports reading.
    /// </summary>
    public override bool CanRead => true;

    /// <summary>
    /// Gets a value indicating whether the current stream supports writing.
    /// </summary>
    public override bool CanWrite => true;

    /// <summary>
    /// Gets a value indicating whether the current stream supports seeking.
    /// </summary>
    public override bool CanSeek => false;

    /// <summary>
    /// Gets or sets the current position within the stream.
    /// </summary>
    public override long Position
    {
        get => 0;

        set => throw new NotImplementedException();
    }

    /// <summary>
    /// Gets the length of the stream. Always returns -1 since the stream is endless.
    /// </summary>
    public override long Length => -1;

    /// <summary>
    /// Gets the number of bytes currently available for reading from the buffer.
    /// </summary>
    public long BytesAvailable => _bytesAvailable;

    /// <summary>
    /// Gets the total number of bytes written to the stream.
    /// </summary>
    public long BytesWritten => _bytesWritten;

    /// <summary>
    /// Gets the total number of bytes read from the stream.
    /// </summary>
    public long BytesRead => _bytesRead;

    /// <summary>
    /// Gets the total number of bytes lost to buffer overruns.
    /// </summary>
    public long BytesOverrun => _bytesOverrun;

    /// <summary>
    /// Reads a chunk of data from the buffer.
    /// </summary>
    /// <returns>An array of bytes containing the data.</returns>
    public byte[] Read()
    {
        lock (_bufferLock)
        {
            while ((_bytesAvailable == 0) && !_isClosed && _blockingReads)
            {
                // wait until Write() signals that bytes are available
                Monitor.Wait(_bufferLock);
            }

            if (_isClosed)
            {
                // return null if stream is closed
                return null;
            }

            byte[] buffer = new byte[_bytesAvailable];
            if (Read(buffer, 0, buffer.Length) == 0)
            {
                return null;
            }

            return buffer;
        }
    }

    /// <summary>
    /// Reads a sequence of bytes from the stream into the supplied buffer.
    /// </summary>
    /// <param name="buffer">
    /// An array of bytes. When this method returns, the buffer contains
    /// the specified byte array with the values between
    /// <paramref name="offset"/> and (<paramref name="offset"/> +
    /// <paramref name="count"/> - 1) replaced by the bytes read from the
    /// current source.
    /// </param>
    /// <param name="offset">
    /// The zero-based byte offset in <paramref name="buffer"/> at which to
    /// begin storing the data read from the current stream.
    /// </param>
    /// <param name="count">
    /// The maximum number of bytes to be read from the stream.
    /// </param>
    /// <returns>
    /// The total number of bytes read into the buffer.
    /// </returns>
    public override int Read(byte[] buffer, int offset, int count)
    {
        unsafe
        {
            fixed (byte* bufferPtr = buffer)
            {
                return Read((IntPtr)(bufferPtr + offset), buffer.Length - offset, count);
            }
        }
    }

    /// <summary>
    /// Reads a sequence of bytes from the stream to a memory location.
    /// </summary>
    /// <param name="destPtr">
    /// A pointer to the memory location to which the data will be copied.
    /// </param>
    /// <param name="destSize">
    /// The maximum number of bytes which may be copied to destPtr.
    /// </param>
    /// <param name="count">
    /// The maximum number of bytes to be read from the stream.
    /// </param>
    /// <returns>
    /// The total number of bytes read into the buffer.
    /// </returns>
    public int Read(IntPtr destPtr, int destSize, int count)
    {
        int bytesToRead = count;

        lock (_bufferLock)
        {
            while ((_bytesAvailable == 0) && !_isClosed && _blockingReads)
            {
                // wait until Write() signals that bytes are available
                Monitor.Wait(_bufferLock);
            }

            if (_isClosed)
            {
                // return 0 if stream is closed
                return 0;
            }

            // limit the number of bytes to read to what's available
            if (count > _bytesAvailable)
            {
                count = (int)_bytesAvailable;
            }

            if (_readIndex + count < _capacity)
            {
                unsafe
                {
                    // if we're not crossing the buffer edge
                    fixed (byte* srcPtr = _buffer)
                    {
                        Buffer.MemoryCopy(srcPtr + _readIndex, destPtr.ToPointer(), destSize, count);
                    }
                }

                _readIndex += count;
                _bytesAvailable -= count;
            }
            else
            {
                // if we're crossing the edge, read in two separate chunks
                int count1 = (int)(_capacity - _readIndex);
                int count2 = count - count1;
                unsafe
                {
                    fixed (byte* srcPtr = _buffer)
                    {
                        Buffer.MemoryCopy(srcPtr + _readIndex, destPtr.ToPointer(), destSize, count1);
                        Buffer.MemoryCopy(srcPtr, (byte*)destPtr.ToPointer() + count1, destSize - count1, count2);
                    }
                }

                _readIndex = count2;
                _bytesAvailable -= count;
            }

            // keep track of the total number of bytes read
            _bytesRead += count;

            // if the buffer is not full, set the not full event
            if (_bytesAvailable < _capacity)
            {
                Monitor.Pulse(_bufferLock);
            }
        }

        // return the number of bytes read
        return count;
    }

    /// <summary>
    /// Writes a sequence of bytes to the stream.
    /// </summary>
    /// <param name="buffer">
    /// An array of bytes. This method copies <paramref name="count"/>
    /// bytes from <paramref name="buffer"/> to the current stream.
    /// </param>
    /// <param name="offset">
    /// The zero-based byte offset in <paramref name="buffer"/> at which to
    /// begin copying bytes to the stream.
    /// </param>
    /// <param name="count">
    /// The number of bytes to be written to the stream.
    /// </param>
    public override void Write(byte[] buffer, int offset, int count)
    {
        WriteOverrun(buffer, offset, count);
    }

    /// <summary>
    /// Writes a sequence of bytes to the buffer without overrunning.
    /// </summary>
    /// <param name="buffer">
    /// An array of bytes. This method copies <paramref name="count"/>
    /// bytes from <paramref name="buffer"/> to the current stream.
    /// </param>
    /// <param name="offset">
    /// The zero-based byte offset in <paramref name="buffer"/> at which to
    /// begin copying bytes to the stream.
    /// </param>
    /// <param name="count">
    /// The number of bytes to be written to the stream.
    /// </param>
    /// <returns>The number of bytes that were written to the buffer.</returns>
    public int WriteNoOverrun(byte[] buffer, int offset, int count)
    {
        lock (_bufferLock)
        {
            while ((_bytesAvailable == _capacity) && !_isClosed)
            {
                // wait until Read() signals that space is available in the buffer
                Monitor.Wait(_bufferLock);
            }

            if (_isClosed)
            {
                // return 0 if stream is closed
                return 0;
            }

            if (count > (_capacity - _bytesAvailable))
            {
                count = (int)(_capacity - _bytesAvailable);
            }

            if (count > 0)
            {
                WriteOverrun(buffer, offset, count);
            }
        }

        return count;
    }

    /// <summary>
    /// Overrides the <see cref="Stream.Flush"/> method so that no action is performed.
    /// </summary>
    public override void Flush()
    {
    }

    /// <summary>
    /// Sets the position within the current stream to the specified value.
    /// </summary>
    /// <param name="offset">
    /// The new position within the stream relative to the loc parameter.
    /// </param>
    /// <param name="origin">
    /// A value of type <see cref="SeekOrigin"/>, which acts as the seek
    /// reference point.
    /// </param>
    /// <returns>
    /// The new position within the stream, calculated by combining the
    /// initial reference point and the offset.
    /// </returns>
    public override long Seek(long offset, SeekOrigin origin)
    {
        return 0;
    }

    /// <summary>
    /// Sets the length of the current stream to the specified value.
    /// </summary>
    /// <param name="value">The value at which to set the length.</param>
    public override void SetLength(long value)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Disposes of resources.
    /// </summary>
    /// <param name="disposing">Flag to indicate whether Dispose() was called.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lock (_bufferLock)
            {
                _isClosed = true;

                // signal shutdown to any waiting threads
                Monitor.PulseAll(_bufferLock);
            }
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// Writes a sequence of bytes to the stream allowing overruns.
    /// </summary>
    /// <param name="buffer">
    /// An array of bytes. This method copies <paramref name="count"/>
    /// bytes from <paramref name="buffer"/> to the current stream.
    /// </param>
    /// <param name="offset">
    /// The zero-based byte offset in <paramref name="buffer"/> at which to
    /// begin copying bytes to the stream.
    /// </param>
    /// <param name="count">
    /// The number of bytes to be written to the stream.
    /// </param>
    private void WriteOverrun(byte[] buffer, int offset, int count)
    {
        lock (_bufferLock)
        {
            // check if the requested write would overrun the buffer
            bool bufferOverrun = _bytesAvailable + count >= _capacity;

            // limit the number of bytes to read to the buffer capacity
            if (count > _capacity)
            {
                offset = offset + (count - (int)_capacity);
                count = (int)_capacity;
            }

            // check if we will cross a boundary in the write
            if (_writeIndex + count < _capacity)
            {
                // if not, simply write
                Array.Copy(buffer, offset, _buffer, _writeIndex, count);
                _writeIndex += count;
            }
            else
            {
                // o/w we're wrapping so write in two chunks
                int count1 = (int)(_capacity - _writeIndex);
                int count2 = count - count1;
                Array.Copy(buffer, offset, _buffer, _writeIndex, count1);
                Array.Copy(buffer, offset + count1, _buffer, 0, count2);
                _writeIndex = count2;
            }

            // keep track of the total number of bytes writeen
            _bytesWritten += count;

            // now if we've spilled over, move the readIndex to the same
            // location as the writeIndex as the buffer is full
            if (bufferOverrun)
            {
                _readIndex = _writeIndex;
                _bytesOverrun += _bytesAvailable + count - _capacity;
                _bytesAvailable = _capacity;
            }
            else
            {
                // o/w just increase available by count
                _bytesAvailable += count;
            }

            // if the buffer is not empty, set the not empty event
            if (_bytesAvailable > 0)
            {
                Monitor.Pulse(_bufferLock);
            }
        }
    }
}
