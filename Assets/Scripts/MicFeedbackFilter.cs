using UnityEngine;
using UnityEngine.Audio;

[RequireComponent(typeof(AudioSource))]
public class MicFeedbackFilter : MonoBehaviour
{
    private AudioClip _micClip;
    private string _micDeviceName;
    private volatile bool _isActive;

    // Lock-free SPSC ring buffer (main thread writes, audio thread reads)
    private float[] _ringBuffer;
    private int _ringMask;
    private volatile int _writePos;
    private int _readPos;

    // Main thread state
    private int _lastMicPos;
    private float[] _tempReadBuffer;
    private int _micSamples;

    // Resampling
    private int _micSampleRate;
    private int _outputSampleRate;

    public void Activate(AudioClip micClip, string micDeviceName, AudioMixerGroup mixerGroup)
    {
        _micClip = micClip;
        _micDeviceName = micDeviceName;
        _micSamples = micClip.samples;
        _micSampleRate = micClip.frequency;
        _outputSampleRate = AudioSettings.outputSampleRate;

        // Ring buffer: ~200ms at mic sample rate (power of 2 for fast masking)
        int ringSize = Mathf.NextPowerOfTwo(_micSampleRate / 5);
        _ringBuffer = new float[ringSize];
        _ringMask = ringSize - 1;
        _writePos = 0;
        _readPos = 0;

        // Pre-allocate temp buffer large enough for worst-case frame intervals
        _tempReadBuffer = new float[Mathf.Max(4096, _micSampleRate / 10)];
        _lastMicPos = Microphone.GetPosition(micDeviceName);

        AudioSource src = GetComponent<AudioSource>();
        src.outputAudioMixerGroup = mixerGroup;
        // Silent clip at output sample rate — its only purpose is to drive OnAudioFilterRead
        src.clip = AudioClip.Create("_mfSilence", _outputSampleRate, 1, _outputSampleRate, false);
        src.loop = true;
        src.volume = 1f;
        src.Play();

        _isActive = true;
    }

    public void Deactivate()
    {
        _isActive = false;
        AudioSource src = GetComponent<AudioSource>();
        if (src != null)
        {
            src.Stop();
            src.clip = null;
        }
        _micClip = null;
    }

    void Update()
    {
        if (!_isActive || _micClip == null) return;

        int micPos = Microphone.GetPosition(_micDeviceName);

        // Calculate how many new samples the mic has written since last frame
        int newSamples;
        if (micPos >= _lastMicPos)
            newSamples = micPos - _lastMicPos;
        else
            newSamples = (_micSamples - _lastMicPos) + micPos;

        _lastMicPos = micPos;

        if (newSamples <= 0 || newSamples > _micSamples) return;

        // Clamp to temp buffer capacity (handles extreme frame-rate drops)
        if (newSamples > _tempReadBuffer.Length)
            newSamples = _tempReadBuffer.Length;

        // Read the latest newSamples from the mic clip (main thread only)
        int readStart = micPos - newSamples;
        if (readStart < 0) readStart += _micSamples;
        _micClip.GetData(_tempReadBuffer, readStart);

        // Copy into the ring buffer
        int wp = _writePos;
        for (int i = 0; i < newSamples; i++)
        {
            _ringBuffer[(wp + i) & _ringMask] = _tempReadBuffer[i];
        }
        _writePos = (wp + newSamples) & _ringMask;
    }

    void OnAudioFilterRead(float[] data, int channels)
    {
        if (!_isActive || _ringBuffer == null) return;

        int outputSamples = data.Length / channels;
        float sampleRatio = (float)_micSampleRate / _outputSampleRate;
        int micSamplesNeeded = Mathf.CeilToInt(outputSamples * sampleRatio) + 1;

        int wp = _writePos;
        int available = (wp - _readPos + _ringBuffer.Length) & _ringMask;

        if (available < micSamplesNeeded)
            return; // Not enough data yet — output silence

        // Stay close to the write head to minimize latency
        if (available > micSamplesNeeded * 2)
            _readPos = (wp - micSamplesNeeded) & _ringMask;

        // Read from ring buffer with linear-interpolation resampling
        for (int i = 0; i < outputSamples; i++)
        {
            float srcIndex = i * sampleRatio;
            int idx0 = (int)srcIndex;
            float frac = srcIndex - idx0;

            float s0 = _ringBuffer[(_readPos + idx0) & _ringMask];
            float s1 = _ringBuffer[(_readPos + idx0 + 1) & _ringMask];
            float sample = s0 + (s1 - s0) * frac;

            for (int ch = 0; ch < channels; ch++)
            {
                data[i * channels + ch] = sample;
            }
        }

        _readPos = (_readPos + micSamplesNeeded) & _ringMask;
    }
}
