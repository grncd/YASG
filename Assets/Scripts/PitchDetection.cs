using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PitchDetection : MonoBehaviour
{
    private AudioSource audioSource;
    private string microphoneDevice;
    private float[] spectrumData;
    private int sampleSize = 8192/2; // Must be a power of 2
    public Slider vocalArrow;

    void Start()
    {
        // Add the AudioSource component if it doesn't exist
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            Debug.Log("AudioSource component not found. Adding it now.");
            audioSource = gameObject.AddComponent<AudioSource>();
        }

        // Check if a microphone is available
        if (Microphone.devices.Length == 0)
        {
            Debug.LogError("No microphone found!");
            return;
        }

        // Get the default microphone
        microphoneDevice = Microphone.devices[0];

        // Initialize the spectrum data array
        spectrumData = new float[sampleSize];

        // Start recording from the microphone
        audioSource.clip = Microphone.Start(microphoneDevice, true, 1, 44100); // 1 second buffer, 44100 Hz sample rate
        if (audioSource.clip == null)
        {
            Debug.LogError("Failed to start microphone!");
            return;
        }

        // Wait for the microphone to initialize
        while (!(Microphone.GetPosition(microphoneDevice) > 0)) { }

        // Start playing the audio source
        audioSource.loop = true;
        audioSource.Play();
    }

    void Update()
    {
        // Check if the AudioSource and spectrum data are properly initialized
        if (audioSource == null || spectrumData == null)
        {
            Debug.LogError("AudioSource or spectrum data is not initialized!");
            return;
        }

        // Get the spectrum data
        audioSource.GetSpectrumData(spectrumData, 0, FFTWindow.BlackmanHarris);

        // Find the dominant frequency
        float maxFrequency = GetDominantFrequency();
        maxFrequency = NormalizeFrequency(maxFrequency); // 130.8 - 261.5

        //Debug.Log("Dominant Frequency: " + maxFrequency + " Hz");
        //debugPitch.text = "Dominant Frequency: " + maxFrequency + " Hz";

        vocalArrow.value = maxFrequency;
    }

    float NormalizeFrequency(float frequency)
    {
        if (frequency < 50)
        {
            return 0;
        }
        // Define the target frequency range
        float minFrequency = 284f; // C3
        float maxFrequency = 568f; // C4

        // If the frequency is below the minimum, shift it up by octaves
        while (frequency < minFrequency)
        {
            frequency *= 2f;
        }

        // If the frequency is above the maximum, shift it down by octaves
        while (frequency > maxFrequency)
        {
            frequency /= 2f;
        }

        return frequency;
    }

    float GetDominantFrequency()
    {
        float maxValue = 0;
        int maxIndex = 0;

        // Find the index of the highest value in the spectrum
        for (int i = 0; i < spectrumData.Length; i++)
        {
            if (spectrumData[i] > maxValue)
            {
                maxValue = spectrumData[i];
                maxIndex = i;
            }
        }

        // Convert the index to a frequency
        float frequency = maxIndex * AudioSettings.outputSampleRate / sampleSize;
        return frequency;
    }
}