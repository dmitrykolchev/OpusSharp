// <copyright file="AcousticFeaturesExtractor.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using Neutrino.Psi.Components;

namespace Neutrino.Psi.Audio;

/// <summary>
/// Component that extracts acoustic features (e.g. LogEnergy, ZeroCrossing, FFT) from an audio stream.
/// </summary>
/// <remarks>
/// The acoustic feature streams available from this component are: <see cref="LogEnergy"/>, <see cref="ZeroCrossingRate"/>,
/// <see cref="FFT"/>, <see cref="FFTPower"/>, <see cref="FrequencyDomainEnergy"/>, <see cref="LowFrequencyEnergy"/>,
/// <see cref="HighFrequencyEnergy"/> and <see cref="SpectralEntropy"/>. Use the <see cref="AcousticFeaturesExtractorConfiguration"/>
/// class to control which acoustic features to compute.
/// </remarks>
public sealed class AcousticFeaturesExtractor : IConsumer<AudioBuffer>
{
    private readonly string _name;
    private readonly Connector<AudioBuffer> _inAudio;

    /// <summary>
    /// Initializes a new instance of the <see cref="AcousticFeaturesExtractor"/> class.
    /// </summary>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="configurationFilename">The component configuration file.</param>
    /// <param name="name">An optional name for the component.</param>
    public AcousticFeaturesExtractor(Pipeline pipeline, string configurationFilename = null, string name = nameof(AcousticFeaturesExtractor))
        : this(pipeline, ConfigurationHelper.ReadFromFileOrDefault(configurationFilename, new AcousticFeaturesExtractorConfiguration(), true), name)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AcousticFeaturesExtractor"/> class.
    /// </summary>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="configuration">The component configuration.</param>
    /// <param name="name">An optional name for the component.</param>
    public AcousticFeaturesExtractor(Pipeline pipeline, AcousticFeaturesExtractorConfiguration configuration, string name = nameof(AcousticFeaturesExtractor))
    {
        // Create the Audio passthrough emitter and hook it up to the receiver
        _name = name;
        _inAudio = pipeline.CreateConnector<AudioBuffer>(nameof(_inAudio));
        In = _inAudio.In;

        float frameRate = configuration.FrameRateInHz;
        int frameSize = (int)((configuration.InputFormat.SamplesPerSec * configuration.FrameDurationInSeconds) + 0.5);
        int frameShift = (int)((configuration.InputFormat.SamplesPerSec / frameRate) + 0.5);
        int bytesPerSample = configuration.InputFormat.BlockAlign;
        int bytesPerFrame = bytesPerSample * frameSize;
        int bytesPerFrameShift = bytesPerSample * frameShift;
        int fftSize = 2;
        while (fftSize < frameSize)
        {
            fftSize *= 2;
        }

        float freqBinWidth = configuration.InputFormat.SamplesPerSec / (float)fftSize;
        int startBand = (int)(configuration.StartFrequency / freqBinWidth);   // round down
        int endBand = (int)((configuration.EndFrequency / freqBinWidth) + 0.5); // round up

        // Construct overlapping frames of audio samples to operate on
        IProducer<byte[]> windowedFrame = _inAudio.ToByteArray().FrameShift(bytesPerFrame, bytesPerFrameShift, configuration.InputFormat.AvgBytesPerSec);

        // Convert the frame to floating point
        IProducer<float[]> frame = windowedFrame.ToFloat(configuration.InputFormat);

        // Optionally add dither
        if (configuration.AddDither)
        {
            frame = frame.Dither(configuration.DitherScaleFactor);
        }

        // Apply Hanning window
        frame = frame.HanningWindow(frameSize);

        if (configuration.ComputeLogEnergy)
        {
            LogEnergy = frame.LogEnergy();
        }

        if (configuration.ComputeZeroCrossingRate)
        {
            ZeroCrossingRate = frame.ZeroCrossingRate();
        }

        if (configuration.ComputeFFT)
        {
            FFT = frame.FFT(fftSize, frameSize);
        }

        if (configuration.ComputeFFTPower)
        {
            FFTPower = FFT.FFTPower();
        }

        if (configuration.ComputeFrequencyDomainEnergy)
        {
            FrequencyDomainEnergy = FFTPower.FrequencyDomainEnergy(startBand, endBand);
        }

        if (configuration.ComputeLowFrequencyEnergy)
        {
            int lowFreqEnergyStartBand = startBand;
            int lowFreqEnergyEndBand = (int)((configuration.LowEndFrequency / freqBinWidth) + 0.5); // round up
            LowFrequencyEnergy = FFTPower.FrequencyDomainEnergy(lowFreqEnergyStartBand, lowFreqEnergyEndBand);
        }

        if (configuration.ComputeHighFrequencyEnergy)
        {
            int highFreqEnergyStartBand = (int)(configuration.HighStartFrequency / freqBinWidth); // round down
            int highFreqEnergyEndBand = endBand;
            HighFrequencyEnergy = FFTPower.FrequencyDomainEnergy(highFreqEnergyStartBand, highFreqEnergyEndBand);
        }

        if (configuration.ComputeSpectralEntropy)
        {
            float entropyEndFreq = configuration.StartFrequency + configuration.EntropyBandwidth;
            int entropyStartBand = startBand;
            int entropyEndBand = (int)(entropyEndFreq / freqBinWidth);
            SpectralEntropy = FFTPower.SpectralEntropy(entropyStartBand, entropyEndBand);
        }
    }

    /// <summary>
    /// Gets the receiver for the input audio for the acoustic features component.
    /// </summary>
    public Receiver<AudioBuffer> In { get; }

    /// <summary>
    /// Gets the stream containing the log energy.
    /// </summary>
    public IProducer<float> LogEnergy { get; }

    /// <summary>
    /// Gets the stream containing the zero crossing rate.
    /// </summary>
    public IProducer<float> ZeroCrossingRate { get; }

    /// <summary>
    /// Gets the stream containing the FFT.
    /// </summary>
    public IProducer<float[]> FFT { get; }

    /// <summary>
    /// Gets the stream containing the FFT power.
    /// </summary>
    public IProducer<float[]> FFTPower { get; }

    /// <summary>
    /// Gets the stream containing the frequency domain energy.
    /// </summary>
    public IProducer<float> FrequencyDomainEnergy { get; }

    /// <summary>
    /// Gets the stream containing the low frequency energy.
    /// </summary>
    public IProducer<float> LowFrequencyEnergy { get; }

    /// <summary>
    /// Gets the stream containing the high frequency energy.
    /// </summary>
    public IProducer<float> HighFrequencyEnergy { get; }

    /// <summary>
    /// Gets the stream containing the spectral entropy.
    /// </summary>
    public IProducer<float> SpectralEntropy { get; }

    /// <inheritdoc/>
    public override string ToString()
    {
        return _name;
    }
}
