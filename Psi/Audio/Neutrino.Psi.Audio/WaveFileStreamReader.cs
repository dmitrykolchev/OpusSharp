// <copyright file="WaveFileStreamReader.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Threading;
using Microsoft.Psi.Data;

namespace Microsoft.Psi.Audio;

/// <summary>
/// Reader that streams audio from a WAVE file.
/// </summary>
[StreamReader("WAVE File", ".wav")]
public sealed class WaveFileStreamReader : IStreamReader
{
    /// <summary>
    /// Name of audio stream.
    /// </summary>
    public const string AudioStreamName = "Audio";

    /// <summary>
    /// Default size of each data buffer in milliseconds.
    /// </summary>
    public const int DefaultAudioBufferSizeMs = 20;

    private const int AudioSourceId = 0;

    private readonly WaveAudioStreamMetadata _audioStreamMetadata;
    private readonly BinaryReader _waveFileReader;
    private readonly WaveFormat _waveFormat;
    private readonly DateTime _startTime;
    private readonly long _dataStart;
    private readonly long _dataLength;

    private readonly List<Delegate> _audioTargets = new();
    private readonly List<Delegate> _audioIndexTargets = new();

    private int _sequenceId = 0;
    private byte[] _buffer;
    private TimeInterval _seekInterval = TimeInterval.Infinite;

    /// <summary>
    /// Initializes a new instance of the <see cref="WaveFileStreamReader"/> class.
    /// </summary>
    /// <param name="name">Name of the WAVE file.</param>
    /// <param name="path">Path of the WAVE file.</param>
    public WaveFileStreamReader(string name, string path)
        : this(name, path, DateTime.UtcNow, DefaultAudioBufferSizeMs)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WaveFileStreamReader"/> class.
    /// </summary>
    /// <param name="name">Name of the WAVE file.</param>
    /// <param name="path">Path of the WAVE file.</param>
    /// <param name="startTime">Starting time for streams of data..</param>
    /// <param name="audioBufferSizeMs">The size of each data buffer in milliseconds.</param>
    internal WaveFileStreamReader(string name, string path, DateTime startTime, int audioBufferSizeMs = DefaultAudioBufferSizeMs)
    {
        Name = name;
        Path = path;
        _startTime = startTime;
        string file = System.IO.Path.Combine(path, name);
        Size = file.Length;
        _waveFileReader = new BinaryReader(new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read));
        _waveFormat = WaveFileHelper.ReadWaveFileHeader(_waveFileReader);
        _dataLength = WaveFileHelper.ReadWaveDataLength(_waveFileReader);
        _dataStart = _waveFileReader.BaseStream.Position;
        int bufferSize = (int)(_waveFormat.AvgBytesPerSec * audioBufferSizeMs / 1000);
        _buffer = new byte[bufferSize];

        // Compute originating times based on audio chunk start time + duration
        DateTime endTime = _startTime.AddSeconds(_dataLength / (double)_waveFormat.AvgBytesPerSec);
        MessageOriginatingTimeInterval = MessageCreationTimeInterval = StreamTimeInterval = new TimeInterval(_startTime, endTime);

        long messageCount = (long)Math.Ceiling((double)_dataLength / bufferSize);
        _audioStreamMetadata = new WaveAudioStreamMetadata(AudioStreamName, typeof(AudioBuffer).AssemblyQualifiedName, name, path, _startTime, endTime, messageCount, (double)_dataLength / messageCount, audioBufferSizeMs);
    }

    /// <inheritdoc />
    public string Name { get; private set; }

    /// <inheritdoc />
    public string Path { get; private set; }

    /// <inheritdoc />
    public IEnumerable<IStreamMetadata> AvailableStreams
    {
        get
        {
            yield return _audioStreamMetadata;
        }
    }

    /// <inheritdoc />
    public TimeInterval MessageCreationTimeInterval { get; private set; }

    /// <inheritdoc />
    public TimeInterval MessageOriginatingTimeInterval { get; private set; }

    /// <inheritdoc />
    public TimeInterval StreamTimeInterval { get; private set; }

    /// <inheritdoc/>
    public long? Size { get; }

    /// <inheritdoc/>
    public int? StreamCount => 1;

    /// <inheritdoc />
    public bool ContainsStream(string name)
    {
        return name == AudioStreamName;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _waveFileReader.Dispose();
    }

    /// <inheritdoc />
    public IStreamMetadata GetStreamMetadata(string name)
    {
        ValidateStreamName(name);
        return _audioStreamMetadata;
    }

    /// <inheritdoc />
    public T GetSupplementalMetadata<T>(string streamName)
    {
        ValidateStreamName(streamName);

        if (typeof(T) != typeof(WaveFormat))
        {
            throw new NotSupportedException("The Audio stream supports only supplemental metadata of type WaveFormat.");
        }

        return (T)(object)_waveFormat;
    }

    /// <inheritdoc />
    public bool IsLive()
    {
        return false;
    }

    /// <inheritdoc />
    public bool MoveNext(out Envelope envelope)
    {
        if (
            !Next(out AudioBuffer audio, out envelope) ||
            !_seekInterval.PointIsWithin(envelope.OriginatingTime))
        {
            return false;
        }

        InvokeTargets(audio, envelope);
        return true;
    }

    /// <inheritdoc />
    public IStreamReader OpenNew()
    {
        return new WaveFileStreamReader(Name, Path, _startTime);
    }

    /// <inheritdoc />
    public IStreamMetadata OpenStream<T>(string name, Action<T, Envelope> target, Func<T> allocator = null, Action<T> deallocator = null, Action<SerializationException> errorHandler = null)
    {
        ValidateStreamName(name);

        if (target == null)
        {
            throw new ArgumentNullException(nameof(target));
        }

        if (allocator != null)
        {
            throw new NotSupportedException($"Allocators are not supported by {nameof(WaveFileStreamReader)} and must be null.");
        }

        // targets are later called when data is read by MoveNext or ReadAll (see InvokeTargets).
        _audioTargets.Add(target);
        return _audioStreamMetadata;
    }

    /// <inheritdoc />
    public IStreamMetadata OpenStreamIndex<T>(string name, Action<Func<IStreamReader, T>, Envelope> target, Func<T> allocator = null)
    {
        ValidateStreamName(name);

        if (target == null)
        {
            throw new ArgumentNullException(nameof(target));
        }

        if (allocator != null)
        {
            throw new NotSupportedException($"Allocators are not supported by {nameof(WaveFileStreamReader)} and must be null.");
        }

        // targets are later called when data is read by MoveNext or ReadAll (see InvokeTargets).
        _audioIndexTargets.Add(target);
        return _audioStreamMetadata;
    }

    /// <inheritdoc />
    public void ReadAll(ReplayDescriptor descriptor, CancellationToken cancelationToken = default)
    {
        Seek(descriptor.Interval);
        while (!cancelationToken.IsCancellationRequested && Next(out AudioBuffer audio, out Envelope envelope))
        {
            if (descriptor.Interval.PointIsWithin(envelope.OriginatingTime))
            {
                InvokeTargets(audio, envelope);
            }
        }
    }

    /// <inheritdoc />
    public void Seek(TimeInterval interval, bool useOriginatingTime = false)
    {
        _seekInterval = interval;
        _waveFileReader.BaseStream.Position = _dataStart;
        _sequenceId = 0;

        long previousPosition = _waveFileReader.BaseStream.Position;
        while (Next(out AudioBuffer _, out Envelope envelope))
        {
            if (interval.PointIsWithin(envelope.OriginatingTime))
            {
                _waveFileReader.BaseStream.Position = previousPosition; // rewind
                return;
            }

            previousPosition = _waveFileReader.BaseStream.Position;
        }
    }

    /// <summary>
    /// Validate that name corresponds to a supported stream.
    /// </summary>
    /// <param name="name">Stream name.</param>
    private static void ValidateStreamName(string name)
    {
        if (name != AudioStreamName)
        {
            // the only supported stream is the single audio stream.
            throw new NotSupportedException($"Only '{AudioStreamName}' stream is supported.");
        }
    }

    /// <summary>
    /// Read an audio buffer of data.
    /// </summary>
    /// <param name="position">Byte position.</param>
    /// <param name="sequenceId">Message sequence ID.</param>
    /// <returns>Audio buffer.</returns>
    private AudioBuffer Read(long position, int sequenceId)
    {
        _waveFileReader.BaseStream.Position = position;
        _sequenceId = sequenceId;
        if (!Next(out AudioBuffer audio, out Envelope _))
        {
            throw new InvalidOperationException("Invalid position (out of bounds).");
        }

        return audio;
    }

    /// <summary>
    /// Invoke target callbacks with currently read message information.
    /// </summary>
    /// <param name="audio">Current audio buffer.</param>
    /// <param name="envelope">Current message envelope.</param>
    /// <remarks>This method is called as the data is read when MoveNext() or ReadAll() are called.</remarks>
    private void InvokeTargets(AudioBuffer audio, Envelope envelope)
    {
        foreach (Delegate action in _audioTargets)
        {
            action.DynamicInvoke(audio, envelope);
        }

        foreach (Delegate action in _audioIndexTargets)
        {
            // Index targets are given the message Envelope and a Func by which to retrieve the message data.
            // This Func may be held as a kind of "index" later called to retrieve the data. It may be called,
            // given the current IStreamReader or a new `reader` instance against the same store.
            // The Func is a closure over the `position` and `sequenceId` information needed for retrieval
            // but these implementation details remain opaque to users of the reader.
            long position = _waveFileReader.BaseStream.Position;
            int sequenceId = _sequenceId;
            action.DynamicInvoke(new Func<IStreamReader, AudioBuffer>(reader => ((WaveFileStreamReader)reader).Read(position, sequenceId)), envelope);
        }
    }

    /// <summary>
    /// Read the next audio buffer of data from the WAVE file.
    /// </summary>
    /// <param name="audio">Audio buffer to be populated.</param>
    /// <param name="envelope">Message envelope to be populated.</param>
    /// <returns>A bool indicating whether the end of available data has been reached.</returns>
    private bool Next(out AudioBuffer audio, out Envelope envelope)
    {
        long bytesRemaining = _dataLength - (_waveFileReader.BaseStream.Position - _dataStart);
        int nextBytesToRead = (int)Math.Min(_buffer.Length, bytesRemaining);

        // Re-allocate buffer if necessary
        if ((_buffer == null) || (_buffer.Length != nextBytesToRead))
        {
            _buffer = new byte[nextBytesToRead];
        }

        // Read next audio chunk
        int bytesRead = _waveFileReader.Read(_buffer, 0, nextBytesToRead);
        if (bytesRead == 0)
        {
            // Break on end of file
            audio = default;
            envelope = default;
            return false;
        }

        // Truncate buffer if necessary
        if (bytesRead < nextBytesToRead)
        {
            byte[] truncated = new byte[bytesRead];
            Array.Copy(_buffer, 0, truncated, 0, bytesRead);
            _buffer = truncated;
        }

        long totalBytesRead = _waveFileReader.BaseStream.Position - _dataStart;
        DateTime time = _startTime.AddSeconds(totalBytesRead / (double)_waveFormat.AvgBytesPerSec);

        audio = new AudioBuffer(_buffer, _waveFormat);
        envelope = new Envelope(time, time, AudioSourceId, _sequenceId++);
        return true;
    }

    /// <summary>
    /// WAVE audio stream metadata.
    /// </summary>
    public class WaveAudioStreamMetadata : StreamMetadataBase
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="WaveAudioStreamMetadata"/> class.
        /// </summary>
        /// <param name="name">Stream name.</param>
        /// <param name="typeName">Stream type name.</param>
        /// <param name="partitionName">Partition/file name.</param>
        /// <param name="partitionPath">Partition/file path.</param>
        /// <param name="first">First message time.</param>
        /// <param name="last">Last message time.</param>
        /// <param name="messageCount">Total message count.</param>
        /// <param name="averageMessageSize">Average message size (bytes).</param>
        /// <param name="averageLatencyMs">Average message latency (milliseconds).</param>
        internal WaveAudioStreamMetadata(string name, string typeName, string partitionName, string partitionPath, DateTime first, DateTime last, long messageCount, double averageMessageSize, double averageLatencyMs)
            : base(name, AudioSourceId, typeName, partitionName, partitionPath, first, last, messageCount, averageMessageSize, averageLatencyMs)
        {
        }
    }
}
