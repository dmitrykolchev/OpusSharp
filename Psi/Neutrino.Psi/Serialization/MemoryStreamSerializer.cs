// <copyright file="MemoryStreamSerializer.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System.IO;
using System.Runtime.CompilerServices;
using Microsoft.Psi.Common;

namespace Microsoft.Psi.Serialization;

/// <summary>
/// Provides serialization and cloning methods for <see cref="MemoryStream"/> objects.
/// </summary>
internal sealed class MemoryStreamSerializer : ISerializer<MemoryStream>
{
    private const int LatestSchemaVersion = 3;
    private ISerializer<MemoryStream> _innerSerializer;

    /// <inheritdoc />
    public bool? IsClearRequired => false;

    public TypeSchema Initialize(KnownSerializers serializers, TypeSchema targetSchema)
    {
        if (targetSchema?.Version <= 2)
        {
            // maintain backward compatibility with older serialized data
            _innerSerializer = new ClassSerializer<MemoryStream>();
        }
        else
        {
            // otherwise default to the new implementation
            _innerSerializer = new MemoryStreamSerializerImpl();
        }

        return _innerSerializer.Initialize(serializers, targetSchema);
    }

    public void Serialize(BufferWriter writer, MemoryStream instance, SerializationContext context)
    {
        _innerSerializer.Serialize(writer, instance, context);
    }

    public void PrepareDeserializationTarget(BufferReader reader, ref MemoryStream target, SerializationContext context)
    {
        _innerSerializer.PrepareDeserializationTarget(reader, ref target, context);
    }

    public void Deserialize(BufferReader reader, ref MemoryStream target, SerializationContext context)
    {
        _innerSerializer.Deserialize(reader, ref target, context);
    }

    public void PrepareCloningTarget(MemoryStream instance, ref MemoryStream target, SerializationContext context)
    {
        _innerSerializer.PrepareCloningTarget(instance, ref target, context);
    }

    public void Clone(MemoryStream instance, ref MemoryStream target, SerializationContext context)
    {
        _innerSerializer.Clone(instance, ref target, context);
    }

    public void Clear(ref MemoryStream target, SerializationContext context)
    {
        _innerSerializer.Clear(ref target, context);
    }

    /// <summary>
    /// Provides serialization and cloning methods for <see cref="MemoryStream"/> objects.
    /// </summary>
    /// <remarks>
    /// Serializes only the actual data in the buffer, rather than the entire buffer capacity, which
    /// may be greater. Deserializing or cloning into an existing instance will attempt to reuse the
    /// existing internal buffer capacity, expanding it if necessary. Does not support deserializing
    /// or cloning into a non-writeable <see cref="MemoryStream"/>. Deserializing or cloning into a
    /// non-expandable <see cref="MemoryStream"/> is supported only if it has sufficient capacity.
    /// </remarks>
    private class MemoryStreamSerializerImpl : ISerializer<MemoryStream>
    {
        /// <inheritdoc />
        public bool? IsClearRequired => false;

        public TypeSchema Initialize(KnownSerializers serializers, TypeSchema targetSchema)
        {
            TypeMemberSchema[] schemaMembers = new[] { new TypeMemberSchema("buffer", typeof(byte[]).AssemblyQualifiedName, true) };
            System.Type type = typeof(MemoryStream);
            string name = TypeSchema.GetContractName(type, serializers.RuntimeInfo.SerializationSystemVersion);
            TypeSchema schema = new(
                type.AssemblyQualifiedName,
                TypeFlags.IsCollection,
                schemaMembers,
                name,
                TypeSchema.GetId(name),
                LatestSchemaVersion,
                GetType().AssemblyQualifiedName,
                serializers.RuntimeInfo.SerializationSystemVersion);
            return targetSchema ?? schema;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Serialize(BufferWriter writer, MemoryStream instance, SerializationContext context)
        {
            // write only as much of the underlying buffer that actually contains data
            writer.Write((int)instance.Length);
            if (instance.TryGetBuffer(out System.ArraySegment<byte> buffer))
            {
                unsafe
                {
                    // use the faster path of reading directly from the underlying buffer if possible
                    fixed (byte* pb = buffer.Array)
                    {
                        writer.Write(pb + buffer.Offset, (int)instance.Length);
                    }
                }
            }
            else
            {
                // if the underlying buffer is not exposed, read the bytes from the stream
                instance.Position = 0;
                writer.CopyFromStream(instance, (int)instance.Length);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PrepareDeserializationTarget(BufferReader reader, ref MemoryStream target, SerializationContext context)
        {
            int length = reader.ReadInt32();
            PrepareTarget(ref target, length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Deserialize(BufferReader reader, ref MemoryStream target, SerializationContext context)
        {
            if (target.TryGetBuffer(out System.ArraySegment<byte> buffer))
            {
                unsafe
                {
                    // use the faster path of writing directly to the underlying buffer if possible
                    fixed (byte* pb = buffer.Array)
                    {
                        reader.Read(pb + buffer.Offset, (int)target.Length);
                    }
                }
            }
            else
            {
                // if the underlying buffer is not exposed, write the bytes into the stream
                reader.CopyToStream(target, (int)target.Length);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PrepareCloningTarget(MemoryStream instance, ref MemoryStream target, SerializationContext context)
        {
            PrepareTarget(ref target, (int)instance.Length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clone(MemoryStream instance, ref MemoryStream target, SerializationContext context)
        {
            instance.WriteTo(target);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear(ref MemoryStream target, SerializationContext context)
        {
            // nothing to clear
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void PrepareTarget(ref MemoryStream target, int length)
        {
            if (target == null)
            {
                // create a new writeable and expandable MemoryStream with the required capacity
                target = new MemoryStream(length);
            }
            else
            {
                // reset the existing stream's buffer and position
                target.SetLength(0);
            }

            // set the length of the target prior to copying into it (increases capacity if needed)
            target.SetLength(length);
        }
    }
}
