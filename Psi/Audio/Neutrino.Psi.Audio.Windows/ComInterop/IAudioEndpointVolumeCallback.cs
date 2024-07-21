// <copyright file="IAudioEndpointVolumeCallback.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Runtime.InteropServices;

namespace Microsoft.Psi.Audio.ComInterop;

/// <summary>
/// IAudioEndpointVolumeCallback COM interface (defined in Endpointvolume.h).
/// </summary>
[ComImport]
[Guid(Guids.IAudioEndpointVolumeCallbackIIDString)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEndpointVolumeCallback
{
    /// <summary>
    /// Notifies the client that the volume level or muting state of the audio endpoint device has changed.
    /// </summary>
    /// <param name="notify">Pointer to the volume-notification data.</param>
    void OnNotify(IntPtr notify);
}
