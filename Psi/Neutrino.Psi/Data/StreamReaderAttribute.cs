// <copyright file="StreamReaderAttribute.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;

namespace Neutrino.Psi.Data;


/// <summary>
/// Represents a stream reader attribute.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class StreamReaderAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="StreamReaderAttribute"/> class.
    /// </summary>
    /// <param name="name">Name of stream reader source (e.g. "Psi Store", "WAV File", ...).</param>
    /// <param name="extension">File extension of stream reader source (e.g. ".psi", ".wav", ...).</param>
    public StreamReaderAttribute(string name, string extension)
    {
        Name = name;
        Extension = extension;
    }

    /// <summary>
    /// Gets the name of stream reader source.
    /// </summary>
    public string Name { get; private set; }

    /// <summary>
    /// Gets the file extension of the stream reader source.
    /// </summary>
    public string Extension { get; private set; }
}
