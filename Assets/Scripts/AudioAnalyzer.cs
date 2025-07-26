using UnityEngine;

public class AudioAnalyzer : MonoBehaviour
{
    [Tooltip("The Material using the audio-reactive shader.")]
    public Material audioMaterial;

    [Header("Audio Analysis")]
    [SerializeField, Tooltip("Number of samples, must be a power of 2")]
    private int numSamples = 512;

    // --- NEW ---
    [Header("Intensity Controls")]
    [SerializeField, Tooltip("The minimum intensity value, for a constant ambient glow.")]
    private float minimumIntensity = 0.2f;

    [Header("Frequency Band Ranges")]
    [SerializeField] private int lowFrequencyThreshold = 200;
    [SerializeField] private int midFrequencyThreshold = 4000;

    [Header("Intensity Multipliers")]
    [SerializeField] private float lowMultiplier = 0.8f;
    [SerializeField] private float midMultiplier = 0.25f;
    [SerializeField] private float highMultiplier = 0.1f;

    [Header("Smoothing")]
    [SerializeField, Range(0f, 1f)]
    private float smoothing = 0.1f;

    private float[] spectrumData;
    private float currentLow, currentMid, currentHigh;

    private int lowIntensityID;
    private int midIntensityID;
    private int highIntensityID;

    void Start()
    {
        if(SettingsManager.Instance.GetSetting<int>("InGameBG") != 3 || !SettingsManager.Instance.GetSetting<bool>("AudioReactiveBGInGame"))
        {
            enabled = false;
        }
        if (audioMaterial == null)
        {
            Debug.LogError("Audio Material is not assigned!");
            this.enabled = false;
            return;
        }

        spectrumData = new float[numSamples];

        lowIntensityID = Shader.PropertyToID("_LowIntensity");
        midIntensityID = Shader.PropertyToID("_MidIntensity");
        highIntensityID = Shader.PropertyToID("_HighIntensity");

        // --- NEW ---
        // Initialize the values at the minimum intensity to prevent starting at 0.
        currentLow = minimumIntensity;
        currentMid = minimumIntensity;
        currentHigh = minimumIntensity;
    }

    void Update()
    {
        AudioListener.GetSpectrumData(spectrumData, 0, FFTWindow.BlackmanHarris);

        float lowSum = 0;
        float midSum = 0;
        float highSum = 0;

        float sampleRate = AudioSettings.outputSampleRate;
        int lowIndexCap = (int)(lowFrequencyThreshold * numSamples / sampleRate);
        int midIndexCap = (int)(midFrequencyThreshold * numSamples / sampleRate);

        for (int i = 0; i < numSamples; i++)
        {
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

        // --- CHANGED LOGIC ---
        // We now add the minimumIntensity as a base, ensuring the value never drops below it.
        // This preserves the proportional reaction of the audio on top of the base glow.
        float targetLow = minimumIntensity + (lowSum * lowMultiplier);
        float targetMid = minimumIntensity + (midSum * midMultiplier);
        float targetHigh = minimumIntensity + (highSum * highMultiplier);

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