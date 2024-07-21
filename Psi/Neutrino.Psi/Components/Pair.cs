// <copyright file="Pair.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;

namespace Microsoft.Psi.Components;


/// <summary>
/// Performs a wall-clock based pairing of streams; taking the last (or provided initial) value from the secondary.
/// </summary>
/// <typeparam name="TPrimary">The type the messages on the primary stream.</typeparam>
/// <typeparam name="TSecondary">The type messages on the secondary stream.</typeparam>
/// <typeparam name="TOut">The type of output message.</typeparam>
public class Pair<TPrimary, TSecondary, TOut> : IProducer<TOut>
{
    private readonly string _name;
    private readonly Func<TPrimary, TSecondary, TOut> _outputCreator;
    private bool _secondaryValueReady = false;
    private TSecondary _lastSecondaryValue = default;

    /// <summary>
    /// Initializes a new instance of the <see cref="Pair{TPrimary, TSecondary, TOut}"/> class.
    /// </summary>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="outputCreator">Mapping function from primary/secondary stream values to output type.</param>
    /// <param name="name">An optional name for the component.</param>
    public Pair(
        Pipeline pipeline,
        Func<TPrimary, TSecondary, TOut> outputCreator,
        string name = nameof(Pair<TPrimary, TSecondary, TOut>))
    {
        _name = name;
        _outputCreator = outputCreator;
        Out = pipeline.CreateEmitter<TOut>(this, nameof(Out));
        InPrimary = pipeline.CreateReceiver<TPrimary>(this, ReceivePrimary, nameof(InPrimary));
        InSecondary = pipeline.CreateReceiver<TSecondary>(this, ReceiveSecondary, nameof(InSecondary));
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Pair{TPrimary, TSecondary, TOut}"/> class.
    /// </summary>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="outputCreator">Mapping function from primary/secondary stream values to output type.</param>
    /// <param name="initialSecondaryValue">An initial secondary value to be used until the first message arrives on the secondary stream.</param>
    /// <param name="name">An optional name for the component.</param>
    public Pair(
        Pipeline pipeline,
        Func<TPrimary, TSecondary, TOut> outputCreator,
        TSecondary initialSecondaryValue,
        string name = nameof(Pair<TPrimary, TSecondary, TOut>))
        : this(pipeline, outputCreator, name)
    {
        _secondaryValueReady = true;
        _lastSecondaryValue = initialSecondaryValue;
    }

    /// <summary>
    /// Gets the output emitter.
    /// </summary>
    public Emitter<TOut> Out { get; }

    /// <summary>
    /// Gets the primary receiver.
    /// </summary>
    public Receiver<TPrimary> InPrimary { get; }

    /// <summary>
    /// Gets the secondary receiver.
    /// </summary>
    public Receiver<TSecondary> InSecondary { get; }

    /// <inheritdoc/>
    public override string ToString()
    {
        return _name;
    }

    private void ReceivePrimary(TPrimary message, Envelope e)
    {
        // drop unless a secondary value has been received or using an initial value
        if (_secondaryValueReady)
        {
            Out.Post(_outputCreator(message, _lastSecondaryValue), e.OriginatingTime);
        }
    }

    private void ReceiveSecondary(TSecondary message, Envelope e)
    {
        message.DeepClone(ref _lastSecondaryValue);
        _secondaryValueReady = true;
    }
}
