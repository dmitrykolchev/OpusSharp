// <copyright file="WaveFileWriter.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.IO;
using Microsoft.Psi.Components;

namespace Microsoft.Psi.Audio;

/// <summary>
/// Component that writes an audio stream into a WAVE file.
/// </summary>
public sealed class WaveFileWriter : SimpleConsumer<AudioBuffer>, IDisposable
{
    private readonly string _outputFilename;
    private WaveDataWriterClass _writer;

    /// <summary>
    /// Initializes a new instance of the <see cref="WaveFileWriter"/> class.
    /// </summary>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="filename">The path name of the Wave file.</param>
    /// <param name="name">An optional name for this component.</param>
    public WaveFileWriter(Pipeline pipeline, string filename, string name = nameof(WaveFileWriter))
        : base(pipeline, name)
    {
        _outputFilename = filename;
    }

    /// <summary>
    /// Disposes the component.
    /// </summary>
    public void Dispose()
    {
        if (_writer != null)
        {
            _writer.Dispose();
            _writer = null;
        }
    }

    /// <summary>
    /// The receiver for the audio messages.
    /// </summary>
    /// <param name="message">The message that was received.</param>
    public override void Receive(Message<AudioBuffer> message)
    {
        if (_writer == null)
        {
            WaveFormat format = message.Data.Format.DeepClone();
            _writer = new WaveDataWriterClass(new FileStream(_outputFilename, FileMode.Create), format);
        }

        _writer.Write(message.Data.Data);
    }
}
