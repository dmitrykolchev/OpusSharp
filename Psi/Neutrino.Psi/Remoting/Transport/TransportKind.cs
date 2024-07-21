// <copyright file="TransportKind.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

namespace Neutrino.Psi.Remoting;

/// <summary>
/// Kinds of supported network transports.
/// </summary>
public enum TransportKind
{
    /// <summary>
    /// Transmission Control Protocol/Internet Protocol.
    /// </summary>
    /// <remarks>No packet loss.</remarks>
    Tcp,

    /// <summary>
    /// User Datagram Protocol/Internet Protocol.
    /// </summary>
    /// <remarks>Possible packet loss.</remarks>
    Udp,

    /// <summary>
    /// Named Pipes protocol.
    /// </summary>
    /// <remarks>No packet loss. Supports security.</remarks>
    NamedPipes,
}
