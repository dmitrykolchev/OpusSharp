// <copyright file="AudioEndpointVolumeCallback.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;

namespace Microsoft.Psi.Audio.ComInterop;

/// <summary>
/// Defines the delegate for the callback that handles new audio volume notification data.
/// </summary>
/// <param name="data">An <see cref="AudioVolumeNotificationData"/> data object.</param>
internal delegate void AudioVolumeNotificationDelegate(AudioVolumeNotificationData data);

/// <summary>
/// Client implementation of the IAudioEndpointVolumeCallback interface. When a method in the
/// IAudioEndpointVolume interface changes the volume level or muting state of the endpoint device,
/// the change initiates a call to the client's IAudioEndpointVolumeCallback::OnNotify method.
/// </summary>
internal class AudioEndpointVolumeCallback : IAudioEndpointVolumeCallback
{
    private readonly AudioVolumeNotificationDelegate _callback;

    /// <summary>
    /// Initializes a new instance of the <see cref="AudioEndpointVolumeCallback"/> class.
    /// </summary>
    /// <param name="callback">
    /// The delegate to call whenever a volume-change notification is received.
    /// </param>
    internal AudioEndpointVolumeCallback(AudioVolumeNotificationDelegate callback)
    {
        _callback = callback;
    }

    /// <summary>
    /// Callback method for endpoint-volume-change notifications.
    /// </summary>
    /// <param name="pNotify">Pointer to the notification data.</param>
    public void OnNotify(IntPtr pNotify)
    {
        _callback?.Invoke(AudioVolumeNotificationData.MarshalFromPtr(pNotify));
    }
}
