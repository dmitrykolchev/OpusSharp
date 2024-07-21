// <copyright file="WaveDataWriterClass.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.IO;
using System.Text;

namespace Microsoft.Psi.Audio;

/// <summary>
/// Provides functionality to create Wave files.
/// </summary>
public sealed class WaveDataWriterClass : IDisposable
{
    private readonly WaveFormat _format;
    private BinaryWriter _writer;
    private uint _dataLength;
    private long _dataLengthFieldPosition;

    /// <summary>
    /// Initializes a new instance of the <see cref="WaveDataWriterClass"/> class.
    /// </summary>
    /// <param name="stream">Stream to which to write.</param>
    /// <param name="format">The audio format.</param>
    public WaveDataWriterClass(Stream stream, WaveFormat format)
    {
        _format = format;
        _writer = new BinaryWriter(stream);
        WriteWaveFileHeader(format);
        WriteWaveDataHeader();
    }

    /// <summary>
    /// Disposes the component.
    /// </summary>
    public void Dispose()
    {
        if (_writer != null)
        {
            try
            {
                Flush();
            }
            finally
            {
                _writer.Close();
                _writer = null;
            }
        }
    }

    /// <summary>
    /// Writes the wave data to the file.
    /// </summary>
    /// <param name="data">The raw wave data.</param>
    public void Write(byte[] data)
    {
        _writer.Write(data);
        _dataLength += (uint)data.Length;
    }

    /// <summary>
    /// Flushes the data to disk and updates the headers.
    /// </summary>
    public void Flush()
    {
        _writer.Flush();

        long pos = _writer.BaseStream.Position;

        // Update the file length
        _writer.Seek(4, SeekOrigin.Begin);
        _writer.Write((uint)_writer.BaseStream.Length - 8);

        // Update the data section length
        _writer.Seek((int)_dataLengthFieldPosition, SeekOrigin.Begin);
        _writer.Write(_dataLength);

        _writer.BaseStream.Position = pos;
    }

    /// <summary>
    /// Writes out the wave header to the file.
    /// </summary>
    /// <param name="format">The wave audio format of the data.</param>
    private void WriteWaveFileHeader(WaveFormat format)
    {
        _writer.Write(Encoding.UTF8.GetBytes("RIFF"));
        _writer.Write(0u); // file length field which needs to be updated as data is written
        _writer.Write(Encoding.UTF8.GetBytes("WAVE"));
        _writer.Write(Encoding.UTF8.GetBytes("fmt "));

        uint headerLength = 18u + format.ExtraSize; // size of fixed portion of WaveFormat is 18
        _writer.Write(headerLength);
        _writer.Write(format);
    }

    /// <summary>
    /// Writes out the data header to the file.
    /// </summary>
    private void WriteWaveDataHeader()
    {
        _writer.Write(Encoding.UTF8.GetBytes("data"));

        // capture the position of the data length field
        _dataLengthFieldPosition = _writer.BaseStream.Position;

        _writer.Write(0u); // data length field which needs to be updated as data is written
    }
}
