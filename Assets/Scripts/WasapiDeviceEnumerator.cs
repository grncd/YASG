#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// Pure C# WASAPI device enumeration. Replaces the native DLL's
/// WMM_GetDeviceCount / WMM_GetDeviceName with COM interop.
/// </summary>
internal static class WasapiDeviceEnumerator
{
    private static readonly List<string> _deviceNames = new List<string>();
    private static bool _initialized;

    public static bool Initialize()
    {
        if (_initialized) return true;
        RefreshDeviceList();
        _initialized = true;
        return _deviceNames.Count >= 0; // always succeeds if COM works
    }

    public static void Shutdown()
    {
        _deviceNames.Clear();
        _initialized = false;
    }

    public static void RefreshDeviceList()
    {
        _deviceNames.Clear();

        WasapiInterop.IMMDeviceEnumerator enumerator = null;
        WasapiInterop.IMMDeviceCollection collection = null;

        try
        {
            var clsid = WasapiInterop.CLSID_MMDeviceEnumerator;
            var iid = WasapiInterop.IID_IMMDeviceEnumerator;
            int hr = WasapiInterop.CoCreateInstance(ref clsid, IntPtr.Zero,
                WasapiInterop.CLSCTX_ALL, ref iid, out IntPtr pEnum);
            if (hr != 0)
            {
                Debug.LogWarning($"[WasapiDeviceEnumerator] CoCreateInstance failed: 0x{hr:X8}");
                return;
            }

            enumerator = (WasapiInterop.IMMDeviceEnumerator)Marshal.GetObjectForIUnknown(pEnum);
            Marshal.Release(pEnum);

            hr = enumerator.EnumAudioEndpoints(WasapiInterop.eCapture,
                WasapiInterop.DEVICE_STATE_ACTIVE, out collection);
            if (hr != 0)
            {
                Debug.LogWarning($"[WasapiDeviceEnumerator] EnumAudioEndpoints failed: 0x{hr:X8}");
                return;
            }

            collection.GetCount(out uint count);

            for (uint i = 0; i < count; i++)
            {
                WasapiInterop.IMMDevice device = null;
                WasapiInterop.IPropertyStore props = null;
                try
                {
                    collection.Item(i, out device);
                    device.OpenPropertyStore(WasapiInterop.STGM_READ, out props);

                    var pkey = WasapiInterop.PKEY_Device_FriendlyName;
                    props.GetValue(ref pkey, out WasapiInterop.PropVariant pv);
                    string name = pv.GetString() ?? $"Unknown Device {i}";
                    _deviceNames.Add(name);
                }
                finally
                {
                    if (props != null) Marshal.ReleaseComObject(props);
                    if (device != null) Marshal.ReleaseComObject(device);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[WasapiDeviceEnumerator] RefreshDeviceList failed: {e.Message}");
        }
        finally
        {
            if (collection != null) Marshal.ReleaseComObject(collection);
            if (enumerator != null) Marshal.ReleaseComObject(enumerator);
        }
    }

    public static int GetDeviceCount()
    {
        return _deviceNames.Count;
    }

    public static string GetDeviceName(int index)
    {
        if (index < 0 || index >= _deviceNames.Count) return null;
        return _deviceNames[index];
    }

    /// <summary>
    /// Maps a Unity Microphone.devices name to a WASAPI device index.
    /// Unity truncates device names to 64 chars; WASAPI provides full names.
    /// Returns -1 if no match found.
    /// </summary>
    public static int FindDeviceIndexByUnityName(string unityMicName)
    {
        if (string.IsNullOrEmpty(unityMicName)) return -1;

        int count = _deviceNames.Count;

        // Pass 1: exact or prefix match
        for (int i = 0; i < count; i++)
        {
            string wasapiName = _deviceNames[i];
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
            string wasapiName = _deviceNames[i];
            if (wasapiName != null &&
                wasapiName.IndexOf(unityMicName, StringComparison.OrdinalIgnoreCase) >= 0)
                return i;
        }

        return -1;
    }
}
#endif
