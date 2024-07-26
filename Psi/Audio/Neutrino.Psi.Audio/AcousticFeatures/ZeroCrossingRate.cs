// <copyright file="ZeroCrossingRate.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using Neutrino.Psi.Common;
using Neutrino.Psi.Components;
using Neutrino.Psi.Executive;

namespace Neutrino.Psi.Audio;

/// <summary>
/// Component that computes the Zero crossing rate of the input signal.
/// </summary>
public sealed class ZeroCrossingRate : ConsumerProducer<float[], float>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ZeroCrossingRate"/> class.
    /// </summary>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="name">An optional name for this component.</param>
    public ZeroCrossingRate(Pipeline pipeline, string name = nameof(ZeroCrossingRate))
        : base(pipeline, name)
    {
    }

    /// <summary>
    /// Receiver for the input data.
    /// </summary>
    /// <param name="data">A buffer containing the input data.</param>
    /// <param name="e">The message envelope for the input data.</param>
    protected override void Receive(float[] data, Envelope e)
    {
        Out.Post(ComputeZeroCrossingRate(data), e.OriginatingTime);
    }

    /// <summary>
    /// Computes the zero crossing rate of the signal.
    /// </summary>
    /// <param name="frame">A data frame of the signal.</param>
    /// <returns>The zero crossing rate.</returns>
    private float ComputeZeroCrossingRate(float[] frame)
    {
        int counter = 0;
        for (int i = 1; i < frame.Length; i++)
        {
            if (frame[i] * frame[i - 1] < 0)
            {
                counter++;
            }
        }

        return counter / (float)frame.Length;
    }
}
