// <copyright file="Reframe.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using Microsoft.Psi.Components;

namespace Microsoft.Psi.Audio;

/// <summary>
/// Component that reframes a stream of audio buffers to fixed size chunks.
/// </summary>
public sealed class Reframe : ConsumerProducer<AudioBuffer, AudioBuffer>
{
    private int _frameSizeInBytes;
    private byte[] _frameBuffer;
    private int _frameBytesRemaining;
    private TimeSpan _frameDuration;
    private DateTime _lastOriginatingTime = DateTime.MinValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="Reframe"/> class.
    /// </summary>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="frameSizeInBytes">The output frame size in bytes.</param>
    /// <param name="name">An optional name for this component.</param>
    public Reframe(Pipeline pipeline, int frameSizeInBytes, string name = nameof(Reframe))
        : base(pipeline, name)
    {
        if (frameSizeInBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frameSizeInBytes), "Please specify a positive output frame size.");
        }

        _frameSizeInBytes = frameSizeInBytes;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Reframe"/> class.
    /// </summary>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="frameDuration">The output frame duration.</param>
    /// <param name="name">An optional name for this component.</param>
    public Reframe(Pipeline pipeline, TimeSpan frameDuration, string name = nameof(Reframe))
        : base(pipeline, name)
    {
        if (frameDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(frameDuration), "Please specify a positive output frame duration.");
        }

        _frameDuration = frameDuration;
    }

    /// <summary>
    /// Receiver for the input data.
    /// </summary>
    /// <param name="audio">A buffer containing the input audio.</param>
    /// <param name="e">The message envelope for the input data.</param>
    protected override void Receive(AudioBuffer audio, Envelope e)
    {
        // initialize the output frame buffer on the first audio message received as
        // we may need information in the audio format to determine the buffer size
        if (_frameBuffer == null)
        {
            // this component is constructed by specifying either an output frame size or duration
            if (_frameSizeInBytes == 0)
            {
                // initialize the output frame size (maintaining block-alignment) based on a specified duration
                _frameSizeInBytes = (int)Math.Ceiling(_frameDuration.TotalSeconds * audio.Format.SamplesPerSec) * audio.Format.BlockAlign;
            }
            else
            {
                // initialize the output frame duration based on a specified size in bytes (for completeness
                // - we don't actually use the value of this.frameDuration for the reframe computation)
                _frameDuration = TimeSpan.FromTicks(TimeSpan.TicksPerSecond * _frameSizeInBytes / audio.Format.AvgBytesPerSec);
            }

            _frameBuffer = new byte[_frameSizeInBytes];
            _frameBytesRemaining = _frameSizeInBytes;
        }

        int messageBytesRemaining = audio.Length;
        while (messageBytesRemaining > 0)
        {
            int bytesToCopy = Math.Min(_frameBytesRemaining, messageBytesRemaining);
            Array.Copy(audio.Data, audio.Length - messageBytesRemaining, _frameBuffer, _frameBuffer.Length - _frameBytesRemaining, bytesToCopy);
            messageBytesRemaining -= bytesToCopy;
            _frameBytesRemaining -= bytesToCopy;
            if (_frameBytesRemaining == 0)
            {
                // Compute the originating time of the frame
                DateTime originatingTime = e.OriginatingTime.AddTicks(
                    -(TimeSpan.TicksPerSecond * messageBytesRemaining / audio.Format.AvgBytesPerSec));

                // Fixup potential out of order timestamps where successive audio buffer timestamps
                // drastically overlap. This could be indicative of a system time adjustment having
                // occurred between captured audio buffers.
                if (originatingTime <= _lastOriginatingTime)
                {
                    originatingTime = _lastOriginatingTime.AddTicks(1); // add tick to avoid time collision
                }

                _lastOriginatingTime = originatingTime;

                // Post the completed frame
                Out.Post(new AudioBuffer(_frameBuffer, audio.Format), originatingTime);

                // Reset the frame
                _frameBytesRemaining = _frameSizeInBytes;
            }
        }
    }
}
