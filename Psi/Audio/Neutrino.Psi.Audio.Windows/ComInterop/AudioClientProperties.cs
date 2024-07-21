// <copyright file="AudioClientProperties.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System.Runtime.InteropServices;

namespace Microsoft.Psi.Audio.ComInterop;

/// <summary>
/// Audio client properties (defined in AudioClient.h).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct AudioClientProperties
{
    /// <summary>
    /// cbSize.
    /// </summary>
    public int Size;

    /// <summary>
    /// bIsOffload.
    /// </summary>
    public bool IsOffload;

    /// <summary>
    /// eCategory.
    /// </summary>
    public AudioStreamCategory Category;

    /// <summary>
    /// Options.
    /// </summary>
    public AudioClientStreamOptions Options;
}
