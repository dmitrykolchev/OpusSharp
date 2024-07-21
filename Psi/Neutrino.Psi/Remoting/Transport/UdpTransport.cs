// <copyright file="UdpTransport.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using Neutrino.Psi.Common;

namespace Neutrino.Psi.Remoting;

/// <summary>
/// UDP network transport.
/// </summary>
internal class UdpTransport : ITransport
{
    private UdpClient _client;
    private int _port;

    /// <summary>
    /// Gets kind of network transport.
    /// </summary>
    public TransportKind Transport => TransportKind.Udp;

    /// <summary>
    /// Start listening on IP port.
    /// </summary>
    public void StartListening()
    {
        _client = new UdpClient(0);
        _port = ((IPEndPoint)_client.Client.LocalEndPoint).Port;
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
    /// Accept new UDP client.
    /// </summary>
    /// <returns>Accepted client.</returns>
    public ITransportClient AcceptClient()
    {
        return new UdpTransportClient(_client);
    }

    /// <summary>
    /// Connect to remote host.
    /// </summary>
    /// <param name="host">Host name to which to connect.</param>
    /// <returns>Connected client.</returns>
    public ITransportClient Connect(string host)
    {
        _client = new UdpClient();
        _client.Connect(host, _port);
        return new UdpTransportClient(_client);
    }

    /// <summary>
    /// Dispose of UDP transport.
    /// </summary>
    public void Dispose()
    {
        _client.Dispose();
        _client = null;
    }

    internal class UdpTransportClient : ITransportClient
    {
        private UdpClient _client;
        private readonly DataChunker _chunker;
        private readonly DataUnchunker _unchunker;
        private long _id = 0;
        private readonly BufferWriter _writer = new(0);

        public UdpTransportClient(UdpClient client)
        {
            const int maxDatagramSize = (64 * 1024) - DataChunker.HeaderSize; // see https://en.wikipedia.org/wiki/User_Datagram_Protocol
            _client = client;
            _chunker = new DataChunker(maxDatagramSize);
            _unchunker = new DataUnchunker(maxDatagramSize, x => Trace.WriteLine($"UdpTransport Chunkset: {x}"), x => Trace.WriteLine($"UdpTransport Abandoned: {x}"));
        }

        public Guid ReadSessionId()
        {
            IPEndPoint endpoint = (IPEndPoint)_client.Client.LocalEndPoint;
            byte[] data = _client.Receive(ref endpoint);
            _client.Connect(endpoint);
            if (!_unchunker.Receive(data) || _unchunker.Length != 16)
            {
                throw new IOException($"Expected single session ID packet");
            }

            byte[] bytes = new byte[16];
            Array.Copy(_unchunker.Payload, bytes, bytes.Length);

            return new Guid(bytes);
        }

        public void WriteSessionId(Guid id)
        {
            Remoting.Transport.WriteSessionId(id, Write);
        }

        public Tuple<Envelope, byte[]> ReadMessage()
        {
            Tuple<byte[], int> data = Read();
            BufferReader reader = new(data.Item1, data.Item2);
            Envelope envelope = reader.ReadEnvelope();
            int length = reader.ReadInt32();
            byte[] buffer = new byte[length];
            reader.Read(buffer, length);
            return Tuple.Create(envelope, buffer);
        }

        public void WriteMessage(Envelope envelope, byte[] message)
        {
            Remoting.Transport.WriteMessage(envelope, message, _writer, Write);
        }

        public void Dispose()
        {
            _client.Dispose();
            _client = null;
        }

        private void Write(byte[] buffer, int size)
        {
            foreach (Tuple<byte[], int> chunk in _chunker.GetChunks(_id++, buffer, size))
            {
                byte[] data = chunk.Item1;
                int len = chunk.Item2;
                _client.Send(data, len);
            }
        }

        private Tuple<byte[], int> Read()
        {
            IPEndPoint endpoint = (IPEndPoint)_client.Client.LocalEndPoint;

            while (!_unchunker.Receive(_client.Receive(ref endpoint)))
            {
            }

            return Tuple.Create(_unchunker.Payload, _unchunker.Length);
        }
    }
}
