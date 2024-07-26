// <copyright file="RemoteImporter.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;
using Neutrino.Psi.Common;
using Neutrino.Psi.Common.Intervals;
using Neutrino.Psi.Data;
using Neutrino.Psi.Executive;
using Neutrino.Psi.Persistence;
using Neutrino.Psi.Serialization;

namespace Neutrino.Psi.Remoting;

/// <summary>
/// Importer for remoting over network transport.
/// </summary>
public sealed class RemoteImporter : IDisposable
{
    private readonly Func<string, Importer> _importerThunk;
    private readonly long _replayEnd;
    private readonly string _host;
    private readonly int _port;
    private readonly bool _allowSequenceRestart;
    private readonly EventWaitHandle _connected = new(false, EventResetMode.ManualReset);

    private readonly bool _replayRemoteLatestStart; // special replayStart of `DateTime.UtcNow` at exporter side
    private readonly Dictionary<int, int> _lastSequenceIdPerStream = new();

    private PsiStoreWriter _storeWriter;
    private long _replayStart; // advanced upon each message for restart
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="RemoteImporter"/> class.
    /// </summary>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="replay">Time interval to be replayed from remote source.</param>
    /// <param name="host">Remote host name.</param>
    /// <param name="port">TCP port on which to connect (default 11411).</param>
    /// <param name="allowSequenceRestart">Whether to allow sequence ID restarts upon connection loss/reacquire.</param>
    public RemoteImporter(Pipeline pipeline, TimeInterval replay, string host, int port = RemoteExporter.DefaultPort, bool allowSequenceRestart = true)
        : this(name => PsiStore.Open(pipeline, name, null), replay, false, host, port, $"RemoteImporter_{Guid.NewGuid()}", null, allowSequenceRestart)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RemoteImporter"/> class.
    /// </summary>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="replayEnd">End of time interval to be replayed from remote.</param>
    /// <param name="host">Remote host name.</param>
    /// <param name="port">TCP port on which to connect (default 11411).</param>
    /// <param name="allowSequenceRestart">Whether to allow sequence ID restarts upon connection loss/reacquire.</param>
    /// <remarks>In this case the start is a special behavior that is `DateTime.UtcNow` _at the sending `RemoteExporter`_.</remarks>
    public RemoteImporter(Pipeline pipeline, DateTime replayEnd, string host, int port = RemoteExporter.DefaultPort, bool allowSequenceRestart = true)
        : this(name => PsiStore.Open(pipeline, name, null), new TimeInterval(DateTime.MinValue, replayEnd), true, host, port, $"RemoteImporter_{Guid.NewGuid()}", null, allowSequenceRestart)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RemoteImporter"/> class.
    /// </summary>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="host">Remote host name.</param>
    /// <param name="port">TCP port on which to connect (default 11411).</param>
    /// <param name="allowSequenceRestart">Whether to allow sequence ID restarts upon connection loss/reacquire.</param>
    /// <remarks>In this case the start is a special behavior that is `DateTime.UtcNow` _at the sending `RemoteExporter`_.</remarks>
    public RemoteImporter(Pipeline pipeline, string host, int port = RemoteExporter.DefaultPort, bool allowSequenceRestart = true)
        : this(name => PsiStore.Open(pipeline, name, null), new TimeInterval(DateTime.MinValue, DateTime.MaxValue), true, host, port, $"RemoteImporter_{Guid.NewGuid()}", null, allowSequenceRestart)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RemoteImporter"/> class.
    /// </summary>
    /// <param name="importer">Importer to receive remoted streams.</param>
    /// <param name="replay">Time interval to be replayed from remote source.</param>
    /// <param name="host">Remote host name.</param>
    /// <param name="port">TCP port on which to connect (default 11411).</param>
    /// <param name="allowSequenceRestart">Whether to allow sequence ID restarts upon connection loss/reacquire.</param>
    public RemoteImporter(Importer importer, TimeInterval replay, string host, int port = RemoteExporter.DefaultPort, bool allowSequenceRestart = true)
        : this(_ => importer, replay, false, host, port, importer.StoreName, importer.StorePath, allowSequenceRestart)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RemoteImporter"/> class.
    /// </summary>
    /// <param name="importer">Importer to receive remoted streams.</param>
    /// <param name="replayEnd">End of time interval to be replayed from remote.</param>
    /// <param name="host">Remote host name.</param>
    /// <param name="port">TCP port on which to connect (default 11411).</param>
    /// <param name="allowSequenceRestart">Whether to allow sequence ID restarts upon connection loss/reacquire.</param>
    /// <remarks>In this case the start is a special behavior that is `DateTime.UtcNow` _at the sending `RemoteExporter`_.</remarks>
    public RemoteImporter(Importer importer, DateTime replayEnd, string host, int port = RemoteExporter.DefaultPort, bool allowSequenceRestart = true)
        : this(_ => importer, new TimeInterval(DateTime.MinValue, replayEnd), true, host, port, importer.StoreName, importer.StorePath, allowSequenceRestart)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RemoteImporter"/> class.
    /// </summary>
    /// <param name="importer">Importer to receive remoted streams.</param>
    /// <param name="host">Remote host name.</param>
    /// <param name="port">TCP port on which to connect (default 11411).</param>
    /// <param name="allowSequenceRestart">Whether to allow sequence ID restarts upon connection loss/reacquire.</param>
    /// <remarks>In this case the start is a special behavior that is `DateTime.UtcNow` _at the sending `RemoteExporter`_.</remarks>
    public RemoteImporter(Importer importer, string host, int port = RemoteExporter.DefaultPort, bool allowSequenceRestart = true)
        : this(_ => importer, new TimeInterval(DateTime.MinValue, DateTime.MaxValue), true, host, port, importer.StoreName, importer.StorePath, allowSequenceRestart)
    {
    }

    private RemoteImporter(
        Func<string, Importer> importerThunk,
        TimeInterval replay,
        bool replayRemoteLatestStart,
        string host,
        int port,
        string storeName,
        string storePath,
        bool allowSequenceRestart)
    {
        _importerThunk = importerThunk;
        _replayStart = replay.Left.Ticks;
        _replayEnd = replay.Right.Ticks;
        _replayRemoteLatestStart = replayRemoteLatestStart;
        _host = host;
        _port = port;
        _allowSequenceRestart = allowSequenceRestart;
        _storeWriter = new PsiStoreWriter(storeName, storePath);
        StartMetaClient();
    }

    /// <summary>
    /// Gets importer receiving remoted streams.
    /// </summary>
    public Importer Importer { get; private set; }

    /// <summary>
    /// Gets wait handle for remote connection being established.
    /// </summary>
    /// <remarks>This should be waited on before opening streams.</remarks>
    public EventWaitHandle Connected => _connected;

    /// <summary>
    /// Dispose of remote importer.
    /// </summary>
    public void Dispose()
    {
        _disposed = true;

        _storeWriter.Dispose();
        _storeWriter = null;

        _connected.Dispose();
    }

    private void StartMetaClient()
    {
        if (_disposed)
        {
            return;
        }

        // data client will be started once GUID and transport are known
        _connected.Reset();
        Thread thread = new(new ThreadStart(MetaClientBackground)) { IsBackground = true };
        thread.Start();
    }

    private void MetaClientBackground()
    {
        Guid guid = Guid.Empty;
        try
        {
            TcpClient metaClient = new();
            metaClient.Connect(_host, _port);
            NetworkStream metaStream = metaClient.GetStream();

            // send protocol version and replay interval
            byte[] buffer = new byte[256];
            BufferWriter writer = new(buffer);
            writer.Write(RemoteExporter.ProtocolVersion);
            writer.Write(_replayRemoteLatestStart ? -1 : _replayStart);
            writer.Write(_replayEnd);
            metaStream.Write(writer.Buffer, 0, writer.Position);

            // receive ID and transport info
            BufferReader reader = new(buffer);
            Transport.Read(reader.Buffer, 4, metaStream);
            int len = reader.ReadInt32();
            reader.Reset(len);
            Transport.Read(reader.Buffer, len, metaStream);
            byte[] id = new byte[16];
            reader.Read(id, id.Length);
            ITransport transport = Transport.TransportOfName(reader.ReadString());
            transport.ReadTransportParams(reader);
            guid = new Guid(id);
            Trace.WriteLine($"{nameof(RemoteImporter)} meta client connected (ID={guid})");

            // process metadata updates
            while (!_disposed)
            {
                reader.Reset(sizeof(int));
                Transport.Read(reader.Buffer, sizeof(int), metaStream);
                int metalen = reader.ReadInt32();
                if (metalen > 0)
                {
                    reader.Reset(metalen);
                    Transport.Read(reader.Buffer, metalen, metaStream);
                    Metadata meta = Metadata.Deserialize(reader);
                    if (meta.Kind == MetadataKind.StreamMetadata)
                    {
                        try
                        {
                            _storeWriter.OpenStream((PsiStreamMetadata)meta);
                        }
                        catch (Exception ex)
                        {
                            Trace.TraceError($"{nameof(RemoteImporter)} meta update duplicate stream - expected after reconnect (Name={meta.Name}, ID={guid}, Error={ex.Message})");
                        }
                    }
                    else if (meta.Kind == MetadataKind.RuntimeInfo)
                    {
                        _storeWriter.WriteToCatalog((RuntimeInfo)meta);
                    }
                    else if (meta.Kind == MetadataKind.TypeSchema)
                    {
                        _storeWriter.WriteToCatalog((TypeSchema)meta);
                    }
                    else
                    {
                        throw new NotSupportedException("Unknown metadata kind.");
                    }

                    Trace.WriteLine($"{nameof(RemoteImporter)} meta update (Name={meta.Name}, ID={guid})");
                }
                else
                {
                    // "intermission" in meta updates
                    Importer = _importerThunk(_storeWriter.Name); // now that we have a populated catalog
                    _storeWriter.InitializeStreamOpenedTimes(Importer.GetCurrentTime());
                    StartDataClient(guid, transport);
                    _connected.Set();
                }
            }
        }
        catch (ArgumentException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"{nameof(RemoteImporter)} meta connection error (Message={ex.Message}, ID={guid})");
            StartMetaClient(); // restart
        }
    }

    private void StartDataClient(Guid id, ITransport transport)
    {
        if (_disposed)
        {
            return;
        }

        ITransportClient dataClient = transport.Connect(_host);
        dataClient.WriteSessionId(id);

        Thread thread = new(new ThreadStart(() =>
        {
            try
            {
                while (!_disposed)
                {
                    Tuple<Envelope, byte[]> data = dataClient.ReadMessage();
                    Envelope envelope = data.Item1;
                    byte[] message = data.Item2;

                    _replayStart = envelope.OriginatingTime.Ticks + 1; // for restart

                    if (_allowSequenceRestart)
                    {
                        // patch sequence ID resents (due to exporter process restart)
                        int sourceId = envelope.SourceId;
                        int sequenceId = envelope.SequenceId;
                        if (!_lastSequenceIdPerStream.ContainsKey(sourceId))
                        {
                            _lastSequenceIdPerStream.Add(sourceId, sequenceId - 1); // tracking new source
                        }

                        int lastSequenceId = _lastSequenceIdPerStream[sourceId];
                        if (lastSequenceId >= sequenceId)
                        {
                            sequenceId = lastSequenceId + 1;
                            envelope = new Envelope(envelope.OriginatingTime, envelope.CreationTime, sourceId, sequenceId);
                        }

                        _lastSequenceIdPerStream[sourceId] = sequenceId;
                    }

                    _storeWriter.Write(new BufferReader(message), envelope);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"{nameof(RemoteImporter)} data connection error (Message={ex.Message}, ID={id})");
                dataClient.Dispose();
            }
        }))
        { IsBackground = true };
        thread.Start();
    }
}
