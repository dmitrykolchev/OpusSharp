// <copyright file="OpusApi.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System.Runtime.InteropServices;

namespace DykBits.Audio.Codecs.Opus;

internal sealed unsafe class OpusApi
{
    private const string OpusDll = "opus.dll";
    private const string OpusSo = "libopus.so.0.9.0";

    private static readonly Lazy<OpusApi> s_api = new(AllocateOpusApi);

    internal static OpusApi Api => s_api.Value;

    private OpusApi(OpusApiTable* apiTable)
    {
        ApiTable = apiTable;
    }

    public bool IsSupported => ApiTable != null;

    public OpusApiTable* ApiTable { get; }

    private static OpusApi AllocateOpusApi()
    {
        if (!TryOpenOpus(out OpusApiTable* apiTable))
        {
            throw new InvalidOperationException();
        }
        return new OpusApi(apiTable);
    }

    private static bool TryOpenOpus(out OpusApiTable* apiTable)
    {
        bool loaded = false;
        IntPtr opusHandle;

        if (OperatingSystem.IsWindows())
        {
            loaded = NativeLibrary.TryLoad(OpusDll,
                typeof(OpusApi).Assembly, DllImportSearchPath.AssemblyDirectory, out opusHandle);
        }
        else if (OperatingSystem.IsLinux())
        {
            loaded = NativeLibrary.TryLoad(OpusSo,
                typeof(OpusApi).Assembly, DllImportSearchPath.AssemblyDirectory, out opusHandle);
        }
        else
        {
            opusHandle = IntPtr.Zero;
        }
        if (!loaded)
        {
            apiTable = null;
            return false;
        }
        OpusApiTable* temp = (OpusApiTable*)Marshal.AllocHGlobal(Marshal.SizeOf<OpusApiTable>()).ToPointer();

        temp->opus_encoder_get_size = (delegate* unmanaged[Cdecl]<int, int>)
            GetExport(nameof(OpusApiTable.opus_encoder_get_size));
        temp->opus_decoder_get_size = (delegate* unmanaged[Cdecl]<int, int>)
            GetExport(nameof(OpusApiTable.opus_decoder_get_size));
        temp->opus_encoder_create = (delegate* unmanaged[Cdecl]<int, int, int, int*, IntPtr>)
            GetExport(nameof(OpusApiTable.opus_encoder_create));
        temp->opus_decoder_create = (delegate* unmanaged[Cdecl]<int, int, int*, IntPtr>)
            GetExport(nameof(OpusApiTable.opus_decoder_create));
        temp->opus_encoder_init = (delegate* unmanaged[Cdecl]<IntPtr, int, int, int, int>)
            GetExport(nameof(OpusApiTable.opus_encoder_init));
        temp->opus_encode = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, int, IntPtr, int, int>)
            GetExport(nameof(OpusApiTable.opus_encode));
        temp->opus_decode = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, int, IntPtr, int, int, int>)
            GetExport(nameof(OpusApiTable.opus_decode));
        temp->opus_encoder_destroy = (delegate* unmanaged[Cdecl]<IntPtr, void>)
            GetExport(nameof(OpusApiTable.opus_encoder_destroy));
        temp->opus_decoder_destroy = (delegate* unmanaged[Cdecl]<IntPtr, void>)
            GetExport(nameof(OpusApiTable.opus_decoder_destroy));
        temp->opus_encoder_ctl = (delegate* unmanaged[Cdecl]<IntPtr, int, IntPtr, int>)
            GetExport(nameof(OpusApiTable.opus_encoder_ctl));
        temp->opus_decoder_ctl = (delegate* unmanaged[Cdecl]<IntPtr, int, IntPtr, int>)
            GetExport(nameof(OpusApiTable.opus_decoder_ctl));

        IntPtr GetExport(string name)
        {
            return NativeLibrary.GetExport(opusHandle, name);
        }
        apiTable = temp;
        return true;
    }
}
