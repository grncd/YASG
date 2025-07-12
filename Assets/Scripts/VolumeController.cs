using MPUIKIT;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;

public class VolumeController : MonoBehaviour
{
    [Header("Audio Mixers")]
    [SerializeField] private AudioMixer musicMixer;
    [SerializeField] private AudioMixer fxMixer;
    [SerializeField] private AudioMixer fxResultsMixer;

    [Header("Mixer Exposed Parameters (Names)")]
    [SerializeField] private string musicVolumeParam = "MusicVolume";
    [SerializeField] private string fxVolumeParam = "FXVolume";
    [SerializeField] private string fxResultsVolumeParam = "FXResultsVolume";

    [Header("UI Fill Images")]
    [Tooltip("The image representing the Master volume percentage.")]
    [SerializeField] private MPImage masterFillImage;
    [Tooltip("The image representing the Music volume percentage. Must have a VolumeHoverArea script.")]
    [SerializeField] private MPImage musicFillImage;
    [Tooltip("The image representing the FX volume percentage. Must have a VolumeHoverArea script.")]
    [SerializeField] private MPImage fxFillImage;

    [Header("Control Settings")]
    [Tooltip("How much the volume changes per scroll wheel tick.")]
    [SerializeField] private float scrollSensitivity = 0.1f;

    // Default "maxed out" volume levels in decibels (dB).
    private const float MAX_MUSIC_DB = 0f;
    private const float MAX_FX_DB = -14f;
    private const float MAX_FXRESULTS_DB = -2f;
    private const float MIN_DB = -80f;

    // PlayerPrefs keys for saving/loading settings
    private const string MASTER_KEY = "MasterVolume";
    private const string MUSIC_KEY = "MusicVolume";
    private const string FX_KEY = "FXVolume";

    // Tracks which VolumeHoverArea the mouse is currently over.
    private VolumeHoverArea.VolumeType? currentHover = null;

    void Start()
    {
        // Load settings and apply them to the mixers and UI on game start.
        LoadAndApplySettings();
    }

    void Update()
    {
        // Check if the user is holding either Alt key.
        if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt))
        {
            // Get the mouse scroll wheel input. It's positive for scrolling up, negative for down.
            float scrollDelta = Input.GetAxis("Mouse ScrollWheel");

            // Only proceed if there was any scrolling.
            if (Mathf.Abs(scrollDelta) > 0.01f)
            {
                // Determine which volume to change based on the current hover state.
                if (currentHover == VolumeHoverArea.VolumeType.Music)
                {
                    AdjustVolume(MUSIC_KEY, scrollDelta);
                }
                else if (currentHover == VolumeHoverArea.VolumeType.FX)
                {
                    AdjustVolume(FX_KEY, scrollDelta);
                }
                else // If not hovering over a specific area, adjust the master volume.
                {
                    AdjustVolume(MASTER_KEY, scrollDelta);
                }
            }
        }
    }

    /// <summary>
    /// Called by VolumeHoverArea to set the current context.
    /// </summary>
    public void SetCurrentHover(VolumeHoverArea.VolumeType type)
    {
        currentHover = type;
    }

    /// <summary>
    /// Called by VolumeHoverArea when the mouse leaves its area.
    /// </summary>
    public void ClearCurrentHover()
    {
        currentHover = null;
    }

    private void AdjustVolume(string volumeKey, float delta)
    {
        // Get the current volume from PlayerPrefs, defaulting to 1.0 (max).
        float currentVolume = PlayerPrefs.GetFloat(volumeKey, 1.0f);

        // Calculate the new volume and clamp it between 0 and 1.
        float newVolume = Mathf.Clamp01(currentVolume + (delta * scrollSensitivity));

        // Save the new value.
        PlayerPrefs.SetFloat(volumeKey, newVolume);

        // Apply all settings to the mixers and update the UI.
        ApplyAllVolumeSettings();
        UpdateFillImages();
    }

    private void LoadAndApplySettings()
    {
        ApplyAllVolumeSettings();
        UpdateFillImages();
    }

    private void ApplyAllVolumeSettings()
    {
        float master = PlayerPrefs.GetFloat(MASTER_KEY, 1.0f);
        float music = PlayerPrefs.GetFloat(MUSIC_KEY, 1.0f);
        float fx = PlayerPrefs.GetFloat(FX_KEY, 1.0f);

        // --- Music ---
        float effectiveMusic = master * music;
        musicMixer.SetFloat(musicVolumeParam, ConvertToDecibels(effectiveMusic, MAX_MUSIC_DB));

        // --- FX ---
        float effectiveFx = master * fx;
        fxMixer.SetFloat(fxVolumeParam, ConvertToDecibels(effectiveFx, MAX_FX_DB));
        fxResultsMixer.SetFloat(fxResultsVolumeParam, ConvertToDecibels(effectiveFx, MAX_FXRESULTS_DB));
    }

    private void UpdateFillImages()
    {
        if (masterFillImage != null) masterFillImage.fillAmount = PlayerPrefs.GetFloat(MASTER_KEY, 1.0f);
        if (musicFillImage != null) musicFillImage.fillAmount = PlayerPrefs.GetFloat(MUSIC_KEY, 1.0f);
        if (fxFillImage != null) fxFillImage.fillAmount = PlayerPrefs.GetFloat(FX_KEY, 1.0f);
    }

    private float ConvertToDecibels(float sliderValue, float maxDb)
    {
        if (sliderValue < 0.0001f) return MIN_DB;
        return (Mathf.Log10(sliderValue) * 20f) + maxDb;
    }
}