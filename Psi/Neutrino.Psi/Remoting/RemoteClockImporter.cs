// <copyright file="RemoteClockImporter.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading;

namespace Neutrino.Psi.Remoting;

/// <summary>
/// Component that reads remote clock information over TCP and synchronizes the local pipeline clock.
/// </summary>
public class RemoteClockImporter : IDisposable
{
    private readonly Pipeline _pipeline;
    private readonly string _name;
    private readonly string _host;
    private readonly int _port;
    private readonly TcpClient _client;
    private readonly EventWaitHandle _connected = new(false, EventResetMode.ManualReset);

    /// <summary>
    /// Initializes a new instance of the <see cref="RemoteClockImporter"/> class.
    /// </summary>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="host">The host name of the remote clock exporter/server.</param>
    /// <param name="port">The port on which to connect.</param>
    /// <param name="name">An optional name for the component.</param>
    public RemoteClockImporter(Pipeline pipeline, string host, int port = RemoteClockExporter.DefaultPort, string name = nameof(RemoteClockImporter))
    {
        _pipeline = pipeline;
        _name = name;
        _client = new TcpClient();
        _host = host;
        _port = port;
        _connected.Reset();
        new Thread(new ThreadStart(SynchronizeLocalPipelineClock)) { IsBackground = true }.Start();
    }

    /// <summary>
    /// Gets wait handle for remote connection being established.
    /// </summary>
    /// <remarks>This should be waited on prior to running the pipeline.</remarks>
    public EventWaitHandle Connected => _connected;

    /// <summary>
    /// Gets or sets machine with which to synchronize pipeline clock.
    /// </summary>
    internal static string PrimaryClockSourceMachineName { get; set; } = string.Empty;

    /// <inheritdoc/>
    public void Dispose()
    {
        _client.Close();
        _connected.Dispose();
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return _name;
    }

    private void SynchronizeLocalPipelineClock()
    {
        bool completed = false;
        while (!completed)
        {
            NetworkStream networkStream = null;
            try
            {
                Trace.WriteLine($"Attempting to connect to {_host} on port {_port} ...");
                _client.Connect(_host, _port);
                networkStream = _client.GetStream();
                Trace.WriteLine($"Connected to {_host} on port {_port}.");

                // send protocol version
                using BinaryWriter writer = new(networkStream);
                Stopwatch stopwatch = new();
                stopwatch.Start();
                writer.Write(RemoteClockExporter.ProtocolVersion);

                using BinaryReader reader = new(networkStream);
                long timeAtExporter = reader.ReadInt64();
                stopwatch.Stop();
                long timeAtImporter = DateTime.UtcNow.Ticks;
                long elapsedTime = stopwatch.ElapsedTicks;
                string machine = reader.ReadString();

                // Elapsed time includes the complete round trip latency between writing the header and receiving the
                // remote (exporter) machine's time. We assume that half of the time was from here to the exporter, meaning
                // that subtracting elapsed / 2 from our current time gives the time as it was on our clock when the exporter
                // sent it's time. The difference becomes an offset to apply to our pipeline clock to synchronize.
                TimeSpan timeOffset = TimeSpan.FromTicks(timeAtExporter - (timeAtImporter - (elapsedTime / 2)));
                Trace.WriteLine($"{nameof(RemoteClockImporter)} clock sync: Local={timeAtImporter} Remote[{machine}]={timeAtExporter} Latency={elapsedTime} Offset={timeOffset.Ticks}.");
                if (machine == Environment.MachineName)
                {
                    // The "remote" machine is actually *this* machine. In this case, assume exactly zero offset.
                    Trace.WriteLine($"{nameof(RemoteClockImporter)} clock sync with self ignored ({machine}). Pipeline clock will remain unchanged.");
                    timeOffset = TimeSpan.Zero;
                }
                else if (RemoteClockExporter.IsPrimaryClockSourceMachine)
                {
                    // An exporter on this machine already thinks that *this* is the primary source, but this importer
                    // is attempting to synchronize with some other machine instead!
                    throw new ArgumentException(
                        $"{nameof(RemoteClockImporter)} treating remote machine ({machine}) as the primary clock source, but this machine ({Environment.MachineName}) is already the " +
                        $"primary. There may be only one machine hosting the primary clock. Check {nameof(RemoteClockImporter)} configurations.");
                }

                if (PrimaryClockSourceMachineName != machine && PrimaryClockSourceMachineName.Length > 0)
                {
                    // Another importer on this machine has already negotiated a clock sync with some machine other than
                    // the one that this importer is syncing with. Importers disagree as to who the primary should be!
                    throw new ArgumentException(
                        $"{nameof(RemoteClockImporter)} treating remote machine ({machine}) as the primary clock source, but another {nameof(RemoteClockImporter)} " +
                        $"is treating a different remote machine ({PrimaryClockSourceMachineName}) as the primary. " +
                        $"There may be only one machine hosting the primary clock. Check {nameof(RemoteClockImporter)} configurations.");
                }

                // synchronize pipeline clock
                _pipeline.VirtualTimeOffset = timeOffset;
                _connected.Set();
                completed = true;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"{nameof(RemoteClockImporter)} Exception: {ex.Message}");
            }
            finally
            {
                networkStream?.Dispose();
                _client.Close();
            }
        }
    }
}
