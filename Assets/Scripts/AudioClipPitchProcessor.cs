using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Collections;
using UnityEngine.Networking;
using System;
using System.Diagnostics;
using TMPro;
using UnityEngine.Audio;
using System.IO; // For file operations
using System.Text; // For MD5 hashing
using System.Security.Cryptography; // For MD5 hashing

// --- JOB SYSTEM & BURST ADDITIONS START ---
using Unity.Collections; // For NativeArray
using Unity.Mathematics; // For math functions (Burst-compatible)
using Unity.Burst;       // For [BurstCompile]
using Unity.Jobs;        // For IJobParallelFor, JobHandle
using Unity.VisualScripting;
using MPUIKIT;
using UnityEditor;
using System.Linq;
// --- JOB SYSTEM & BURST ADDITIONS END ---

public class AudioClipPitchProcessor : MonoBehaviour
{
    private bool debugMode = true;

    [Header("UI Elements")]
    public Slider pitchSlider;
    public Slider pitchSlider2;
    public Slider pitchSlider3;
    public Slider pitchSlider4;
    public Slider pitchSliderDBG;
    public Slider progressBar;
    public GameObject loadingFX;
    public AudioSource stage4FX;

    [Header("Audio Clip Settings")]
    public AudioClip audioClip; // Vocal track for analysis
    private AudioClip audioClipFull; // Full track for playback
    public AudioSource audioSource;

    [Header("Pitch Detection Settings")]
    public float minFrequency = 80f;
    public float maxFrequency = 1000f;
    public float volumeThreshold = 0.0335f;

    [Header("Processing Settings")]
    public int analysisWindowSize = 2048;
    public int hopSize = 512;

    [Header("Dynamic Threshold Settings")]
    [Tooltip("The overall average RMS value (e.g., from a quiet song) at which minVolumeThresholdFactor is applied.")]
    [Range(0.01f, 0.15f)]
    public float minOverallAverageRMSForFactorMapping = 0.0942f;

    [Tooltip("The overall average RMS value (e.g., from an energetic song) at which maxVolumeThresholdFactor is applied.")]
    [Range(0.1f, 0.5f)]
    public float maxOverallAverageRMSForFactorMapping = 0.1266f; 

    [Tooltip("The scaling factor applied to overallAverageRMS when the song is at minOverallAverageRMSForFactorMapping.")]
    [Range(0.01f, 1.0f)] // Factor should generally be <= 1
    public float minVolumeThresholdFactor = 0.743f; // 0.07 / 0.0942

    [Tooltip("The scaling factor applied to overallAverageRMS when the song is at maxOverallAverageRMSForFactorMapping.")]
    [Range(0.5f, 2.0f)] // Can be > 1 if threshold needs to be higher than average
    public float maxVolumeThresholdFactor = 0.948f; // 0.12 / 0.1266

    [Tooltip("An optional dB boost added to the dynamically calculated threshold. Use to fine-tune across all songs.")]
    [Range(-10f, 10f)]
    public float dynamicVolumeThresholdDbBoost = 0.0f;

    private List<float> _allFrameRMSValues; // Still useful for calculating overallAverageRMS


    [SerializeField]
    public List<float> pitchOverTime = new List<float>();

    private float[] audioSamples; // Managed array for original data and compatibility
    private int sampleRate;
    public float AUDIO_TRIM_TIME = 0.05f;
    public GameObject guideArrow;
    public GameObject guideArrow2;
    public GameObject guideArrow3;
    public GameObject guideArrow4;
    public float currentPitch = 0f;
    public float scoreIncrement = 0f;
    public LyricsHandler lyricsHandler;
    public GameObject loadingScreen;
    public float AUDIO_LATENCY_COMPENSATION = 0.1f;
    public float ProcessedAudioLength;
    public Animator countdown;
    public TextMeshProUGUI countdownText;
    public AudioSource countdownFX;
    public ParticleSystem[] guideArrows;
    AudioSource musicSource;
    private Coroutine transitionCoroutine;
    public AudioMixerGroup musicControl;
    public float overallSongActivity;
    public bool paused = false;
    public Transform playersParent;
    public RawImage BG;
    public List<Material> backgrounds;
    public List<Color> backgroundDarkens;
    public Image darken;
    private bool showPitch;
    public AudioMixerGroup mixerForMusic;
    public TextMeshProUGUI tempDebugText;

    // Precomputed values for optimization
    private int minLag;
    private int maxLag;

    // --- JOB SYSTEM & BURST ADDITIONS START ---
    private NativeArray<float> hannWindowNative;
    private bool nativeResourcesInitialized = false;
    // --- JOB SYSTEM & BURST ADDITIONS END ---

    private const string PITCH_DATA_SUBFOLDER = "pitchdata";
    [System.Serializable]
    private class PitchDataWrapper { public List<float> pitches; }

    public float processingProgress = 0f; // <<< ADDED: Tracks overall loading/processing progress (0.0 to 1.0)
    private const float AUDIO_LOADING_PHASE_END_PROGRESS = 0.2f; // Audio loading takes up to 20% of total progress

    public static AudioClipPitchProcessor Instance { get; private set; }

    private void Awake()
    {
        Instance = this;
        if(PlayerPrefs.GetInt("multiplayer") == 1)
        {
            foreach (Transform child in playersParent)
            {
                Destroy(child.gameObject);
            }
        }
        switch (SettingsManager.Instance.GetSetting<int>("PitchProcessingQuality"))
        {
            case 0:
                analysisWindowSize = 1024;
                break;
            case 1:
                analysisWindowSize = 2048;
                break;
            case 2:
                analysisWindowSize = 4096;
                break;
            default:
                analysisWindowSize = 4096;
                break;
        }
        if(SettingsManager.Instance.GetSetting<int>("InGameBG") == 0)
        {
            BG.gameObject.SetActive(false);
        }
        else
        {
            BG.material = backgrounds[SettingsManager.Instance.GetSetting<int>("InGameBG")-1];
            darken.color = backgroundDarkens[SettingsManager.Instance.GetSetting<int>("InGameBG") - 1];
        }
        showPitch = SettingsManager.Instance.GetSetting<bool>("ShowDetectedPitch");

    }

    private string GetCacheFilePath(string audioClipPath)
    {
        string dataPath = PlayerPrefs.GetString("dataPath");
        if (string.IsNullOrEmpty(dataPath))
        {
            UnityEngine.Debug.LogError("[AudioClipPitchProcessor] PlayerPrefs 'dataPath' is not set. Cannot determine cache path.");
            return null;
        }
        string pitchDataFolder = Path.Combine(dataPath, PITCH_DATA_SUBFOLDER);
        string fileName = GenerateMD5(audioClipPath) + ".json";
        return Path.Combine(pitchDataFolder, fileName);
    }

    private string GenerateMD5(string input)
    {
        using (MD5 md5 = MD5.Create())
        {
            byte[] inputBytes = Encoding.UTF8.GetBytes(input);
            byte[] hashBytes = md5.ComputeHash(inputBytes);
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < hashBytes.Length; i++) sb.Append(hashBytes[i].ToString("x2"));
            return sb.ToString();
        }
    }

    public static float AnalyzeOverallSongPitchActivity(List<float> allPitches, float validPitchThreshold)
    {
        if (allPitches == null || allPitches.Count == 0)
        {
            UnityEngine.Debug.LogWarning("[AnalyzeOverallSongPitchActivity] Input pitch list is null or empty.");
            return 0f;
        }

        List<float> validSungPitches = new List<float>();
        foreach (float pitch in allPitches)
        {
            if (pitch >= validPitchThreshold)
            {
                validSungPitches.Add(pitch);
            }
        }

        if (validSungPitches.Count < 2)
        {
            UnityEngine.Debug.LogWarning($"[AnalyzeOverallSongPitchActivity] Less than 2 valid pitch frames (found {validSungPitches.Count}) above threshold {validPitchThreshold}. Returning 0 activity.");
            return 0f;
        }

        float sumOfAbsoluteDifferences = 0f;
        int validTransitions = 0;

        for (int i = 0; i < validSungPitches.Count - 1; i++)
        {
            sumOfAbsoluteDifferences += Mathf.Abs(validSungPitches[i + 1] - validSungPitches[i]);
            validTransitions++;
        }

        if (validTransitions == 0) return 0f;
        UnityEngine.Debug.Log("[AnalyzeOverallSongPitchActivity] "+ sumOfAbsoluteDifferences / validTransitions);
        return sumOfAbsoluteDifferences / validTransitions;
    }

    private void SavePitchData(List<float> pitches, string filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return;
        try
        {
            string directory = Path.GetDirectoryName(filePath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                UnityEngine.Debug.Log($"[AudioClipPitchProcessor] Created directory: {directory}");
            }
            PitchDataWrapper wrapper = new PitchDataWrapper { pitches = pitches };
            string json = JsonUtility.ToJson(wrapper, false);
            File.WriteAllText(filePath, json);
            UnityEngine.Debug.Log($"[AudioClipPitchProcessor] Pitch data saved to: {filePath}");
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError($"[AudioClipPitchProcessor] Error saving pitch data to {filePath}: {e.Message}");
        }
    }

    private List<float> LoadPitchData(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return null;
        try
        {
            string json = File.ReadAllText(filePath);
            PitchDataWrapper wrapper = JsonUtility.FromJson<PitchDataWrapper>(json);
            if (wrapper != null && wrapper.pitches != null)
            {
                UnityEngine.Debug.Log($"[AudioClipPitchProcessor] Pitch data loaded from: {filePath}");
                return wrapper.pitches;
            }
            UnityEngine.Debug.LogWarning($"[AudioClipPitchProcessor] Pitch data file {filePath} is corrupted or empty. Reprocessing.");
            return null;
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError($"[AudioClipPitchProcessor] Error loading pitch data from {filePath}: {e.Message}. Reprocessing.");
            return null;
        }
    }

    private void Start()
    {
        Application.targetFrameRate = -1;
        dynamicVolumeThresholdDbBoost = Mathf.Clamp(float.Parse(SettingsManager.Instance.GetSetting<string>("DynamicVolumeThreshold")), -10f, 10f);
        AUDIO_TRIM_TIME = Mathf.Clamp(float.Parse(SettingsManager.Instance.GetSetting<string>("SongOffset")), 0f, 1.5f);

        float temp;
        musicControl.audioMixer.GetFloat("LowpassCutoff", out temp);
        if (temp != 650f) LowpassTransition(true);

        musicSource = GameObject.Find("Music")?.GetComponent<AudioSource>();
        //guideArrowP = guideArrow.transform.GetChild(1).GetChild(0).GetChild(0).GetComponent<ParticleSystem>();
        //guideArrowP2 = guideArrow2.transform.GetChild(1).GetChild(0).GetChild(0).GetComponent<ParticleSystem>();
        // = guideArrow3.transform.GetChild(1).GetChild(0).GetChild(0).GetComponent<ParticleSystem>();
        //guideArrowP4 = guideArrow4.transform.GetChild(1).GetChild(0).GetChild(0).GetComponent<ParticleSystem>();

        processingProgress = 0f; // <<< ADDED: Initialize progress
        if (progressBar != null) progressBar.value = processingProgress; // <<< ADDED: Update UI
        loadingScreen.SetActive(true);


        string vocalPath = PlayerPrefs.GetString("vocalLocation");
        string fullPath = PlayerPrefs.GetString("fullLocation");

        UnityEngine.Debug.Log($"Vocal track path for analysis: {vocalPath}");
        UnityEngine.Debug.Log($"Full track path for playback: {fullPath}");

        StartCoroutine(LoadAudio(vocalPath, fullPath));
    }

    void OnDestroy()
    {
        if (nativeResourcesInitialized && hannWindowNative.IsCreated)
        {
            hannWindowNative.Dispose();
            nativeResourcesInitialized = false;
        }
    }

    public void LowpassTransition(bool inOut)
    {
        if (transitionCoroutine != null) StopCoroutine(transitionCoroutine);
        if (inOut) transitionCoroutine = StartCoroutine(ChangeCutoffOverTime(20000f, 650f, 0.5f));
        else transitionCoroutine = StartCoroutine(ChangeCutoffOverTime(650f, 20000f, 1f));
    }

    private IEnumerator ChangeCutoffOverTime(float startFreq, float endFreq, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float newFreq = Mathf.Lerp(startFreq, endFreq, elapsed / duration);
            musicControl.audioMixer.SetFloat("LowpassCutoff", newFreq);
            yield return null;
        }
        musicControl.audioMixer.SetFloat("LowpassCutoff", endFreq);
    }

    IEnumerator LoadAudio(string vocalTrackPath, string fullTrackPath)
    {
        if (string.IsNullOrEmpty(vocalTrackPath))
        {
            UnityEngine.Debug.LogError("Vocal file path is empty! Cannot proceed.");
            processingProgress = 1f; // <<< MODIFIED: Indicate completion (failure)
            if (progressBar != null) progressBar.value = processingProgress;
            StartCoroutine(FadeOutLoadingScreen());
            yield break;
        }

        // Vocal track loading (0% to 10% of overall, i.e., 0.0 to AUDIO_LOADING_PHASE_END_PROGRESS / 2)
        string urlVocal = "file://" + vocalTrackPath;
        using (UnityWebRequest wwwVocal = UnityWebRequestMultimedia.GetAudioClip(urlVocal, AudioType.MPEG))
        {
            var asyncOp = wwwVocal.SendWebRequest();
            while (!asyncOp.isDone)
            {
                processingProgress = wwwVocal.downloadProgress * (AUDIO_LOADING_PHASE_END_PROGRESS / 2f);
                // No need to update progressBar here, Update() will handle it.
                yield return null;
            }

            if (wwwVocal.result != UnityWebRequest.Result.Success)
            {
                UnityEngine.Debug.LogError($"Failed to load vocal audio: {wwwVocal.error} from path: {urlVocal}");
                processingProgress = 1f; // <<< MODIFIED: Indicate completion (failure)
                StartCoroutine(FadeOutLoadingScreen());
                yield break;
            }
            audioClip = DownloadHandlerAudioClip.GetContent(wwwVocal);
            if (audioClip == null)
            {
                UnityEngine.Debug.LogError($"Vocal AudioClip is null after download from: {urlVocal}. Cannot proceed.");
                processingProgress = 1f; // <<< MODIFIED: Indicate completion (failure)
                StartCoroutine(FadeOutLoadingScreen());
                yield break;
            }
            UnityEngine.Debug.Log($"Vocal audio loaded successfully: {audioClip.name}, Length: {audioClip.length}s");
        }
        processingProgress = AUDIO_LOADING_PHASE_END_PROGRESS / 2f; // <<< MODIFIED: Vocal loading complete

        // Full track loading (10% to 20% of overall, i.e., AUDIO_LOADING_PHASE_END_PROGRESS / 2 to AUDIO_LOADING_PHASE_END_PROGRESS)
        if (string.IsNullOrEmpty(fullTrackPath))
        {
            UnityEngine.Debug.LogWarning("Full track file path is empty! Using vocal track for playback as fallback.");
            audioClipFull = audioClip;
        }
        else
        {
            string urlFull = "file://" + fullTrackPath;
            using (UnityWebRequest wwwFull = UnityWebRequestMultimedia.GetAudioClip(urlFull, AudioType.MPEG))
            {
                var asyncOp = wwwFull.SendWebRequest();
                while (!asyncOp.isDone)
                {
                    processingProgress = (AUDIO_LOADING_PHASE_END_PROGRESS / 2f) + (wwwFull.downloadProgress * (AUDIO_LOADING_PHASE_END_PROGRESS / 2f));
                    yield return null;
                }

                if (wwwFull.result != UnityWebRequest.Result.Success)
                {
                    UnityEngine.Debug.LogWarning($"Failed to load full audio: {wwwFull.error} from path: {urlFull}. Using vocal track for playback as fallback.");
                    audioClipFull = audioClip;
                }
                else
                {
                    audioClipFull = DownloadHandlerAudioClip.GetContent(wwwFull);
                    if (audioClipFull == null)
                    {
                        UnityEngine.Debug.LogWarning($"Full AudioClip was null after download from: {urlFull}. Using vocal track for playback as fallback.");
                        audioClipFull = audioClip;
                    }
                    else
                    {
                        UnityEngine.Debug.Log($"Full audio loaded successfully: {audioClipFull.name}, Length: {audioClipFull.length}s");
                    }
                }
            }
        }
        if (audioClipFull == null) audioClipFull = audioClip;
        processingProgress = AUDIO_LOADING_PHASE_END_PROGRESS; // <<< MODIFIED: Full audio loading (or skip) complete

        StartProcessingAsync(vocalTrackPath);
    }


    public async void StartProcessingAsync(string vocalTrackPathForCache)
    {
        float phaseStartProgress = AUDIO_LOADING_PHASE_END_PROGRESS;
        float phaseSpan = 1.0f - phaseStartProgress;
        Action<float> SetPhaseProgress = (localProgress) => { // localProgress is 0.0 to 1.0 for this phase
            processingProgress = phaseStartProgress + (localProgress * phaseSpan);
        };

        SetPhaseProgress(0f);

        if (audioClip == null)
        {
            UnityEngine.Debug.LogError("[AudioClipPitchProcessor] No AudioClip (vocals) assigned for analysis! Aborting.");
            SetPhaseProgress(1f);
            StartCoroutine(FadeOutLoadingScreen());
            return;
        }
        SetPhaseProgress(0.02f);

        ProcessedAudioLength = audioClip.length;

        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.outputAudioMixerGroup = mixerForMusic;
        }
        SetPhaseProgress(0.05f);

        string cacheFilePath = GetCacheFilePath(vocalTrackPathForCache);
        List<float> loadedPitches = LoadPitchData(cacheFilePath);
        SetPhaseProgress(0.1f);

        ExtractAudioData();
        if (audioSamples == null || audioSamples.Length == 0)
        {
            UnityEngine.Debug.LogError("[AudioClipPitchProcessor] audioSamples are null or empty after ExtractAudioData. Aborting processing.");
            SetPhaseProgress(1f);
            StartCoroutine(FadeOutLoadingScreen());
            return;
        }

        // --- NEW: Calculate Dynamic Volume Threshold ---
        float phaseStartProgressForDynamicThreshold = 0.10f; // After initial audio load and cache check
        float phaseEndProgressForDynamicThreshold = 0.20f;   // Before actual pitch detection begins

        CalculateDynamicVolumeThreshold(
            audioSamples,
            sampleRate,
            analysisWindowSize,
            hopSize,
            (progress) => {
                // Map the 0-1 progress of this calculation phase to the overall loading progress
                processingProgress = phaseStartProgressForDynamicThreshold +
                                     (progress * (phaseEndProgressForDynamicThreshold - phaseStartProgressForDynamicThreshold));
            }
        );
        SetPhaseProgress(phaseEndProgressForDynamicThreshold); // Ensure progress is at the end of this phase
        // The 'volumeThreshold' field is now dynamically set!
        UnityEngine.Debug.Log($"[AudioClipPitchProcessor] Dynamic volumeThreshold set to: {volumeThreshold:F4} (dBFS: {20 * Mathf.Log10(volumeThreshold):F2} dB)");
        // ---------------------------------------------


        PrecomputeValues(); // This uses the now-updated volumeThreshold implicitly via minLag/maxLag or future uses.

        if (loadedPitches != null)
        {
            pitchOverTime = loadedPitches;
            UnityEngine.Debug.Log($"[AudioClipPitchProcessor] Loaded pitch data from cache. Total frames: {pitchOverTime.Count}");
            SetPhaseProgress(0.5f); // Cache hit skips heavy processing
        }
        else
        {
            UnityEngine.Debug.Log("[AudioClipPitchProcessor] Cache not found or invalid. Starting pitch processing...");
            ExtractAudioData();
            if (audioSamples == null || audioSamples.Length == 0)
            {
                UnityEngine.Debug.LogError("[AudioClipPitchProcessor] audioSamples are null or empty after ExtractAudioData. Aborting processing.");
                SetPhaseProgress(1f);
                StartCoroutine(FadeOutLoadingScreen());
                return;
            }

            PrecomputeValues();

            // --- MODIFIED JOB SYSTEM EXECUTION WITH PROGRESS ---
            if (this.audioSamples == null || this.audioSamples.Length == 0)
            {
                UnityEngine.Debug.LogError("[AudioClipPitchProcessor] Managed audioSamples is null or empty before Job System.");
                pitchOverTime = new List<float>();
                SetPhaseProgress(1f); // Mark job system phase as 'done' for progress consistency
            }
            else if (analysisWindowSize <= 0 || hopSize <= 0)
            {
                UnityEngine.Debug.LogError($"[AudioClipPitchProcessor] Invalid processing parameters: WindowSize={analysisWindowSize}, HopSize={hopSize}");
                pitchOverTime = new List<float>();
                SetPhaseProgress(1f);
            }
            else if (this.audioSamples.Length < analysisWindowSize)
            {
                UnityEngine.Debug.LogWarning($"[AudioClipPitchProcessor] audioSamples length ({this.audioSamples.Length}) is less than analysisWindowSize ({analysisWindowSize}). No frames to process.");
                pitchOverTime = new List<float>();
                SetPhaseProgress(1f);
            }
            else
            {
                int numFrames = (this.audioSamples.Length - analysisWindowSize) / hopSize;
                if (numFrames <= 0)
                {
                    UnityEngine.Debug.LogWarning($"[AudioClipPitchProcessor] Not enough audio data for any frames. Samples: {this.audioSamples.Length}, Window: {analysisWindowSize}, Hop: {hopSize}");
                    pitchOverTime = new List<float>();
                    SetPhaseProgress(1f);
                }
                else
                {
                    NativeArray<float> audioSamplesNative = new NativeArray<float>(this.audioSamples, Allocator.TempJob);
                    NativeArray<float> jobOutputPitchesNative; // Will be allocated in SchedulePitchDetectionJob

                    bool hannWindowOk = nativeResourcesInitialized && hannWindowNative.IsCreated && hannWindowNative.Length == analysisWindowSize;
                    if (!hannWindowOk)
                    {
                        UnityEngine.Debug.LogWarning("[AudioClipPitchProcessor] Hann window not ready or wrong size. Attempting reinitialization.");
                        PrecomputeValues(); // Attempt reinitialization
                        hannWindowOk = nativeResourcesInitialized && hannWindowNative.IsCreated && hannWindowNative.Length == analysisWindowSize;
                    }

                    if (hannWindowOk)
                    {
                        UnityEngine.Debug.Log("[AudioClipPitchProcessor] Scheduling Pitch Detection Job.");
                        JobHandle jobHandle = SchedulePitchDetectionJob(audioSamplesNative, out jobOutputPitchesNative, numFrames);

                        UnityEngine.Debug.Log("[AudioClipPitchProcessor] Monitoring Pitch Detection Job.");
                        // These are local progress values for the *analysis phase* (0.0 to 1.0)
                        float jobSystemPhaseStartLocalProgress = 0f;
                        float jobSystemPhaseEndLocalProgress = 1f;

                        await MonitorJobAndExtractPitches(
                            jobHandle,
                            audioSamplesNative,       // Disposed by MonitorJob
                            jobOutputPitchesNative,   // Disposed by MonitorJob
                            SetPhaseProgress,         // Delegate to update overall progress
                            jobSystemPhaseStartLocalProgress,
                            jobSystemPhaseEndLocalProgress
                        );
                        // pitchOverTime is now populated by MonitorJobAndExtractPitches
                        UnityEngine.Debug.Log($"[AudioClipPitchProcessor] Finished Job System processing pitch. Total frames: {pitchOverTime.Count}");
                    }
                    else
                    {
                        UnityEngine.Debug.LogError("[AudioClipPitchProcessor] Hann window still invalid after reinitialization. Aborting job.");
                        if (audioSamplesNative.IsCreated) audioSamplesNative.Dispose();
                        pitchOverTime = new List<float>();
                        SetPhaseProgress(1f); // Mark job system phase as 'done' for progress consistency
                    }
                }
            }
            // SetPhaseProgress(0.80f); // This is now handled by MonitorJobAndExtractPitches or error paths above
            // --- END MODIFIED JOB SYSTEM EXECUTION ---

            if (!string.IsNullOrEmpty(cacheFilePath) && pitchOverTime != null && pitchOverTime.Count > 0)
            {
                SavePitchData(pitchOverTime, cacheFilePath);
            }
        }

        // ... (rest of StartProcessingAsync method remains the same from here)
        if (pitchOverTime == null || pitchOverTime.Count == 0)
        {
            UnityEngine.Debug.LogError("[AudioClipPitchProcessor] pitchOverTime is null or empty after processing/loading. Cannot calculate score or play.");
            SetPhaseProgress(1f);
            StartCoroutine(FadeOutLoadingScreen());
            return;
        }

        if (pitchOverTime != null && pitchOverTime.Count > 0)
        {
            overallSongActivity = AnalyzeOverallSongPitchActivity(pitchOverTime, 32f);
        }

        int singingFrames = 0;
        int totalFrames = pitchOverTime.Count;
        bool loggedLyricSample = false;
        for (int i = 0; i < totalFrames; i++)
        {
            float frameTime = ((float)i / totalFrames) * audioClip.length;
            string lyricLine = "LYRICS_HANDLER_NULL_OR_ERROR";
            if (lyricsHandler != null) lyricLine = lyricsHandler.GetLyricForTime(frameTime);
            bool isSingingPitch = pitchOverTime[i] >= 32f;
            bool isValidLyric = lyricLine != "♪" && lyricLine != "" && lyricLine != null && lyricLine != "LYRICS_HANDLER_NULL_OR_ERROR";
            // ... (debug log removed for brevity)
            if (isSingingPitch && isValidLyric) singingFrames++;
        }

        if (singingFrames > 0)
        {
            scoreIncrement = (1000000f / singingFrames) * 1.85f;
        }
        else scoreIncrement = 0f;

        if (audioClipFull == null)
        {
            UnityEngine.Debug.LogError("[AudioClipPitchProcessor] audioClipFull is null before trimming. Playback will fail.");
            SetPhaseProgress(1f);
            StartCoroutine(FadeOutLoadingScreen());
            return;
        }

        AudioClip trimmedClip = TrimAudioClip(audioClipFull, AUDIO_TRIM_TIME);
        if (trimmedClip == null) trimmedClip = audioClipFull;
        audioSource.clip = trimmedClip;;

        loadingFX.SetActive(false);
        progressBar.value = 1f;
        GameObject stage4 = loadingScreen.transform.GetChild(0).GetChild(4).GetChild(3).GetChild(3).gameObject;
        GameObject progress3 = loadingScreen.transform.GetChild(0).GetChild(4).GetChild(2).gameObject;
        stage4.transform.GetChild(1).gameObject.SetActive(false);
        stage4.transform.GetChild(0).GetComponent<TextMeshProUGUI>().color = new Color(0.3443396f, 1f, 0.3759922f);
        stage4.transform.GetChild(2).GetComponent<TextMeshProUGUI>().color = new Color(0.3443396f, 1f, 0.3759922f);
        stage4.GetComponent<MPImage>().color = new Color(0.1116768f, 0.333f, 0.1218292f);
        stage4.GetComponent<MPImage>().OutlineColor = new Color(0.313548f, 0.901f, 0.3404953f);
        stage4.GetComponent<Animator>().Play("Done");
        progress3.GetComponent<Slider>().value = 1f;
        progress3.transform.GetChild(1).GetChild(0).GetComponent<MPImage>().color = new Color(0.313548f, 0.901f, 0.3404953f);
        stage4FX.Play();
        progressBar.value = 1f;
        await Task.Delay(TimeSpan.FromSeconds(0.1f));
        progressBar.value = 1f;
        await Task.Delay(TimeSpan.FromSeconds(0.1f));
        progressBar.value = 1f;
        await Task.Delay(TimeSpan.FromSeconds(0.8f));

        StartCoroutine(FadeOutAndStop(musicSource, 2f));
        StartCoroutine(FadeOutLoadingScreen());

        await Task.Delay(TimeSpan.FromSeconds(1.2f));
        if(PlayerPrefs.GetInt("multiplayer") == 1)
        {
            UnityEngine.Debug.Log("Audio processing finished. Reporting GameReady status to the server.");
            if (PlayerData.LocalPlayerInstance != null)
            {
                PlayerData.LocalPlayerInstance.RequestReportGameReady_ServerRpc();
            }
            else
            {
                UnityEngine.Debug.LogError("Cannot report GameReady status because LocalPlayerInstance is null!");
            }
        }
        else
        {
            StartCoroutine(FinalCountdown_Coroutine());
        }
    }

    public void StartFinalCountdown()
    {
        StartCoroutine(FinalCountdown_Coroutine());
    }

    private IEnumerator FinalCountdown_Coroutine()
    {
        UnityEngine.Debug.Log("Starting final synchronized countdown!");

        countdownText.text = "";
        countdownFX.Play();
        yield return new WaitForSeconds(0.15f);
        countdown.Play("Countdown");
        yield return new WaitForSeconds(0.985f);
        countdownText.text = "3";
        yield return new WaitForSeconds(1);
        countdownText.text = "2";
        yield return new WaitForSeconds(1);
        countdownText.text = "1";
        if (musicSource != null) musicSource.Stop();
        musicControl.audioMixer.SetFloat("LowpassCutoff", 20000f);
        yield return new WaitForSeconds(1);
        countdownText.text = "0";
        if (lyricsHandler != null) lyricsHandler.StartLyrics();
        audioSource.Play();
        // Find all "Player" children under the "Players" GameObject
        Transform playersRoot = transform.Find("Canvas/Players");

        if (playersRoot != null)
        {
            // Collect all ParticleSystems named exactly "Particle System"
            guideArrows = playersRoot
                .GetComponentsInChildren<ParticleSystem>(true)
                .Where(ps => ps.gameObject.name == "Particle System")
                .ToArray();
        }
    }


    // --- NEW HELPER METHOD ---
    // This method schedules the job but doesn't wait for completion.
    private JobHandle SchedulePitchDetectionJob(
        NativeArray<float> audioSamplesNative, // Input, needs disposal later
        out NativeArray<float> outputPitchesNative, // Output, needs disposal later
        int numFrames)
    {
        // Output array is created here, needs to be TempJob for job scheduling
        outputPitchesNative = new NativeArray<float>(numFrames, Allocator.TempJob);

        // Ensure Hann window is valid (this check is also done before calling, but good for safety)
        if (!nativeResourcesInitialized || !hannWindowNative.IsCreated || hannWindowNative.Length != analysisWindowSize)
        {
            UnityEngine.Debug.LogError("[AudioClipPitchProcessor] Hann window native array not properly initialized in SchedulePitchDetectionJob. This should not happen if pre-checks are done.");
            // Fallback: create an empty job or handle error, for now, we assume it's valid due to prior checks
        }

        ProcessAudioFrameJob pitchJob = new ProcessAudioFrameJob
        {
            AudioSamples_Native = audioSamplesNative,
            HannWindow_Native = this.hannWindowNative,
            OutputPitches_Native = outputPitchesNative,
            SampleRate = this.sampleRate,
            AnalysisWindowSize = this.analysisWindowSize,
            HopSize = this.hopSize,
            VolumeThreshold = this.volumeThreshold,
            MinLag = this.minLag,
            MaxLag = this.maxLag
        };

        return pitchJob.Schedule(numFrames, 32); // Batch size 32
    }

    // --- NEW ASYNC MONITORING METHOD ---
    private async Task MonitorJobAndExtractPitches(
        JobHandle jobHandle,
        NativeArray<float> audioSamplesNativeToDispose,    // To dispose after job
        NativeArray<float> outputPitchesNative,            // To read from and dispose
        Action<float> setAnalysisPhaseProgress,            // Delegate to set overall progress (takes 0-1 for analysis phase)
        float analysisPhaseJobStartLocalProgress,          // e.g., 0.35 (local progress value for analysis phase)
        float analysisPhaseJobEndLocalProgress)            // e.g., 0.80 (local progress value for analysis phase)
    {
        float jobLocalProgressRange = analysisPhaseJobEndLocalProgress - analysisPhaseJobStartLocalProgress;
        int timeSliceMs = 30; // Update roughly every couple of frames to reduce overhead
        int maxUpdatesBeforeJobLikelyDone = 75; // Estimate: fill in N steps. Adjust for desired smoothness/speed.
        int updatesDone = 0;

        while (!jobHandle.IsCompleted)
        {
            updatesDone++;
            // Calculate this job's internal progress (0 to 1, estimated)
            float jobInternalEstimatedProgress = Mathf.Clamp01((float)updatesDone / maxUpdatesBeforeJobLikelyDone);

            // Map this job's internal progress to the relevant slice of the analysis phase's local progress
            float currentAnalysisPhaseLocalProgress = analysisPhaseJobStartLocalProgress + jobInternalEstimatedProgress * jobLocalProgressRange;

            // Ensure we don't quite hit the end progress until the job is truly done, to show the final jump.
            if (currentAnalysisPhaseLocalProgress >= analysisPhaseJobEndLocalProgress && updatesDone <= maxUpdatesBeforeJobLikelyDone)
            {
                // Stay just shy if we haven't 'timed out' our estimated updates
                currentAnalysisPhaseLocalProgress = analysisPhaseJobEndLocalProgress - (0.01f * jobLocalProgressRange);
            }

            setAnalysisPhaseProgress(currentAnalysisPhaseLocalProgress);
            await Task.Delay(timeSliceMs);
        }

        jobHandle.Complete(); // Ensures job is truly finished and syncs results.

        // Populate pitchOverTime (class member) from the job output
        if (outputPitchesNative.IsCreated && outputPitchesNative.Length > 0)
        {
            this.pitchOverTime = new List<float>(outputPitchesNative.ToArray());
        }
        else
        {
            this.pitchOverTime = new List<float>(); // Ensure it's not null
            UnityEngine.Debug.LogWarning("[AudioClipPitchProcessor] Job output was empty or invalid after completion.");
        }

        // Dispose NativeArrays used by the job
        if (audioSamplesNativeToDispose.IsCreated) audioSamplesNativeToDispose.Dispose();
        if (outputPitchesNative.IsCreated) outputPitchesNative.Dispose();

        // Set final progress for this job's contribution to the analysis phase
        setAnalysisPhaseProgress(analysisPhaseJobEndLocalProgress);
    }

    public IEnumerator FadeOutAndStop(AudioSource sourceToFade, float fadeDuration)
    {
        if (sourceToFade == null) yield break;
        float startVolume = sourceToFade.volume;
        while (sourceToFade.volume > 0)
        {
            sourceToFade.volume -= startVolume * Time.deltaTime / fadeDuration;
            yield return null;
        }
        sourceToFade.Stop();
        sourceToFade.volume = startVolume;
    }

    private IEnumerator FadeOutLoadingScreen()
    {
        CanvasGroup canvasGroup = loadingScreen.GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            canvasGroup = loadingScreen.AddComponent<CanvasGroup>();
        }

        float duration = 1f;
        float currentTime = 0f;
        float startAlpha = canvasGroup.alpha;

        while (currentTime < duration)
        {
            currentTime += Time.deltaTime;
            canvasGroup.alpha = Mathf.Lerp(startAlpha, 0, currentTime / duration);
            yield return null;
        }
        canvasGroup.alpha = 0;
        loadingScreen.SetActive(false);
    }

    private void PrecomputeValues()
    {
        if (sampleRate == 0)
        {
            UnityEngine.Debug.LogError("[AudioClipPitchProcessor] SampleRate is 0 in PrecomputeValues.");
            minLag = 50; maxLag = 550;
        }
        else
        {
            minLag = math.max(1, (int)math.floor(sampleRate / maxFrequency));
            maxLag = (int)math.floor(sampleRate / minFrequency);
        }

        if (analysisWindowSize <= 1)
        {
            UnityEngine.Debug.LogError("[AudioClipPitchProcessor] analysisWindowSize is too small for Hann window.");
            if (nativeResourcesInitialized && hannWindowNative.IsCreated) hannWindowNative.Dispose();
            hannWindowNative = new NativeArray<float>(0, Allocator.Persistent);
            nativeResourcesInitialized = true;
            return;
        }

        if (nativeResourcesInitialized && hannWindowNative.IsCreated)
        {
            if (hannWindowNative.Length != analysisWindowSize)
            {
                hannWindowNative.Dispose();
                hannWindowNative = new NativeArray<float>(analysisWindowSize, Allocator.Persistent);
            }
        }
        else hannWindowNative = new NativeArray<float>(analysisWindowSize, Allocator.Persistent);

        for (int i = 0; i < analysisWindowSize; i++)
        {
            hannWindowNative[i] = 0.5f * (1f - math.cos(2f * math.PI * i / (analysisWindowSize - 1f)));
        }
        nativeResourcesInitialized = true;
    }

    private AudioClip TrimAudioClip(AudioClip clip, float trimTime)
    {
        if (clip == null) return null;
        if (trimTime <= 0) return clip;

        int trimSamples = Mathf.FloorToInt(trimTime * clip.frequency * clip.channels);
        int totalSamples = clip.samples * clip.channels;

        if (trimSamples >= totalSamples)
        {
            UnityEngine.Debug.LogWarning($"Trim time too large. Returning empty clip.");
            return AudioClip.Create(clip.name + "_trimmed_empty", 1, clip.channels, clip.frequency, false);
        }

        float[] originalData = new float[totalSamples];
        clip.GetData(originalData, 0);

        int remainingSamples = totalSamples - trimSamples;
        float[] trimmedData = new float[remainingSamples];
        System.Array.Copy(originalData, trimSamples, trimmedData, 0, remainingSamples);

        AudioClip newClip = AudioClip.Create(clip.name + "_trimmed", remainingSamples / clip.channels, clip.channels, clip.frequency, false);
        if (!newClip.SetData(trimmedData, 0)) return clip;
        return newClip;
    }

    private void ExtractAudioData()
    {
        if (audioClip == null)
        {
            sampleRate = 0; this.audioSamples = new float[0]; return;
        }

        sampleRate = audioClip.frequency;
        int totalSamplesOriginal = audioClip.samples * audioClip.channels;
        this.audioSamples = new float[totalSamplesOriginal];
        audioClip.GetData(this.audioSamples, 0);

        if (audioClip.channels > 1)
        {
            float[] monoSamples = new float[audioClip.samples];
            for (int i = 0; i < audioClip.samples; i++)
            {
                float sum = 0f;
                for (int ch = 0; ch < audioClip.channels; ch++) sum += this.audioSamples[i * audioClip.channels + ch];
                monoSamples[i] = sum / audioClip.channels;
            }
            this.audioSamples = monoSamples;
        }
    }


    [BurstCompile]
    private struct ProcessAudioFrameJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float> AudioSamples_Native;
        [ReadOnly] public NativeArray<float> HannWindow_Native;
        [WriteOnly] public NativeArray<float> OutputPitches_Native;
        public int SampleRate;
        public int AnalysisWindowSize;
        public int HopSize;
        public float VolumeThreshold;
        public int MinLag;
        public int MaxLag;

        public void Execute(int frameIndex)
        {
            int offset = frameIndex * HopSize;
            if (offset + AnalysisWindowSize > AudioSamples_Native.Length)
            {
                OutputPitches_Native[frameIndex] = 0f; return;
            }
            NativeSlice<float> frameSamplesSlice = AudioSamples_Native.Slice(offset, AnalysisWindowSize);

            float clarity;
            OutputPitches_Native[frameIndex] = DetectPitch_MPM_Burst(
                frameSamplesSlice, HannWindow_Native, SampleRate, VolumeThreshold, MinLag, MaxLag,
                out clarity, // <-- MOVED: Pass the out parameter first
                0.7f         // <-- MOVED: Pass the optional parameter last
            );
        }
    }

    [BurstCompile]
    public static float DetectPitch_MPM_Burst(
        NativeSlice<float> samples, 
        NativeArray<float> hannWindow,
        int sampleRate, 
        float volumeThreshold,
        int minLag, 
        int maxLag, 
        out float clarity,                 // <-- MOVED: Required parameter now comes first
        float clarityThreshold = 0.8f)   // <-- MOVED: Optional parameter is now last
    {
        clarity = 0f; // Default clarity
        int size = samples.Length;
        if (size == 0 || size != hannWindow.Length) return 0f;

        // --- Step 1: Volume Check ---
        float sumSquared = 0f;
        for (int i = 0; i < size; i++) sumSquared += samples[i] * samples[i];
        float rms = math.sqrt(sumSquared / size);
        if (rms < volumeThreshold) return 0f;

        // Apply Hann Window
        Span<float> windowedSamples = stackalloc float[size];
        for (int i = 0; i < size; i++) windowedSamples[i] = samples[i] * hannWindow[i];

        // --- Step 2: Autocorrelation (used by NSDF) ---
        Span<float> acf = stackalloc float[maxLag + 1];
        for (int lag = 0; lag <= maxLag; lag++)
        {
            float sum = 0;
            for (int i = 0; i < size - lag; i++)
            {
                sum += windowedSamples[i] * windowedSamples[i + lag];
            }
            acf[lag] = sum;
        }

        // --- Step 3: Normalized Square Difference Function (NSDF) ---
        Span<float> nsdf = stackalloc float[maxLag + 1];
        float m0 = acf[0]; // Energy of the signal
        for (int lag = 0; lag <= maxLag; lag++)
        {
            float m_lag = 0;
            for (int i = 0; i < size - lag; i++)
            {
                m_lag += windowedSamples[i + lag] * windowedSamples[i + lag];
            }
            
            float denominator = m0 + m_lag;
            if (denominator > 1e-9f) {
                nsdf[lag] = 2 * acf[lag] / denominator;
            } else {
                nsdf[lag] = 0;
            }
        }

        // --- Step 4: Peak Picking ---
        int period = 0;
        float maxVal = 0f;

        for (int lag = minLag + 1; lag < maxLag; lag++)
        {
            if (nsdf[lag] > nsdf[lag - 1] && nsdf[lag] > nsdf[lag + 1])
            {
                if (period == 0)
                {
                    if (nsdf[lag] > 0.1f) 
                    {
                        period = lag;
                        maxVal = nsdf[lag];
                    }
                }
                else if (nsdf[lag] > maxVal)
                {
                    period = lag;
                    maxVal = nsdf[lag];
                }
            }
        }
        
        clarity = maxVal;

        // --- Step 5: Sibilance/Noise Rejection using Clarity ---
        if (clarity < clarityThreshold)
        {
            return 0f;
        }

        if (period == 0) return 0f;

        // --- Step 6: Parabolic Interpolation for Accuracy ---
        float finalLag;
        if (period > 1 && period < maxLag)
        {
            float y1 = nsdf[period - 1];
            float y2 = nsdf[period];
            float y3 = nsdf[period + 1];
            float denom = y1 - 2 * y2 + y3;
            float shift = 0;
            if (math.abs(denom) > 1e-6f)
            {
                shift = 0.5f * (y1 - y3) / denom;
            }
            finalLag = period - shift;
        }
        else
        {
            finalLag = period;
        }

        if (finalLag <= 0) return 0f;
        return (float)sampleRate / finalLag;
    }


    public void Pause()
    {
        paused = !paused;
        if (paused)
        {
            audioSource.Pause();
        }
        else
        {
            audioSource.Play();
        }
    }

    private void Update()
    {
        // <<< ADDED: Update progress bar UI >>>
        if (progressBar != null && loadingScreen != null && loadingScreen.activeSelf)
        {
            progressBar.gameObject.SetActive(true);
            progressBar.value = processingProgress;
        }
        // <<< END ADDED >>>


        if (audioSource != null && audioSource.isPlaying && pitchOverTime != null && pitchOverTime.Count > 0)
        {
            if (audioClip == null || audioClip.length == 0f) return;

            // --- CURRENT TIME LOGIC (for scoring, etc.) ---
            float adjustedTime = audioSource.time + AUDIO_LATENCY_COMPENSATION;
            int index = Mathf.FloorToInt((adjustedTime / audioClip.length) * pitchOverTime.Count);
            index = Mathf.Clamp(index, 0, pitchOverTime.Count - 1);

            // --- VISUALIZER LOOKAHEAD LOGIC ---
            float visualizerTime = adjustedTime + 0.9f; // Look ahead 0.9 seconds
            int visualizerIndex = Mathf.FloorToInt((visualizerTime / audioClip.length) * pitchOverTime.Count);
            visualizerIndex = Mathf.Clamp(visualizerIndex, 0, pitchOverTime.Count - 1);

            if (showPitch)
            {
                // Update current pitch for scoring or other real-time logic
                float pitch = pitchOverTime[index];
                bool isSinging = pitch >= 32f;
                currentPitch = isSinging ? pitch : 0f;

                // Get future values for the visualizer to give the user time to react
                float futurePitchValue = pitchOverTime[visualizerIndex];
                bool futureIsSinging = futurePitchValue >= 32f;
                float futurePitchForVisuals = futureIsSinging ? futurePitchValue : 0f;

                foreach (ParticleSystem ps in FindObjectsOfType<ParticleSystem>())
                {
                    if (ps.gameObject.name == "Particle System")
                    {
                        // This particle system should use the current singing state
                        if (isSinging && !ps.isPlaying) ps.Play();
                        else if (!isSinging && ps.isPlaying) ps.Stop();
                    }
                    if (ps.gameObject.name == "Particle System Main")
                    {
                        // Use future singing state for note particles
                        if (futureIsSinging && !ps.isEmitting) ps.Play();
                        else if (!futureIsSinging && ps.isEmitting) ps.Stop();
                        var shape = ps.shape;
                        // Use future pitch for positioning note particles
                        shape.position = new Vector3(0f, Mathf.Clamp(futurePitchForVisuals, minFrequency, maxFrequency) * 0.0032f, 0f);
                        
                    }
                }


                foreach (Slider slider in FindObjectsOfType<Slider>())
                {
                    if (slider.gameObject.name == "MainPitch")
                    {
                        slider.value = Mathf.Clamp(currentPitch, minFrequency, maxFrequency);
                    }
                }

                //pitchSlider.value = Mathf.Clamp(currentPitch, minFrequency, maxFrequency);
                //if (pitchSlider2 != null) pitchSlider2.value = Mathf.Clamp(currentPitch, minFrequency, maxFrequency);
                //if (pitchSlider3 != null) pitchSlider3.value = Mathf.Clamp(currentPitch, minFrequency, maxFrequency);
                //if (pitchSlider4 != null) pitchSlider4.value = Mathf.Clamp(currentPitch, minFrequency, maxFrequency);
                // Debug slider still shows the current pitch for scoring verification
                if (debugMode && pitchSliderDBG != null) pitchSliderDBG.value = Mathf.Clamp(currentPitch, minFrequency, maxFrequency);
            }
        }
        else if (audioSource != null && !audioSource.isPlaying && currentPitch != 0f)
        {
            currentPitch = 0f;
            Action<ParticleSystem> stopParticle = ps => { if (ps != null && ps.isPlaying) ps.Stop(); };

            foreach (ParticleSystem ps in FindObjectsOfType<ParticleSystem>())
            {
                if (ps.gameObject.name == "Particle System")
                {
                    ps.Stop();
                }
            }

            foreach (Slider slider in FindObjectsOfType<Slider>())
            {
                if (slider.gameObject.name == "MainPitch")
                {
                    slider.value = minFrequency;
                }
            }

            //if (pitchSlider != null) pitchSlider.value = minFrequency;
            //if (pitchSlider2 != null) pitchSlider2.value = minFrequency;
            //if (pitchSlider3 != null) pitchSlider3.value = minFrequency;
            //if (pitchSlider4 != null) pitchSlider4.value = minFrequency;
            //if (debugMode && pitchSliderDBG != null) pitchSliderDBG.value = minFrequency;
        }
    }

    private void CalculateDynamicVolumeThreshold(float[] samples, int sampleRate, int windowSize, int hopSize, Action<float> progressCallback = null)
    {
        if (samples == null || samples.Length == 0 || windowSize <= 0 || hopSize <= 0)
        {
            volumeThreshold = 0.001f; // Fallback
            UnityEngine.Debug.LogWarning("[AudioClipPitchProcessor] Insufficient audio data or invalid parameters for dynamic threshold calculation. Falling back to default low threshold.");
            progressCallback?.Invoke(1f);
            return;
        }

        _allFrameRMSValues = new List<float>();
        float totalRMSSumForAverage = 0f;
        int numFrames = (samples.Length - windowSize) / hopSize;

        if (numFrames <= 0)
        {
            volumeThreshold = 0.001f;
            UnityEngine.Debug.LogWarning("[AudioClipPitchProcessor] Not enough frames to calculate dynamic threshold. Falling back to default low threshold.");
            progressCallback?.Invoke(1f);
            return;
        }

        for (int i = 0; i < numFrames; i++)
        {
            int offset = i * hopSize;
            if (offset + windowSize > samples.Length) break;

            float sumSquared = 0f;
            for (int j = 0; j < windowSize; j++)
            {
                sumSquared += samples[offset + j] * samples[offset + j];
            }
            float rms = Mathf.Sqrt(sumSquared / windowSize);
            _allFrameRMSValues.Add(rms);
            totalRMSSumForAverage += rms;

            if (progressCallback != null && i % 100 == 0)
            {
                progressCallback.Invoke((float)i / numFrames);
            }
        }

        if (_allFrameRMSValues.Count == 0)
        {
            volumeThreshold = 0.001f;
            UnityEngine.Debug.LogWarning("[AudioClipPitchProcessor] No RMS values calculated for dynamic threshold. Falling back to default low threshold.");
            progressCallback?.Invoke(1f);
            return;
        }

        // Calculate overall average RMS of the entire track
        float overallAverageRMS = totalRMSSumForAverage / _allFrameRMSValues.Count;

        // --- NEW NON-LINEAR THRESHOLD CALCULATION ---
        // 1. Normalize the song's overallAverageRMS within the defined mapping range.
        // This gives us a 0-1 value indicating where this song's loudness falls in our intended mapping.
        float normalizedAvgRMSForFactor = Mathf.InverseLerp(minOverallAverageRMSForFactorMapping, maxOverallAverageRMSForFactorMapping, overallAverageRMS);
        normalizedAvgRMSForFactor = Mathf.Clamp01(normalizedAvgRMSForFactor); // Ensure it's strictly between 0 and 1

        // 2. Interpolate the actual scaling factor to use based on this normalized value.
        // Quieter songs will get a factor closer to minVolumeThresholdFactor.
        // Louder songs will get a factor closer to maxVolumeThresholdFactor.
        float dynamicScalingFactor = Mathf.Lerp(minVolumeThresholdFactor, maxVolumeThresholdFactor, normalizedAvgRMSForFactor);

        // 3. Apply the dynamic scaling factor to the overall average RMS to get the base volume threshold.
        volumeThreshold = overallAverageRMS * dynamicScalingFactor;
        // --------------------------------------------

        // Apply an optional dB boost (or cut)
        float boostFactor = Mathf.Pow(10, dynamicVolumeThresholdDbBoost / 20f);
        volumeThreshold *= boostFactor;

        // Ensure a reasonable absolute minimum threshold as a safety net
        volumeThreshold = Mathf.Max(volumeThreshold, 0.005f);

        progressCallback?.Invoke(1f);

        // Log detailed information for tuning and debugging
        UnityEngine.Debug.Log($"[AudioClipPitchProcessor] OverallAvgRMS: {overallAverageRMS:F4}, NormAvgRMSForFactor: {normalizedAvgRMSForFactor:F2}, DynamicScalingFactor: {dynamicScalingFactor:F3}, Dynamic volumeThreshold set to: {volumeThreshold:F4} (dBFS: {20 * Mathf.Log10(volumeThreshold):F2} dB)");
    }
}
