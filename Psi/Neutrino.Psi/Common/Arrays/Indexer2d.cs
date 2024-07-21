// <copyright file="Indexer2d.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Collections.Generic;


namespace Microsoft.Psi.Arrays;

/// <summary>
/// Represents a 2d index domain.
/// </summary>
public class Indexer2d : Indexer, IIndexer
{
    private readonly IndexDefinition _rowIndexDef;
    private readonly IndexDefinition _columnIndexDef;

    /// <summary>
    /// Initializes a new instance of the <see cref="Indexer2d"/> class.
    /// </summary>
    /// <param name="rows">The row definition (most significant dimension).</param>
    /// <param name="columns">The column definition (least significant dimension).</param>
    public Indexer2d(IndexDefinition rows, IndexDefinition columns)
        : base(rows.Count * columns.Count)
    {
        _rowIndexDef = rows;
        _columnIndexDef = columns;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Indexer2d"/> class.
    /// </summary>
    /// <param name="rows">The count of rows (most significant dimension).</param>
    /// <param name="columns">The count of columns (least significant dimension).</param>
    public Indexer2d(int rows, int columns)
        : this(rows, columns, columns)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Indexer2d"/> class.
    /// </summary>
    /// <param name="rows">The count of rows (most significant dimension).</param>
    /// <param name="columns">The count of columns (least significant dimension).</param>
    /// <param name="stride">The spacing between rows. Must be greater than the column count.</param>
    public Indexer2d(int rows, int columns, int stride)
        : base(rows * columns)
    {
        if (stride < columns)
        {
            throw new ArgumentException(nameof(stride));
        }

        _rowIndexDef = new RangeIndexDefinition(rows, stride);
        _columnIndexDef = new RangeIndexDefinition(columns);
    }

    /// <summary>
    /// Gets the set of contiguous ranges of absolute values in this index domain.
    /// </summary>
    public override IEnumerable<Range> Ranges
    {
        get
        {
            if (_rowIndexDef.TryReduce(_columnIndexDef, out IndexDefinition combined))
            {
                return combined.Ranges;
            }

            return EnumerateRangesExplicit();
        }
    }

    /// <summary>
    /// Gets the absolute values over the index domain.
    /// </summary>
    public override IEnumerable<int> Values
    {
        get
        {
            foreach (int row in _rowIndexDef.Values)
            {
                foreach (int column in _columnIndexDef.Values)
                {
                    yield return (row * _rowIndexDef.ElementStride) + column;
                }
            }
        }
    }

    /// <summary>
    /// Returns the absolute value of the index given the row and column values.
    /// </summary>
    /// <param name="row">The row index.</param>
    /// <param name="column">The column index.</param>
    /// <returns>The absolute value, computed as row *stride + column.</returns>
    public int this[int row, int column] 
        => (_rowIndexDef[row] * _rowIndexDef.ElementStride) + _columnIndexDef[column];

    /// <summary>
    /// Creates an indexer based on a rectangular slice of the index domain.
    /// </summary>
    /// <param name="rowRange">The set of rows to include.</param>
    /// <param name="columnRange">The set of columns to include.</param>
    /// <returns>A new indexer over the specified rectangular slice.</returns>
    public Indexer2d Slice(Range rowRange, Range columnRange)
    {
        return new Indexer2d(_rowIndexDef.Slice(rowRange), _columnIndexDef.Slice(columnRange));
    }

    /// <summary>
    /// Creates an indexer based on a rectangular slice of the index domain.
    /// </summary>
    /// <param name="ranges">The row and column ranges to include.</param>
    /// <returns>A new indexer over the specified rectangular slice.</returns>
    Indexer IIndexer.Slice(params Range[] ranges)
    {
        if (ranges.Length != 2)
        {
            throw new ArgumentException(nameof(ranges));
        }

        return Slice(ranges[0], ranges[1]);
    }

    private IEnumerable<Range> EnumerateRangesExplicit()
    {
        foreach (int row in _rowIndexDef.Values)
        {
            int start = row * _rowIndexDef.ElementStride;
            foreach (Range columnRange in _columnIndexDef.Ranges)
            {
                yield return new Range(start + columnRange.Start, start + columnRange.End);
            }
        }
    }
}
