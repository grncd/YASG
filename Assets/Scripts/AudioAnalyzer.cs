using UnityEngine;

public class AudioAnalyzer : MonoBehaviour
{
    [Tooltip("Materials using audio-reactive shaders.")]
    public Material[] audioMaterials;

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
        if (audioMaterials == null || audioMaterials.Length == 0)
        {
            Debug.LogError("Audio Materials are not assigned!");
            this.enabled = false;
            return;
        }

        spectrumData = new float[numSamples];

        lowIntensityID = Shader.PropertyToID("_LowIntensity");
        midIntensityID = Shader.PropertyToID("_MidIntensity");
        highIntensityID = Shader.PropertyToID("_HighIntensity");

        currentLow = minimumIntensity;
        currentMid = minimumIntensity;
        currentHigh = minimumIntensity;
    }

    private bool IsActiveForCurrentSettings()
    {
        int bgIndex = SettingsManager.Instance.GetSetting<int>("InGameBG");
        return (bgIndex == 1 || bgIndex == 3) && SettingsManager.Instance.GetSetting<bool>("AudioReactiveBGInGame");
    }

    private void ResetMaterials()
    {
        foreach (Material mat in audioMaterials)
        {
            if (mat == null) continue;
            mat.SetFloat(lowIntensityID, 0f);
            mat.SetFloat(midIntensityID, 0f);
            mat.SetFloat(highIntensityID, 0f);
        }
        currentLow = minimumIntensity;
        currentMid = minimumIntensity;
        currentHigh = minimumIntensity;
    }

    void Update()
    {
        if (!IsActiveForCurrentSettings())
        {
            ResetMaterials();
            return;
        }

        AudioListener.GetSpectrumData(spectrumData, 0, FFTWindow.BlackmanHarris);

        // Compensate for player volume settings so the background reacts fully regardless
        float effectiveVolume = PlayerPrefs.GetFloat("MasterVolume", 1f) * PlayerPrefs.GetFloat("MusicVolume", 1f);
        float volumeCompensation = effectiveVolume > 0.01f ? 1f / effectiveVolume : 1f;

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
        float targetLow = minimumIntensity + (lowSum * volumeCompensation * lowMultiplier);
        float targetMid = minimumIntensity + (midSum * volumeCompensation * midMultiplier);
        float targetHigh = minimumIntensity + (highSum * volumeCompensation * highMultiplier);

        // Smooth the transitions to prevent overly jittery visuals
        currentLow = Mathf.Lerp(currentLow, targetLow, smoothing);
        currentMid = Mathf.Lerp(currentMid, targetMid, smoothing);
        currentHigh = Mathf.Lerp(currentHigh, targetHigh, smoothing);

        // Send the smoothed values to all audio-reactive materials
        foreach (Material mat in audioMaterials)
        {
            if (mat == null) continue;
            mat.SetFloat(lowIntensityID, currentLow);
            mat.SetFloat(midIntensityID, currentMid);
            mat.SetFloat(highIntensityID, currentHigh);
        }
    }
}