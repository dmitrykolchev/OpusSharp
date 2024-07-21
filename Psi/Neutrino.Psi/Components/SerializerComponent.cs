// <copyright file="SerializerComponent.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using Neutrino.Psi.Common;
using Neutrino.Psi.Serialization;


namespace Neutrino.Psi.Components;

/// <summary>
/// Serializer optimized for streaming scenarios, where buffers and instances can be cached.
/// </summary>
/// <typeparam name="T">The type of messages to serialize.</typeparam>
internal sealed class SerializerComponent<T> : ConsumerProducer<Message<T>, Message<BufferReader>>
{
    private readonly SerializationContext _context;
    private readonly BufferWriter _serializationBuffer = new(16);
    private readonly SerializationHandler<T> _handler;

    /// <summary>
    /// Initializes a new instance of the <see cref="SerializerComponent{T}"/> class.
    /// </summary>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="serializers">Known serializers.</param>
    /// <param name="name">An optional name for the component.</param>
    internal SerializerComponent(Pipeline pipeline, KnownSerializers serializers, string name = nameof(SerializerComponent<T>))
        : base(pipeline, name)
    {
        _context = new SerializationContext(serializers);
        _handler = serializers.GetHandler<T>();
    }

    /// <inheritdoc />
    protected override void Receive(Message<T> data, Envelope e)
    {
        _serializationBuffer.Reset();
        _handler.Serialize(_serializationBuffer, data.Data, _context);
        _context.Reset();
        BufferReader outputBuffer = new(_serializationBuffer);

        // preserve the envelope we received
        Message<BufferReader> resultMsg = Message.Create(outputBuffer, data.Envelope);
        Out.Post(resultMsg, e.OriginatingTime);
    }
}
