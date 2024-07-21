// <copyright file="CloningFlags.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;

namespace Neutrino.Psi.Serialization;

/// <summary>
/// Enumeration of flags that control the behavior of cloning.
/// </summary>
[Flags]
public enum CloningFlags
{
    /// <summary>
    /// No flags set.
    /// </summary>
    None = 0x00,

    /// <summary>
    /// Allow cloning of IntPtr fields.
    /// </summary>
    CloneIntPtrFields = 0x01,

    /// <summary>
    /// Allow cloning of unmanaged pointer fields.
    /// </summary>
    ClonePointerFields = 0x02,

    /// <summary>
    /// Skip cloning of NonSerialized fields.
    /// </summary>
    SkipNonSerializedFields = 0x04,
}
