// <copyright file="InfiniteFileWriter.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Psi.Common;

namespace Microsoft.Psi.Persistence;

internal unsafe sealed class InfiniteFileWriter : IDisposable
{
    internal const string FileNameFormat = "{0}_{1:000000}.psi";
    private const string PulseEventFormat = @"Global\PulseEvent_{0}_{1}";
    private readonly object _syncRoot = new();
    private string _extentName;
    private int _extentSize;
    private byte* _startPointer;
    private byte* _freePointer;
    private byte* _currentPointer;
    private byte* _currentBlock;
    private int _currentBlockSize;
    private int _remainingAllocationSize;
    private MemoryMappedFile _mappedFile;
    private MemoryMappedViewAccessor _view;
    private int _fileId;
    private int _freeSpace;
    private bool _disposed = false;
    private EventWaitHandle _localWritePulse;
    private Mutex _globalWritePulse;
    private Queue<MemoryMappedFile> _priorExtents;
    private readonly int _priorExtentQueueLength;
    private readonly object _viewDisposeLock = new();

    public InfiniteFileWriter(string fileName, int extentSize, int retentionQueueLength)
        : this(null, fileName, extentSize)
    {
        _priorExtentQueueLength = retentionQueueLength;
        _priorExtents = new Queue<MemoryMappedFile>(retentionQueueLength);
    }

    public InfiniteFileWriter(string path, string fileName, int extentSize)
    {
        Path = path;
        FileName = fileName;
        _extentSize = extentSize + sizeof(int); // eof marker
        _localWritePulse = new EventWaitHandle(false, EventResetMode.ManualReset);
        new Thread(new ThreadStart(() =>
        {
            try
            {
                _globalWritePulse = new Mutex(true, PulseEventName(path, fileName));
            }
            catch (UnauthorizedAccessException)
            {
                // Some platforms don't allow global mutexes.  In this case
                // we can still continue on with a slight perf degradation.
            }

            try
            {
                while (!_disposed)
                {
                    _localWritePulse?.WaitOne();
                    _globalWritePulse?.ReleaseMutex();
                    _globalWritePulse?.WaitOne();
                }
            }
            catch (ObjectDisposedException)
            {
                // ignore if localWritePulse was disposed
            }
            catch (AbandonedMutexException)
            {
                // ignore if globalWritePulse was disposed
            }
        }))
        { IsBackground = true }.Start();

        CreateNewExtent();
    }

    public string FileName { get; }

    public string Path { get; }

    public bool IsVolatile => Path == null;

    public int CurrentExtentId => _fileId - 1;

    public int CurrentBlockStart => (int)(_freePointer - _startPointer);

    public void Dispose()
    {
        CloseCurrent(true);
        if (_priorExtentQueueLength > 0)
        {
            foreach (MemoryMappedFile extent in _priorExtents)
            {
                extent.Dispose();
            }

            _priorExtents = null;
        }

        _disposed = true;
        _localWritePulse.Set();
        _localWritePulse.Dispose();
        _localWritePulse = null;
        _globalWritePulse?.Dispose();
        _globalWritePulse = null;

        // may have already been disposed in CloseCurrent
        _view?.Dispose();
    }

    public void Write(BufferWriter bufferWriter)
    {
        ReserveBlock(bufferWriter.Position);
        WriteToBlock(bufferWriter.Buffer, 0, bufferWriter.Position);
        CommitBlock();
    }

    public void WriteToBlock(byte[] source)
    {
        unsafe
        {
            fixed (byte* b = source)
            {
                WriteToBlock(b, source.Length);
            }
        }
    }

    public void WriteToBlock(byte[] source, int start, int count)
    {
        if (count == 0)
        {
            return;
        }

        if (start + count > source.Length)
        {
            throw new InvalidOperationException("Attempted to read beyond the end of the source buffer.");
        }

        fixed (byte* b = &source[start])
        {
            WriteToBlock(b, count);
        }
    }

    public void WriteToBlock(byte* source, int bytes)
    {
        Buffer.MemoryCopy(source, _currentPointer, _remainingAllocationSize, bytes); // this performs the bounds check
        _currentPointer += bytes;
        _remainingAllocationSize -= bytes;
    }

    public void ReserveBlock(int bytes)
    {
        // pad the block to guarantee atomicity of write/read of block markers
        int padding = 4 - (bytes % 4);
        if (padding != 4)
        {
            bytes += padding;
        }

        int totalBytes = bytes + sizeof(int);
        if (_freeSpace < totalBytes)
        {
            // we don't break the data across extents, to ensure extents are independently readable
            if (_extentSize < totalBytes)
            {
                _extentSize = totalBytes;
            }

            CreateNewExtent();
        }

        // remember the start of the block
        _currentBlock = _freePointer;
        _currentBlockSize = bytes;

        // remember the start of the free space
        _currentPointer = _freePointer + sizeof(int);
        _remainingAllocationSize = bytes;

        _freePointer += totalBytes;
        _freeSpace -= totalBytes;

        // write the tail marker but don't move the pointer. Simply let the next write override it, that's how we know there is more data when reading.
        *(uint*)_freePointer = 0;
    }

    public void CommitBlock()
    {
        // write the header. This MUST be 32-bit aligned (this is achieved by padding the reserved block size to multiples of 4)
        *(int*)_currentBlock = _currentBlockSize;
        _localWritePulse.Set();
        _localWritePulse.Reset();
    }

    /// <summary>
    /// Clears all buffers for this view and causes any buffered data to be written to the underlying file,
    /// by calling <see cref="MemoryMappedViewAccessor.Flush"/>.
    /// Warning: calling this function can make the persistence system less efficient,
    /// diminishing the overall throughput.
    /// </summary>
    public void Flush()
    {
        _view.Flush();
    }

    internal static string PulseEventName(string path, string fileName)
    {
        return MakeHandleName(PulseEventFormat, path, fileName);
    }

    private static string MakeHandleName(string format, string path, string fileName)
    {
        string name = string.Format(format, path?.ToLower().GetDeterministicHashCode(), fileName.ToLower());
        if (name.Length > 260)
        {
            // exceeded the name length limit
            return string.Format(format, path?.ToLower().GetDeterministicHashCode(), fileName.ToLower().GetDeterministicHashCode());
        }

        return name;
    }

    private void CreateNewExtent()
    {
        int newFileId = _fileId;
        _fileId++;
        _extentName = string.Format(FileNameFormat, FileName, newFileId);

        // create a new file first, just in case anybody is reading
        MemoryMappedFile newMMF;
        if (!IsVolatile)
        {
            _extentName = System.IO.Path.Combine(Path, _extentName);
            FileStream file = File.Open(_extentName, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
            try
            {
                newMMF = MemoryMappedFile.CreateFromFile(file, null, _extentSize, MemoryMappedFileAccess.ReadWrite, HandleInheritability.Inheritable, false);
            }
            catch (IOException)
            {
                file.Dispose();
                throw;
            }
        }
        else
        {
            newMMF = MemoryMappedFile.CreateNew(_extentName, _extentSize);
        }

        // store the id of the new file in the old file and close it
        if (_mappedFile != null)
        {
            // the id of the next file is encoded as a negative value to mark the end of the current file
            *(int*)_freePointer = -newFileId;
            CloseCurrent(false);
        }

        // re-initialize
        _mappedFile = newMMF;
        _view = _mappedFile.CreateViewAccessor(0, 0, MemoryMappedFileAccess.ReadWrite);
        _view.SafeMemoryMappedViewHandle.AcquirePointer(ref _startPointer);
        _freeSpace = _extentSize - sizeof(int);
        _freePointer = _startPointer;
        *(uint*)_freePointer = 0;
    }

    // closes the current extent, and trims the file if requested (should only be requested for the very last extent, when Disposing).
    private void CloseCurrent(bool disposing)
    {
        if (_mappedFile != null)
        {
            _view.SafeMemoryMappedViewHandle.ReleasePointer();

            // Calling `view.Dispose()` flushes the underlying MemoryMappedView, in turn making
            // blocking system calls and with retry logic and `Thread.Sleep()`s.
            // To avoid taking this hit on our thread here (which blocks writing to the infinite file for
            // human-noticeable time when crossing extents), we queue this work to the thread pool.
            // See: https://referencesource.microsoft.com/#System.Core/System/IO/MemoryMappedFiles/MemoryMappedView.cs,176
            MemoryMappedViewAccessor temp = _view;
            Task viewDisposeTask = Task.Run(() =>
            {
                // serialize disposal to avoid disk thrashing
                lock (_viewDisposeLock)
                {
                    temp.Dispose();
                }
            });

            _view = null;

            if (_priorExtentQueueLength > 0 && !disposing)
            {
                // if the queue reached its limit, remove one item first
                if (_priorExtents.Count == _priorExtentQueueLength)
                {
                    MemoryMappedFile pe = _priorExtents.Dequeue();
                    pe.Dispose();
                }

                _priorExtents.Enqueue(_mappedFile);
            }
            else
            {
                _mappedFile.Dispose();
            }

            _mappedFile = null;

            if (disposing && !IsVolatile)
            {
                // need to wait for the view to be completely disposed before resizing the file,
                // otherwise there will be an access conflict and the file will not be resized
                viewDisposeTask.Wait();

                try
                {
                    using FileStream file = File.Open(_extentName, FileMode.Open, FileAccess.Write, FileShare.None);
                    // resize the file to a multiple of 4096 (page size)
                    int actualSize = _extentSize - _freeSpace;
                    file.SetLength(((actualSize >> 12) + 1) << 12);
                }
                catch (IOException)
                {
                    // ignore
                }
            }
        }

        _freeSpace = 0;
    }
}
