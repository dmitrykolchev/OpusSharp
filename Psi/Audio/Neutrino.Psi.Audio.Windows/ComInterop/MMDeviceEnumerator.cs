﻿// <copyright file="MMDeviceEnumerator.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Runtime.InteropServices;

namespace Neutrino.Psi.Audio.ComInterop;

/// <summary>
/// MMDeviceEnumerator COM class declaration.
/// </summary>
[ComImport]
[Guid(Guids.MMDeviceEnumeratorCLSIDString)]
internal class MMDeviceEnumerator
{
}
