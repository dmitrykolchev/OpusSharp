// <copyright file="ToFloat.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using Neutrino.Psi.Common;
using Neutrino.Psi.Components;
using Neutrino.Psi.Executive;

namespace Neutrino.Psi.Audio;

/// <summary>
/// Component that converts raw audio data to floating point samples.
/// </summary>
public sealed class ToFloat : ConsumerProducer<byte[], float[]>
{
    private readonly ushort _bytesPerSample;
    private readonly Func<byte[], int, float> _convertSample;
    private float[] _buffer;

    /// <summary>
    /// Initializes a new instance of the <see cref="ToFloat"/> class.
    /// </summary>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="format">The format of the input audio.</param>
    /// <param name="name">An optional name for this component.</param>
    public ToFloat(Pipeline pipeline, WaveFormat format, string name = nameof(ToFloat))
        : base(pipeline, name)
    {
        _bytesPerSample = format.BlockAlign;
        _convertSample = format.BitsPerSample switch
        {
            8 => (a, i) => a[i],
            16 => (a, i) => BitConverter.ToInt16(a, i),
            24 => (a, i) => BitConverter.ToInt32(new byte[] { a[i], a[i + 1], a[i + 2], (byte)((a[i + 2] & 0x80) == 0 ? 0 : 0xFF) }, 0),
            32 => (a, i) => BitConverter.ToInt32(a, i),
            _ => throw new FormatException("Valid sample sizes are 8, 16, 24 or 32 bits"),
        };
    }

    /// <summary>
    /// Receiver for the input data.
    /// </summary>
    /// <param name="data">A buffer containing the input data.</param>
    /// <param name="e">The message envelope for the input data.</param>
    protected override void Receive(byte[] data, Envelope e)
    {
        int frameSize = data.Length / _bytesPerSample;

        if ((_buffer == null) || (_buffer.Length != frameSize))
        {
            _buffer = new float[frameSize];
        }

        // Create the window frame
        for (int i = 0, j = 0; i < frameSize; ++i, j += _bytesPerSample)
        {
            // NOTE: only first channel is taken (no averaging over multiple channels).
            _buffer[i] = _convertSample(data, j);
        }

        Out.Post(_buffer, e.OriginatingTime);
    }
}
