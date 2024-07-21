// <copyright file="InfiniteFileReader.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace Neutrino.Psi.Persistence;

internal unsafe sealed class InfiniteFileReader : IDisposable
{
    private const int WriteEventTimeout = 1000; // ms
    private byte* _startPointer;
    private int _currentPosition;
    private int _currentBlockStart;
    private MemoryMappedFile _mappedFile;
    private MemoryMappedViewAccessor _view;
    private readonly string _path;
    private readonly string _fileName;
    private int _fileId;
    private readonly Mutex _writePulse;
    private int _remainingBlockSize;

    public InfiniteFileReader(string path, string fileName, int fileId = 0)
    {
        _path = path;
        _fileName = fileName;
        _fileId = fileId;
        Mutex.TryOpenExisting(InfiniteFileWriter.PulseEventName(path, fileName), out Mutex pulse);
        _writePulse = pulse ?? new Mutex(false);
    }

    public InfiniteFileReader(string name, int fileId = 0)
        : this(null, name, fileId)
    {
    }

    public Mutex WritePulse => _writePulse;

    public string FileName => _fileName;

    public string Path => _path;

    public int CurrentExtentId => _fileId;

    public int CurrentBlockStart => _currentBlockStart;

    /// <summary>
    /// Indicates whether the specified file is already loaded by a reader or writer.
    /// </summary>
    /// <param name="name">Infinite file name.</param>
    /// <param name="path">Infinite file path.</param>
    /// <returns>Returns true if the store is already loaded.</returns>
    public static bool IsActive(string name, string path)
    {
        if (!OperatingSystem.IsOSPlatform("windows"))
        {
            throw new NotSupportedException("OS is not supported");
        }
        if (!EventWaitHandle.TryOpenExisting(InfiniteFileWriter.PulseEventName(path, name), out EventWaitHandle eventHandle))
        {
            return false;
        }

        eventHandle.Dispose();
        return true;
    }

    public void Dispose()
    {
        _writePulse.Dispose();
        CloseCurrent();

        // may have already been disposed in CloseCurrent
        _view?.Dispose();
    }

    // Seeks to the next block (assumes the position points to a block entry)
    public void Seek(int extentId, int position)
    {
        if (_fileId != extentId || _startPointer == null)
        {
            CloseCurrent();
            _fileId = extentId;
            LoadNextExtent();
        }

        _currentPosition = position;
        _currentBlockStart = position;
        _remainingBlockSize = 0;
    }

    /// <summary>
    /// Returns true if we are in the middle of a block or
    /// if we are positioned at the start of the block and the block size prefix is greater than zero.
    /// If false, use <see cref="PsiStoreMonitor.IsStoreLive(string, string)"/> to determine if there could ever be more data
    /// (i.e. if a writer is still active).
    /// </summary>
    /// <returns>True if more data is present, false if no more data is available.</returns>
    public bool HasMoreData()
    {
        return _mappedFile == null || _remainingBlockSize != 0 || *(int*)(_startPointer + _currentPosition) != 0;
    }

    /// <summary>
    /// Prepares to read the next message if one is present.
    /// </summary>
    /// <returns>True if a message exists, false if no message is present.</returns>
    public bool MoveNext()
    {
        if (_startPointer == null)
        {
            LoadNextExtent();
        }

        _currentPosition += _remainingBlockSize;
        _currentBlockStart = _currentPosition;
        _remainingBlockSize = *(int*)(_startPointer + _currentPosition);
        if (_remainingBlockSize == 0)
        {
            // A zero block size means there is no more data to read for now. This
            // may change if more data is subsequently written to this extent, if
            // it is open for simultaneous reading/writing.
            return false;
        }

#if DEBUG
        // read twice to detect unaligned writes, which would mean we introduced a bug in the writer (unaligned writes mean we could read incomplete data).
        int check = *(int*)(_startPointer + _currentPosition);
        if (check != _remainingBlockSize)
        {
            throw new InvalidDataException();
        }
#endif
        _currentPosition += sizeof(int);

        // a negative remaining block size indicates we have reached the end of the extent
        if (_remainingBlockSize < 0)
        {
            // clear the start pointer and move to the next extent
            _startPointer = null;
            return MoveNext();
        }

        return true; // more data available
    }

    public int ReadBlock(ref byte[] target)
    {
        if (target == null || _remainingBlockSize > target.Length)
        {
            target = new byte[_remainingBlockSize];
        }

        fixed (byte* b = target)
        {
            return Read(b, _remainingBlockSize);
        }
    }

    public int Read(byte[] target, int bytesToRead)
    {
        if (_remainingBlockSize < bytesToRead)
        {
            bytesToRead = _remainingBlockSize;
        }

        fixed (byte* b = target)
        {
            return Read(b, bytesToRead);
        }
    }

    public int Read(byte* target, int bytes)
    {
        if (bytes > _remainingBlockSize)
        {
            throw new ArgumentException("Attempted to read past the end of the block.");
        }

        UncheckedRead(target, bytes);
        return bytes;
    }

    private void UncheckedRead(byte* target, int bytes)
    {
        Buffer.MemoryCopy(_startPointer + _currentPosition, target, bytes, bytes);
        _currentPosition += bytes;
        _remainingBlockSize -= bytes;
    }

    private void LoadNextExtent()
    {
        // If there is a current extent open, it means we have reached the EOF and remainingBlockSize
        // will be a negative number whose absolute value represents the next file extent id.
        if (_mappedFile != null)
        {
            // Get the fileId of the next extent to load and close the current extent.
            _fileId = -_remainingBlockSize;
            CloseCurrent();
        }

        string extentName = string.Format(InfiniteFileWriter.FileNameFormat, _fileName, _fileId);

        if (_path != null)
        {
            // create a new MMF from persisted file, if the file can be found
            string fullName = System.IO.Path.Combine(_path, extentName);
            if (File.Exists(fullName))
            {
                int maxAttempts = 5;
                int attempts = 0;

                // Retry opening the file up to a maximum number of attempts - this is to handle the possible race
                // condition where the file is being resized on disposal (see InfiniteFileWriter.CloseCurrent),
                // which will result in an IOException being thrown if we attempt to open the file simultaneously.
                while (_mappedFile == null && attempts++ < maxAttempts)
                {
                    try
                    {
                        FileStream file = File.Open(fullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        _mappedFile = MemoryMappedFile.CreateFromFile(file, null, 0, MemoryMappedFileAccess.Read, HandleInheritability.Inheritable, false);
                    }
                    catch (IOException)
                    {
                        if (attempts == maxAttempts)
                        {
                            // rethrow the exception if we have exhausted the maximum number of attempts
                            throw;
                        }
                    }
                }
            }
        }

        if (_mappedFile == null)
        {
            // attach to an in-memory MMF
            try
            {
                if (!OperatingSystem.IsOSPlatform("windows"))
                {
                    throw new NotSupportedException("OS is not supported");
                }
                _mappedFile = MemoryMappedFile.OpenExisting(extentName);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to open extent: {extentName}.", ex);
            }
        }

        _view = _mappedFile.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
        _view.SafeMemoryMappedViewHandle.AcquirePointer(ref _startPointer);
        _remainingBlockSize = 0;
        _currentPosition = 0;
    }

    private void CloseCurrent()
    {
        // if the Writer creates and releases volatile extents too fast, a slow reader could lose the next extent
        // we could hold on to the next extent (if we can sufficiently parallelize the reads, so the reader skips forward as fast as possible without waiting for deserialization).
        // we should create an ExtentReader (and ExtentWriter) class to partition responsibilities. The EXtentWriter could help with locking too.
        if (_mappedFile != null)
        {
            _view.SafeMemoryMappedViewHandle.ReleasePointer();

            // Calling `view.Dispose()` flushes the underlying MemoryMappedView, in turn making
            // blocking system calls and with retry logic and `Thread.Sleep()`s.
            // To avoid taking this hit on our thread here (which blocks writing to the infinite file for
            // human-noticeable time when crossing extents), we queue this work to the thread pool.
            // See: https://referencesource.microsoft.com/#System.Core/System/IO/MemoryMappedFiles/MemoryMappedView.cs,176
            MemoryMappedViewAccessor temp = _view;
            Task.Run(() => temp.Dispose());
            _view = null;
            _mappedFile.Dispose();
            _mappedFile = null;
            _remainingBlockSize = 0;
        }
    }
}
