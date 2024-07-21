// <copyright file="MemoryAccess.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Neutrino.Psi.Common;

public static unsafe class MemoryAccess
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SizeOf<T>() where T : struct
    {
        return Unsafe.SizeOf<T>();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static T ReadValue<T>(IntPtr source) where T : struct
    {
        return Unsafe.Read<T>((void*)source);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static ref T ReadRef<T>(IntPtr source) where T : struct
    {
        return ref Unsafe.AsRef<T>(source.ToPointer());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ReadValue<T>(ref T target, IntPtr source) where T : struct
    {
        target = Unsafe.Read<T>((void*)source);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void WriteValue<T>(T source, IntPtr target) where T : struct
    {
        Unsafe.Write((void*)target, source);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void CopyToArray<T>(IntPtr src, T[] dest, int targetIndex, int elementCount) where T : struct
    {
        Pinnable<T> pinnable = Unsafe.As<Pinnable<T>>(dest);
        Unsafe.CopyBlock(
            Unsafe.AsPointer<T>(ref Unsafe.AddByteOffset<T>(ref pinnable.Data, targetIndex * Unsafe.SizeOf<T>())),
            src.ToPointer(),
            (uint)(elementCount * Unsafe.SizeOf<T>()));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void CopyFromArray<T>(T[] src, int srcIndex, IntPtr dest, int destSizeInBytes, int elementCount) where T : struct
    {
        Pinnable<T> pinnable = Unsafe.As<Pinnable<T>>(src);
        Unsafe.CopyBlock(dest.ToPointer(),
            Unsafe.AsPointer<T>(ref Unsafe.AddByteOffset<T>(ref pinnable.Data, srcIndex * Unsafe.SizeOf<T>())),
            (uint)(elementCount * Unsafe.SizeOf<T>()));
    }

    [StructLayout(LayoutKind.Sequential)]
    internal sealed class Pinnable<T>
    {
        public T Data;
    }
}
