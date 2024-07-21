// <copyright file="Range.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Diagnostics;


namespace Microsoft.Psi.Arrays;

/// <summary>
/// Defines an inclusive range of int values.
/// </summary>
[DebuggerDisplay("[{start}-{end}]")]
public struct Range
{
    /// <summary>
    /// An empty range.
    /// </summary>
    public static Range Empty = new(0, -1);

    /// <summary>
    /// An all-inclusive range. Useful when slicing, to keep a dimension unchanged.
    /// </summary>
    public static Range All = new(int.MinValue, int.MaxValue);

    private readonly int _start;
    private readonly int _end;

    /// <summary>
    /// Initializes a new instance of the <see cref="Range"/> struct.
    /// </summary>
    /// <param name="start">The first value in the range.</param>
    /// <param name="end">The last value in the range.</param>
    public Range(int start, int end)
    {
        _start = start;
        _end = end;
    }

    /// <summary>
    /// Gets the first value in the range.
    /// </summary>
    public int Start => _start;

    /// <summary>
    /// Gets the last value in the range.
    /// </summary>
    public int End => _end;

    /// <summary>
    /// Gets a value indicating whether this range is in increasing (true) or decreasing (false) order.
    /// </summary>
    public bool IsIncreasing => _end >= _start;

    /// <summary>
    /// Gets a value indicating whether the range consists of a single value or not. Same as Size == 0;.
    /// </summary>
    public bool IsSingleValued => _end == _start;

    /// <summary>
    /// Gets the size of the range, computed as Math.Abs(end-start) + 1.
    /// </summary>
    public int Size => Math.Abs(_end - _start) + 1;

    /// <summary>
    /// Converts a tuple to a range.
    /// </summary>
    /// <param name="def">The tuple to convert to a range.</param>
    public static implicit operator Range((int start, int end) def)
    {
        return new Range(def.start, def.end);
    }

    /// <summary>
    /// Equality comparer. Returns true if the two ranges have the same start and end, false otherwise.
    /// </summary>
    /// <param name="first">The first value to compare.</param>
    /// <param name="second">The second value to compare.</param>
    /// <returns>True if the two ranges have the same start and end, false otherwise.</returns>
    public static bool operator ==(Range first, Range second)
    {
        return first._start == second._start && first._end == second._end;
    }

    /// <summary>
    /// Inequality comparer. Returns true if the two ranges have a different start and/or end, false otherwise.
    /// </summary>
    /// <param name="first">The first value to compare.</param>
    /// <param name="second">The second value to compare.</param>
    /// <returns>True if the two ranges have a different start and/or end, false otherwise.</returns>
    public static bool operator !=(Range first, Range second)
    {
        return first._start != second._start || first._end != second._end;
    }

    /// <summary>
    /// Equality comparer. Returns true if the current range have the same start and end as the specified range, false otherwise.
    /// </summary>
    /// <param name="obj">The value to compare to.</param>
    /// <returns>True if the two ranges have the same start and end, false otherwise.</returns>
    public override bool Equals(object obj)
    {
        if (obj is Range)
        {
            return this == (Range)obj;
        }

        return false;
    }

    /// <summary>
    /// Computes a hashcode based on start and end.
    /// </summary>
    /// <returns>A hash code for the range.</returns>
    public override int GetHashCode()
    {
        return ((((long)_start) << 32) + _end).GetHashCode();
    }

    /// <summary>
    /// Returns a string representation of the range.
    /// </summary>
    /// <returns>A string that represents the range.</returns>
    public override string ToString()
    {
        return $"[{_start}-{_end}]";
    }
}
