// <copyright file="FrequencyDomainEnergy.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using Microsoft.Psi.Components;

namespace Microsoft.Psi.Audio;

/// <summary>
/// Component that computes the frequency domain energy.
/// </summary>
public sealed class FrequencyDomainEnergy : ConsumerProducer<float[], float>
{
    private readonly int _start;
    private readonly int _end;

    /// <summary>
    /// Initializes a new instance of the <see cref="FrequencyDomainEnergy"/> class.
    /// </summary>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="start">The starting frequency of the band.</param>
    /// <param name="end">The ending frequency of the band.</param>
    /// <param name="name">An optional name for this component.</param>
    public FrequencyDomainEnergy(Pipeline pipeline, int start, int end, string name = nameof(FrequencyDomainEnergy))
        : base(pipeline, name)
    {
        _start = start;
        _end = end;
    }

    /// <summary>
    /// Receiver for the input data.
    /// </summary>
    /// <param name="data">A buffer containing the input data.</param>
    /// <param name="e">The message envelope for the input data.</param>
    protected override void Receive(float[] data, Envelope e)
    {
        Out.Post(ComputeFrequencyDomainEnergy(data, _start, _end), e.OriginatingTime);
    }

    /// <summary>
    /// Computes the energy within a frequency band.
    /// </summary>
    /// <param name="power">The power spectrum.</param>
    /// <param name="start">The starting frequency of the band.</param>
    /// <param name="end">The ending frequency of the band.</param>
    /// <returns>The total energy in the frequency band.</returns>
    private float ComputeFrequencyDomainEnergy(float[] power, int start, int end)
    {
        float energy = 0.0f;
        for (int i = start; i <= end; i++)
        {
            energy += power[i];
        }

        return energy;
    }
}
