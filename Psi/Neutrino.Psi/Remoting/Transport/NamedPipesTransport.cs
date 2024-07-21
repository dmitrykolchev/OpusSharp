// <copyright file="NamedPipesTransport.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.IO;
using System.IO.Pipes;
using System.Runtime.Versioning;
using Neutrino.Psi.Common;

namespace Neutrino.Psi.Remoting;

/// <summary>
/// Named pipes transport.
/// </summary>
[SupportedOSPlatform("windows")]
internal class NamedPipesTransport : ITransport
{
    private string _name;

    /// <summary>
    /// Gets kind of network transport.
    /// </summary>
    public TransportKind Transport => TransportKind.NamedPipes;

    /// <summary>
    /// Start listening (really, allocate GUID used as pipe name).
    /// </summary>
    public void StartListening()
    {
        _name = Guid.NewGuid().ToString();
    }

    /// <summary>
    /// Write transport-specific parameter (pipe name).
    /// </summary>
    /// <param name="writer">Buffer writer to which to write.</param>
    public void WriteTransportParams(BufferWriter writer)
    {
        writer.Write(_name);
    }

    /// <summary>
    /// Read transport-specific parameter (pipe name).
    /// </summary>
    /// <param name="reader">Buffer reader from which to read.</param>
    public void ReadTransportParams(BufferReader reader)
    {
        _name = reader.ReadString();
    }

    /// <summary>
    /// Accept new named pipes client.
    /// </summary>
    /// <returns>Accepted client.</returns>
    public ITransportClient AcceptClient()
    {
        NamedPipeServerStream server = new(_name, PipeDirection.InOut);
        server.WaitForConnection();
        return new NamedPipesTransportClient(server);
    }

    /// <summary>
    /// Connect to remote host.
    /// </summary>
    /// <param name="host">Host name to which to connect.</param>
    /// <returns>Connected client.</returns>
    public ITransportClient Connect(string host)
    {
        NamedPipeClientStream client = new(host, _name, PipeDirection.InOut);
        client.Connect();
        client.ReadMode = PipeTransmissionMode.Message;
        return new NamedPipesTransportClient(client);
    }

    /// <summary>
    /// Dispose of named pipes transport.
    /// </summary>
    public void Dispose()
    {
    }

    internal class NamedPipesTransportClient : ITransportClient
    {
        private Stream _stream;
        private readonly BufferReader _reader = new();
        private readonly BufferWriter _writer = new(0);

        public NamedPipesTransportClient(Stream stream)
        {
            _stream = stream;
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
            _stream.Dispose();
            _stream = null;
        }

        private void Write(byte[] buffer, int size)
        {
            const int maxPacketSize = 64 * 1024;
            int p = 0;
            do
            {
                int s = Math.Min(maxPacketSize, size - p);
                _stream.Write(buffer, p, s);
                p += maxPacketSize;
            }
            while (p < size);
            _stream.Flush();
        }

        private void Read(byte[] buffer, int size)
        {
            Remoting.Transport.Read(buffer, size, _stream);
        }
    }
}
