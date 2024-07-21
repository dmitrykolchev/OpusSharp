// <copyright file="ITransport.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using Neutrino.Psi.Common;

namespace Neutrino.Psi.Remoting;

/// <summary>
/// Interface representing network transport.
/// </summary>
internal interface ITransport : IDisposable
{
    /// <summary>
    /// Gets kind of network transport.
    /// </summary>
    TransportKind Transport { get; }

    /// <summary>
    /// Start listening (e.g. on IP port).
    /// </summary>
    void StartListening();

    /// <summary>
    /// Write transport-specific parameters (e.g. port number, pipe name, ...).
    /// </summary>
    /// <param name="writer">Buffer writer to which to write.</param>
    void WriteTransportParams(BufferWriter writer);

    /// <summary>
    /// Read transport-specific parameters (e.g. port number, pipe name, ...).
    /// </summary>
    /// <param name="reader">Buffer reader from which to read.</param>
    void ReadTransportParams(BufferReader reader);

    /// <summary>
    /// Accept new transport client.
    /// </summary>
    /// <returns>Accepted client.</returns>
    ITransportClient AcceptClient();

    /// <summary>
    /// Connect to remote host.
    /// </summary>
    /// <param name="host">Host name to which to connect.</param>
    /// <returns>Connected client.</returns>
    ITransportClient Connect(string host);
}
