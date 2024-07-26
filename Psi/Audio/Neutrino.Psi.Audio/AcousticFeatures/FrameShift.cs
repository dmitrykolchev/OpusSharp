// <copyright file="FrameShift.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using Neutrino.Psi.Common;
using Neutrino.Psi.Components;
using Neutrino.Psi.Executive;

namespace Neutrino.Psi.Audio;

/// <summary>
/// Component that performs an accumulate and shift operation on a stream of audio buffers.
/// </summary>
public sealed class FrameShift : ConsumerProducer<byte[], byte[]>
{
    private readonly int _frameSizeInBytes;
    private readonly int _frameShiftInBytes;
    private readonly int _frameOverlapInBytes;
    private readonly byte[] _frameBuffer;
    private readonly double _bytesPerSec;
    private int _frameBytesRemaining;
    private DateTime _lastOriginatingTime = DateTime.MinValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="FrameShift"/> class.
    /// </summary>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="frameSizeInBytes">The frame size in bytes.</param>
    /// <param name="frameShiftInBytes">The number of bytes to shift by.</param>
    /// <param name="bytesPerSec">The sampling frequency in bytes per second.</param>
    /// <param name="name">An optional name for this component.</param>
    public FrameShift(Pipeline pipeline, int frameSizeInBytes, int frameShiftInBytes, double bytesPerSec, string name = nameof(FrameShift))
        : base(pipeline, name)
    {
        _frameSizeInBytes = frameSizeInBytes;
        _frameShiftInBytes = frameShiftInBytes;
        _frameOverlapInBytes = frameSizeInBytes - frameShiftInBytes;
        _bytesPerSec = bytesPerSec;
        _frameBuffer = new byte[frameSizeInBytes];
        _frameBytesRemaining = frameSizeInBytes;
    }

    /// <summary>
    /// Receiver for the input data.
    /// </summary>
    /// <param name="data">A buffer containing the input data.</param>
    /// <param name="e">The message envelope for the input data.</param>
    protected override void Receive(byte[] data, Envelope e)
    {
        int messageBytesRemaining = data.Length;
        while (messageBytesRemaining > 0)
        {
            int bytesToCopy = Math.Min(_frameBytesRemaining, messageBytesRemaining);
            Array.Copy(data, data.Length - messageBytesRemaining, _frameBuffer, _frameBuffer.Length - _frameBytesRemaining, bytesToCopy);
            messageBytesRemaining -= bytesToCopy;
            _frameBytesRemaining -= bytesToCopy;
            if (_frameBytesRemaining == 0)
            {
                // Compute the originating time of the frame
                DateTime originatingTime = e.OriginatingTime.AddTicks(
                    -(long)(TimeSpan.TicksPerSecond * (messageBytesRemaining / _bytesPerSec)));

                // Fixup potential out of order timestamps where successive audio buffer timestamps
                // drastically overlap. This could be indicative of a system time adjustment having
                // occurred between captured audio buffers.
                if (originatingTime <= _lastOriginatingTime)
                {
                    originatingTime = _lastOriginatingTime.AddTicks(1); // add tick to avoid time collision
                }

                _lastOriginatingTime = originatingTime;

                // Post the completed frame
                byte[] frame = _frameBuffer;
                Out.Post(frame, originatingTime);

                // Shift the frame
                Array.Copy(_frameBuffer, _frameShiftInBytes, _frameBuffer, 0, _frameOverlapInBytes);
                _frameBytesRemaining += _frameShiftInBytes;
            }
        }
    }
}
