// <copyright file="PsiStoreStreamReader.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Threading;
using Neutrino.Psi.Common;
using Neutrino.Psi.Persistence;
using Neutrino.Psi.Serialization;


namespace Neutrino.Psi.Data;

/// <summary>
/// Implements a reader of multiple streams of typed messages from a single store.
/// </summary>
public sealed class PsiStoreStreamReader : IStreamReader
{
    private readonly Dictionary<int, List<Delegate>> _targets = new();
    private readonly Dictionary<int, List<Action<SerializationException>>> _errorHandlers = new();
    private readonly Dictionary<int, Action<BufferReader, Envelope>> _outputs = new();
    private readonly Dictionary<int, Action<IndexEntry, Envelope>> _indexOutputs = new();

    private SerializationContext context;
    private byte[] buffer;

    /// <summary>
    /// Initializes a new instance of the <see cref="PsiStoreStreamReader"/> class.
    /// </summary>
    /// <param name="name">The name of the application that generated the persisted files, or the root name of the files.</param>
    /// <param name="path">The directory in which the main persisted file resides or will reside, or null to create a volatile data store.</param>
    public PsiStoreStreamReader(string name, string path)
    {
        PsiStoreReader = new PsiStoreReader(name, path, LoadMetadata);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PsiStoreStreamReader"/> class.
    /// </summary>
    /// <param name="name">The name of the application that generated the persisted files, or the root name of the files.</param>
    /// <param name="path">The directory in which the main persisted file resides or will reside, or null to create a volatile data store.</param>
    /// <param name="defaultStartTime">Default start time (unused).</param>
    public PsiStoreStreamReader(string name, string path, DateTime defaultStartTime)
        : this(name, path)
    {
    }

    private PsiStoreStreamReader(PsiStoreStreamReader other)
    {
        PsiStoreReader = new PsiStoreReader(other.PsiStoreReader); // copy constructor
        context = new SerializationContext(other.context?.Serializers);
    }

    /// <inheritdoc />
    public IEnumerable<IStreamMetadata> AvailableStreams => PsiStoreReader.AvailableStreams;

    /// <inheritdoc />
    public string Name => PsiStoreReader.Name;

    /// <inheritdoc />
    public string Path => PsiStoreReader.Path;

    /// <inheritdoc />
    public TimeInterval MessageCreationTimeInterval => PsiStoreReader.MessageCreationTimeInterval;

    /// <inheritdoc />
    public TimeInterval MessageOriginatingTimeInterval => PsiStoreReader.MessageOriginatingTimeInterval;

    /// <inheritdoc />
    public TimeInterval StreamTimeInterval => PsiStoreReader.StreamTimeInterval;

    /// <inheritdoc />
    public long? Size => PsiStoreReader.Size;

    /// <inheritdoc />
    public int? StreamCount => PsiStoreReader.StreamCount;

    /// <summary>
    /// Gets underlying PsiStoreReader (internal only, not part of IStreamReader interface).
    /// </summary>
    internal PsiStoreReader PsiStoreReader { get; }

    /// <inheritdoc />
    public void Seek(TimeInterval interval, bool useOriginatingTime = false)
    {
        PsiStoreReader.Seek(interval, useOriginatingTime);
    }

    /// <inheritdoc />
    public bool MoveNext(out Envelope envelope)
    {
        if (PsiStoreReader.MoveNext(out envelope))
        {
            if (_indexOutputs.ContainsKey(envelope.SourceId))
            {
                IndexEntry indexEntry = PsiStoreReader.ReadIndex();
                _indexOutputs[envelope.SourceId](indexEntry, envelope);
            }
            else
            {
                int count = PsiStoreReader.Read(ref buffer);
                BufferReader bufferReader = new(buffer, count);
                _outputs[envelope.SourceId](bufferReader, envelope);
            }

            return true;
        }

        return false;
    }

    /// <inheritdoc />
    public bool IsLive()
    {
        return PsiStoreMonitor.IsStoreLive(Name, Path);
    }

    /// <inheritdoc />
    public IStreamMetadata OpenStream<T>(string name, Action<T, Envelope> target, Func<T> allocator = null, Action<T> deallocator = null, Action<SerializationException> errorHandler = null)
    {
        PsiStreamMetadata meta = PsiStoreReader.OpenStream(name); // this checks for duplicates
        OpenStream(meta, target, allocator, deallocator, errorHandler);
        return meta;
    }

    /// <inheritdoc />
    public IStreamMetadata OpenStreamIndex<T>(string streamName, Action<Func<IStreamReader, T>, Envelope> target, Func<T> allocator = null)
    {
        PsiStreamMetadata meta = PsiStoreReader.OpenStream(streamName); // this checks for duplicates

        // Target `indexOutputs` are later called when data is read by MoveNext or ReadAll (see InvokeTargets).
        _indexOutputs[meta.Id] = new Action<IndexEntry, Envelope>((indexEntry, envelope) =>
        {
            // Index targets are given the message Envelope and a Func by which to retrieve the message data.
            // This Func may be held as a kind of "index" later called to retrieve the data. It may be called,
            // given the current IStreamReader or a new `reader` instance against the same store.
            // The Func is a closure over the `indexEntry` needed for retrieval by `Read<T>(...)`
            // but this implementation detail remain opaque to users of the reader.
            target(new Func<IStreamReader, T>(reader => ((PsiStoreStreamReader)reader).Read<T>(indexEntry, allocator)), envelope);
        });
        return meta;
    }

    /// <inheritdoc />
    public IStreamMetadata GetStreamMetadata(string name)
    {
        return PsiStoreReader.GetMetadata(name);
    }

    /// <inheritdoc />
    public T GetSupplementalMetadata<T>(string streamName)
    {
        PsiStreamMetadata meta = PsiStoreReader.GetMetadata(streamName);
        return meta.GetSupplementalMetadata<T>(context.Serializers);
    }

    /// <inheritdoc />
    public bool ContainsStream(string name)
    {
        return PsiStoreReader.Contains(name);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        PsiStoreReader.Dispose();
    }

    /// <inheritdoc />
    public IStreamReader OpenNew()
    {
        return new PsiStoreStreamReader(this);
    }

    /// <inheritdoc />
    public void ReadAll(ReplayDescriptor descriptor, CancellationToken cancelationToken = default)
    {
        bool result = true;
        PsiStoreReader.Seek(descriptor.Interval, true);
        while (result || IsLive())
        {
            if (cancelationToken.IsCancellationRequested)
            {
                return;
            }

            result = PsiStoreReader.MoveNext(out Envelope e);
            if (result)
            {
                if (_indexOutputs.ContainsKey(e.SourceId))
                {
                    IndexEntry indexEntry = PsiStoreReader.ReadIndex();
                    _indexOutputs[e.SourceId](indexEntry, e);
                }
                else if (_outputs.ContainsKey(e.SourceId))
                {
                    int count = PsiStoreReader.Read(ref buffer);
                    BufferReader bufferReader = new(buffer, count);

                    // Deserialize the data and call the listeners.  Note that due to polymorphic types, we
                    // may be attempting to create some handlers on the fly which may result in a serialization
                    // exception being thrown due to type mismatch errors.
                    try
                    {
                        _outputs[e.SourceId](bufferReader, e);
                    }
                    catch (SerializationException ex)
                    {
                        // If any error occurred while processing the message and there are
                        // registered error handlers, stop attempting to process messages
                        // from the stream and notify all registered error handler listeners.
                        // otherwise, rethrow the exception to exit the application.
                        if (_errorHandlers.ContainsKey(e.SourceId))
                        {
                            _outputs.Remove(e.SourceId);
                            foreach (Action<SerializationException> errorAction in _errorHandlers[e.SourceId])
                            {
                                errorAction.Invoke(ex);
                            }
                        }
                        else
                        {
                            throw;
                        }
                    }
                }
            }
        }
    }

    internal KnownSerializers GetSerializers()
    {
        return context.Serializers;
    }

    /// <summary>
    /// Read message data at the given index.
    /// </summary>
    /// <typeparam name="T">The type of message data.</typeparam>
    /// <param name="indexEntry">Index entry describing the location of a particular message.</param>
    /// <param name="allocator">An optional allocator to use when constructing messages.</param>
    /// <returns>Message data.</returns>
    private T Read<T>(IndexEntry indexEntry, Func<T> allocator = null)
    {
        T target = (allocator == null) ? default : allocator();
        int count = PsiStoreReader.ReadAt(indexEntry, ref buffer);
        BufferReader bufferReader = new(buffer, count);
        SerializationHandler<T> handler = context.Serializers.GetHandler<T>();
        target = Deserialize(handler, bufferReader, default /* only used by raw */, false, false, target, null /* only used by dynamic */, null /* only used by dynamic */);
        return target;
    }

    /// <summary>
    /// Initializes the serialization subsystem with the metadata from the store.
    /// </summary>
    /// <param name="metadata">The collection of metadata entries from the store catalog.</param>
    /// <param name="runtimeInfo">The runtime info for the runtime that produced the store.</param>
    private void LoadMetadata(IEnumerable<Metadata> metadata, RuntimeInfo runtimeInfo)
    {
        context ??= new SerializationContext(new KnownSerializers(runtimeInfo));

        context.Serializers.RegisterMetadata(metadata);
    }

    private void OpenStream<T>(IStreamMetadata meta, Action<T, Envelope> target, Func<T> allocator = null, Action<T> deallocator = null, Action<SerializationException> errorHandler = null)
    {
        // If no deallocator is specified, use the default
        deallocator ??= data =>
        {
            if (data is IDisposable disposable)
            {
                disposable.Dispose();
            }
        };

        // If there's no list of targets for this stream, create it now
        if (!_targets.ContainsKey(meta.Id))
        {
            _targets[meta.Id] = new List<Delegate>();
        }

        // Add the target to the list to call when this stream has new data
        _targets[meta.Id].Add(target);

        // Add the error handler, if any
        if (errorHandler != null)
        {
            if (!_errorHandlers.ContainsKey(meta.Id))
            {
                _errorHandlers[meta.Id] = new List<Action<SerializationException>>();
            }

            _errorHandlers[meta.Id].Add(errorHandler);
        }

        // Get the deserialization handler for this stream type
        SerializationHandler<T> handler = null;
        try
        {
            // A serialization exception may be thrown here if the handler is unable to be initialized due to a
            // mismatch between the format of the messages in the stream and the current format of T, probably
            // because a field has been added, removed, or renamed in T since the stream was created.
            handler = context.Serializers.GetHandler<T>();

            bool isDynamic = typeof(T).FullName == typeof(object).FullName;
            bool isRaw = typeof(T).FullName == typeof(Message<BufferReader>).FullName;

            if (!isDynamic && !isRaw)
            {
                // check that the requested type matches the stream type
                string streamType = meta.TypeName;
                string handlerType = handler.Name;
                if (streamType != handlerType)
                {
                    // check if the handler is able to handle the stream type
                    if (handlerType != streamType)
                    {
                        if (context.Serializers.Schemas.TryGetValue(streamType, out TypeSchema streamTypeSchema) &&
                            context.Serializers.Schemas.TryGetValue(handlerType, out TypeSchema handlerTypeSchema))
                        {
                            // validate compatibility - will throw if types are incompatible
                            handlerTypeSchema.ValidateCompatibleWith(streamTypeSchema);
                        }
                    }
                }
            }

            // Update the code to execute when this stream receives new data
            _outputs[meta.Id] = (br, e) =>
            {
                // Deserialize the data
                T target = (allocator == null) ? default : allocator();
                T data = Deserialize(handler, br, e, isDynamic, isRaw, target, meta.TypeName, context.Serializers.Schemas);

                // Call each of the targets
                foreach (Delegate action in _targets[meta.Id])
                {
                    (action as Action<T, Envelope>)(data, e);
                }

                deallocator(data);
            };
        }
        catch (SerializationException ex)
        {
            // If there are any registered error handlers, call the ones registered for
            // this stream, otherwise rethrow the exception to exit the application.
            if (_errorHandlers.ContainsKey(meta.Id))
            {
                foreach (Action<SerializationException> errorAction in _errorHandlers[meta.Id])
                {
                    errorAction.Invoke(ex);
                }
            }
            else
            {
                throw;
            }
        }
    }

    private T Deserialize<T>(SerializationHandler<T> handler, BufferReader br, Envelope env, bool isDynamic, bool isRaw, T objectToReuse, string typeName, IDictionary<string, TypeSchema> schemas)
    {
        if (isDynamic)
        {
            DynamicMessageDeserializer deserializer = new(typeName, schemas, context.Serializers.TypeNameSynonyms);
            objectToReuse = deserializer.Deserialize(br);
        }
        else if (isRaw)
        {
            objectToReuse = (T)(object)Message.Create(br, env);
        }
        else
        {
            int currentPosition = br.Position;
            try
            {
                handler.Deserialize(br, ref objectToReuse, context);
            }
            catch
            {
                PsiStoreReader.EnsureMetadataUpdate();
                br.Seek(currentPosition);
                handler.Deserialize(br, ref objectToReuse, context);
            }
        }

        context.Reset();
        return objectToReuse;
    }
}
