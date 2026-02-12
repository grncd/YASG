using System;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// P/Invoke wrapper for the WasapiMicMonitor native DLL.
/// Provides near-zero-latency microphone monitoring via WASAPI on Windows.
/// </summary>
public static class WasapiMicMonitorNative
{
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    private const string DLL_NAME = "WasapiMicMonitor";

    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    private static extern int WMM_Initialize();

    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    private static extern void WMM_Shutdown();

    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    private static extern int WMM_GetDeviceCount();

    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    private static extern int WMM_GetDeviceName(int index,
        [MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder nameBuffer, int bufferLen);

    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    private static extern int WMM_StartMonitoring(int captureDeviceIndex, float targetLatencyMs);

    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    private static extern void WMM_StopMonitoring();

    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    private static extern int WMM_IsMonitoring();

    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    private static extern void WMM_SetVolume(float linearGain);

    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    private static extern float WMM_GetVolume();

    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    private static extern float WMM_GetActualLatencyMs();

    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr WMM_GetLastError();

    private static bool _initialized;

    public static bool IsSupported => true;
#else
    private static bool _initialized;
    public static bool IsSupported => false;
#endif

    public static bool Initialize()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (_initialized) return true;
        try
        {
            int result = WMM_Initialize();
            _initialized = (result == 0);
            if (!_initialized)
                Debug.LogWarning($"[WasapiMicMonitor] Initialize failed: {GetLastError()}");
            return _initialized;
        }
        catch (DllNotFoundException)
        {
            Debug.LogWarning("[WasapiMicMonitor] Native DLL not found. Using fallback audio path.");
            return false;
        }
#else
        return false;
#endif
    }

    public static void Shutdown()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (!_initialized) return;
        WMM_Shutdown();
        _initialized = false;
#endif
    }

    public static int GetDeviceCount()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (!_initialized) return 0;
        return WMM_GetDeviceCount();
#else
        return 0;
#endif
    }

    public static string GetDeviceName(int index)
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (!_initialized) return null;
        var sb = new System.Text.StringBuilder(512);
        int result = WMM_GetDeviceName(index, sb, sb.Capacity);
        return (result == 0) ? sb.ToString() : null;
#else
        return null;
#endif
    }

    public static bool StartMonitoring(int deviceIndex, float targetLatencyMs = 10f)
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (!_initialized) return false;
        int result = WMM_StartMonitoring(deviceIndex, targetLatencyMs);
        if (result != 0)
            Debug.LogWarning($"[WasapiMicMonitor] StartMonitoring failed: {GetLastError()}");
        return result == 0;
#else
        return false;
#endif
    }

    public static void StopMonitoring()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (!_initialized) return;
        WMM_StopMonitoring();
#endif
    }

    public static bool IsMonitoring()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (!_initialized) return false;
        return WMM_IsMonitoring() != 0;
#else
        return false;
#endif
    }

    public static void SetVolume(float linearGain)
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (!_initialized) return;
        WMM_SetVolume(linearGain);
#endif
    }

    public static float GetActualLatencyMs()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (!_initialized) return -1f;
        return WMM_GetActualLatencyMs();
#else
        return -1f;
#endif
    }

    public static string GetLastError()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        IntPtr ptr = WMM_GetLastError();
        return (ptr != IntPtr.Zero) ? Marshal.PtrToStringAnsi(ptr) : "Unknown error";
#else
        return "Not supported on this platform";
#endif
    }

    /// <summary>
    /// Maps a Unity Microphone.devices name to a WASAPI device index.
    /// Unity truncates device names to 64 chars; WASAPI provides full names.
    /// Returns -1 if no match found.
    /// </summary>
    public static int FindDeviceIndexByUnityName(string unityMicName)
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (!_initialized || string.IsNullOrEmpty(unityMicName)) return -1;

        int count = GetDeviceCount();

        // Pass 1: exact or prefix match
        for (int i = 0; i < count; i++)
        {
            string wasapiName = GetDeviceName(i);
            if (wasapiName == null) continue;

            if (wasapiName.Equals(unityMicName, StringComparison.OrdinalIgnoreCase))
                return i;
            if (wasapiName.StartsWith(unityMicName, StringComparison.OrdinalIgnoreCase))
                return i;
            if (unityMicName.StartsWith(wasapiName, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        // Pass 2: substring containment
        for (int i = 0; i < count; i++)
        {
            string wasapiName = GetDeviceName(i);
            if (wasapiName != null &&
                wasapiName.IndexOf(unityMicName, StringComparison.OrdinalIgnoreCase) >= 0)
                return i;
        }

        return -1;
#else
        return -1;
#endif
    }
}
