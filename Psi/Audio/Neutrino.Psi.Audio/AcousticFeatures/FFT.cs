// <copyright file="FFT.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using Neutrino.Psi.Common;
using Neutrino.Psi.Components;
using Neutrino.Psi.Executive;

namespace Neutrino.Psi.Audio;

/// <summary>
/// Component that performs an FFT on a stream of sample buffers.
/// </summary>
public sealed class FFT : ConsumerProducer<float[], float[]>
{
    private readonly FastFourierTransform _fft;
    private float[] _fftOutput;

    /// <summary>
    /// Initializes a new instance of the <see cref="FFT"/> class.
    /// </summary>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="fftSize">The FFT size.</param>
    /// <param name="inputSize">The window size.</param>
    /// <param name="name">An optional name for this component.</param>
    public FFT(Pipeline pipeline, int fftSize, int inputSize, string name = nameof(FFT))
        : base(pipeline, name)
    {
        _fft = new FastFourierTransform(fftSize, inputSize);
    }

    /// <summary>
    /// Receiver for the input data.
    /// </summary>
    /// <param name="data">A buffer containing the input data.</param>
    /// <param name="e">The message envelope for the input data.</param>
    protected override void Receive(float[] data, Envelope e)
    {
        _fft.ComputeFFT(data, ref _fftOutput);
        Out.Post(_fftOutput, e.OriginatingTime);
    }
}
