// <copyright file="JsonSimpleWriter.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Neutrino.Psi.Serialization;
using Newtonsoft.Json.Linq;

namespace Neutrino.Psi.Data.Json;

/// <summary>
/// Represents a simple writer for JSON data stores.
/// </summary>
public class JsonSimpleWriter : ISimpleWriter, IDisposable
{
    private readonly Dictionary<int, Func<(bool hasData, JToken data, Envelope envelope)>> _outputs = new();
    private readonly string _extension;

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonSimpleWriter"/> class.
    /// </summary>
    /// <param name="name">The name of the application that generated the persisted files, or the root name of the files.</param>
    /// <param name="path">The directory in which the main persisted file resides or will reside, or null to create a volatile data store.</param>
    /// <param name="createSubdirectory">If true, a numbered subdirectory is created for this store.</param>
    /// <param name="extension">The extension for the underlying file.</param>
    public JsonSimpleWriter(string name, string path, bool createSubdirectory = true, string extension = JsonStoreBase.DefaultExtension)
        : this(extension)
    {
        Writer = new JsonStoreWriter(name, path, createSubdirectory, extension);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonSimpleWriter"/> class.
    /// </summary>
    /// <param name="extension">The extension for the underlying file.</param>
    public JsonSimpleWriter(string extension = JsonStoreBase.DefaultExtension)
    {
        _extension = extension;
    }

    /// <inheritdoc />
    public string Name => Writer?.Name;

    /// <inheritdoc />
    public string Path => Writer?.Path;

    /// <summary>
    /// Gets or sets the underlying store writer.
    /// </summary>
    protected JsonStoreWriter Writer { get; set; }

    /// <inheritdoc />
    public virtual void CreateStore(string name, string path, bool createSubdirectory = true, KnownSerializers serializers = null)
    {
        if (serializers != null)
        {
            throw new ArgumentException("Serializers are not used by JsonSimpleWriter and must be null.", nameof(serializers));
        }

        Writer = new JsonStoreWriter(name, path, createSubdirectory, _extension);
    }

    /// <inheritdoc />
    public void CreateStream<TData>(IStreamMetadata metadata, IEnumerable<Message<TData>> source)
    {
        if (metadata == null)
        {
            throw new ArgumentNullException(nameof(metadata));
        }

        if (!(metadata is JsonStreamMetadata))
        {
            throw new ArgumentException($"Metadata must be of type '{nameof(JsonStreamMetadata)}'.", nameof(metadata));
        }

        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        JsonStreamMetadata streamMetadata = Writer.OpenStream(metadata as JsonStreamMetadata);
        IEnumerator<Message<TData>> enumerator = source.GetEnumerator();
        _outputs[streamMetadata.Id] = () =>
        {
            bool hasData = enumerator.MoveNext();
            JToken data = null;
            Envelope envelope = default;

            if (hasData)
            {
                Message<TData> message = enumerator.Current;
                data = JToken.FromObject(message.Data);
                envelope = message.Envelope;
            }

            return (hasData, data, envelope);
        };
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Writer?.Dispose();
    }

    /// <inheritdoc />
    public void WriteAll(ReplayDescriptor descriptor, CancellationToken cancelationToken = default)
    {
        List<Func<(bool hasData, JToken data, Envelope envelope)>> doneStreamWriters = new();
        List<Func<(bool hasData, JToken data, Envelope envelope)>> streamWriters = _outputs.Values.ToList();
        while (streamWriters.Any())
        {
            foreach (Func<(bool hasData, JToken data, Envelope envelope)> streamWriter in streamWriters)
            {
                (bool hasData, JToken data, Envelope envelope) = streamWriter();
                if (hasData)
                {
                    Writer.Write(data, envelope);
                }
                else
                {
                    doneStreamWriters.Add(streamWriter);
                }
            }

            foreach (Func<(bool hasData, JToken data, Envelope envelope)> doneStreamWriter in doneStreamWriters)
            {
                streamWriters.Remove(doneStreamWriter);
            }

            doneStreamWriters.Clear();
        }
    }
}
