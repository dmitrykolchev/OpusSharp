// <copyright file="Indexer.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

#pragma warning disable SA1649 // File name must match first type name
using System.Collections.Generic;

namespace Microsoft.Psi.Arrays;


/// <summary>
/// Common interface for multi-dimensional indexers.
/// The interface contract is needed by NdArray, but might not be optimal from a user standpoint.
/// </summary>
public interface IIndexer
{
    /// <summary>
    /// Takes a rectangular slice of the possible values of this indexer.
    /// </summary>
    /// <param name="ranges">The set of restrictions to apply to each dimension.</param>
    /// <returns>A rectangular slice of the current index space.</returns>
    Indexer Slice(params Range[] ranges);
}

/// <summary>
/// Base class for multi-dimensional indexers.
/// </summary>
public abstract class Indexer
{
    private readonly int _count;

    /// <summary>
    /// Initializes a new instance of the <see cref="Indexer"/> class.
    /// </summary>
    /// <param name="count">The count of distinct possible index values.</param>
    public Indexer(int count)
    {
        _count = count;
    }

    /// <summary>
    /// Gets the count of distinct possible index values.
    /// </summary>
    public int Count => _count;

    /// <summary>
    /// Gets the absolute index values.
    /// </summary>
    public abstract IEnumerable<int> Values { get; }

    /// <summary>
    /// Gets the absolute index values, expressed as contiguous ranges.
    /// </summary>
    public abstract IEnumerable<Range> Ranges { get; }
}
