// <copyright file="Program.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System.Net.Sockets;
using System.Text;

namespace Server;

internal class Program
{
    static async Task Main(string[] args)
    {
        CancellationTokenSource tokenSource = new ();
        CancellationToken cancellationToken = tokenSource.Token;

        AudioServer server = new ();
        await server.StartAsync(cancellationToken);

    }
}
