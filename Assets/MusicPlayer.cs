using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System;
using TMPro;

[RequireComponent(typeof(AudioSource))]
public class MusicPlayer : MonoBehaviour
{
    // --- Public Fields ---
    [Header("Audio Clip")]
    public AudioClip initialClip;

    [Header("UI Elements")]
    public Slider playbackSlider;
    public TextMeshProUGUI timeDisplay;

    [Header("Time Display (Read-Only)")]
    public string currentTimeFormatted;
    public string totalTimeFormatted;

    // --- Private Fields ---
    [SerializeField]
    public AudioSource audioSource;
    private const float seekTimeAmount = 10f;
    private bool isScrubbing = false;
    private bool wasPlayingBeforeScrub;
    public static MusicPlayer Instance { get; private set; }

    // --- Unity Methods ---
    void Awake()
    {
        Instance = this;
        audioSource = GetComponent<AudioSource>();
        audioSource.playOnAwake = false;
        if (initialClip != null) SetClip(initialClip);
    }

    void Update()
    {
        if (audioSource.clip == null)
        {
            currentTimeFormatted = "0:00.00";
            totalTimeFormatted = "0:00.00";
            if (timeDisplay != null) timeDisplay.text = "0:00.00 / 0:00.00";
            if (playbackSlider != null) playbackSlider.value = 0;
            return;
        }

        currentTimeFormatted = FormatTime(audioSource.time);
        totalTimeFormatted = FormatTime(audioSource.clip.length);
        if (timeDisplay != null) timeDisplay.text = $"{currentTimeFormatted} / {totalTimeFormatted}";

        if (playbackSlider != null && !isScrubbing && audioSource.clip.length > 0)
        {
            playbackSlider.value = audioSource.time / audioSource.clip.length;
        }
    }

    // --- Public Control Functions ---
    public void SetClip(AudioClip clip)
    {
        if (clip == null) { Debug.LogError("Cannot set a null audio clip."); return; }
        audioSource.Stop();
        audioSource.clip = clip;
        totalTimeFormatted = FormatTime(audioSource.clip.length);
        audioSource.time = 0f;
        if (playbackSlider != null) playbackSlider.value = 0;
        Debug.Log($"Clip '{clip.name}' loaded. Duration: {totalTimeFormatted}");
    }

    public void PlayMusic()
    {
        if (audioSource.clip == null) return;
        if (Mathf.Approximately(audioSource.time, audioSource.clip.length)) audioSource.time = 0f;
        if (!audioSource.isPlaying) audioSource.Play();
    }

    public void PauseMusic()
    {
        if (audioSource.isPlaying) audioSource.Pause();
    }

    public void SeekForward()
    {
        if (audioSource.clip == null) return;
        audioSource.time = Mathf.Min(audioSource.clip.length, audioSource.time + seekTimeAmount);
    }

    public void SeekBackward()
    {
        if (audioSource.clip == null) return;
        audioSource.time = Mathf.Max(0f, audioSource.time - seekTimeAmount);
    }

    // --- Slider/Scrubbing Control Functions ---

    /// <summary>
    /// Called by the PointerDown event. Initializes the entire scrubbing process.
    /// </summary>
    public void OnPointerDownOnSlider()
    {
        isScrubbing = true;
        wasPlayingBeforeScrub = audioSource.isPlaying;
        if (wasPlayingBeforeScrub)
        {
            audioSource.Pause();
        }
        // Immediately update the time to the clicked position
        OnScrub();
    }

    /// <summary>
    /// Called by the Slider's OnValueChanged event while dragging.
    /// </summary>
    public void OnScrub()
    {
        if (isScrubbing && audioSource.clip != null && audioSource.clip.length > 0)
        {
            audioSource.time = playbackSlider.value * audioSource.clip.length;
        }
    }

    /// <summary>
    /// Called by the PointerUp event. Finalizes the scrubbing process.
    /// </summary>
    public void OnEndScrub()
    {
        // Check if we were actually scrubbing, to avoid issues.
        if (!isScrubbing) return;

        isScrubbing = false;
        if (wasPlayingBeforeScrub)
        {
            if (audioSource.clip != null && !Mathf.Approximately(audioSource.time, audioSource.clip.length))
            {
                audioSource.Play();
            }
        }
    }

    // --- Helper Functions ---
    public string FormatTime(float timeInSeconds)
    {
        TimeSpan timeSpan = TimeSpan.FromSeconds(timeInSeconds);
        return string.Format("{0}:{1:D2}.{2:D2}",
            timeSpan.Minutes, timeSpan.Seconds, timeSpan.Milliseconds / 10);
    }
}