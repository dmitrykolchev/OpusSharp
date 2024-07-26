// <copyright file="SessionImporter.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Neutrino.Psi.Common;
using Neutrino.Psi.Common.Intervals;
using Neutrino.Psi.Executive;

namespace Neutrino.Psi.Data;

/// <summary>
/// Defines a class used in importing data into a session.
/// </summary>
public class SessionImporter
{
    private readonly Dictionary<string, Importer> _importers = new();

    private SessionImporter(Pipeline pipeline, Session session, bool usePerStreamReaders)
    {
        foreach (IPartition partition in session.Partitions.Where(p => p.IsStoreValid))
        {
            IStreamReader reader = StreamReader.Create(partition.StoreName, partition.StorePath, partition.StreamReaderTypeName);
            Importer importer = new(pipeline, reader, usePerStreamReaders);
            _importers.Add(partition.Name, importer);
        }

        MessageOriginatingTimeInterval = TimeInterval.Coverage(_importers.Values.Select(i => i.MessageOriginatingTimeInterval));
        MessageCreationTimeInterval = TimeInterval.Coverage(_importers.Values.Select(i => i.MessageCreationTimeInterval));
        StreamTimeInterval = TimeInterval.Coverage(_importers.Values.Select(i => i.StreamTimeInterval));
        Name = session.Name;
    }

    /// <summary>
    /// Gets the name of the session.
    /// </summary>
    public string Name { get; private set; }

    /// <summary>
    /// Gets the originating time interval (earliest to latest) of the messages in the session.
    /// </summary>
    public TimeInterval MessageOriginatingTimeInterval { get; private set; }

    /// <summary>
    /// Gets the interval between the creation time of the first and last message in the session.
    /// </summary>
    public TimeInterval MessageCreationTimeInterval { get; private set; }

    /// <summary>
    /// Gets the interval between the opened and closed times, across all streams in the session.
    /// </summary>
    public TimeInterval StreamTimeInterval { get; private set; }

    /// <summary>
    /// Gets a dictionary of named importers.
    /// </summary>
    public IReadOnlyDictionary<string, Importer> PartitionImporters => _importers;

    /// <summary>
    /// Opens a session importer.
    /// </summary>
    /// <param name="pipeline">Pipeline to use for imports.</param>
    /// <param name="session">Session to import into.</param>
    /// <param name="usePerStreamReaders">Optional flag indicating whether to use per-stream readers.</param>
    /// <returns>The newly created session importer.</returns>
    public static SessionImporter Open(Pipeline pipeline, Session session, bool usePerStreamReaders = true)
    {
        return new(pipeline, session, usePerStreamReaders);
    }

    /// <summary>
    /// Determines if any importer contains the specified stream.
    /// </summary>
    /// <param name="streamSpecification">A stream specification in the form of a stream name or [PartitionName]:StreamName.</param>
    /// <returns>true if any importer contains the named stream; otherwise false.</returns>
    public bool Contains(string streamSpecification)
    {
        if (TryGetImporterAndStreamName(streamSpecification, out Importer _, out string _, out bool streamSpecificationIsAmbiguous))
        {
            return true;
        }
        else if (streamSpecificationIsAmbiguous)
        {
            // If the stream specification is ambiguous that means multiple streams matching the specification exist.
            return true;
        }
        else
        {
            return false;
        }
    }

    /// <summary>
    /// Determines if a specific importer contains the named stream.
    /// </summary>
    /// <param name="partitionName">Partition name of the specific importer.</param>
    /// <param name="streamName">The stream to search for.</param>
    /// <returns>true if the specific importer contains the named stream; otherwise false.</returns>
    public bool HasStream(string partitionName, string streamName)
    {
        return _importers[partitionName].Contains(streamName);
    }

    /// <summary>
    /// Opens a specified stream via an importer open stream function.
    /// </summary>
    /// <typeparam name="T">The type of stream to open.</typeparam>
    /// <param name="streamSpecification">The stream specification in the form of a stream name or [PartitionName]:StreamName.</param>
    /// <param name="openStreamFunc">A function that opens the stream given an importer and optional allocator and deallocator.</param>
    /// <param name="allocator">An optional allocator of messages.</param>
    /// <param name="deallocator">An optional deallocator of messages.</param>
    /// <returns>The opened stream.</returns>
    /// <exception cref="Exception">An exception is thrown when the stream specification is ambiguous.</exception>
    public IProducer<T> OpenStream<T>(string streamSpecification, Func<Importer, string, Func<T>, Action<T>, IProducer<T>> openStreamFunc, Func<T> allocator = null, Action<T> deallocator = null)
    {
        if (TryGetImporterAndStreamName(streamSpecification, out Importer importer, out string streamName, out bool streamSpecificationIsAmbiguous))
        {
            return openStreamFunc(importer, streamName, allocator, deallocator);
        }
        else if (streamSpecificationIsAmbiguous)
        {
            if (streamSpecification.StartsWith("["))
            {
                throw new Exception($"The stream specification is ambiguous. To open the stream, please use the {nameof(Importer.OpenStream)} API with a specific partition importer.");
            }
            else
            {
                throw new Exception($"The stream specification is ambiguous. To open the stream, please use a [PartitionName]:StreamName specification, or use the {nameof(Importer.OpenStream)} API with a specific partition importer.");
            }
        }
        else
        {
            throw new Exception($"Stream specification not found: {streamSpecification}");
        }
    }

    /// <summary>
    /// Opens a specified stream via an importer open stream function, or returns null if the stream does not exist.
    /// </summary>
    /// <typeparam name="T">The type of stream to open.</typeparam>
    /// <param name="streamSpecification">The stream specification in the form of a stream name or [PartitionName]:StreamName.</param>
    /// <param name="openStreamFunc">A function that opens the stream given an importer and optional allocator and deallocator.</param>
    /// <param name="allocator">An optional allocator of messages.</param>
    /// <param name="deallocator">An optional deallocator of messages.</param>
    /// <returns>The opened stream, or null if the stream does not exist.</returns>
    /// <exception cref="Exception">An exception is thrown when the stream specification is ambiguous.</exception>
    public IProducer<T> OpenStreamOrDefault<T>(string streamSpecification, Func<Importer, string, Func<T>, Action<T>, IProducer<T>> openStreamFunc, Func<T> allocator = null, Action<T> deallocator = null)
    {
        if (TryGetImporterAndStreamName(streamSpecification, out Importer importer, out string streamName, out bool streamSpecificationIsAmbiguous))
        {
            return openStreamFunc(importer, streamName, allocator, deallocator);
        }
        else if (streamSpecificationIsAmbiguous)
        {
            if (streamSpecification.StartsWith("["))
            {
                throw new Exception($"The stream specification is ambiguous. To open the stream, please use the {nameof(Importer.OpenStream)} API with a specific partition importer.");
            }
            else
            {
                throw new Exception($"The stream specification is ambiguous. To open the stream, please use a [PartitionName]:StreamName specification, or use the {nameof(Importer.OpenStream)} API with a specific partition importer.");
            }
        }
        else
        {
            return null;
        }
    }

    /// <summary>
    /// Opens a specified stream.
    /// </summary>
    /// <typeparam name="T">The type of stream to open.</typeparam>
    /// <param name="streamSpecification">A stream specification in the form of a stream name or [PartitionName]:StreamName.</param>
    /// <param name="allocator">An optional allocator of messages.</param>
    /// <param name="deallocator">An optional deallocator to use after the messages have been sent out (defaults to disposing <see cref="IDisposable"/> messages.)</param>
    /// <returns>The opened stream.</returns>
    public IProducer<T> OpenStream<T>(string streamSpecification, Func<T> allocator = null, Action<T> deallocator = null)
    {
        return OpenStream(streamSpecification, (importer, streamName, allocator, deallocator) => importer.OpenStream(streamName, allocator, deallocator), allocator, deallocator);
    }

    /// <summary>
    /// Opens a specified stream, or returns null if the stream does not exist.
    /// </summary>
    /// <typeparam name="T">The type of stream to open.</typeparam>
    /// <param name="streamSpecification">A stream specification in the form of a stream name or [PartitionName]:StreamName.</param>
    /// <param name="allocator">An optional allocator of messages.</param>
    /// <param name="deallocator">An optional deallocator to use after the messages have been sent out (defaults to disposing <see cref="IDisposable"/> messages.)</param>
    /// <returns>The opened stream, or null if no stream with the specified name exists.</returns>
    public IProducer<T> OpenStreamOrDefault<T>(string streamSpecification, Func<T> allocator = null, Action<T> deallocator = null)
    {
        return OpenStreamOrDefault(streamSpecification, (importer, streamName, allocator, deallocator) => importer.OpenStream(streamName, allocator, deallocator), allocator, deallocator);
    }

    /// <summary>
    /// Opens the named stream in a specific partition.
    /// </summary>
    /// <typeparam name="T">The type of stream to open.</typeparam>
    /// <param name="partitionName">The partition to open stream in.</param>
    /// <param name="streamName">The name of stream to open.</param>
    /// <param name="allocator">An optional allocator of messages.</param>
    /// <param name="deallocator">An optional deallocator to use after the messages have been sent out (defaults to disposing <see cref="IDisposable"/> messages.)</param>
    /// <returns>The opened stream.</returns>
    public IProducer<T> OpenStream<T>(string partitionName, string streamName, Func<T> allocator = null, Action<T> deallocator = null)
    {
        return _importers[partitionName].OpenStream(streamName, allocator, deallocator);
    }

    /// <summary>
    /// Opens the named stream in a specific partition, if one exists.
    /// </summary>
    /// <typeparam name="T">The type of stream to open.</typeparam>
    /// <param name="partitionName">The partition to open stream in.</param>
    /// <param name="streamName">The name of stream to open.</param>
    /// <param name="allocator">An optional allocator of messages.</param>
    /// <param name="deallocator">An optional deallocator to use after the messages have been sent out (defaults to disposing <see cref="IDisposable"/> messages.)</param>
    /// <returns>The opened stream, or null if no stream with the specified name exists in the specified partition.</returns>
    public IProducer<T> OpenStreamOrDefault<T>(string partitionName, string streamName, Func<T> allocator = null, Action<T> deallocator = null)
    {
        return _importers[partitionName].OpenStreamOrDefault(streamName, allocator, deallocator);
    }

    private bool TryGetImporterAndStreamName(string streamSpecification, out Importer importer, out string streamName, out bool streamSpecificationIsAmbiguous)
    {
        if (streamSpecification.StartsWith("["))
        {
            MatchCollection matches = Regex.Matches(streamSpecification, @"^\[(.*?)\]\:(.*?)$");
            if (matches.Count == 1)
            {
                // Determine the partition and stream name within that partition
                string partitionName = matches[0].Groups[1].Value;
                streamName = matches[0].Groups[2].Value;

                // Determine if the same stream specification appears in one of the partitions
                // i.e. a stream name that starts with the partition name.
                Importer importerContainingStreamSpecification = _importers.Values.FirstOrDefault(importer => importer.AvailableStreams.Any(s => s.Name == streamSpecification));

                if (importerContainingStreamSpecification != default)
                {
                    importer = default;
                    streamName = default;
                    streamSpecificationIsAmbiguous = true;
                    return false;
                }
                else if (_importers.TryGetValue(partitionName, out importer))
                {
                    streamSpecificationIsAmbiguous = false;
                    return true;
                }
                else
                {
                    streamName = default;
                    streamSpecificationIsAmbiguous = false;
                    return false;
                }
            }
            else
            {
                throw new Exception($"Invalid stream specification/name: {streamSpecification}");
            }
        }
        else
        {
            IEnumerable<Importer> all = _importers.Values.Where(importer => importer.Contains(streamSpecification));
            int count = all.Count();
            if (count == 1)
            {
                importer = all.First();
                streamName = streamSpecification;
                streamSpecificationIsAmbiguous = false;
                return true;
            }
            else if (count > 1)
            {
                importer = default;
                streamName = default;
                streamSpecificationIsAmbiguous = true;
                return false;
            }
            else
            {
                importer = default;
                streamName = default;
                streamSpecificationIsAmbiguous = false;
                return false;
            }
        }
    }
}
