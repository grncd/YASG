using UnityEngine;
using UnityEngine.UI;     // Required for the Slider
using UnityEngine.EventSystems; // Required for event handling
using System;             // Required for TimeSpan
using TMPro;

/// <summary>
/// A music player that controls an AudioSource on the same GameObject.
/// Provides functionality for play, pause, seeking, time display, and a UI slider for playback control.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class MusicPlayer : MonoBehaviour
{
    // --- Public Fields ---

    [Header("Audio Clip")]
    [Tooltip("The initial audio clip to be played. Can be set here or with SetClip().")]
    public AudioClip initialClip;

    [Header("UI Elements")]
    [Tooltip("The UI Slider that will represent and control the song's playback progress.")]
    public Slider playbackSlider;

    [Header("Time Display (Read-Only)")]
    [Tooltip("The current playback time, formatted as M:SS.ms.")]
    public string currentTimeFormatted;

    [Tooltip("The total duration of the audio clip, formatted as M:SS.ms.")]
    public string totalTimeFormatted;
    public TextMeshProUGUI timeDisplay;

    // --- Private Fields ---
    private AudioSource audioSource;
    private const float seekTimeAmount = 10f; // Seconds to jump forward or back

    // This flag prevents the Update loop from fighting with the user's scrubbing input.
    private bool isScrubbing = false;

    // --- Unity Methods ---

    void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        audioSource.playOnAwake = false;

        if (initialClip != null)
        {
            SetClip(initialClip);
        }
    }

    void Update()
    {
        if (audioSource.clip == null)
        {
            // If no clip, display zeroed time and reset slider
            currentTimeFormatted = "0:00.00";
            totalTimeFormatted = "0:00.00";
            if (playbackSlider != null) playbackSlider.value = 0;
            return;
        }

        // Update the public time strings every frame
        currentTimeFormatted = FormatTime(audioSource.time);
        totalTimeFormatted = FormatTime(audioSource.clip.length);
        timeDisplay.text = $"{currentTimeFormatted} / {totalTimeFormatted}";

        // If a slider is assigned and the user isn't currently dragging it, update its value
        if (playbackSlider != null && !isScrubbing)
        {
            // Slider value is a normalized value (0 to 1)
            playbackSlider.value = audioSource.time / audioSource.clip.length;
        }
    }

    // --- Public Control Functions ---

    public void SetClip(AudioClip clip)
    {
        if (clip == null)
        {
            Debug.LogError("Cannot set a null audio clip.");
            return;
        }

        audioSource.Stop();
        audioSource.clip = clip;
        totalTimeFormatted = FormatTime(audioSource.clip.length);

        // Reset slider for the new clip
        if (playbackSlider != null)
        {
            playbackSlider.value = 0;
        }

        Debug.Log($"Clip '{clip.name}' loaded. Duration: {totalTimeFormatted}");
    }

    public void PlayMusic()
    {
        if (audioSource.clip == null) return;
        if (!audioSource.isPlaying) audioSource.Play();
    }

    public void PauseMusic()
    {
        if (audioSource.isPlaying) audioSource.Pause();
    }

    public void SeekForward()
    {
        if (audioSource.clip == null) return;
        audioSource.time += seekTimeAmount;
    }

    public void SeekBackward()
    {
        if (audioSource.clip == null) return;
        audioSource.time -= seekTimeAmount;
    }

    // --- Slider/Scrubbing Control Functions ---

    /// <summary>
    /// This function should be called when the user begins to drag the slider.
    /// It sets a flag to prevent the Update loop from interfering.
    /// </summary>
    public void OnBeginScrub()
    {
        isScrubbing = true;
    }

    /// <summary>
    /// This function should be called by the Slider's OnValueChanged event.
    /// It updates the audio source's time to match the slider's new value.
    /// </summary>
    public void OnScrub()
    {
        if (isScrubbing && audioSource.clip != null)
        {
            // We multiply the slider's normalized value (0-1) by the clip's total length
            // to get the correct time in seconds to seek to.
            audioSource.time = playbackSlider.value * audioSource.clip.length;
        }
    }

    /// <summary>
    /// This function should be called when the user releases the slider.
    /// It unsets the flag, allowing the Update loop to resume control of the slider's position.
    /// </summary>
    public void OnEndScrub()
    {
        isScrubbing = false;
    }

    // --- Helper Functions ---

    private string FormatTime(float timeInSeconds)
    {
        TimeSpan timeSpan = TimeSpan.FromSeconds(timeInSeconds);
        return string.Format("{0}:{1:D2}.{2:D2}",
            timeSpan.Minutes,
            timeSpan.Seconds,
            timeSpan.Milliseconds / 10);
    }
}