// <copyright file="BufferReader.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.IO;
using System.Text;


namespace Microsoft.Psi.Common;

/// <summary>
/// Auto-resizable buffer (similar to MemoryStream) but with methods to read arrays of any simple value type, not just byte[].
/// This class is typically used in conjunction with <see cref="BufferWriter"/>.
/// </summary>
public class BufferReader
{
    private int _currentPosition;
    private int _length;
    private byte[] _buffer;

    /// <summary>
    /// Initializes a new instance of the <see cref="BufferReader"/> class.
    /// </summary>
    public BufferReader()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BufferReader"/> class using the specified buffer.
    /// </summary>
    /// <param name="buffer">The buffer to read from.</param>
    public BufferReader(byte[] buffer)
        : this(buffer, buffer.Length)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BufferReader"/> class using the specified buffer.
    /// </summary>
    /// <param name="buffer">The buffer to read from.</param>
    /// <param name="length">The count of valid bytes in the buffer.</param>
    public BufferReader(byte[] buffer, int length)
    {
        _buffer = buffer;
        _length = length;
        _currentPosition = 0;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BufferReader"/> class using the buffer already filled in by the specified <see cref="BufferWriter"/>.
    /// Note that the underlying buffer is shared, and the writer could corrupt it.
    /// </summary>
    /// <param name="writer">The writer to share the buffer with.</param>
    public BufferReader(BufferWriter writer)
    {
        _buffer = writer.Buffer;
        _length = writer.Position;
        _currentPosition = 0;
    }

    /// <summary>
    /// Gets the current position of the reader.
    /// </summary>
    public int Position => _currentPosition;

    /// <summary>
    /// Gets the number of valid bytes in the underlying buffer.
    /// </summary>
    public int Length => _length;

    /// <summary>
    /// Gets the number of unread bytes.
    /// </summary>
    public int RemainingLength => _length - _currentPosition;

    /// <summary>
    /// Gets the underlying buffer.
    /// </summary>
    public byte[] Buffer => _buffer;

    /// <summary>
    /// Resets the position of the reader to the beginning of the buffer.
    /// </summary>
    public void Reset()
    {
        _currentPosition = 0;
    }

    /// <summary>
    /// Moves the current position to the specified place in the underlying buffer.
    /// </summary>
    /// <param name="position">The position to move the to.</param>
    public void Seek(int position)
    {
        if (position < 0 || position > _length)
        {
            throw new ArgumentException("The specified position is outside the valid range.");
        }

        _currentPosition = position;
    }

    /// <summary>
    /// Resets the reader to the beginning and resizes the buffer as needed. Note that any data in the buffer is lost.
    /// </summary>
    /// <param name="length">The new buffer length.</param>
    public void Reset(int length)
    {
        if (_buffer == null || _buffer.Length < length)
        {
            _buffer = new byte[length];
        }

        _length = length;
        _currentPosition = 0;
    }

    /// <summary>
    /// Copies the specified number of bytes from the underlying buffer to the specified memory address.
    /// </summary>
    /// <param name="target">The target memory address.</param>
    /// <param name="lengthInBytes">The number of bytes to copy.</param>
    public unsafe void Read(void* target, int lengthInBytes)
    {
        int start = MoveCurrentPosition(lengthInBytes);

        fixed (byte* buf = _buffer)
        {
            // more efficient then Array.Copy or cpblk IL instruction because it handles small sizes explicitly
            // http://referencesource.microsoft.com/#mscorlib/system/buffer.cs,c2ca91c0d34a8f86
            System.Buffer.MemoryCopy(buf + start, target, lengthInBytes, lengthInBytes);
        }
    }

    /// <summary>
    /// Copies the specified number of bytes from the underlying buffer into the specified array.
    /// </summary>
    /// <param name="target">The array to copy to.</param>
    /// <param name="count">The number of bytes to copy.</param>
    public void Read(byte[] target, int count)
    {
        unsafe
        {
            fixed (byte* t = target)
            {
                Read(t, count);
            }
        }
    }

    /// <summary>
    /// Copies the specified number of elements of type Double from the underlying buffer into the specified array.
    /// </summary>
    /// <param name="target">The array to copy to.</param>
    /// <param name="count">The number of elements to copy.</param>
    public void Read(double[] target, int count)
    {
        unsafe
        {
            fixed (double* t = target)
            {
                Read(t, count * sizeof(double));
            }
        }
    }

    /// <summary>
    /// Copies the specified number of elements of type Single from the underlying buffer into the specified array.
    /// </summary>
    /// <param name="target">The array to copy to.</param>
    /// <param name="count">The number of elements to copy.</param>
    public void Read(float[] target, int count)
    {
        unsafe
        {
            fixed (float* t = target)
            {
                Read(t, count * sizeof(float));
            }
        }
    }

    /// <summary>
    /// Copies the specified number of elements of type UInt16 from the underlying buffer into the specified array.
    /// </summary>
    /// <param name="target">The array to copy to.</param>
    /// <param name="count">The number of elements to copy.</param>
    public void Read(ushort[] target, int count)
    {
        unsafe
        {
            fixed (ushort* t = target)
            {
                Read(t, count * sizeof(ushort));
            }
        }
    }

    /// <summary>
    /// Copies the specified number of elements of type Int16 from the underlying buffer into the specified array.
    /// </summary>
    /// <param name="target">The array to copy to.</param>
    /// <param name="count">The number of elements to copy.</param>
    public void Read(short[] target, int count)
    {
        unsafe
        {
            fixed (short* t = target)
            {
                Read(t, count * sizeof(short));
            }
        }
    }

    /// <summary>
    /// Copies the specified number of elements of type Int32 from the underlying buffer into the specified array.
    /// </summary>
    /// <param name="target">The array to copy to.</param>
    /// <param name="count">The number of elements to copy.</param>
    public void Read(int[] target, int count)
    {
        unsafe
        {
            fixed (int* t = target)
            {
                Read(t, count * sizeof(int));
            }
        }
    }

    /// <summary>
    /// Copies the specified number of elements of type Int64 from the underlying buffer into the specified array.
    /// </summary>
    /// <param name="target">The array to copy to.</param>
    /// <param name="count">The number of elements to copy.</param>
    public void Read(long[] target, int count)
    {
        unsafe
        {
            fixed (long* t = target)
            {
                Read(t, count * sizeof(long));
            }
        }
    }

    /// <summary>
    /// Copies the specified number of elements of type Char from the underlying buffer into the specified array.
    /// </summary>
    /// <param name="target">The array to copy to.</param>
    /// <param name="count">The number of elements to copy.</param>
    public void Read(char[] target, int count)
    {
        unsafe
        {
            fixed (char* t = target)
            {
                Read(t, count * sizeof(char));
            }
        }
    }

    /// <summary>
    /// Copies the specified number of bytes from the underlying buffer into the specified stream.
    /// </summary>
    /// <param name="target">The stream to copy to.</param>
    /// <param name="count">The number of bytes to copy.</param>
    public void CopyToStream(Stream target, int count)
    {
        int start = MoveCurrentPosition(count);
        target.Write(_buffer, start, count);
    }

    /// <summary>
    /// Reads one Int16 value from the underlying buffer.
    /// </summary>
    /// <returns>The value read from the buffer.</returns>
    public short ReadInt16()
    {
        int start = MoveCurrentPosition(sizeof(short));

        unsafe
        {
            fixed (byte* buf = _buffer)
            {
                return *(short*)(buf + start);
            }
        }
    }

    /// <summary>
    /// Reads one UInt16 value from the underlying buffer.
    /// </summary>
    /// <returns>The value read from the buffer.</returns>
    public ushort ReadUInt16()
    {
        int start = MoveCurrentPosition(sizeof(ushort));

        unsafe
        {
            fixed (byte* buf = _buffer)
            {
                return *(ushort*)(buf + start);
            }
        }
    }

    /// <summary>
    /// Reads one Int32 value from the underlying buffer.
    /// </summary>
    /// <returns>The value read from the buffer.</returns>
    public int ReadInt32()
    {
        int start = MoveCurrentPosition(sizeof(int));

        unsafe
        {
            fixed (byte* buf = _buffer)
            {
                return *(int*)(buf + start);
            }
        }
    }

    /// <summary>
    /// Reads one UInt32 value from the underlying buffer.
    /// </summary>
    /// <returns>The value read from the buffer.</returns>
    public uint ReadUInt32()
    {
        int start = MoveCurrentPosition(sizeof(uint));

        unsafe
        {
            fixed (byte* buf = _buffer)
            {
                return *(uint*)(buf + start);
            }
        }
    }

    /// <summary>
    /// Reads one Int64 value from the underlying buffer.
    /// </summary>
    /// <returns>The value read from the buffer.</returns>
    public long ReadInt64()
    {
        int start = MoveCurrentPosition(sizeof(long));

        unsafe
        {
            fixed (byte* buf = _buffer)
            {
                return *(long*)(buf + start);
            }
        }
    }

    /// <summary>
    /// Reads one UInt64 value from the underlying buffer.
    /// </summary>
    /// <returns>The value read from the buffer.</returns>
    public ulong ReadUInt64()
    {
        int start = MoveCurrentPosition(sizeof(ulong));

        unsafe
        {
            fixed (byte* buf = _buffer)
            {
                return *(ulong*)(buf + start);
            }
        }
    }

    /// <summary>
    /// Reads one Byte value from the underlying buffer.
    /// </summary>
    /// <returns>The value read from the buffer.</returns>
    public byte ReadByte()
    {
        int start = MoveCurrentPosition(sizeof(byte));

        unsafe
        {
            fixed (byte* buf = _buffer)
            {
                return *(buf + start);
            }
        }
    }

    /// <summary>
    /// Reads one SByte value from the underlying buffer.
    /// </summary>
    /// <returns>The value read from the buffer.</returns>
    public sbyte ReadSByte()
    {
        int start = MoveCurrentPosition(sizeof(sbyte));

        unsafe
        {
            fixed (byte* buf = _buffer)
            {
                return *(sbyte*)(buf + start);
            }
        }
    }

    /// <summary>
    /// Reads one Single value from the underlying buffer.
    /// </summary>
    /// <returns>The value read from the buffer.</returns>
    public float ReadSingle()
    {
        int start = MoveCurrentPosition(sizeof(float));

        unsafe
        {
            fixed (byte* buf = _buffer)
            {
                return *(float*)(buf + start);
            }
        }
    }

    /// <summary>
    /// Reads one Double value from the underlying buffer.
    /// </summary>
    /// <returns>The value read from the buffer.</returns>
    public double ReadDouble()
    {
        int start = MoveCurrentPosition(sizeof(double));

        unsafe
        {
            fixed (byte* buf = _buffer)
            {
                return *(double*)(buf + start);
            }
        }
    }

    /// <summary>
    /// Reads one DateTime value from the underlying buffer.
    /// </summary>
    /// <returns>The value read from the buffer.</returns>
    public DateTime ReadDateTime()
    {
        unsafe
        {
            int start = MoveCurrentPosition(sizeof(DateTime));
            fixed (byte* buf = _buffer)
            {
                return *(DateTime*)(buf + start);
            }
        }
    }

    /// <summary>
    /// Reads one Char value from the underlying buffer.
    /// </summary>
    /// <returns>The value read from the buffer.</returns>
    public char ReadChar()
    {
        int start = MoveCurrentPosition(sizeof(char));

        unsafe
        {
            fixed (byte* buf = _buffer)
            {
                return *(char*)(buf + start);
            }
        }
    }

    /// <summary>
    /// Reads one Bool value from the underlying buffer.
    /// </summary>
    /// <returns>The value read from the buffer.</returns>
    public bool ReadBool()
    {
        int start = MoveCurrentPosition(sizeof(bool));

        unsafe
        {
            fixed (byte* buf = _buffer)
            {
                return *(bool*)(buf + start);
            }
        }
    }

    /// <summary>
    /// Reads one String value from the underlying buffer.
    /// </summary>
    /// <returns>The value read from the buffer.</returns>
    public string ReadString()
    {
        int length = ReadInt32();
        if (length == -1)
        {
            return null;
        }

        int start = MoveCurrentPosition(length);
        unsafe
        {
            fixed (byte* buf = _buffer)
            {
                return Encoding.UTF8.GetString(buf + start, length);
            }
        }
    }

    /// <summary>
    /// Reads one Envelope value from the underlying buffer.
    /// </summary>
    /// <returns>The value read from the buffer.</returns>
    public Envelope ReadEnvelope()
    {
        unsafe
        {
            int start = MoveCurrentPosition(sizeof(Envelope));
            fixed (byte* buf = _buffer)
            {
                return *(Envelope*)(buf + start);
            }
        }
    }

    private int MoveCurrentPosition(int requiredBytes)
    {
        if (requiredBytes > _length - _currentPosition)
        {
            throw new InvalidOperationException("Attempted to read past the end of the buffer");
        }

        int start = _currentPosition;
        _currentPosition += requiredBytes;
        return start;
    }
}
