// <copyright file="IMMDeviceCollection.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Runtime.InteropServices;

namespace Neutrino.Psi.Audio.ComInterop;

/// <summary>
/// IMMDeviceCollection COM interface (defined in Mmdeviceapi.h).
/// </summary>
[ComImport]
[Guid(Guids.IMMDeviceCollectionIIDString)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceCollection
{
    /// <summary>
    /// Retrieves a count of the devices in the device collection.
    /// </summary>
    /// <returns>The number of devices in the collection.</returns>
    int GetCount();

    /// <summary>
    /// Retrieves the specified item in the device collection.
    /// </summary>
    /// <param name="deviceNumber">The device number.</param>
    /// <returns>The IMMDevice interface of the specified item in the device collection.</returns>
    IMMDevice Item(int deviceNumber);
}
