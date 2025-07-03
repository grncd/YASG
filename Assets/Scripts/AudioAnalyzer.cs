using UnityEngine;

public class AudioAnalyzer : MonoBehaviour
{
    [Tooltip("The Material using the audio-reactive shader.")]
    public Material audioMaterial;

    [Header("Audio Analysis")]
    // The number of samples to take from the audio. Must be a power of 2.
    [SerializeField, Tooltip("Number of samples, must be a power of 2")]
    private int numSamples = 512;

    [Header("Frequency Band Ranges")]
    [SerializeField] private int lowFrequencyThreshold = 200;  // e.g., up to 250 Hz
    [SerializeField] private int midFrequencyThreshold = 4000; // e.g., 250 Hz to 4000 Hz
    // High frequency is anything above mid

    [Header("Intensity Multipliers")]
    [SerializeField] private float lowMultiplier = 0.9f;
    [SerializeField] private float midMultiplier = 0.25f;
    [SerializeField] private float highMultiplier = 0.1f;

    [Header("Smoothing")]
    [SerializeField, Range(0f, 1f)]
    private float smoothing = 0.1f;

    private float[] spectrumData;
    private float currentLow, currentMid, currentHigh;

    // Shader property IDs for efficiency
    private int lowIntensityID;
    private int midIntensityID;
    private int highIntensityID;

    void Start()
    {
        if (audioMaterial == null)
        {
            Debug.LogError("Audio Material is not assigned!");
            this.enabled = false;
            return;
        }

        spectrumData = new float[numSamples];

        // Cache the shader property IDs
        lowIntensityID = Shader.PropertyToID("_LowIntensity");
        midIntensityID = Shader.PropertyToID("_MidIntensity");
        highIntensityID = Shader.PropertyToID("_HighIntensity");
    }

    void Update()
    {
        // Get the audio spectrum data
        AudioListener.GetSpectrumData(spectrumData, 0, FFTWindow.BlackmanHarris);

        // Calculate average intensity for each frequency band
        float lowSum = 0;
        float midSum = 0;
        float highSum = 0;

        float sampleRate = AudioSettings.outputSampleRate;
        int lowIndexCap = (int)(lowFrequencyThreshold * numSamples / sampleRate);
        int midIndexCap = (int)(midFrequencyThreshold * numSamples / sampleRate);

        for (int i = 0; i < numSamples; i++)
        {
            float freq = i * sampleRate / numSamples;
            if (i <= lowIndexCap)
            {
                lowSum += spectrumData[i];
            }
            else if (i <= midIndexCap)
            {
                midSum += spectrumData[i];
            }
            else
            {
                highSum += spectrumData[i];
            }
        }

        // Apply multipliers
        float targetLow = lowSum * lowMultiplier;
        float targetMid = midSum * midMultiplier;
        float targetHigh = highSum * highMultiplier;

        // Smooth the transitions to prevent overly jittery visuals
        currentLow = Mathf.Lerp(currentLow, targetLow, smoothing);
        currentMid = Mathf.Lerp(currentMid, targetMid, smoothing);
        currentHigh = Mathf.Lerp(currentHigh, targetHigh, smoothing);

        // Send the smoothed values to the shader
        audioMaterial.SetFloat(lowIntensityID, currentLow);
        audioMaterial.SetFloat(midIntensityID, currentMid);
        audioMaterial.SetFloat(highIntensityID, currentHigh);
    }
}