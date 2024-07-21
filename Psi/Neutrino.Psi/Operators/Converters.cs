// <copyright file="Converters.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

namespace Neutrino.Psi;

/// <summary>
/// Extension methods that simplify operator usage.
/// </summary>
public static partial class Operators
{
    /// <summary>
    /// Assign name (meta) to the stream.
    /// </summary>
    /// <typeparam name="T">Type of stream messages.</typeparam>
    /// <param name="source">Source stream.</param>
    /// <param name="name">Name to give stream.</param>
    /// <returns>Output stream.</returns>
    public static IProducer<T> Name<T>(this IProducer<T> source, string name)
    {
        source.Out.Name = name;
        return source;
    }
}
