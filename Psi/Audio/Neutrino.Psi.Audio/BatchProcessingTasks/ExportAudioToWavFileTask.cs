// <copyright file="ExportAudioToWavFileTask.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using Neutrino.Psi.Common;
using Neutrino.Psi.Data;
using Neutrino.Psi.Executive;

namespace Neutrino.Psi.Audio;

/// <summary>
/// Batch task that exports audio streams to a wav file.
/// </summary>
[BatchProcessingTask(
    "Export Audio to Wav File",
    Description = "This task exports an audio stream to a wav file.")]
public class ExportAudioToWavFileTask : BatchProcessingTask<ExportAudioToWavFileTaskConfiguration>
{
    /// <inheritdoc/>
    public override void Run(Pipeline pipeline, SessionImporter sessionImporter, Exporter exporter, ExportAudioToWavFileTaskConfiguration configuration)
    {
        IProducer<AudioBuffer> audio = sessionImporter.OpenStream<AudioBuffer>(configuration.AudioStreamName);
        Importer partition = sessionImporter.PartitionImporters.Values.First();
        WaveFileWriter wavFileWriter = new(pipeline, Path.Combine(partition.StorePath, configuration.WavOutputFilename));

        IProducer<AudioBuffer> streamlinedAudio = audio.Streamline(configuration.AudioStreamlineMethod, configuration.MaxOffsetBeforeUnpleatedRealignmentMs);
        if (exporter != null)
        {
            streamlinedAudio.Write(configuration.OutputAudioStreamName, exporter);
        }

        streamlinedAudio.PipeTo(wavFileWriter);
    }
}

/// <summary>
/// Represents the configuration for the <see cref="ExportAudioToWavFileTask"/>.
/// </summary>
#pragma warning disable SA1402 // File may only contain a single type
public class ExportAudioToWavFileTaskConfiguration : BatchProcessingTaskConfiguration
#pragma warning restore SA1402 // File may only contain a single type
{
    private string _audioStreamName = "Audio";
    private string _outputAudioStreamName = "Audio";
    private string _wavOutputFilename = "Audio.wav";
    private AudioStreamlineMethod _audioStreamlineMethod = AudioStreamlineMethod.Unpleat;
    private double _maxOffsetBeforeUnpleatedRealignmentMs = 20;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExportAudioToWavFileTaskConfiguration"/> class.
    /// </summary>
    public ExportAudioToWavFileTaskConfiguration()
        : base()
    {
        OutputStoreName = string.Empty;
        OutputPartitionName = string.Empty;
        DeliveryPolicySpec = DeliveryPolicySpec.Throttle;
        ReplayAllRealTime = false;
    }

    /// <summary>
    /// Gets or sets the name of the audio stream.
    /// </summary>
    [DataMember]
    [DisplayName("Audio Stream Name")]
    [Description("The name of the audio stream.")]
    public string AudioStreamName
    {
        get => _audioStreamName;
        set => Set(nameof(AudioStreamName), ref _audioStreamName, value);
    }

    /// <summary>
    /// Gets or sets the name of the output audio stream.
    /// </summary>
    [DataMember]
    [DisplayName("Output Audio Stream Name")]
    [Description("The name of the output audio stream.")]
    public string OutputAudioStreamName
    {
        get => _outputAudioStreamName;
        set => Set(nameof(OutputAudioStreamName), ref _outputAudioStreamName, value);
    }

    /// <summary>
    /// Gets or sets the name of a wave output file.
    /// </summary>
    [DataMember]
    [DisplayName("Wave Output Filename")]
    [Description("The filename is relative to the partition folder.")]
    public string WavOutputFilename
    {
        get => _wavOutputFilename;
        set => Set(nameof(WavOutputFilename), ref _wavOutputFilename, value);
    }

    /// <summary>
    /// Gets or sets the method used to streamline the audio stream.
    /// </summary>
    [DataMember]
    [DisplayName("Audio Streamline Method")]
    [Description("The method used to streamline the audio stream.")]
    public AudioStreamlineMethod AudioStreamlineMethod
    {
        get => _audioStreamlineMethod;
        set => Set(nameof(AudioStreamlineMethod), ref _audioStreamlineMethod, value);
    }

    /// <summary>
    /// Gets or sets the maximum offset before realignment in milliseconds.
    /// </summary>
    [DataMember]
    [DisplayName("Max Offset Before Realignment (ms)")]
    [Description("The maximum time offset between the unpleated stream and originating times before a realignment is enforced (in milliseconds).")]
    public double MaxOffsetBeforeUnpleatedRealignmentMs
    {
        get => _maxOffsetBeforeUnpleatedRealignmentMs;
        set => Set(nameof(MaxOffsetBeforeUnpleatedRealignmentMs), ref _maxOffsetBeforeUnpleatedRealignmentMs, value);
    }
}
