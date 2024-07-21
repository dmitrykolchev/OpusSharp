// <copyright file="JsonStoreWriter.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Neutrino.Psi.Persistence;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Neutrino.Psi.Data.Json;

/// <summary>
/// Represents a writer for JSON data stores.
/// </summary>
public class JsonStoreWriter : JsonStoreBase
{
    private readonly Dictionary<int, JsonStreamMetadata> _catalog = new();

    private StreamWriter _streamWriter = null;
    private JsonWriter _jsonWriter = null;

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonStoreWriter"/> class.
    /// </summary>
    /// <param name="name">The name of the application that generated the persisted files, or the root name of the files.</param>
    /// <param name="path">The directory in which the main persisted file resides or will reside, or null to create a volatile data store.</param>
    /// <param name="createSubdirectory">If true, a numbered subdirectory is created for this store.</param>
    /// <param name="extension">The extension for the underlying file.</param>
    public JsonStoreWriter(string name, string path, bool createSubdirectory = true, string extension = DefaultExtension)
        : base(extension)
    {
        ushort id = 0;
        Name = name;
        Path = System.IO.Path.GetFullPath(path);
        if (createSubdirectory)
        {
            // if the root directory already exists, look for the next available id
            if (Directory.Exists(Path))
            {
                IEnumerable<ushort> existingIds = Directory.EnumerateDirectories(Path, Name + ".????")
                    .Select(d => d.Split('.').Last())
                    .Where(n => ushort.TryParse(n, out ushort i))
                    .Select(n => ushort.Parse(n));
                id = (ushort)(existingIds.Count() == 0 ? 0 : existingIds.Max() + 1);
            }

            Path = System.IO.Path.Combine(Path, $"{Name}.{id:0000}");
        }

        if (!Directory.Exists(Path))
        {
            Directory.CreateDirectory(Path);
        }

        string dataPath = System.IO.Path.Combine(Path, PsiStoreCommon.GetDataFileName(Name) + Extension);
        _streamWriter = File.CreateText(dataPath);
        _jsonWriter = new JsonTextWriter(_streamWriter);
        _jsonWriter.WriteStartArray();
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        WriteCatalog();
        _jsonWriter.WriteEndArray();
        _streamWriter.Dispose();
        _streamWriter = null;
        _jsonWriter.Close();
        _jsonWriter = null;
    }

    /// <summary>
    /// Opens the stream for the specified stream.
    /// </summary>
    /// <param name="metadata">The metadata of the stream.</param>
    /// <returns>The stream metadata.</returns>
    public JsonStreamMetadata OpenStream(JsonStreamMetadata metadata)
    {
        if (metadata == null)
        {
            throw new ArgumentNullException(nameof(metadata));
        }

        return OpenStream(metadata.Id, metadata.Name, metadata.TypeName);
    }

    /// <summary>
    /// Opens the stream for the specified stream.
    /// </summary>
    /// <param name="streamId">The stream id.</param>
    /// <param name="streamName">The stream name.</param>
    /// <param name="typeName">The stream type name.</param>
    /// <returns>The stream metadata.</returns>
    public JsonStreamMetadata OpenStream(int streamId, string streamName, string typeName)
    {
        if (_catalog.ContainsKey(streamId))
        {
            throw new InvalidOperationException($"The stream id {streamId} has already been registered with this writer.");
        }

        JsonStreamMetadata metadata = new() { Id = streamId, Name = streamName, StoreName = Name, StorePath = Path, TypeName = typeName };
        _catalog[metadata.Id] = metadata;
        WriteCatalog(); // ensure catalog is up to date even if crashing later
        return metadata;
    }

    /// <summary>
    /// Writes the next message to the data store.
    /// </summary>
    /// <param name="data">The data associated with the message write.</param>
    /// <param name="envelope">The envelope associated with the message write.</param>
    public void Write(JToken data, Envelope envelope)
    {
        JsonStreamMetadata metadata = _catalog[envelope.SourceId];
        metadata.Update(envelope, data.ToString().Length);
        WriteMessage(data, envelope, _jsonWriter);
    }

    private void WriteCatalog()
    {
        string metadataPath = System.IO.Path.Combine(Path, PsiStoreCommon.GetCatalogFileName(Name) + Extension);
        using StreamWriter file = File.CreateText(metadataPath);
        using JsonTextWriter writer = new(file);
        Serializer.Serialize(writer, _catalog.Values.ToList());
    }

    private void WriteMessage(JToken data, Envelope envelope, JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("Envelope");
        writer.WriteStartObject();
        writer.WritePropertyName("SourceId");
        writer.WriteValue(envelope.SourceId);
        writer.WritePropertyName("SequenceId");
        writer.WriteValue(envelope.SequenceId);
        writer.WritePropertyName("OriginatingTime");
        writer.WriteValue(envelope.OriginatingTime);
        writer.WritePropertyName("Time");
        writer.WriteValue(envelope.CreationTime);
        writer.WriteEndObject();
        writer.WritePropertyName("Data");
        data.WriteTo(writer);
        writer.WriteEndObject();
    }
}
