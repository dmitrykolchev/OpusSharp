// <copyright file="RemoteClockExporter.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Microsoft.Psi.Remoting;

/// <summary>
/// Component that exports pipeline clock information over TCP to enable synchronization.
/// </summary>
public class RemoteClockExporter : IDisposable
{
    /// <summary>
    /// Default TCP port used to communicate with <see cref="RemoteClockImporter"/>.
    /// </summary>
    public const int DefaultPort = 11511;

    internal const short ProtocolVersion = 0;

    private TcpListener _listener;
    private bool _isDisposing;

    /// <summary>
    /// Initializes a new instance of the <see cref="RemoteClockExporter"/> class.
    /// </summary>
    /// <param name="port">The connection port.</param>
    public RemoteClockExporter(int port = DefaultPort)
    {
        Port = port;
        _listener = new TcpListener(IPAddress.Any, port);
        Start();
    }

    /// <summary>
    /// Gets the connection port.
    /// </summary>
    public int Port { get; }

    /// <summary>
    /// Gets or sets a value indicating whether this machine hosts the primary pipeline clock.
    /// </summary>
    internal static bool IsPrimaryClockSourceMachine { get; set; } = false;

    /// <inheritdoc/>
    public void Dispose()
    {
        _isDisposing = true;
        _listener.Stop();
        _listener = null;
    }

    private void Start()
    {
        new Thread(new ThreadStart(Listen)) { IsBackground = true }.Start();
    }

    private void Listen()
    {
        if (_listener != null)
        {
            NetworkStream networkStream = null;
            try
            {
                _listener.Start();
                networkStream = _listener.AcceptTcpClient().GetStream();

                // clock synchroniztion
                IsPrimaryClockSourceMachine = true;
                if (RemoteClockImporter.PrimaryClockSourceMachineName != Environment.MachineName &&
                    RemoteClockImporter.PrimaryClockSourceMachineName.Length > 0)
                {
                    // client intends to use this machine as the primary clock source. However, a
                    // RemoteClockImporter on this machine also intends to sync with some other machine!
                    throw new ArgumentException(
                        $"A {nameof(RemoteClockImporter)} on this machine is expecting the remote machine ({RemoteClockImporter.PrimaryClockSourceMachineName}) " +
                        $"to serve as the primary clock, but this machine is instead being asked to serve as the primary." +
                        $"There may be only one machine hosting the primary clock.");
                }

                // check protocol version
                using BinaryReader reader = new(networkStream);
                short version = reader.ReadInt16();
                if (version != ProtocolVersion)
                {
                    throw new IOException($"Unsupported remote clock protocol version: {version}");
                }

                using BinaryWriter writer = new(networkStream);
                writer.Write(DateTime.UtcNow.Ticks); // current machine time, used by client to sync clocks
                writer.Write(Environment.MachineName);
                writer.Flush();
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"{nameof(RemoteClockExporter)} Exception: {ex.Message}");
            }
            finally
            {
                networkStream?.Dispose();
                if (!_isDisposing)
                {
                    _listener.Stop();
                    Start();
                }
            }
        }
    }
}
