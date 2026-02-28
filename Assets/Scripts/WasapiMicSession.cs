#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using System;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;

/// <summary>
/// Per-microphone WASAPI monitoring session. Each instance owns its own
/// COM objects and background thread, enabling multiple simultaneous
/// low-latency mic feedback paths.
/// </summary>
internal sealed class WasapiMicSession : IDisposable
{
    // ── Public state (read from main thread) ───────────────────────────
    public volatile bool IsActive;
    public float ActualLatencyMs { get; private set; }
    public string LastError { get; private set; }

    private volatile float _volume = 1f;
    public float Volume
    {
        get => _volume;
        set => _volume = Mathf.Clamp01(value);
    }

    // ── Thread / sync ──────────────────────────────────────────────────
    private Thread _thread;
    private IntPtr _stopEvent = IntPtr.Zero;
    private volatile bool _disposed;

    // ── Session params (set before thread start, read-only from thread) ─
    private int _captureDeviceIndex;
    private float _targetLatencyMs;

    // Ring buffer for format bridging between capture and render
    private float[] _bridgeBuffer;
    private int _bridgeMask;
    private volatile int _bridgeWritePos;
    private int _bridgeReadPos;

    public bool Start(int captureDeviceIndex, float targetLatencyMs)
    {
        if (IsActive || _disposed) return false;

        _captureDeviceIndex = captureDeviceIndex;
        _targetLatencyMs = Mathf.Clamp(targetLatencyMs, 1f, 100f);
        LastError = null;

        _stopEvent = WasapiInterop.CreateEventW(IntPtr.Zero, true, false, null);
        if (_stopEvent == IntPtr.Zero)
        {
            LastError = "Failed to create stop event";
            return false;
        }

        _thread = new Thread(ThreadProc)
        {
            Name = $"WasapiMic_{captureDeviceIndex}",
            IsBackground = true,
            Priority = System.Threading.ThreadPriority.Highest
        };
        _thread.Start();
        return true;
    }

    public void Stop()
    {
        if (_stopEvent != IntPtr.Zero)
            WasapiInterop.SetEvent(_stopEvent);

        if (_thread != null && _thread.IsAlive)
        {
            if (!_thread.Join(2000))
                Debug.LogWarning("[WasapiMicSession] Thread did not exit within 2s timeout");
        }
        _thread = null;

        if (_stopEvent != IntPtr.Zero)
        {
            WasapiInterop.CloseHandle(_stopEvent);
            _stopEvent = IntPtr.Zero;
        }

        IsActive = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }

    // ── Background thread ──────────────────────────────────────────────

    private void ThreadProc()
    {
        bool comInitialized = false;
        IntPtr mmcssHandle = IntPtr.Zero;
        IntPtr captureEvent = IntPtr.Zero;

        WasapiInterop.IMMDeviceEnumerator enumerator = null;
        WasapiInterop.IMMDevice captureDevice = null;
        WasapiInterop.IMMDevice renderDevice = null;
        WasapiInterop.IAudioClient captureClient = null;
        WasapiInterop.IAudioClient renderClient = null;
        WasapiInterop.IAudioCaptureClient captureService = null;
        WasapiInterop.IAudioRenderClient renderService = null;

        IntPtr captureMixFormatPtr = IntPtr.Zero;
        IntPtr renderMixFormatPtr = IntPtr.Zero;

        try
        {
            // COM init
            int hr = WasapiInterop.CoInitializeEx(IntPtr.Zero, WasapiInterop.COINIT_MULTITHREADED);
            if (hr != 0 && hr != 1) // S_OK or S_FALSE (already initialized)
            {
                LastError = $"CoInitializeEx failed: 0x{hr:X8}";
                return;
            }
            comInitialized = true;

            // MMCSS for pro audio scheduling
            uint taskIndex = 0;
            mmcssHandle = WasapiInterop.AvSetMmThreadCharacteristicsW("Pro Audio", ref taskIndex);

            // Create device enumerator
            var clsid = WasapiInterop.CLSID_MMDeviceEnumerator;
            var iid = WasapiInterop.IID_IMMDeviceEnumerator;
            hr = WasapiInterop.CoCreateInstance(ref clsid, IntPtr.Zero,
                WasapiInterop.CLSCTX_ALL, ref iid, out IntPtr pEnum);
            if (hr != 0) { LastError = $"CoCreateInstance failed: 0x{hr:X8}"; return; }
            enumerator = (WasapiInterop.IMMDeviceEnumerator)Marshal.GetObjectForIUnknown(pEnum);
            Marshal.Release(pEnum);

            // Get capture device by index
            WasapiInterop.IMMDeviceCollection captureCollection = null;
            try
            {
                hr = enumerator.EnumAudioEndpoints(WasapiInterop.eCapture,
                    WasapiInterop.DEVICE_STATE_ACTIVE, out captureCollection);
                if (hr != 0) { LastError = $"EnumAudioEndpoints(capture) failed: 0x{hr:X8}"; return; }
                hr = captureCollection.Item((uint)_captureDeviceIndex, out captureDevice);
                if (hr != 0) { LastError = $"GetCaptureDevice({_captureDeviceIndex}) failed: 0x{hr:X8}"; return; }
            }
            finally
            {
                if (captureCollection != null) Marshal.ReleaseComObject(captureCollection);
            }

            // Get default render device
            hr = enumerator.GetDefaultAudioEndpoint(WasapiInterop.eRender,
                WasapiInterop.eConsole, out renderDevice);
            if (hr != 0) { LastError = $"GetDefaultRenderDevice failed: 0x{hr:X8}"; return; }

            // Activate audio clients
            var audioClientIid = WasapiInterop.IID_IAudioClient;

            hr = captureDevice.Activate(ref audioClientIid, WasapiInterop.CLSCTX_ALL,
                IntPtr.Zero, out object capObj);
            if (hr != 0) { LastError = $"Activate capture client failed: 0x{hr:X8}"; return; }
            captureClient = (WasapiInterop.IAudioClient)capObj;

            hr = renderDevice.Activate(ref audioClientIid, WasapiInterop.CLSCTX_ALL,
                IntPtr.Zero, out object renObj);
            if (hr != 0) { LastError = $"Activate render client failed: 0x{hr:X8}"; return; }
            renderClient = (WasapiInterop.IAudioClient)renObj;

            // Get mix formats
            hr = captureClient.GetMixFormat(out captureMixFormatPtr);
            if (hr != 0) { LastError = $"GetMixFormat(capture) failed: 0x{hr:X8}"; return; }
            var captureFormat = WasapiInterop.WAVEFORMATEX.FromPtr(captureMixFormatPtr);

            hr = renderClient.GetMixFormat(out renderMixFormatPtr);
            if (hr != 0) { LastError = $"GetMixFormat(render) failed: 0x{hr:X8}"; return; }
            var renderFormat = WasapiInterop.WAVEFORMATEX.FromPtr(renderMixFormatPtr);

            // Determine if formats use float samples
            bool captureIsFloat = IsFloatFormat(captureMixFormatPtr, captureFormat);
            bool renderIsFloat = IsFloatFormat(renderMixFormatPtr, renderFormat);

            // Calculate buffer duration (in 100ns units)
            long hnsBufferDuration = (long)(_targetLatencyMs * 10000);
            // Minimum reasonable buffer
            if (hnsBufferDuration < 30000) hnsBufferDuration = 30000; // 3ms minimum

            // Initialize capture client with event callback
            hr = captureClient.Initialize(
                WasapiInterop.AUDCLNT_SHAREMODE_SHARED,
                WasapiInterop.AUDCLNT_STREAMFLAGS_EVENTCALLBACK,
                hnsBufferDuration, 0, captureMixFormatPtr, IntPtr.Zero);
            if (hr != 0) { LastError = $"Initialize capture failed: 0x{hr:X8}"; return; }

            // Initialize render client (shared mode, no special flags)
            hr = renderClient.Initialize(
                WasapiInterop.AUDCLNT_SHAREMODE_SHARED,
                WasapiInterop.AUDCLNT_STREAMFLAGS_NOPERSIST,
                hnsBufferDuration, 0, renderMixFormatPtr, IntPtr.Zero);
            if (hr != 0) { LastError = $"Initialize render failed: 0x{hr:X8}"; return; }

            // Create and set capture event
            captureEvent = WasapiInterop.CreateEventW(IntPtr.Zero, false, false, null);
            if (captureEvent == IntPtr.Zero) { LastError = "Failed to create capture event"; return; }
            hr = captureClient.SetEventHandle(captureEvent);
            if (hr != 0) { LastError = $"SetEventHandle failed: 0x{hr:X8}"; return; }

            // Get buffer sizes
            captureClient.GetBufferSize(out uint captureBufferFrames);
            renderClient.GetBufferSize(out uint renderBufferFrames);

            // Compute actual latency
            captureClient.GetStreamLatency(out long capLatency);
            renderClient.GetStreamLatency(out long renLatency);
            ActualLatencyMs = (capLatency + renLatency) / 10000f;

            // Get services
            var capServiceIid = WasapiInterop.IID_IAudioCaptureClient;
            hr = captureClient.GetService(ref capServiceIid, out object capSvcObj);
            if (hr != 0) { LastError = $"GetService(capture) failed: 0x{hr:X8}"; return; }
            captureService = (WasapiInterop.IAudioCaptureClient)capSvcObj;

            var renServiceIid = WasapiInterop.IID_IAudioRenderClient;
            hr = renderClient.GetService(ref renServiceIid, out object renSvcObj);
            if (hr != 0) { LastError = $"GetService(render) failed: 0x{hr:X8}"; return; }
            renderService = (WasapiInterop.IAudioRenderClient)renSvcObj;

            // Set up bridge buffer (size covers worst case: 200ms at highest rate)
            int bridgeRate = (int)Math.Max(captureFormat.nSamplesPerSec, renderFormat.nSamplesPerSec);
            int bridgeSize = NextPowerOfTwo(bridgeRate / 5); // ~200ms
            _bridgeBuffer = new float[bridgeSize];
            _bridgeMask = bridgeSize - 1;
            _bridgeWritePos = 0;
            _bridgeReadPos = 0;

            // Start both clients
            hr = captureClient.Start();
            if (hr != 0) { LastError = $"Capture Start failed: 0x{hr:X8}"; return; }
            hr = renderClient.Start();
            if (hr != 0)
            {
                captureClient.Stop();
                LastError = $"Render Start failed: 0x{hr:X8}";
                return;
            }

            IsActive = true;

            // ── Main loop ──────────────────────────────────────────────
            IntPtr[] waitHandles = new IntPtr[] { captureEvent, _stopEvent };

            while (true)
            {
                uint waitResult = WasapiInterop.WaitForMultipleObjects(
                    2, waitHandles, false, 100);

                // Stop event signaled (index 1)
                if (waitResult == WasapiInterop.WAIT_OBJECT_0 + 1)
                    break;

                // Drain all available capture packets
                hr = DrainCapturePackets(captureService, captureFormat, captureIsFloat);
                if (hr == WasapiInterop.AUDCLNT_E_DEVICE_INVALIDATED)
                {
                    LastError = "Capture device disconnected";
                    break;
                }

                // Write to render
                hr = WriteToRender(renderClient, renderService, renderFormat,
                    captureFormat, renderIsFloat, renderBufferFrames);
                if (hr == WasapiInterop.AUDCLNT_E_DEVICE_INVALIDATED)
                {
                    LastError = "Render device disconnected";
                    break;
                }
            }
        }
        catch (Exception e)
        {
            LastError = $"Thread exception: {e.Message}";
            Debug.LogError($"[WasapiMicSession] {LastError}\n{e.StackTrace}");
        }
        finally
        {
            IsActive = false;

            // Stop and release
            try { captureClient?.Stop(); } catch { }
            try { renderClient?.Stop(); } catch { }
            try { captureClient?.Reset(); } catch { }
            try { renderClient?.Reset(); } catch { }

            if (captureMixFormatPtr != IntPtr.Zero) WasapiInterop.CoTaskMemFree(captureMixFormatPtr);
            if (renderMixFormatPtr != IntPtr.Zero) WasapiInterop.CoTaskMemFree(renderMixFormatPtr);

            SafeRelease(captureService);
            SafeRelease(renderService);
            SafeRelease(captureClient);
            SafeRelease(renderClient);
            SafeRelease(captureDevice);
            SafeRelease(renderDevice);
            SafeRelease(enumerator);

            if (captureEvent != IntPtr.Zero) WasapiInterop.CloseHandle(captureEvent);

            if (mmcssHandle != IntPtr.Zero)
                WasapiInterop.AvRevertMmThreadCharacteristics(mmcssHandle);

            if (comInitialized)
                WasapiInterop.CoUninitialize();
        }
    }

    // ── Capture drain ──────────────────────────────────────────────────

    private int DrainCapturePackets(WasapiInterop.IAudioCaptureClient captureService,
        WasapiInterop.WAVEFORMATEX captureFormat, bool isFloat)
    {
        int channels = captureFormat.nChannels;
        int bytesPerSample = captureFormat.wBitsPerSample / 8;
        float vol = _volume;

        while (true)
        {
            int hr = captureService.GetNextPacketSize(out uint packetSize);
            if (hr != 0) return hr;
            if (packetSize == 0) break;

            hr = captureService.GetBuffer(out IntPtr dataPtr, out uint numFrames,
                out uint flags, out _, out _);
            if (hr != 0) return hr;

            bool isSilent = (flags & 0x2) != 0; // AUDCLNT_BUFFERFLAGS_SILENT

            int wp = _bridgeWritePos;

            if (isSilent)
            {
                for (uint f = 0; f < numFrames; f++)
                    _bridgeBuffer[(wp + (int)f) & _bridgeMask] = 0f;
            }
            else
            {
                for (uint f = 0; f < numFrames; f++)
                {
                    float sample = ReadSampleFromBuffer(dataPtr, f, channels, bytesPerSample, isFloat);
                    _bridgeBuffer[(wp + (int)f) & _bridgeMask] = sample * vol;
                }
            }

            _bridgeWritePos = (wp + (int)numFrames) & _bridgeMask;

            hr = captureService.ReleaseBuffer(numFrames);
            if (hr != 0) return hr;
        }

        return 0;
    }

    // ── Render write ───────────────────────────────────────────────────

    private int WriteToRender(WasapiInterop.IAudioClient renderClient,
        WasapiInterop.IAudioRenderClient renderService,
        WasapiInterop.WAVEFORMATEX renderFormat,
        WasapiInterop.WAVEFORMATEX captureFormat,
        bool renderIsFloat, uint renderBufferFrames)
    {
        int hr = renderClient.GetCurrentPadding(out uint padding);
        if (hr != 0) return hr;

        uint availableFrames = renderBufferFrames - padding;
        if (availableFrames == 0) return 0;

        // How many bridge samples are available
        int wp = _bridgeWritePos;
        int rp = _bridgeReadPos;
        int bridgeAvailable = (wp - rp + _bridgeBuffer.Length) & _bridgeMask;
        if (bridgeAvailable == 0) return 0;

        float resampleRatio = (float)captureFormat.nSamplesPerSec / renderFormat.nSamplesPerSec;
        int renderChannels = renderFormat.nChannels;
        int renderBytesPerSample = renderFormat.wBitsPerSample / 8;

        // Calculate how many render frames we can produce from available bridge samples
        uint framesToWrite = (uint)Math.Min(availableFrames,
            (uint)(bridgeAvailable / resampleRatio));
        if (framesToWrite == 0) return 0;

        hr = renderService.GetBuffer(framesToWrite, out IntPtr renderPtr);
        if (hr != 0) return hr;

        // Resample from bridge → render buffer
        float srcPos = 0f;
        for (uint f = 0; f < framesToWrite; f++)
        {
            int idx0 = (int)srcPos;
            float frac = srcPos - idx0;

            float s0 = _bridgeBuffer[(rp + idx0) & _bridgeMask];
            float s1 = _bridgeBuffer[(rp + idx0 + 1) & _bridgeMask];
            float sample = s0 + (s1 - s0) * frac;

            WriteSampleToBuffer(renderPtr, f, renderChannels, renderBytesPerSample, renderIsFloat, sample);
            srcPos += resampleRatio;
        }

        int consumed = (int)srcPos;
        _bridgeReadPos = (rp + consumed) & _bridgeMask;

        hr = renderService.ReleaseBuffer(framesToWrite, 0);
        return hr;
    }

    // ── Sample conversion helpers ──────────────────────────────────────

    private static bool IsFloatFormat(IntPtr formatPtr, WasapiInterop.WAVEFORMATEX format)
    {
        if (format.wFormatTag == WasapiInterop.WAVE_FORMAT_IEEE_FLOAT)
            return true;
        if (format.wFormatTag == WasapiInterop.WAVE_FORMAT_EXTENSIBLE)
        {
            var ext = WasapiInterop.WAVEFORMATEXTENSIBLE.FromPtr(formatPtr);
            return ext.SubFormat == WasapiInterop.KSDATAFORMAT_SUBTYPE_IEEE_FLOAT;
        }
        return false;
    }

    private static unsafe float ReadSampleFromBuffer(IntPtr dataPtr, uint frame,
        int channels, int bytesPerSample, bool isFloat)
    {
        // Read first channel only (mono mix-down)
        int offset = (int)(frame * channels * bytesPerSample);
        IntPtr samplePtr = dataPtr + offset;

        if (isFloat && bytesPerSample == 4)
            return *(float*)samplePtr;

        if (!isFloat && bytesPerSample == 2)
            return *(short*)samplePtr / 32768f;

        if (!isFloat && bytesPerSample == 3)
        {
            byte* p = (byte*)samplePtr;
            int val = p[0] | (p[1] << 8) | (p[2] << 16);
            if ((val & 0x800000) != 0) val |= unchecked((int)0xFF000000);
            return val / 8388608f;
        }

        return 0f;
    }

    private static unsafe void WriteSampleToBuffer(IntPtr dataPtr, uint frame,
        int channels, int bytesPerSample, bool isFloat, float sample)
    {
        int offset = (int)(frame * channels * bytesPerSample);

        if (isFloat && bytesPerSample == 4)
        {
            // Write same sample to all channels
            for (int ch = 0; ch < channels; ch++)
            {
                float* dest = (float*)(dataPtr + offset + ch * 4);
                *dest = sample;
            }
        }
        else if (!isFloat && bytesPerSample == 2)
        {
            short s = (short)Math.Max(-32768, Math.Min(32767, (int)(sample * 32768f)));
            for (int ch = 0; ch < channels; ch++)
            {
                short* dest = (short*)(dataPtr + offset + ch * 2);
                *dest = s;
            }
        }
    }

    // ── Utilities ──────────────────────────────────────────────────────

    private static void SafeRelease(object comObj)
    {
        if (comObj != null)
        {
            try { Marshal.ReleaseComObject(comObj); } catch { }
        }
    }

    private static int NextPowerOfTwo(int v)
    {
        v--;
        v |= v >> 1;
        v |= v >> 2;
        v |= v >> 4;
        v |= v >> 8;
        v |= v >> 16;
        return v + 1;
    }
}
#endif
