// <copyright file="Native.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Runtime.InteropServices;
using static System.Runtime.InteropServices.JavaScript.JSType;
using System.Runtime.Intrinsics.X86;

namespace DykBits.Audio.Codecs.Opus;

internal class Native
{
    public const string OpusDll = "opus.dll";

    public const int OPUS_SET_APPLICATION_REQUEST = 4000;
    public const int OPUS_GET_APPLICATION_REQUEST = 4001;
    public const int OPUS_SET_BITRATE_REQUEST = 4002;
    public const int OPUS_GET_BITRATE_REQUEST = 4003;
    public const int OPUS_SET_MAX_BANDWIDTH_REQUEST = 4004;
    public const int OPUS_GET_MAX_BANDWIDTH_REQUEST = 4005;
    public const int OPUS_SET_VBR_REQUEST = 4006;
    public const int OPUS_GET_VBR_REQUEST = 4007;
    public const int OPUS_SET_BANDWIDTH_REQUEST = 4008;
    public const int OPUS_GET_BANDWIDTH_REQUEST = 4009;
    public const int OPUS_SET_COMPLEXITY_REQUEST = 4010;
    public const int OPUS_GET_COMPLEXITY_REQUEST = 4011;
    public const int OPUS_SET_INBAND_FEC_REQUEST = 4012;
    public const int OPUS_GET_INBAND_FEC_REQUEST = 4013;
    public const int OPUS_SET_PACKET_LOSS_PERC_REQUEST = 4014;
    public const int OPUS_GET_PACKET_LOSS_PERC_REQUEST = 4015;
    public const int OPUS_SET_DTX_REQUEST = 4016;
    public const int OPUS_GET_DTX_REQUEST = 4017;
    public const int OPUS_SET_VBR_CONSTRAINT_REQUEST = 4020;
    public const int OPUS_GET_VBR_CONSTRAINT_REQUEST = 4021;
    public const int OPUS_SET_FORCE_CHANNELS_REQUEST = 4022;
    public const int OPUS_GET_FORCE_CHANNELS_REQUEST = 4023;
    public const int OPUS_SET_SIGNAL_REQUEST = 4024;
    public const int OPUS_GET_SIGNAL_REQUEST = 4025;
    public const int OPUS_GET_LOOKAHEAD_REQUEST = 4027;
    /* public const int OPUS_RESET_STATE 4028 */
    public const int OPUS_GET_SAMPLE_RATE_REQUEST = 4029;
    public const int OPUS_GET_FINAL_RANGE_REQUEST = 4031;
    public const int OPUS_GET_PITCH_REQUEST = 4033;
    public const int OPUS_SET_GAIN_REQUEST = 4034;
    public const int OPUS_GET_GAIN_REQUEST = 4045; /* Should have been 4035 */
    public const int OPUS_SET_LSB_DEPTH_REQUEST = 4036;
    public const int OPUS_GET_LSB_DEPTH_REQUEST = 4037;
    public const int OPUS_GET_LAST_PACKET_DURATION_REQUEST = 4039;
    public const int OPUS_SET_EXPERT_FRAME_DURATION_REQUEST = 4040;
    public const int OPUS_GET_EXPERT_FRAME_DURATION_REQUEST = 4041;
    public const int OPUS_SET_PREDICTION_DISABLED_REQUEST = 4042;
    public const int OPUS_GET_PREDICTION_DISABLED_REQUEST = 4043;
    /* Don't use 4045, it's already taken by OPUS_GET_GAIN_REQUEST */
    public const int OPUS_SET_PHASE_INVERSION_DISABLED_REQUEST = 4046;
    public const int OPUS_GET_PHASE_INVERSION_DISABLED_REQUEST = 4047;
    public const int OPUS_GET_IN_DTX_REQUEST = 4049;


    /// <summary>
    /// No error 
    public const int OPUS_OK = 0;
    /// <summary>
    /// One or more invalid/out of range arguments 
    /// </summary>
    public const int OPUS_BAD_ARG = -1;
    /// <summary>
    /// Not enough bytes allocated in the buffer 
    /// </summary>
    public const int OPUS_BUFFER_TOO_SMALL = -2;
    /// <summary>
    /// An internal error was detected 
    /// </summary>
    public const int OPUS_INTERNAL_ERROR = -3;
    /// <summary>
    /// The compressed data passed is corrupted 
    /// </summary>
    public const int OPUS_INVALID_PACKET = -4;
    /// <summary>
    /// Invalid/unsupported request number 
    /// </summary>
    public const int OPUS_UNIMPLEMENTED = -5;
    /// <summary>
    /// An encoder or decoder structure is invalid or already freed 
    /// </summary>
    public const int OPUS_INVALID_STATE = -6;
    /// <summary>
    /// Memory allocation has failed 
    /// </summary>
    public const int OPUS_ALLOC_FAIL = -7;


    // Best for most VoIP/videoconference applications where listening quality and intelligibility matter most
    public const int OPUS_APPLICATION_VOIP = 2048;
    // Best for broadcast/high-fidelity application where the decoded audio should be as close as possible to the input
    public const int OPUS_APPLICATION_AUDIO = 2049;
    // Only use when lowest-achievable latency is what matters most. Voice-optimized modes cannot be used.
    public const int OPUS_APPLICATION_RESTRICTED_LOWDELAY = 2051;

    /// <summary>
    /// Gets the size of an <code>OpusEncoder</code> structure
    /// </summary>
    /// <param name="channels">Number of channels. This must be 1 or 2.</param>
    /// <returns>The size in bytes.</returns>
    [DllImport(OpusDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int opus_encoder_get_size(int channels);

    /// <summary>
    /// Gets the size of an <code>OpusDecoder</code> structure.
    /// </summary>
    /// <param name="channels">Number of channels. This must be 1 or 2.</param>
    /// <returns></returns>
    [DllImport(OpusDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int opus_decoder_get_size(int channels);

    /// <summary>
    /// Allocates and initializes an encoder state
    /// </summary>
    /// <param name="fs"> Sampling rate of input signal (Hz) 
    /// This must be one of 8000, 12000, 16000, 24000, or 48000</param>
    /// <param name="channels">Number of channels (1 or 2) in input signal</param>
    /// <param name="application">Coding mode (one of @ref OPUS_APPLICATION_VOIP, @ref OPUS_APPLICATION_AUDIO, or @ref OPUS_APPLICATION_RESTRICTED_LOWDELAY)</param>
    /// <param name="error"></param>
    /// <returns>encoder state</returns>
    [DllImport(OpusDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr opus_encoder_create(int fs, int channels, int application, out int error);

    /// <summary>
    /// Allocates and initializes a decoder state.
    /// 
    /// Internally Opus stores data at 48000 Hz, so that should be the default value for Fs.
    /// However, the decoder can efficiently decode to buffers at 8, 12, 16, and 24 kHz so if for 
    /// some reason the caller cannot use data at the full sample rate, or knows the compressed data 
    /// doesn't use the full frequency range, it can request decoding at a reduced rate.
    /// Likewise, the decoder is capable of filling in either mono or interleaved stereo pcm buffers, 
    /// at the caller's request.
    /// </summary>
    /// <param name="fs">Sample rate to decode at (Hz). 
    /// This must be one of 8000, 12000, 16000, 24000, or 48000.</param>
    /// <param name="channels">Number of channels (1 or 2) to decode</param>
    /// <param name="error">error code</param>
    /// <returns>#OPUS_OK Success or opus_errorcodes</returns>
    [DllImport(OpusDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr opus_decoder_create(int fs, int channels, out int error);


    /// <summary>
    /// Initializes a previously allocated encoder state
    /// The memory pointed to by st must be at least the size returned by opus_encoder_get_size().
    /// This is intended for applications which use their own allocator instead of malloc.
    /// <see cref="opus_encoder_create(int, int, int, out int)"/>
    /// <see cref=""/>,opus_encoder_get_size()
    /// To reset a previously initialized state, use the #OPUS_RESET_STATE CTL.
    /// </summary>
    /// <param name="encoder">encoder state</param>
    /// <param name="fs"> Sampling rate of input signal (Hz) 
    /// This must be one of 8000, 12000, 16000, 24000, or 48000</param>
    /// <param name="channels">Number of channels (1 or 2) in input signal</param>
    /// <param name="application">Coding mode (one of @ref OPUS_APPLICATION_VOIP, @ref OPUS_APPLICATION_AUDIO, or @ref OPUS_APPLICATION_RESTRICTED_LOWDELAY)</param>
    /// <returns>OPUS_OK Success or @ref opus_errorcodes</returns>
    [DllImport(OpusDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int opus_encoder_init(IntPtr encoder, int fs, int channels, int application);

    /// <summary>
    /// Encodes an Opus frame
    /// </summary>
    /// <param name="encoder">Encoder state</param>
    /// <param name="pcm">Input signal (interleaved if 2 channels). length is frame_size*channels*sizeof(opus_int16)</param>
    /// <param name="frameSize">Number of samples per channel in the input signal. 
    /// This must be an Opus frame size for the encoder's sampling rate. 
    /// For example, at 48 kHz the permitted values are 120, 240, 480, 960, 1920, and 2880. 
    /// Passing in a duration of less than 10 ms (480 samples at 48 kHz) will prevent the 
    /// encoder from using the LPC or hybrid modes.</param>
    /// <param name="data">Output payload. This must contain storage for at least max_data_bytes</param>
    /// <param name="maxDataBytes">Size of the allocated memory for the output payload. 
    /// This may be used to impose an upper limit on the instant bitrate, but should not 
    /// be used as the only bitrate control. Use #OPUS_SET_BITRATE to control the bitrate.</param>
    /// <returns>The length of the encoded packet (in bytes) on success or a negative error code on failure.</returns>
    [DllImport(OpusDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int opus_encode(IntPtr encoder, IntPtr pcm, int frameSize, IntPtr data, int maxDataBytes);


    [DllImport(OpusDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int opus_decode(IntPtr decoder, IntPtr data, int len, IntPtr pcm, int frameSize, int decodeFec);

    /// <summary>
    /// Frees an <code>OpusEncoder</code> allocated by opus_encoder_create()
    /// </summary>
    /// <param name="encoder">State to be freed</param>
    [DllImport(OpusDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void opus_encoder_destroy(IntPtr encoder);

    /// <summary>
    /// Frees an <code>OpusDecoder</code> allocated by <see cref="opus_decoder_create(int, int, out int)"/>
    /// </summary>
    /// <param name="decoder">State to be freed</param>
    [DllImport(OpusDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void opus_decoder_destroy(IntPtr decoder);

    /// <summary>
    /// Perform a CTL function on an Opus encoder.
    /// Generally the request and subsequent arguments are generated
    /// by a convenience macro.
    /// </summary>
    /// <param name="encoder"></param>
    /// <param name="request"></param>
    /// <param name="value"></param>
    /// <returns></returns>
    [DllImport(OpusDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int opus_encoder_ctl(IntPtr encoder, int request, int value);

    [DllImport(OpusDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int opus_encoder_ctl(IntPtr encoder, int request, out int value);
}
