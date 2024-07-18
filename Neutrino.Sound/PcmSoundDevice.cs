// <copyright file="PcmSoundDevice.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

namespace Neutrino.Sound;

public abstract class PcmSoundDevice : IDisposable
{
    private readonly PcmSoundDeviceOptions _options;

    protected PcmSoundDevice(PcmSoundDeviceOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public static PcmSoundDevice Create(PcmSoundDeviceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (OperatingSystem.IsWindows())
        {
            throw new NotImplementedException();
        }
        else if (OperatingSystem.IsLinux())
        {
            return new PcmLinuxSoundDevice(options);
        }
        else if (OperatingSystem.IsMacOS())
        {
            throw new NotImplementedException();
        }
        else
        {
            throw new NotSupportedException("Not supported operating system");
        }
    }

    public abstract void Open(string name = "default");

    public abstract void Close(bool drop);

    public void Play(string wavFileName)
    {
        using FileStream file = File.OpenRead(wavFileName);
        Play(file);
    }

    public abstract void Play(Stream wavSream);

    protected virtual void Dispose(bool disposing)
    {
        Close(true);
    }

    ~PcmSoundDevice()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: false);
    }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
