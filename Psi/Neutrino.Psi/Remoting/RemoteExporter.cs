// <copyright file="RemoteExporter.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Microsoft.Psi.Common;
using Microsoft.Psi.Data;
using Microsoft.Psi.Persistence;

namespace Microsoft.Psi.Remoting;

/// <summary>
/// Exporter for remoting over network transport.
/// </summary>
public sealed class RemoteExporter : IDisposable
{
    internal const short ProtocolVersion = 0;
    internal const int DefaultPort = 11411;
    private const TransportKind DefaultTransport = TransportKind.NamedPipes;

    private readonly int _port;
    private readonly TransportKind _transport;
    private readonly string _name;
    private readonly string _path;
    private readonly long _maxBytesPerSecond;
    private readonly TcpListener _metaListener;
    private readonly ITransport _dataTransport;
    private readonly double _bytesPerSecondSmoothingWindowSeconds;

    private ConcurrentDictionary<Guid, Connection> _connections = new();
    private bool _disposed = false;
    private Thread _metaClientThread;
    private Thread _dataClientThread;

    /// <summary>
    /// Initializes a new instance of the <see cref="RemoteExporter"/> class.
    /// </summary>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="port">TCP port on which to listen (default 11411).</param>
    /// <param name="transport">Transport kind to use.</param>
    /// <param name="maxBytesPerSecond">Maximum bytes/sec quota (default infinite).</param>
    /// <param name="bytesPerSecondSmoothingWindowSeconds">Smoothing window over which to compute bytes/sec (default 5 sec.).</param>
    public RemoteExporter(Pipeline pipeline, int port = DefaultPort, TransportKind transport = DefaultTransport, long maxBytesPerSecond = long.MaxValue, double bytesPerSecondSmoothingWindowSeconds = 5.0)
        : this(PsiStore.Create(pipeline, $"RemoteExporter_{Guid.NewGuid()}", null, true), port, transport, maxBytesPerSecond, bytesPerSecondSmoothingWindowSeconds)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RemoteExporter"/> class.
    /// </summary>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="transport">Transport kind to use.</param>
    /// <param name="maxBytesPerSecond">Maximum bytes/sec quota (default infinite).</param>
    /// <param name="bytesPerSecondSmoothingWindowSeconds">Smoothing window over which to compute bytes/sec (default 5 sec.).</param>
    public RemoteExporter(Pipeline pipeline, TransportKind transport, long maxBytesPerSecond = long.MaxValue, double bytesPerSecondSmoothingWindowSeconds = 5.0)
        : this(pipeline, DefaultPort, transport, maxBytesPerSecond, bytesPerSecondSmoothingWindowSeconds)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RemoteExporter"/> class.
    /// </summary>
    /// <param name="exporter">Exporter to be remoted.</param>
    /// <param name="transport">Transport kind to use.</param>
    public RemoteExporter(Exporter exporter, TransportKind transport)
        : this(exporter, DefaultPort, transport)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RemoteExporter"/> class.
    /// </summary>
    /// <param name="importer">Importer to be remoted.</param>
    /// <param name="transport">Transport kind to use.</param>
    public RemoteExporter(Importer importer, TransportKind transport)
        : this(importer, DefaultPort, transport)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RemoteExporter"/> class.
    /// </summary>
    /// <param name="exporter">Exporter to be remoted.</param>
    /// <param name="port">TCP port on which to listen (default 11411).</param>
    /// <param name="transport">Transport kind to use.</param>
    /// <param name="maxBytesPerSecond">Maximum bytes/sec quota (default infinite).</param>
    /// <param name="bytesPerSecondSmoothingWindowSeconds">Smoothing window over which to compute bytes/sec (default 5 sec.).</param>
    public RemoteExporter(Exporter exporter, int port = DefaultPort, TransportKind transport = DefaultTransport, long maxBytesPerSecond = long.MaxValue, double bytesPerSecondSmoothingWindowSeconds = 5.0)
        : this(exporter.Name, exporter.Path, port, transport, maxBytesPerSecond, bytesPerSecondSmoothingWindowSeconds)
    {
        Exporter = exporter;

        // add this as a node in the exporter so that it gets disposed
        exporter.GetOrCreateNode(this);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RemoteExporter"/> class.
    /// </summary>
    /// <param name="importer">Importer to be remoted.</param>
    /// <param name="port">TCP port on which to listen (default 11411).</param>
    /// <param name="transport">Transport kind to use.</param>
    /// <param name="maxBytesPerSecond">Maximum bytes/sec quota (default infinite).</param>
    /// <param name="bytesPerSecondSmoothingWindowSeconds">Smoothing window over which to compute bytes/sec (default 5 sec.).</param>
    public RemoteExporter(Importer importer, int port = DefaultPort, TransportKind transport = DefaultTransport, long maxBytesPerSecond = long.MaxValue, double bytesPerSecondSmoothingWindowSeconds = 5.0)
        : this(importer.StoreName, importer.StorePath, port, transport, maxBytesPerSecond, bytesPerSecondSmoothingWindowSeconds)
    {
        // used to remote an existing store. this.Exporter remains null

        // add this as a node in the importer so that it gets disposed
        importer.GetOrCreateNode(this);
    }

    private RemoteExporter(string name, string path, int port, TransportKind transport, long maxBytesPerSecond, double bytesPerSecondSmoothingWindowSeconds)
    {
        _name = name;
        _path = path;
        _port = port;
        _transport = transport;
        _metaListener = new TcpListener(IPAddress.Any, _port);
        _dataTransport = Transport.TransportOfKind(transport);
        _maxBytesPerSecond = maxBytesPerSecond;
        _bytesPerSecondSmoothingWindowSeconds = bytesPerSecondSmoothingWindowSeconds;

        _metaClientThread = new Thread(new ThreadStart(AcceptMetaClientsBackground)) { IsBackground = true };
        _metaClientThread.Start();
        _dataClientThread = new Thread(new ThreadStart(AcceptDataClientsBackground)) { IsBackground = true };
        _dataClientThread.Start();
    }

    /// <summary>
    /// Gets the TCP port being used.
    /// </summary>
    public int Port => _port;

    /// <summary>
    /// Gets the transport being used.
    /// </summary>
    public TransportKind TransportKind => _transport;

    /// <summary>
    /// Gets exporter being remoted.
    /// </summary>
    public Exporter Exporter { get; private set; }

    /// <summary>
    /// Dispose of remote exporter.
    /// </summary>
    public void Dispose()
    {
        _disposed = true;
        foreach (KeyValuePair<Guid, Connection> connection in _connections)
        {
            connection.Value.Dispose();
        }

        _connections = null;
        _metaClientThread = null;
        _dataClientThread = null;

        _metaListener.Stop();
        _dataTransport.Dispose();
    }

    private void AddConnection(Connection connection)
    {
        if (!_connections.TryAdd(connection.Id, connection))
        {
            throw new ArgumentException($"Remoting connection already exists (ID={connection.Id}");
        }
    }

    private void RemoveConnection(Guid id)
    {
        if (!_connections.TryRemove(id, out _))
        {
            throw new ArgumentException($"Remoting connection could not be removed (ID={id})");
        }
    }

    private void AcceptMetaClientsBackground()
    {
        try
        {
            _metaListener.Start();
            while (!_disposed)
            {
                TcpClient client = _metaListener.AcceptTcpClient();
                Connection connection = null;
                try
                {
                    connection = new Connection(client, _dataTransport, _name, _path, RemoveConnection, Exporter, _maxBytesPerSecond, _bytesPerSecondSmoothingWindowSeconds);
                    AddConnection(connection);
                    connection.Connect();
                    Trace.WriteLine($"RemoteExporter meta client accepted (ID={connection.Id})");
                }
                catch (Exception ex)
                {
                    Trace.TraceError($"RemoteExporter meta connection error (Message={ex.Message}, ID={connection?.Id})");
                    client.Dispose();
                }
            }
        }
        catch (SocketException se)
        {
            Trace.TraceError($"RemoteExporter meta listener error (Message={se.Message})");
        }
    }

    private void AcceptDataClientsBackground()
    {
        try
        {
            _dataTransport.StartListening();
            while (!_disposed)
            {
                ITransportClient client = _dataTransport.AcceptClient();
                Guid guid = Guid.Empty;
                try
                {
                    guid = client.ReadSessionId();
                    Trace.WriteLine($"RemoteExporter data client accepted (ID={guid})");

                    if (_connections.TryGetValue(guid, out Connection connection))
                    {
                        connection.JoinBackground(client);
                    }
                    else
                    {
                        throw new IOException($"RemoteExporter error: Invalid remoting connection ID: {guid}");
                    }
                }
                catch (Exception ex)
                {
                    Trace.TraceError($"RemoteExporter data connection error (Message={ex.Message}, ID={guid})");
                    client.Dispose();
                }
            }
        }
        catch (SocketException se)
        {
            Trace.TraceError($"RemoteExporter data transport error (Message={se.Message})");
        }
    }

    private sealed class Connection : IDisposable
    {
        private readonly Guid _id;
        private readonly ITransport _dataTransport;
        private readonly Action<Guid> _onDisconnect;
        private readonly Exporter _exporter;
        private readonly long _maxBytesPerSecond;
        private readonly double _bytesPerSecondSmoothingWindowSeconds;

        private readonly string _storeName;
        private readonly string _storePath;

        private TcpClient _client;
        private Stream _stream;
        private PsiStoreReader _storeReader;
        private TimeInterval _interval;

        public Connection(TcpClient client, ITransport dataTransport, string name, string path, Action<Guid> onDisconnect, Exporter exporter, long maxBytesPerSecond, double bytesPerSecondSmoothingWindowSeconds)
        {
            _id = Guid.NewGuid();
            _client = client;
            _stream = client.GetStream();
            _dataTransport = dataTransport;
            _storeName = name;
            _storePath = path;
            _onDisconnect = onDisconnect;
            _exporter = exporter;
            _maxBytesPerSecond = maxBytesPerSecond;
            _bytesPerSecondSmoothingWindowSeconds = bytesPerSecondSmoothingWindowSeconds;
        }

        public Guid Id => _id;

        public void Connect()
        {
            try
            {
                // check client version
                byte[] buffer = new byte[128];
                Transport.Read(buffer, sizeof(short), _stream);
                BufferReader reader = new(buffer);
                short version = reader.ReadInt16();
                if (version != ProtocolVersion)
                {
                    throw new IOException($"Unsupported remoting protocol version: {version}");
                }

                // get replay info
                int length = sizeof(long) + sizeof(long); // start ticks, end ticks
                Transport.Read(buffer, length, _stream);
                reader.Reset();

                // get replay interval
                long startTicks = reader.ReadInt64();
                if (startTicks == -1)
                {
                    // special indication of `DateTime.UtcNow` at the exporter end
                    startTicks = DateTime.UtcNow.Ticks;
                }

                DateTime start = new(startTicks);
                DateTime end = new(reader.ReadInt64());
                _interval = new TimeInterval(start, end);

                // send ID, stream count, transport and protocol params
                BufferWriter writer = new(buffer);
                writer.Write(0); // length placeholder
                writer.Write(_id.ToByteArray());
                writer.Write(_dataTransport.Transport.ToString());
                _dataTransport.WriteTransportParams(writer);
                int len = writer.Position;
                writer.Reset();
                writer.Write(len - 4);
                _stream.Write(writer.Buffer, 0, len);
                _storeReader = new PsiStoreReader(_storeName, _storePath, MetaUpdateHandler, true);
            }
            catch (Exception)
            {
                Disconnect();
                throw;
            }
        }

        public void JoinBackground(ITransportClient client)
        {
            double avgBytesPerSec = 0;
            DateTime lastTime = DateTime.MinValue;
            byte[] buffer = new byte[0];
            long envelopeSize;
            unsafe
            {
                envelopeSize = sizeof(Envelope);
            }

            _storeReader.Seek(_interval);

            while (true)
            {
                if (_storeReader.MoveNext(out Envelope envelope))
                {
                    int length = _storeReader.Read(ref buffer);
                    _exporter.Throttle.Reset();
                    try
                    {
                        client.WriteMessage(envelope, buffer);
                        if (lastTime > DateTime.MinValue /* at least second message */)
                        {
                            if (_maxBytesPerSecond < long.MaxValue)
                            {
                                // throttle to arbitrary max BPS
                                double elapsed = (envelope.OriginatingTime - lastTime).TotalSeconds;
                                double bytesPerSec = (envelopeSize + length) / elapsed;
                                double smoothingFactor = 1.0 / (_bytesPerSecondSmoothingWindowSeconds / elapsed);
                                avgBytesPerSec = (bytesPerSec * smoothingFactor) + (avgBytesPerSec * (1.0 - smoothingFactor));
                                if (bytesPerSec > _maxBytesPerSecond)
                                {
                                    int wait = (int)(((avgBytesPerSec / _maxBytesPerSecond) - elapsed) * 1000.0);
                                    if (wait > 0)
                                    {
                                        Thread.Sleep(wait);
                                    }
                                }
                            }
                        }

                        lastTime = envelope.OriginatingTime;
                    }
                    finally
                    {
                        // writers continue upon failure - meanwhile, remote client may reconnect and resume based on replay interval
                        _exporter.Throttle.Set();
                    }
                }
            }
        }

        public void Dispose()
        {
            _storeReader.Dispose();
            _storeReader = null;
            _client.Dispose();
            _client = null;
            _stream.Dispose();
            _stream = null;
        }

        private void Disconnect()
        {
            _onDisconnect(_id);
            Dispose();
        }

        private void MetaUpdateHandler(IEnumerable<Metadata> meta, RuntimeInfo runtimeInfo)
        {
            try
            {
                if (_client.Connected)
                {
                    BufferWriter writer = new(0);
                    foreach (Metadata m in meta)
                    {
                        writer.Reset();
                        writer.Write(0); // length placeholder
                        m.Serialize(writer);
                        int len = writer.Position;
                        writer.Reset();
                        writer.Write(len - 4);
                        _stream.Write(writer.Buffer, 0, len);
                        Trace.WriteLine($"RemoteExporter meta update (Name={m.Name}, ID={_id})");
                    }

                    writer.Reset();
                    writer.Write(0); // burst "intermission" marker
                    _stream.Write(writer.Buffer, 0, writer.Position);
                    Trace.WriteLine($"RemoteExporter meta intermission (ID={_id})");
                }
                else
                {
                    Trace.WriteLine($"RemoteExporter connection closed (ID={_id})");
                    Disconnect();
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"RemoteExporter connection error (Message={ex.Message}, ID={_id})");
                Disconnect();
            }
        }
    }
}
