// <copyright file="IndexerNd.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Collections.Generic;
using System.Linq;


namespace Neutrino.Psi.Common.Arrays;

/// <summary>
/// Represents an n-dimensional index domain.
/// </summary>
public class IndexerNd : Indexer, IIndexer
{
    private readonly IndexDefinition[] _dimensions;

    /// <summary>
    /// Initializes a new instance of the <see cref="IndexerNd"/> class.
    /// </summary>
    /// <param name="dimensions">The index dimensions, from most significant to least significant.</param>
    public IndexerNd(params IndexDefinition[] dimensions)
        : base(dimensions.Aggregate(1, (c, d) => c * d.Count))
    {
        if (dimensions == null || dimensions.Length == 0)
        {
            throw new ArgumentException(nameof(dimensions));
        }

        _dimensions = dimensions;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IndexerNd"/> class.
    /// </summary>
    /// <param name="dimensions">The index dimensions, from most significant to least significant.</param>
    public IndexerNd(params int[] dimensions)
        : base(dimensions.Aggregate(1, (c, d) => c * d))
    {
        if (dimensions == null || dimensions.Length == 0)
        {
            throw new ArgumentException(nameof(dimensions));
        }

        _dimensions = new IndexDefinition[dimensions.Length];
        int stride = 1;
        for (int d = dimensions.Length - 1; d >= 0; d--)
        {
            _dimensions[d] = new RangeIndexDefinition(dimensions[d], stride);
            stride = stride * dimensions[d];
        }
    }

    /// <summary>
    /// Gets the set of contiguous ranges of absolute values in this index domain.
    /// </summary>
    public override IEnumerable<Range> Ranges
    {
        get
        {
            int count = _dimensions.Length - 1;
            var leastSignificantDimension = _dimensions[count];
            while (count > 0 && _dimensions[count - 1].TryReduce(leastSignificantDimension, out IndexDefinition combined))
            {
                leastSignificantDimension = combined;
                count--;
            }

            if (count == 0)
            {
                return leastSignificantDimension.Ranges;
            }

            IEnumerable<int> result = _dimensions[0].Values.Select(v => v * _dimensions[0].ElementStride);
            for (int i = 1; i < count; i++)
            {
                int d = i; // avoid by-ref capture of d in the closure below
                result = result
                    .SelectMany(b => _dimensions[d].Values.Select(v => b + (v * _dimensions[d].ElementStride)));
            }

            return result
                .SelectMany(b => leastSignificantDimension.Ranges.Select(r => new Range(b + r.Start, b + r.End)));
        }
    }

    /// <summary>
    /// Gets the absolute values over the index domain.
    /// </summary>
    public override IEnumerable<int> Values
    {
        get
        {
            IEnumerable<int> result = _dimensions[0].Values.Select(v => v * _dimensions[0].ElementStride);
            for (int i = 1; i < _dimensions.Length; i++)
            {
                int d = i; // avoid by-ref capture of d in the closure below
                result = result
                    .SelectMany(b => _dimensions[d].Values.Select(v => b + (v * _dimensions[d].ElementStride)));
            }

            return result;
        }
    }

    /// <summary>
    /// Returns the absolute value of the index given its components in each dimension.
    /// </summary>
    /// <param name="indices">Index components in each dimension.</param>
    public int this[params int[] indices]
    {
        get
        {
            if (indices.Length != _dimensions.Length)
            {
                throw new ArgumentException("Invalid number of dimensions. Expected " + _dimensions.Length);
            }

            int index = 0;
            for (int d = 0; d < _dimensions.Length - 1; d++)
            {
                int ix = indices[d];
                index += _dimensions[d][indices[d]] * _dimensions[d].ElementStride;
            }

            return index;
        }
    }

    /// <summary>
    /// Creates an indexer based on a rectangular slice of the index domain.
    /// </summary>
    /// <param name="ranges">The slice in each dimension.</param>
    /// <returns>A new indexer over the specified rectangular slice.</returns>
    public Indexer Slice(params Range[] ranges)
    {
        if (_dimensions.Length != ranges.Length)
        {
            throw new ArgumentException(nameof(ranges));
        }

        IndexDefinition[] newdefs = new IndexDefinition[ranges.Length];
        for (int d = 0; d < ranges.Length; d++)
        {
            newdefs[d] = _dimensions[d].Slice(ranges[d]);
        }

        return new IndexerNd(newdefs);
    }
}
