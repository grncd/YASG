#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using System;
using System.Runtime.InteropServices;

internal static class WasapiInterop
{
    // ── P/Invoke ───────────────────────────────────────────────────────

    [DllImport("ole32.dll")]
    internal static extern int CoCreateInstance(
        ref Guid clsid, IntPtr pUnkOuter, uint dwClsContext,
        ref Guid iid, out IntPtr ppv);

    [DllImport("ole32.dll")]
    internal static extern int CoInitializeEx(IntPtr pvReserved, uint dwCoInit);

    [DllImport("ole32.dll")]
    internal static extern void CoUninitialize();

    [DllImport("ole32.dll")]
    internal static extern void CoTaskMemFree(IntPtr pv);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern IntPtr CreateEventW(
        IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, string lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint WaitForMultipleObjects(
        uint nCount, IntPtr[] lpHandles, bool bWaitAll, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool SetEvent(IntPtr hEvent);

    [DllImport("avrt.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern IntPtr AvSetMmThreadCharacteristicsW(string TaskName, ref uint TaskIndex);

    [DllImport("avrt.dll", SetLastError = true)]
    internal static extern bool AvRevertMmThreadCharacteristics(IntPtr avrtHandle);

    // ── GUIDs ──────────────────────────────────────────────────────────

    internal static Guid CLSID_MMDeviceEnumerator = new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E");
    internal static Guid IID_IMMDeviceEnumerator = new Guid("A95664D2-9614-4F35-A746-DE8DB63617E6");
    internal static Guid IID_IAudioClient = new Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
    internal static Guid IID_IAudioCaptureClient = new Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317");
    internal static Guid IID_IAudioRenderClient = new Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2");

    // PKEY_Device_FriendlyName = {a45c254e-df1c-4efd-8020-67d146a850e0}, 14
    internal static PropertyKey PKEY_Device_FriendlyName = new PropertyKey
    {
        fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"),
        pid = 14
    };

    // ── Constants ──────────────────────────────────────────────────────

    internal const uint CLSCTX_ALL = 0x17;
    internal const uint COINIT_MULTITHREADED = 0x0;

    internal const int AUDCLNT_SHAREMODE_SHARED = 0;
    internal const uint AUDCLNT_STREAMFLAGS_EVENTCALLBACK = 0x00040000;
    internal const uint AUDCLNT_STREAMFLAGS_NOPERSIST = 0x00080000;

    internal const int eRender = 0;
    internal const int eCapture = 1;
    internal const int eConsole = 0;
    internal const uint DEVICE_STATE_ACTIVE = 0x00000001;
    internal const int STGM_READ = 0;

    internal const uint WAIT_OBJECT_0 = 0x00000000;
    internal const uint WAIT_TIMEOUT = 0x00000102;
    internal const uint WAIT_FAILED = 0xFFFFFFFF;
    internal const uint INFINITE = 0xFFFFFFFF;

    // HRESULT codes
    internal const int S_OK = 0;
    internal const int AUDCLNT_E_DEVICE_INVALIDATED = unchecked((int)0x88890004);
    internal const int AUDCLNT_E_BUFFER_TOO_LARGE = unchecked((int)0x88890006);
    internal const int AUDCLNT_S_BUFFER_EMPTY = 0x08890001;

    // ── Structs ────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    internal struct PropertyKey
    {
        public Guid fmtid;
        public uint pid;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PropVariant
    {
        public ushort vt;
        public ushort wReserved1;
        public ushort wReserved2;
        public ushort wReserved3;
        public IntPtr data1;
        public IntPtr data2;

        public string GetString()
        {
            // VT_LPWSTR = 31
            if (vt == 31 && data1 != IntPtr.Zero)
                return Marshal.PtrToStringUni(data1);
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct WAVEFORMATEX
    {
        public ushort wFormatTag;
        public ushort nChannels;
        public uint nSamplesPerSec;
        public uint nAvgBytesPerSec;
        public ushort nBlockAlign;
        public ushort wBitsPerSample;
        public ushort cbSize;

        internal static WAVEFORMATEX FromPtr(IntPtr ptr)
        {
            return Marshal.PtrToStructure<WAVEFORMATEX>(ptr);
        }
    }

    // WAVEFORMATEXTENSIBLE subformat GUIDs
    internal static readonly Guid KSDATAFORMAT_SUBTYPE_IEEE_FLOAT =
        new Guid("00000003-0000-0010-8000-00aa00389b71");
    internal static readonly Guid KSDATAFORMAT_SUBTYPE_PCM =
        new Guid("00000001-0000-0010-8000-00aa00389b71");

    // WAVEFORMATEXTENSIBLE: WAVEFORMATEX + extra fields
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct WAVEFORMATEXTENSIBLE
    {
        public WAVEFORMATEX Format;
        public ushort wValidBitsPerSample; // union with wSamplesPerBlock
        public uint dwChannelMask;
        public Guid SubFormat;

        internal static WAVEFORMATEXTENSIBLE FromPtr(IntPtr ptr)
        {
            return Marshal.PtrToStructure<WAVEFORMATEXTENSIBLE>(ptr);
        }
    }

    internal const ushort WAVE_FORMAT_PCM = 1;
    internal const ushort WAVE_FORMAT_IEEE_FLOAT = 3;
    internal const ushort WAVE_FORMAT_EXTENSIBLE = 0xFFFE;

    // ── COM Interfaces ─────────────────────────────────────────────────

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(int dataFlow, uint dwStateMask, out IMMDeviceCollection ppDevices);
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppEndpoint);
        // Remaining vtable slots (not used):
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string pwstrId, out IMMDevice ppDevice);
        int RegisterEndpointNotificationCallback(IntPtr pClient);
        int UnregisterEndpointNotificationCallback(IntPtr pClient);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceCollection
    {
        int GetCount(out uint pcDevices);
        int Item(uint nDevice, out IMMDevice ppDevice);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDevice
    {
        int Activate(ref Guid iid, uint dwClsCtx, IntPtr pActivationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
        int OpenPropertyStore(int stgmAccess, out IPropertyStore ppProperties);
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);
        int GetState(out uint pdwState);
    }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPropertyStore
    {
        int GetCount(out uint cProps);
        int GetAt(uint iProp, out PropertyKey pkey);
        int GetValue(ref PropertyKey key, out PropVariant pv);
        int SetValue(ref PropertyKey key, ref PropVariant propvar);
        int Commit();
    }

    [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioClient
    {
        int Initialize(int ShareMode, uint StreamFlags, long hnsBufferDuration,
            long hnsPeriodicity, IntPtr pFormat, IntPtr AudioSessionGuid);
        int GetBufferSize(out uint pNumBufferFrames);
        int GetStreamLatency(out long phnsLatency);
        int GetCurrentPadding(out uint pNumPaddingFrames);
        int IsFormatSupported(int ShareMode, IntPtr pFormat, out IntPtr ppClosestMatch);
        int GetMixFormat(out IntPtr ppDeviceFormat);
        int GetDevicePeriod(out long phnsDefaultDevicePeriod, out long phnsMinimumDevicePeriod);
        int Start();
        int Stop();
        int Reset();
        int SetEventHandle(IntPtr eventHandle);
        int GetService(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
    }

    [ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioCaptureClient
    {
        int GetBuffer(out IntPtr ppData, out uint pNumFramesToRead,
            out uint pdwFlags, out ulong pu64DevicePosition, out ulong pu64QPCPosition);
        int ReleaseBuffer(uint NumFramesRead);
        int GetNextPacketSize(out uint pNumFramesInNextPacket);
    }

    [ComImport, Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioRenderClient
    {
        int GetBuffer(uint NumFramesRequested, out IntPtr ppData);
        int ReleaseBuffer(uint NumFramesWritten, uint dwFlags);
    }
}
#endif
