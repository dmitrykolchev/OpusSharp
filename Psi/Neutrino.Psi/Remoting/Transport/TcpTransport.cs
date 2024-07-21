// <copyright file="TcpTransport.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Net;
using System.Net.Sockets;
using Microsoft.Psi.Common;

namespace Microsoft.Psi.Remoting;

/// <summary>
/// TCP network transport.
/// </summary>
internal class TcpTransport : ITransport
{
    private TcpListener _listener;
    private int _port;

    /// <summary>
    /// Gets kind of network transport.
    /// </summary>
    public TransportKind Transport => TransportKind.Tcp;

    /// <summary>
    /// Start listening on IP port.
    /// </summary>
    public void StartListening()
    {
        _listener = new TcpListener(IPAddress.Any, 0);
        _listener.Start();
        _port = ((IPEndPoint)_listener.LocalEndpoint).Port;
    }

    /// <summary>
    /// Write transport-specific parameter (port number).
    /// </summary>
    /// <param name="writer">Buffer writer to which to write.</param>
    public void WriteTransportParams(BufferWriter writer)
    {
        writer.Write(_port);
    }

    /// <summary>
    /// Read transport-specific parameter (port number).
    /// </summary>
    /// <param name="reader">Buffer reader from which to read.</param>
    public void ReadTransportParams(BufferReader reader)
    {
        _port = reader.ReadInt32();
    }

    /// <summary>
    /// Accept new TCP client.
    /// </summary>
    /// <returns>Accepted client.</returns>
    public ITransportClient AcceptClient()
    {
        return new TcpTransportClient(_listener.AcceptTcpClient());
    }

    /// <summary>
    /// Connect to remote host.
    /// </summary>
    /// <param name="host">Host name to which to connect.</param>
    /// <returns>Connected client.</returns>
    public ITransportClient Connect(string host)
    {
        TcpClient client = new();
        client.Connect(host, _port);
        return new TcpTransportClient(client);
    }

    /// <summary>
    /// Dispose of TCP transport.
    /// </summary>
    public void Dispose()
    {
        _listener.Stop();
    }

    internal class TcpTransportClient : ITransportClient
    {
        private readonly NetworkStream _stream;
        private TcpClient _client;
        private readonly BufferReader _reader = new();
        private readonly BufferWriter _writer = new(0);

        public TcpTransportClient(TcpClient client)
        {
            _client = client;
            _stream = client.GetStream();
        }

        public Guid ReadSessionId()
        {
            return Remoting.Transport.ReadSessionId(_stream);
        }

        public void WriteSessionId(Guid id)
        {
            Remoting.Transport.WriteSessionId(id, Write);
        }

        public Tuple<Envelope, byte[]> ReadMessage()
        {
            return Remoting.Transport.ReadMessage(_reader, _stream);
        }

        public void WriteMessage(Envelope envelope, byte[] message)
        {
            Remoting.Transport.WriteMessage(envelope, message, _writer, Write);
        }

        public void Dispose()
        {
            _stream.Close();
            _client.Dispose();
            _client = null;
        }

        private void Write(byte[] buffer, int size)
        {
            _stream.Write(buffer, 0, size);
            _stream.Flush();
        }

        private void Read(byte[] buffer, int size)
        {
            Remoting.Transport.Read(buffer, size, _stream);
        }
    }
}
