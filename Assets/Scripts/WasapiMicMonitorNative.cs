using UnityEngine;

/// <summary>
/// Thin facade for WASAPI device enumeration. Delegates to WasapiDeviceEnumerator
/// (pure C# COM interop). Per-mic session lifecycle is handled by WasapiMicSession.
/// </summary>
public static class WasapiMicMonitorNative
{
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
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
            _initialized = WasapiDeviceEnumerator.Initialize();
            if (!_initialized)
                Debug.LogWarning("[WasapiMicMonitor] Device enumeration init failed");
            return _initialized;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[WasapiMicMonitor] Initialize failed: {e.Message}");
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
        WasapiDeviceEnumerator.Shutdown();
        _initialized = false;
#endif
    }

    public static int GetDeviceCount()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (!_initialized) return 0;
        return WasapiDeviceEnumerator.GetDeviceCount();
#else
        return 0;
#endif
    }

    public static string GetDeviceName(int index)
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (!_initialized) return null;
        return WasapiDeviceEnumerator.GetDeviceName(index);
#else
        return null;
#endif
    }

    public static void RefreshDevices()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (!_initialized) return;
        WasapiDeviceEnumerator.RefreshDeviceList();
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
        return WasapiDeviceEnumerator.FindDeviceIndexByUnityName(unityMicName);
#else
        return -1;
#endif
    }
}
