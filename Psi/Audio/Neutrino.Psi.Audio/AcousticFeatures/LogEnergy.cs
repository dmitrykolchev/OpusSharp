﻿// <copyright file="LogEnergy.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using Microsoft.Psi.Components;

namespace Microsoft.Psi.Audio;

/// <summary>
/// Component that computes the Log energy.
/// </summary>
public sealed class LogEnergy : ConsumerProducer<float[], float>
{
    /// <summary>
    /// Constants for log energy computation.
    /// </summary>
    private const float EpsInLog = 1e-40f;

    /// <summary>
    /// Constants for log energy computation.
    /// </summary>
    private const float LogOfEps = -92.1f;

    /// <summary>
    /// Initializes a new instance of the <see cref="LogEnergy"/> class.
    /// </summary>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="name">An optional name for this component.</param>
    public LogEnergy(Pipeline pipeline, string name = nameof(LogEnergy))
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
        Out.Post(ComputeLogEnergy(data), e.OriginatingTime);
    }

    /// <summary>
    /// Compute the log energy of the supplied frame.
    /// </summary>
    /// <param name="frame">The frame over which to compute the log energy.</param>
    /// <returns>The log energy.</returns>
    private float ComputeLogEnergy(float[] frame)
    {
        float egy = 0.0f;
        for (int i = 0; i < frame.Length; i++)
        {
            egy += frame[i] * frame[i];
        }

        egy /= frame.Length;
        if (egy < EpsInLog)
        {
            egy = LogOfEps;
        }
        else
        {
            egy = (float)Math.Log(egy);
        }

        return egy;
    }
}
