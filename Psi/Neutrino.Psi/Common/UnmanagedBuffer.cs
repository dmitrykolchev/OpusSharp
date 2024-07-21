// <copyright file="UnmanagedBuffer.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Runtime.InteropServices;
using Neutrino.Psi.Serialization;


namespace Neutrino.Psi.Common;

/// <summary>
/// Unmanaged buffer wrapper class.
/// </summary>
[Serializer(typeof(UnmanagedBuffer.CustomSerializer))]
public class UnmanagedBuffer : IDisposable
{
    private IntPtr _data;

    private int _size;

    private bool _mustDeallocate;

    private UnmanagedBuffer(IntPtr data, int size, bool mustDeallocate)
    {
        _data = data;
        _size = size;
        _mustDeallocate = mustDeallocate;
    }

    private UnmanagedBuffer(int size)
        : this(Marshal.AllocHGlobal(size), size, true)
    {
    }

    /// <summary>
    /// Finalizes an instance of the <see cref="UnmanagedBuffer"/> class.
    /// </summary>
    ~UnmanagedBuffer()
    {
        DisposeUnmanaged();
    }

    /// <summary>
    /// Gets a pointer to underlying data.
    /// </summary>
    public IntPtr Data => _data;

    /// <summary>
    /// Gets size of underlying data.
    /// </summary>
    public int Size => _size;

    /// <summary>
    /// Allocate unmanaged buffer.
    /// </summary>
    /// <param name="size">Size (bytes) to allocate.</param>
    /// <returns>Allocated unmanaged buffer.</returns>
    public static UnmanagedBuffer Allocate(int size)
    {
        nint data = Marshal.AllocHGlobal(size);
        unsafe
        {
            byte* d = (byte*)data.ToPointer();
            for (byte* i = d; i < d + size; i++)
            {
                *i = 0;
            }
        }

        return new UnmanagedBuffer(data, size, true);
    }

    /// <summary>
    /// Wrap existing unmanaged memory.
    /// </summary>
    /// <param name="data">Pointer to data.</param>
    /// <param name="size">Data size (bytes).</param>
    /// <returns>Wrapped unmanaged buffer.</returns>
    public static UnmanagedBuffer WrapIntPtr(IntPtr data, int size)
    {
        return new UnmanagedBuffer(data, size, false);
    }

    /// <summary>
    /// Create a copy of existing unmanaged memory.
    /// </summary>
    /// <param name="data">Pointer to data.</param>
    /// <param name="size">Data size (bytes).</param>
    /// <returns>Wrapped copy of unmanaged buffer.</returns>
    public static UnmanagedBuffer CreateCopyFrom(IntPtr data, int size)
    {
        nint newData = Marshal.AllocHGlobal(size);
        CopyUnmanagedMemory(newData, data, size);
        return new UnmanagedBuffer(newData, size, true);
    }

    /// <summary>
    /// Create a copy of existing managed data.
    /// </summary>
    /// <param name="data">Data to be copied.</param>
    /// <returns>Wrapped copy to unmanaged buffer.</returns>
    public static UnmanagedBuffer CreateCopyFrom(byte[] data)
    {
        UnmanagedBuffer result = UnmanagedBuffer.Allocate(data.Length);
        result.CopyFrom(data);
        return result;
    }

    /// <summary>
    /// Clone unmanaged buffer.
    /// </summary>
    /// <returns>Cloned unmanaged buffer.</returns>
    public UnmanagedBuffer Clone()
    {
        nint newData = Marshal.AllocHGlobal(_size);
        CopyUnmanagedMemory(newData, _data, _size);
        return new UnmanagedBuffer(newData, _size, true);
    }

    /// <summary>
    /// Copy this unmanaged buffer to another instance.
    /// </summary>
    /// <param name="destination">Destination instance to which to copy.</param>
    /// <param name="size">Size (bytes) to copy.</param>
    public void CopyTo(UnmanagedBuffer destination, int size)
    {
        if (destination == null)
        {
            throw new ArgumentException("Destination unmanaged buffer is null.");
        }
        else if (_size < size)
        {
            throw new ArgumentException("Source unmanaged buffer is not of sufficient size.");
        }
        else if (destination.Size < size)
        {
            throw new ArgumentException("Destination unmanaged buffer is not of sufficient size.");
        }
        else
        {
            CopyUnmanagedMemory(destination._data, _data, _size);
        }
    }

    /// <summary>
    /// Read bytes from unmanaged buffer.
    /// </summary>
    /// <param name="count">Count of bytes to copy.</param>
    /// <param name="offset">Offset into buffer.</param>
    /// <returns>Bytes having been copied.</returns>
    public byte[] ReadBytes(int count, int offset = 0)
    {
        if (_size < count + offset)
        {
            throw new ArgumentException("Unmanaged buffer is not of sufficient size.");
        }

        byte[] result = new byte[count];
        Marshal.Copy(IntPtr.Add(_data, offset), result, 0, count);
        return result;
    }

    /// <summary>
    /// Copy unmanaged buffer to managed array.
    /// </summary>
    /// <param name="destination">Destination array to which to copy.</param>
    /// <param name="size">Size (bytes) to copy.</param>
    public void CopyTo(byte[] destination, int size)
    {
        if (destination == null)
        {
            throw new ArgumentException("Destination array is null.");
        }
        else if (_size < size)
        {
            throw new ArgumentException("Source unmanaged buffer is not of sufficient size.");
        }
        else if (destination.Length < size)
        {
            throw new ArgumentException("Destination array is not of sufficient size.");
        }

        Marshal.Copy(_data, destination, 0, size);
    }

    /// <summary>
    /// Copy unmanaged buffer to address.
    /// </summary>
    /// <param name="destination">Destination address to which to copy.</param>
    /// <param name="size">Size (bytes) to copy.</param>
    public void CopyTo(IntPtr destination, int size)
    {
        if (_size < size)
        {
            throw new ArgumentException("Source unmanaged buffer is not of sufficient size.");
        }

        CopyUnmanagedMemory(destination, _data, size);
    }

    /// <summary>
    /// Copy from unmanaged buffer.
    /// </summary>
    /// <param name="source">Unmanaged buffer from which to copy.</param>
    /// <param name="size">Size (bytes) to copy.</param>
    public void CopyFrom(UnmanagedBuffer source, int size)
    {
        if (source == null)
        {
            throw new ArgumentException("Source unmanaged array is null.");
        }
        else if (_size < size)
        {
            throw new ArgumentException("Destination unmanaged array is not of sufficient size.");
        }
        else if (source.Size < size)
        {
            throw new ArgumentException("Source unmanaged array is not of sufficient size.");
        }

        CopyUnmanagedMemory(_data, source._data, size);
    }

    /// <summary>
    /// Copy from managed array.
    /// </summary>
    /// <param name="source">Managed array from which to copy.</param>
    public void CopyFrom(byte[] source)
    {
        CopyFrom(source, 0, source.Length);
    }

    /// <summary>
    /// Copy from managed array.
    /// </summary>
    /// <param name="source">Managed array from which to copy.</param>
    /// <param name="offset">The zero-based index in the source array where copying should start.</param>
    /// <param name="length">The number of bytes to copy.</param>
    public void CopyFrom(byte[] source, int offset, int length)
    {
        if (source == null)
        {
            throw new ArgumentException("Source array is null.");
        }
        else if (_size < length)
        {
            throw new ArgumentException("Destination unmanaged buffer is not of sufficient size.");
        }
        else if (source.Length < offset + length)
        {
            throw new ArgumentException("Source array is not of sufficient size.");
        }

        Marshal.Copy(source, offset, _data, length);
    }

    /// <summary>
    /// Copy from address.
    /// </summary>
    /// <param name="source">Source address from which to copy.</param>
    /// <param name="size">Size (bytes) to copy.</param>
    public void CopyFrom(IntPtr source, int size)
    {
        if (_size < size)
        {
            throw new ArgumentException("Destination unmanaged buffer is not of sufficient size.");
        }

        CopyUnmanagedMemory(_data, source, size);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        DisposeUnmanaged();
        GC.SuppressFinalize(this);
    }

    private static unsafe void CopyUnmanagedMemory(IntPtr dst, IntPtr src, int count)
    {
        unsafe
        {
            Buffer.MemoryCopy(src.ToPointer(), dst.ToPointer(), count, count);
        }
    }

    private void DisposeUnmanaged()
    {
        if (_mustDeallocate && (_data != IntPtr.Zero))
        {
            Marshal.FreeHGlobal(_data);
            _data = IntPtr.Zero;
            _size = 0;
            _mustDeallocate = false;
        }
    }

    private class CustomSerializer : ISerializer<UnmanagedBuffer>
    {
        public const int LatestSchemaVersion = 2;

        /// <inheritdoc />
        public bool? IsClearRequired => false;

        public TypeSchema Initialize(KnownSerializers serializers, TypeSchema targetSchema)
        {
            serializers.GetHandler<byte>(); // register element type
            Type type = typeof(byte[]);
            string name = TypeSchema.GetContractName(type, serializers.RuntimeInfo.SerializationSystemVersion);
            TypeMemberSchema elementsMember = new TypeMemberSchema("Elements", typeof(byte).AssemblyQualifiedName, true);
            TypeSchema schema = new TypeSchema(
                type.AssemblyQualifiedName,
                TypeFlags.IsCollection,
                new TypeMemberSchema[] { elementsMember },
                name,
                TypeSchema.GetId(name),
                LatestSchemaVersion,
                GetType().AssemblyQualifiedName,
                serializers.RuntimeInfo.SerializationSystemVersion);
            return targetSchema ?? schema;
        }

        public void Serialize(BufferWriter writer, UnmanagedBuffer instance, SerializationContext context)
        {
            unsafe
            {
                writer.Write(instance.Size);
                writer.Write(instance.Data.ToPointer(), instance.Size);
            }
        }

        public void PrepareCloningTarget(UnmanagedBuffer instance, ref UnmanagedBuffer target, SerializationContext context)
        {
            if (target == null || target.Size != instance.Size)
            {
                target?.Dispose();
                target = new UnmanagedBuffer(instance.Size);
            }
        }

        public void Clone(UnmanagedBuffer instance, ref UnmanagedBuffer target, SerializationContext context)
        {
            CopyUnmanagedMemory(target._data, instance._data, instance._size);
        }

        public void PrepareDeserializationTarget(BufferReader reader, ref UnmanagedBuffer target, SerializationContext context)
        {
            int size = reader.ReadInt32();
            if (target == null || target.Size != size)
            {
                target?.Dispose();
                target = new UnmanagedBuffer(size);
            }
        }

        public void Deserialize(BufferReader reader, ref UnmanagedBuffer target, SerializationContext context)
        {
            unsafe
            {
                reader.Read(target.Data.ToPointer(), target.Size);
            }
        }

        public void Clear(ref UnmanagedBuffer target, SerializationContext context)
        {
            // nothing to clear in an unmanaged buffer
        }
    }
}
