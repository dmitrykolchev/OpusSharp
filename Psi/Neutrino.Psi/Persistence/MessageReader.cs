// <copyright file="MessageReader.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Threading;
using Neutrino.Psi.Common;

namespace Neutrino.Psi.Persistence;

/// <summary>
/// Reads message blocks from an infinite file.
/// This class is not thread safe. It is the caller's responsibility to synchronize the calls to MoveNext and Read.
/// Concurrent read can still be achieved, by instantiating multiple message readers against the same file.
/// </summary>
internal sealed class MessageReader : IDisposable
{
    private InfiniteFileReader _fileReader;
    private Envelope _currentEnvelope;

    public MessageReader(string fileName, string path)
    {
        if (path == null)
        {
            _fileReader = new InfiniteFileReader(fileName);
        }
        else
        {
            _fileReader = new InfiniteFileReader(path, fileName);
        }
    }

    public Mutex DataReady => _fileReader.WritePulse;

    public string FileName => _fileReader.FileName;

    public string Path => _fileReader.Path;

    public Envelope Current => _currentEnvelope;

    public int CurrentExtentId => _fileReader.CurrentExtentId;

    public int CurrentMessageStart => _fileReader.CurrentBlockStart;

    public void Seek(int extentId, int position)
    {
        _fileReader.Seek(extentId, position);
    }

    public bool MoveNext(HashSet<int> ids)
    {
        bool hasData = MoveNext();
        while (hasData && !ids.Contains(_currentEnvelope.SourceId))
        {
            hasData = MoveNext();
        }

        return hasData;
    }

    public bool MoveNext()
    {
        Envelope e;
        if (!_fileReader.MoveNext())
        {
            return false;
        }

        unsafe
        {
            _fileReader.Read((byte*)&e, sizeof(Envelope));
            _currentEnvelope = e;
        }

        return true;
    }

    public int Read(ref byte[] buffer)
    {
        return _fileReader.ReadBlock(ref buffer);
    }

    public unsafe int Read(byte* buffer, int size)
    {
        return _fileReader.Read(buffer, size);
    }

    public void Dispose()
    {
        _fileReader?.Dispose();
        _fileReader = null;
    }
}
