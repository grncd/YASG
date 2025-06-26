using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AudioPitchEstimator : MonoBehaviour
{
    [Tooltip("AudioSource containing the microphone input")]
    public AudioSource audioSource;

    [Tooltip("Lowest frequency that can estimate [Hz]")]
    [Range(40, 150)]
    public int frequencyMin = 40;

    [Tooltip("Highest frequency that can estimate [Hz]")]
    [Range(300, 1200)]
    public int frequencyMax = 600;

    [Tooltip("Number of overtones to use for estimation")]
    [Range(1, 8)]
    public int harmonicsToUse = 5;

    [Tooltip("Frequency bandwidth of spectral smoothing filter [Hz]")]
    public float smoothingWidth = 500;

    [Tooltip("Threshold to judge silence or not")]
    public float thresholdSRH = 7;

    const int spectrumSize = 1024;
    const int outputResolution = 200;
    float[] spectrum = new float[spectrumSize];
    float[] specRaw = new float[spectrumSize];
    float[] specCum = new float[spectrumSize];
    float[] specRes = new float[spectrumSize];
    public float[] srh = new float[outputResolution];

    void Update()
    {
        if (audioSource != null && audioSource.clip != null)
        {
            float estimatedPitch = Estimate(audioSource);
            Debug.Log("Estimated Pitch: " + estimatedPitch + " Hz");
        }
    }

    public float Estimate(AudioSource audioSource)
    {
        var nyquistFreq = AudioSettings.outputSampleRate / 2.0f;

        if (!audioSource.isPlaying) return float.NaN;
        audioSource.GetSpectrumData(spectrum, 0, FFTWindow.Hanning);

        for (int i = 0; i < spectrumSize; i++)
        {
            specRaw[i] = Mathf.Log(spectrum[i] + 1e-9f);
        }

        specCum[0] = 0;
        for (int i = 1; i < spectrumSize; i++)
        {
            specCum[i] = specCum[i - 1] + specRaw[i];
        }

        var halfRange = Mathf.RoundToInt((smoothingWidth / 2) / nyquistFreq * spectrumSize);
        for (int i = 0; i < spectrumSize; i++)
        {
            var indexUpper = Mathf.Min(i + halfRange, spectrumSize - 1);
            var indexLower = Mathf.Max(i - halfRange + 1, 0);
            var upper = specCum[indexUpper];
            var lower = specCum[indexLower];
            var smoothed = (upper - lower) / (indexUpper - indexLower);
            specRes[i] = specRaw[i] - smoothed;
        }

        float bestFreq = 0, bestSRH = 0;
        for (int i = 0; i < outputResolution; i++)
        {
            var currentFreq = (float)i / (outputResolution - 1) * (frequencyMax - frequencyMin) + frequencyMin;
            var currentSRH = GetSpectrumAmplitude(specRes, currentFreq, nyquistFreq);
            for (int h = 2; h <= harmonicsToUse; h++)
            {
                currentSRH += GetSpectrumAmplitude(specRes, currentFreq * h, nyquistFreq);
                currentSRH -= GetSpectrumAmplitude(specRes, currentFreq * (h - 0.5f), nyquistFreq);
            }
            srh[i] = currentSRH;

            if (currentSRH > bestSRH)
            {
                bestFreq = currentFreq;
                bestSRH = currentSRH;
            }
        }

        if (bestSRH < thresholdSRH) return float.NaN;
        return bestFreq;
    }

    float GetSpectrumAmplitude(float[] spec, float frequency, float nyquistFreq)
    {
        var position = frequency / nyquistFreq * spec.Length;
        var index0 = (int)position;
        var index1 = index0 + 1;
        var delta = position - index0;
        return (1 - delta) * spec[index0] + delta * spec[index1];
    }
}