using UnityEngine;
using System.Collections.Generic;
using System.Numerics; // For complex numbers

public class OfflineAudioFileFFTAnalysis : MonoBehaviour
{
    public AudioClip audioClip; // Assign your audio file in the Inspector
    private float[] audioData; // Stores the entire audio data
    private int sampleRate;
    private int sampleSize = 8192; // Must be a power of 2
    private List<float> dominantFrequencies = new List<float>(); // Stores dominant frequencies for each frame
    private const float analysisInterval = 0.1f; // Analyze every 100ms

    void Start()
    {
        // Check if the audio clip is assigned
        if (audioClip == null)
        {
            Debug.LogError("No audio clip assigned!");
            return;
        }

        // Get the sample rate and length of the audio clip
        sampleRate = audioClip.frequency;
        int audioLengthSamples = audioClip.samples;

        // Load the entire audio data into memory
        audioData = new float[audioLengthSamples];
        if (!audioClip.GetData(audioData, 0))
        {
            Debug.LogError("Failed to load audio data!");
            return;
        }

        // Debug: Print the first few samples of the audio data
        Debug.Log("First 10 audio samples:");
        for (int i = 0; i < 10; i++)
        {
            Debug.Log("Sample " + i + ": " + audioData[i]);
        }

        // Perform FFT analysis on the audio data
        AnalyzeAudioData();

        // Output the results
        Debug.Log("Frequency analysis complete. Total frames: " + dominantFrequencies.Count);

        // Example: Print the dominant frequencies for all frames
        for (int i = 0; i < dominantFrequencies.Count; i++)
        {
            Debug.Log("Frame " + i + " Dominant Frequency: " + dominantFrequencies[i] + " Hz");
        }
    }

    void AnalyzeAudioData()
    {
        // Calculate the number of samples per analysis interval
        int samplesPerInterval = (int)(analysisInterval * sampleRate);

        // Iterate through the audio data in chunks
        for (int offset = 0; offset < audioData.Length; offset += samplesPerInterval)
        {
            // Determine the size of the current chunk
            int chunkSize = Mathf.Min(sampleSize, audioData.Length - offset);

            // Extract a chunk of audio data for analysis
            float[] chunk = new float[chunkSize];
            System.Array.Copy(audioData, offset, chunk, 0, chunkSize);

            // If the chunk is smaller than sampleSize, pad it with zeros
            if (chunkSize < sampleSize)
            {
                float[] paddedChunk = new float[sampleSize];
                System.Array.Copy(chunk, paddedChunk, chunkSize);
                chunk = paddedChunk;
            }

            // Apply a windowing function to the chunk
            ApplyWindowFunction(chunk);

            // Perform FFT on the chunk
            Complex[] spectrum = FFT(chunk);

            // Find the dominant frequency in the spectrum
            float dominantFrequency = GetDominantFrequency(spectrum);

            // Save the dominant frequency
            dominantFrequencies.Add(dominantFrequency);

            // Debug: Print the current frame's dominant frequency
            Debug.Log("Frame " + dominantFrequencies.Count + " Dominant Frequency: " + dominantFrequency + " Hz");
        }
    }

    Complex[] FFT(float[] samples)
    {
        // Convert the samples to complex numbers
        Complex[] complexSamples = new Complex[samples.Length];
        for (int i = 0; i < samples.Length; i++)
        {
            complexSamples[i] = new Complex(samples[i], 0);
        }

        // Perform the FFT
        return FFT(complexSamples);
    }

    Complex[] FFT(Complex[] buffer)
    {
        int n = buffer.Length;
        if (n <= 1)
        {
            return buffer;
        }

        // Divide
        Complex[] even = new Complex[n / 2];
        Complex[] odd = new Complex[n / 2];
        for (int i = 0; i < n / 2; i++)
        {
            even[i] = buffer[2 * i];
            odd[i] = buffer[2 * i + 1];
        }

        // Conquer
        even = FFT(even);
        odd = FFT(odd);

        // Combine
        for (int k = 0; k < n / 2; k++)
        {
            Complex t = Complex.FromPolarCoordinates(1, -2 * Mathf.PI * k / n) * odd[k];
            buffer[k] = even[k] + t;
            buffer[k + n / 2] = even[k] - t;
        }

        return buffer;
    }

    float GetDominantFrequency(Complex[] spectrum)
    {
        float maxAmplitude = 0;
        int maxIndex = 0;

        // Find the index of the highest amplitude in the spectrum
        for (int i = 1; i < spectrum.Length / 2; i++) // Start from 1 to ignore DC offset
        {
            float amplitude = (float)spectrum[i].Magnitude;
            if (amplitude > maxAmplitude)
            {
                maxAmplitude = amplitude;
                maxIndex = i;
            }
        }

        // Convert the index to a frequency
        float dominantFrequency = maxIndex * sampleRate / sampleSize;
        return dominantFrequency;
    }

    void ApplyWindowFunction(float[] data)
    {
        // Apply a Blackman-Harris window to reduce spectral leakage
        for (int i = 0; i < data.Length; i++)
        {
            float window = 0.35875f -
                            0.48829f * Mathf.Cos((2 * Mathf.PI * i) / (data.Length - 1)) +
                            0.14128f * Mathf.Cos((4 * Mathf.PI * i) / (data.Length - 1)) -
                            0.01168f * Mathf.Cos((6 * Mathf.PI * i) / (data.Length - 1));
            data[i] *= window;
        }
    }
}