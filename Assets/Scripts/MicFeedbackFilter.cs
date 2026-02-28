using UnityEngine;
using UnityEngine.Audio;

[RequireComponent(typeof(AudioSource))]
public class MicFeedbackFilter : MonoBehaviour
{
    private AudioClip _micClip;
    private string _micDeviceName;
    private volatile bool _isActive;

    // Native WASAPI monitoring — per-instance session (supports multiple mics)
    private bool _usingNativePath;
    private AudioMixerGroup _mixerGroup;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    private WasapiMicSession _session;
#endif

    private static bool _nativeInitialized;

    // Lock-free SPSC ring buffer (main thread writes, audio thread reads) — fallback path
    private float[] _ringBuffer;
    private int _ringMask;
    private volatile int _writePos;
    private int _readPos;

    // Main thread state — fallback path
    private int _lastMicPos;
    private float[] _tempReadBuffer;
    private int _micSamples;

    // Resampling — fallback path
    private int _micSampleRate;
    private int _outputSampleRate;

    // Latency and drift correction — fallback path
    private int _targetLatencySamples;
    private float _resampleRatioAdjustment;
    private float _lastSample;
    private bool _bufferPrimed;

    // Constants for fallback path tuning
    private const float TARGET_LATENCY_MS = 35f;
    private const float MAX_DRIFT_CORRECTION = 0.03f;
    private const float DRIFT_SMOOTHING = 0.003f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void OnDomainReload()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        // Clear device cache on domain reload
        if (_nativeInitialized)
        {
            WasapiMicMonitorNative.Shutdown();
            _nativeInitialized = false;
        }
#endif
    }

    public void Activate(AudioClip micClip, string micDeviceName, AudioMixerGroup mixerGroup)
    {
        _micClip = micClip;
        _micDeviceName = micDeviceName;
        _mixerGroup = mixerGroup;
        _usingNativePath = false;

        // Read WASAPI latency from settings
        float wasapiLatencyMs = 10f;
        if (SettingsManager.Instance != null)
        {
            string latencyStr = SettingsManager.Instance.GetSetting<string>("WASAPILatency", "10");
            if (!float.TryParse(latencyStr, out wasapiLatencyMs))
                wasapiLatencyMs = 10f;
            wasapiLatencyMs = Mathf.Clamp(wasapiLatencyMs, 1f, 100f);
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        // Attempt native WASAPI path — each instance gets its own session
        if (WasapiMicMonitorNative.IsSupported)
        {
            if (!_nativeInitialized)
                _nativeInitialized = WasapiMicMonitorNative.Initialize();

            if (_nativeInitialized)
            {
                int deviceIndex = WasapiMicMonitorNative.FindDeviceIndexByUnityName(micDeviceName);
                if (deviceIndex >= 0)
                {
                    _session = new WasapiMicSession();
                    if (_session.Start(deviceIndex, wasapiLatencyMs))
                    {
                        _usingNativePath = true;
                        _isActive = true;
                        Debug.Log($"[MicFeedbackFilter] Native WASAPI monitoring active for '{micDeviceName}'. " +
                                  $"Latency: {_session.ActualLatencyMs:F1}ms");
                        return;
                    }
                    else
                    {
                        Debug.LogWarning($"[MicFeedbackFilter] Native start failed, falling back. " +
                                         $"Error: {_session.LastError}");
                        _session.Dispose();
                        _session = null;
                    }
                }
                else
                {
                    Debug.LogWarning($"[MicFeedbackFilter] Could not map mic '{micDeviceName}' " +
                                     "to WASAPI device. Falling back.");
                }
            }
        }
#endif

        // Fallback: ring buffer path
        SetupFallbackPath(micClip, micDeviceName, mixerGroup);
    }

    private void SetupFallbackPath(AudioClip micClip, string micDeviceName, AudioMixerGroup mixerGroup)
    {
        _micSamples = micClip.samples;
        _micSampleRate = micClip.frequency;
        _outputSampleRate = AudioSettings.outputSampleRate;

        int ringSize = Mathf.NextPowerOfTwo(_micSampleRate / 2);
        _ringBuffer = new float[ringSize];
        _ringMask = ringSize - 1;
        _writePos = 0;
        _readPos = 0;

        _targetLatencySamples = Mathf.RoundToInt((_micSampleRate * TARGET_LATENCY_MS) / 1000f);
        _resampleRatioAdjustment = 0f;
        _lastSample = 0f;
        _bufferPrimed = false;

        _tempReadBuffer = new float[Mathf.Max(4096, _micSampleRate / 10)];
        _lastMicPos = Microphone.GetPosition(micDeviceName);

        AudioSource src = GetComponent<AudioSource>();
        src.outputAudioMixerGroup = mixerGroup;
        src.clip = AudioClip.Create("_mfSilence", _outputSampleRate, 1, _outputSampleRate, false);
        src.loop = true;
        src.volume = 1f;
        src.Play();

        _isActive = true;
    }

    public void Deactivate()
    {
        _isActive = false;

        if (_usingNativePath)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            _session?.Dispose();
            _session = null;
#endif
            _usingNativePath = false;
        }
        else
        {
            AudioSource src = GetComponent<AudioSource>();
            if (src != null)
            {
                src.Stop();
                src.clip = null;
            }
        }

        _micClip = null;
    }

    /// <summary>
    /// Sets the mic feedback volume (0.0 = silent, 1.0 = full).
    /// </summary>
    public void SetVolume(float linearGain)
    {
        if (_usingNativePath)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (_session != null)
                _session.Volume = linearGain;
#endif
        }
        else
        {
            AudioSource src = GetComponent<AudioSource>();
            if (src != null)
                src.volume = linearGain;
        }
    }

    void Update()
    {
        if (!_isActive) return;

        if (_usingNativePath)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            // Check if native monitoring stopped unexpectedly (e.g. device disconnected)
            if (_session == null || !_session.IsActive)
            {
                Debug.LogWarning("[MicFeedbackFilter] Native monitoring stopped unexpectedly. Falling back.");
                _session?.Dispose();
                _session = null;
                _usingNativePath = false;

                if (_micClip != null && Microphone.IsRecording(_micDeviceName))
                    SetupFallbackPath(_micClip, _micDeviceName, _mixerGroup);
            }
#endif
            return;
        }

        // Fallback ring buffer path
        if (_micClip == null) return;

        int micPos = Microphone.GetPosition(_micDeviceName);

        int newSamples;
        if (micPos >= _lastMicPos)
            newSamples = micPos - _lastMicPos;
        else
            newSamples = (_micSamples - _lastMicPos) + micPos;

        _lastMicPos = micPos;

        if (newSamples <= 0 || newSamples > _micSamples) return;

        if (newSamples > _tempReadBuffer.Length)
            newSamples = _tempReadBuffer.Length;

        int readStart = micPos - newSamples;
        if (readStart < 0) readStart += _micSamples;
        _micClip.GetData(_tempReadBuffer, readStart);

        int wp = _writePos;
        for (int i = 0; i < newSamples; i++)
        {
            _ringBuffer[(wp + i) & _ringMask] = _tempReadBuffer[i];
        }
        _writePos = (wp + newSamples) & _ringMask;
    }

    void OnApplicationQuit()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (_usingNativePath)
        {
            _session?.Dispose();
            _session = null;
            _usingNativePath = false;
        }
#endif
    }

    void OnAudioFilterRead(float[] data, int channels)
    {
        if (!_isActive || _usingNativePath || _ringBuffer == null) return;

        int outputSamples = data.Length / channels;
        float baseRatio = (float)_micSampleRate / _outputSampleRate;

        int wp = _writePos;
        int available = (wp - _readPos + _ringBuffer.Length) & _ringMask;

        if (!_bufferPrimed)
        {
            if (available >= (_targetLatencySamples * 3) / 2)
            {
                _readPos = (wp - _targetLatencySamples) & _ringMask;
                _bufferPrimed = true;
            }
            else
            {
                return;
            }
        }

        int latencyError = available - _targetLatencySamples;

        float targetAdjustment = Mathf.Clamp(
            (float)latencyError / _targetLatencySamples * MAX_DRIFT_CORRECTION,
            -MAX_DRIFT_CORRECTION,
            MAX_DRIFT_CORRECTION
        );
        _resampleRatioAdjustment = Mathf.Lerp(_resampleRatioAdjustment, targetAdjustment, DRIFT_SMOOTHING);

        float adjustedRatio = baseRatio * (1f + _resampleRatioAdjustment);
        int micSamplesNeeded = Mathf.CeilToInt(outputSamples * adjustedRatio) + 2;

        if (available < micSamplesNeeded / 2)
        {
            for (int i = 0; i < outputSamples; i++)
            {
                float fadeOut = 1f - (float)i / outputSamples;
                float sample = _lastSample * fadeOut;
                for (int ch = 0; ch < channels; ch++)
                {
                    data[i * channels + ch] = sample;
                }
            }
            _lastSample = 0f;
            _bufferPrimed = false;
            return;
        }

        float srcPos = 0f;
        for (int i = 0; i < outputSamples; i++)
        {
            int idx0 = (int)srcPos;
            float frac = srcPos - idx0;

            float s0 = _ringBuffer[(_readPos + idx0) & _ringMask];
            float s1 = _ringBuffer[(_readPos + idx0 + 1) & _ringMask];
            float sample = s0 + (s1 - s0) * frac;

            sample = _lastSample * 0.05f + sample * 0.95f;
            _lastSample = sample;

            for (int ch = 0; ch < channels; ch++)
            {
                data[i * channels + ch] = sample;
            }

            srcPos += adjustedRatio;
        }

        int samplesConsumed = (int)srcPos;
        _readPos = (_readPos + samplesConsumed) & _ringMask;
    }
}
