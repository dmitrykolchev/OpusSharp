// <copyright file="PropertyKey.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;

namespace Microsoft.Psi.Audio.ComInterop;

/// <summary>
/// PROPERTYKEY struct.
/// </summary>
internal struct PropertyKey
{
    /// <summary>
    /// Format ID.
    /// </summary>
    internal Guid FormatId;

    /// <summary>
    /// Property ID.
    /// </summary>
    internal int PropertyId;
}
