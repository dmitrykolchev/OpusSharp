// <copyright file="MemoryAccessBenchmark.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using Neutrino.Psi.Common;

namespace Neutrino.Benchmarks;

[MemoryDiagnoser(true)]
public class MemoryAccessBenchmark
{
    [Benchmark]
    public int MemoryAccessNetBenchmark()
    {
        return MemoryAccess.SizeOf<long>();
    }

    [Benchmark]
    public int UnsafeBenchmark()
    {
        return Unsafe.SizeOf<long>();
    }

    [Benchmark]
    public int MemoryMarshalBenchmark()
    {
        return Marshal.SizeOf<long>();
    }
}
