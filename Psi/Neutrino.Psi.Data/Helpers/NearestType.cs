// <copyright file="NearestType.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

namespace Neutrino.Psi.Data;

/// <summary>
/// Defines various modes for finding a nearest message to a specified time.
/// </summary>
public enum NearestType
{
    /// <summary>
    /// The nearest message.
    /// </summary>
    Nearest,

    /// <summary>
    /// The nearest previous message.
    /// </summary>
    Previous,

    /// <summary>
    /// The nearest next message.
    /// </summary>
    Next,
}
