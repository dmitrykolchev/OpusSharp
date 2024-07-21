// <copyright file="ImportAttribute.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

namespace Neutrino.Sound.Native;

/// <summary>
/// Attribute for exported API
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public class ImportAttribute : Attribute
{
    /// <summary>
    /// Exported name
    /// </summary>
    public string? Name { get; set; }
}
