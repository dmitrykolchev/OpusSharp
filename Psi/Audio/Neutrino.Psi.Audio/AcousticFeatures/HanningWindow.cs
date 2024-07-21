// <copyright file="HanningWindow.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;

namespace Neutrino.Psi.Audio;

/// <summary>
/// Provides a class for applying a Hanning window.
/// </summary>
internal sealed class HanningWindow
{
    private readonly float[] _kernel;
    private readonly float[] _output;

    /// <summary>
    /// Initializes a new instance of the <see cref="HanningWindow"/> class.
    /// </summary>
    /// <param name="kernelLength">The Hanning window length.</param>
    public HanningWindow(int kernelLength)
    {
        _kernel = new float[kernelLength];
        _output = new float[kernelLength];
        ComputeHanningKernel(kernelLength);
    }

    /// <summary>
    /// Applies the Hanning window over the data.
    /// </summary>
    /// <param name="data">
    /// The data to apply the Hanning window to. This must be of the same size as the kernel.
    /// </param>
    /// <returns>The computed hannign window over the data.</returns>
    public float[] Apply(float[] data)
    {
        if (data.Length != _output.Length)
        {
            throw new ArgumentException("Data must be of the same size as the kernel.");
        }

        for (int i = 0; i < data.Length; ++i)
        {
            _output[i] = data[i] * _kernel[i];
        }

        return _output;
    }

    /// <summary>
    /// Computes the Hanning kernel.
    /// </summary>
    /// <param name="length">The desired length of the kernel.</param>
    private void ComputeHanningKernel(int length)
    {
        double x;
        for (int i = 0; i < length; i++)
        {
            x = 2.0 * Math.PI * i / length;
            _kernel[i] = (float)(0.5 * (1.0 - Math.Cos(x)));
        }
    }
}
