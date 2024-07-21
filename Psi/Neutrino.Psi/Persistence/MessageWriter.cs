// <copyright file="MessageWriter.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using Neutrino.Psi.Common;

namespace Neutrino.Psi.Persistence;

/// <summary>
/// Writes message blocks to an infinite file
/// The Write methods are thread safe, allowing shared use of one message writer from multiple threads.
/// </summary>
internal sealed class MessageWriter : IDisposable
{
    private const int DefaultExtentCapacity64 = 256 * 1024 * 1024;
    private const int DefaultExtentCapacity32 = 32 * 1024 * 1024;
    private const int DefaultRetentionQueueLength64 = 6;
    private const int DefaultRetentionQueueLength32 = 0;
    private InfiniteFileWriter _fileWriter;

    public MessageWriter(string name, string path, int extentSize = 0)
    {
        if (extentSize == 0)
        {
            extentSize = Environment.Is64BitProcess ? DefaultExtentCapacity64 : DefaultExtentCapacity32;
        }

        if (path != null)
        {
            _fileWriter = new InfiniteFileWriter(path, name, extentSize);
        }
        else
        {
            int retentionQueueLength = Environment.Is64BitProcess ? DefaultRetentionQueueLength64 : DefaultRetentionQueueLength32;
            _fileWriter = new InfiniteFileWriter(name, extentSize, retentionQueueLength);
        }
    }

    public string FileName => _fileWriter.FileName;

    public string Path => _fileWriter.Path;

    public int CurrentExtentId => _fileWriter.CurrentExtentId;

    public int CurrentMessageStart => _fileWriter.CurrentBlockStart;

    public int Write(Envelope envelope, byte[] source)
    {
        return Write(envelope, source, 0, source.Length);
    }

    public int Write(BufferReader buffer, Envelope envelope)
    {
        return Write(envelope, buffer.Buffer, buffer.Position, buffer.RemainingLength);
    }

    public int Write(Envelope envelope, byte[] source, int start, int count)
    {
        // for now, lock. To get rid of it we need to split an ExtentWriter out of the InfiniteFileWriter
        lock (_fileWriter)
        {
            unsafe
            {
                int totalBytes = sizeof(Envelope) + count;
                _fileWriter.ReserveBlock(totalBytes);
                _fileWriter.WriteToBlock((byte*)&envelope, sizeof(Envelope));
                _fileWriter.WriteToBlock(source, start, count);
                _fileWriter.CommitBlock();
                return totalBytes;
            }
        }
    }

    public int Write(BufferWriter buffer, Envelope envelope)
    {
        return Write(envelope, buffer.Buffer, 0, buffer.Position);
    }

    public void Dispose()
    {
        _fileWriter.Dispose();
        _fileWriter = null;
    }
}
