
using UnityEngine;
using System.Collections;
using UnityEngine.UI; // Assuming you still need Slider
using TMPro;
using System; // For Exception
using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;
using MyAudioProcessing;
using System.Collections.Generic;

public class RealTimePitchDetector : MonoBehaviour
{
    public bool debugMode = true;

    [Header("Balancing Settings")]
    public float leniencyCoefficient = 1f;
    private float currentLeniencyCoefficient = 1f;
    public float gracePeriod = 0.10f;
    private float gracePeriodElapsed = 0f;

    [Header("Multiplayer Settings")]
    [Tooltip("The player index this detector is for (0 for Player 1, 1 for Player 2, etc.). Must be set in the Inspector.")]
    public int playerIndex = 0;


    [Header("Microphone Settings")]
    public int device = -1; // Externally settable device INDEX. -1 means not set/used by external script.
    public string selectedDeviceNameOverride = ""; // Optional: for forcing a device if PlayerPrefs isn't set
    public int sampleRate = 44100;
    public int bufferLengthSeconds = 1;
    public int analysisWindowSize = 2048;

    [Header("Detection Settings")]
    public float smoothing = 0.9f;

    [Header("EQ Settings (Low Shelf)")]
    public float fixedLowShelfGainDB = 55f;
    public float lowShelfGainDB = 55f;
    [Tooltip("Cutoff frequency for the low shelf filter (Hz). Boosts frequencies below this.")]
    public float lowShelfCutoff = 250f;
    [Tooltip("Q factor for the low shelf filter.")]
    public float lowShelfQ = 1.3f;
    private float _previousLowShelfGainDB = -1000f;
    private float _previousLowShelfCutoff = -1f;
    private float _previousLowShelfQ = -1f;

    [Header("Advanced Anti-Monotony Settings")]
    public bool enableAdvancedAntiMonotony = false;

    // --- User Pitch Activity ---
    [Tooltip("How many seconds of user pitch history to analyze for activity.")]
    public float userPitchActivityWindowSecs = 0.6f;
    private Queue<float> userPitchesForActivity;
    private int userPitchActivityBufferSize; // Calculated based on FixedUpdate rate
    [Tooltip("If user's average pitch change (Hz) in the window is below this, they are considered 'inactive'.")]
    public float userMonotonyThresholdHz = 1.7f;
    private float currentUserPitchActivity = 0f; // The calculated activity of the user's pitch
    private float timeUserHasBeenMonotonous = 0f; // How long the user has been 'inactive'

    // --- Dynamic Allowed Monotony Duration (based on Reference Track) ---
    [Tooltip("How many seconds of the reference track (ahead/around current time) to analyze for its activity.")]
    public float referenceAnalysisWindowSecs = 1.0f;
    [Tooltip("Minimum duration (seconds) the user can be monotonous if the reference is highly articulate (e.g., rap).")]
    public float minAllowedMonotonyDuration = 0.36f;
    [Tooltip("Maximum duration (seconds) the user can be monotonous if the reference is sustained (e.g., long held note).")]
    public float maxAllowedMonotonyDuration = 10f;

    [Tooltip("Reference track activity (avg Hz change) below this value will grant 'maxAllowedMonotonyDuration'.")]
    public float referenceActivitySustainThresholdHz = 3f;
    [Tooltip("Reference track activity (avg Hz change) above this value will enforce 'minAllowedMonotonyDuration'.")]
    public float referenceActivityArticulationThresholdHz = 12f;

    private float currentDynamicAllowedMonotonyDuration = 1.0f; // This is your dynamic [x]

    // --- Penalty ---
    public float monotonyPenaltyAmount = 40f; // Score to deduct
    public float monotonyPenaltyCooldownSecs = 0.02f; // Cooldown after a penalty
    private float timeSinceLastMonotonyPenalty = 0f;
    private bool penaltyGrace = true;

    [Header("Misc Settings")]

    public float volumeThreshold = 0.02f;
    public float minFrequency = 80f;
    public float maxFrequency = 1000f;
    public float _localScore = 0f;

    private AudioClip micClip;
    private float[] audioBuffer;
    private float currentPitch = 0f;
    private float currentPitch2 = 0f;

    public float vocalArrow;
    public float vocalArrow2;
    public float vocalArrow3;
    public float vocalArrow4;
    public Slider vocalArrowS;
    public Slider vocalArrowSDBG;
    public Slider vocalArrowS2DBG;
    public Slider vocalArrowS3DBG;
    public Slider vocalArrowS4DBG;
    private ParticleSystem vocalArrowP;
    public float score = 0f;
    public int placement = 0;
    public TextMeshProUGUI scoreDisplay;
    private float scoreIncrement = 0f;
    private string selectedDevice;
    public TextMeshProUGUI tempDebug;
    public TextMeshProUGUI tempDebug2;
    public PlayerPerformance PP;
    public RectTransform songDebugLeniency;
    private int diffIndex = 1;

    private NativeArray<float> nativeAudioBuffer;
    private NativeArray<float> nativeWindowedSamples;
    private NativeArray<float> nativeAutocorrelation;
    private NativeArray<float> nativePitchResult;
    private NativeArray<float> nativeCurrentPitch2Container;

    private JobHandle pitchDetectionJobHandle;
    private BiquadCoefficients currentBiquadCoeffs;
    private Biquad lowShelfFilterInstance;

    private bool _isInitialized = false;
    private bool _isRecording = false;
    private bool _scoreIncrementInitialized = false;


    private void Start()
    {
        if(PlayerPrefs.GetInt("multiplayer") == 0)
        {
            Setup();
        }
    }

    public void Setup()
    {
        if (_isInitialized) return;

        if (enableAdvancedAntiMonotony)
        {
            if (Time.fixedDeltaTime > 0)
            {
                userPitchActivityBufferSize = Mathf.Max(1, Mathf.CeilToInt(userPitchActivityWindowSecs / Time.fixedDeltaTime));
            }
            else
            {
                userPitchActivityBufferSize = 30; // Fallback if fixedDeltaTime is 0 (e.g. editor not playing)
                Debug.LogWarning("[RealTimePitchDetector] Time.fixedDeltaTime is 0. Using default buffer size for user pitch activity.");
            }

            userPitchesForActivity = new Queue<float>(userPitchActivityBufferSize);
            for (int i = 0; i < userPitchActivityBufferSize; ++i)
            {
                userPitchesForActivity.Enqueue(0f); // Pre-fill with silence
            }
            currentDynamicAllowedMonotonyDuration = maxAllowedMonotonyDuration; // Start more lenient
            timeSinceLastMonotonyPenalty = monotonyPenaltyCooldownSecs; // Allow penalty from start
        }

        if (analysisWindowSize <= 0 || !Mathf.IsPowerOfTwo(analysisWindowSize))
        {
            Debug.LogWarning("[RealTimePitchDetector] analysisWindowSize should ideally be a power of two. Setting to 1024.");
            analysisWindowSize = 1024;
        }

        if (vocalArrowS != null && vocalArrowS.transform.childCount > 1 &&
            vocalArrowS.transform.GetChild(1).childCount > 0 &&
            vocalArrowS.transform.GetChild(1).GetChild(0).childCount > 0)
        {
            vocalArrowP = vocalArrowS.transform.GetChild(1).GetChild(0).GetChild(0).GetComponent<ParticleSystem>();
            if (vocalArrowP == null) Debug.LogWarning("[RealTimePitchDetector] ParticleSystem not found at expected path in vocalArrowS.");
        }
        else if (vocalArrowS != null)
        { // Only log if vocalArrowS itself is assigned
            Debug.LogWarning("[RealTimePitchDetector] vocalArrowS hierarchy not as expected for ParticleSystem. Skipping ParticleSystem setup.");
        }


        if (PP == null) PP = GetComponent<PlayerPerformance>();
        if (PP == null && debugMode) Debug.LogWarning("[RealTimePitchDetector] PlayerPerformance component not found (PP is null).");

        lowShelfFilterInstance = new Biquad();
        _previousLowShelfGainDB = lowShelfGainDB + 1f;
        UpdateBiquadCoefficients();

        nativeAudioBuffer = new NativeArray<float>(analysisWindowSize, Allocator.Persistent);
        nativeWindowedSamples = new NativeArray<float>(analysisWindowSize, Allocator.Persistent);
        nativeAutocorrelation = new NativeArray<float>(analysisWindowSize, Allocator.Persistent);
        nativePitchResult = new NativeArray<float>(1, Allocator.Persistent);
        nativeCurrentPitch2Container = new NativeArray<float>(1, Allocator.Persistent);
        nativeCurrentPitch2Container[0] = 0f;

        audioBuffer = new float[analysisWindowSize];

        score = 0f; // Reset score
        if (scoreDisplay != null)
        {
            scoreDisplay.text = "0"; // Initialize display
            Debug.Log($"[RealTimePitchDetector] Initialize: Score display reset to '0'. Current score: {score}");
        }
        else if (debugMode)
        {
            Debug.LogWarning("[RealTimePitchDetector] Initialize: scoreDisplay (TextMeshProUGUI) is NOT assigned. Cannot display score.");
        }

        _isInitialized = true;
        if(PlayerPrefs.GetInt("multiplayer") == 0)
        {
            ActivateAndStartMicrophone();
            Debug.Log("Start function called");
        }
        else
        {
            Debug.Log("[RealTimePitchDetector] Setup complete. Waiting for activation signal to start microphone.");
        }
    }

    public void ActivateAndStartMicrophone()
    {
        // Check if we're already recording to prevent starting twice.
        if (_isRecording)
        {
            Debug.LogWarning("[RealTimePitchDetector] ActivateAndStartMicrophone called, but already recording.");
            return;
        }

        Debug.Log("[RealTimePitchDetector] Activation signal received. Starting microphone and scoring setup...");

        vocalArrowSDBG = GameObject.Find("DebugPanel").transform.GetChild(0).GetChild(0).GetComponent<Slider>();
        vocalArrowS2DBG = GameObject.Find("DebugPanel").transform.GetChild(0).GetChild(1).GetComponent<Slider>();
        vocalArrowS3DBG = GameObject.Find("DebugPanel").transform.GetChild(0).GetChild(2).GetComponent<Slider>();
        vocalArrowS4DBG = GameObject.Find("DebugPanel").transform.GetChild(0).GetChild(3).GetComponent<Slider>();
        songDebugLeniency = GameObject.Find("DebugPanel").transform.GetChild(0).GetChild(4).GetChild(0).GetChild(0).GetComponent<RectTransform>();

        // Now that we are activated, we can start waiting for the score value and start the mic.
        StartCoroutine(WaitForScoreIncrement());
        StartCoroutine(StartRecordingCoroutine());
    }

    private float CalculatePitchActivity(IEnumerable<float> pitchHistory, float pitchThreshold = 1.0f)
    {
        if (pitchHistory == null) return 0f;

        List<float> validPitches = new List<float>();
        foreach (float p in pitchHistory)
        {
            if (p > pitchThreshold) // Only consider actual sung pitches
            {
                validPitches.Add(p);
            }
        }

        if (validPitches.Count < 2) return 0f; // Not enough data for comparison

        float sumOfAbsoluteDifferences = 0f;
        for (int i = 0; i < validPitches.Count - 1; i++)
        {
            sumOfAbsoluteDifferences += Mathf.Abs(validPitches[i + 1] - validPitches[i]);
        }

        return sumOfAbsoluteDifferences / (validPitches.Count - 1);
    }

    IEnumerator WaitForScoreIncrement()
    {
        if (AudioClipPitchProcessor.Instance == null)
        {
            Debug.LogError("[RealTimePitchDetector] Cannot wait for score increment: AudioClipPitchProcessor.Instance is null.");
            _scoreIncrementInitialized = true; // Mark as "done" to prevent infinite loops if AudioClipPitchProcessor.Instance is never set
            yield break;
        }

        Debug.Log("[RealTimePitchDetector] Waiting for AudioClipPitchProcessor.Instance to calculate scoreIncrement...");
        // Wait until AudioClipPitchProcessor.Instance.scoreIncrement is greater than a very small positive number (to avoid float precision issues with exactly 0)
        // And also add a timeout to prevent an infinite loop if AudioClipPitchProcessor.Instance never sets it.
        float timeout = 30f; // 30 seconds timeout
        float elapsedTime = 0f;

        while (AudioClipPitchProcessor.Instance.scoreIncrement <= 0.0001f && elapsedTime < timeout)
        {
            // Check if the AudioClipPitchProcessor.Instance component itself might have been destroyed or become inactive
            if (AudioClipPitchProcessor.Instance == null || !AudioClipPitchProcessor.Instance.gameObject.activeInHierarchy)
            {
                Debug.LogError("[RealTimePitchDetector] AudioClipPitchProcessor.Instance component became null or inactive while waiting for scoreIncrement.");
                _scoreIncrementInitialized = true;
                yield break;
            }
            yield return null; // Wait for the next frame
            elapsedTime += Time.deltaTime;
        }

        if (AudioClipPitchProcessor.Instance.scoreIncrement > 0.0001f)
        {
            scoreIncrement = AudioClipPitchProcessor.Instance.scoreIncrement;
            Debug.Log($"[RealTimePitchDetector] scoreIncrement acquired from AudioClipPitchProcessor.Instance: {scoreIncrement}");
        }
        else
        {
            scoreIncrement = 0f; // Default if timeout or still zero
            Debug.LogError($"[RealTimePitchDetector] Timed out or AudioClipPitchProcessor.Instance.scoreIncrement is still 0 after waiting. scoreIncrement set to 0. Check AudioClipPitchProcessor. ({AudioClipPitchProcessor.Instance.scoreIncrement})");
        }
        _scoreIncrementInitialized = true;

        if (AudioClipPitchProcessor.Instance.overallSongActivity < 16f || diffIndex == 0)
        {
            enableAdvancedAntiMonotony = false;
        }
        if (diffIndex == 2)
        {
            gracePeriod = 0.10f;
        }
    }

    IEnumerator StartRecordingCoroutine()
    {

        Debug.Log("Recording coroutine called");
        if (PlayerPrefs.GetInt("multiplayer") == 0)
        {
            diffIndex = PlayerPrefs.GetInt(gameObject.name + "Difficulty");
        }
        else
        {
            diffIndex = PlayerPrefs.GetInt("Player1Difficulty");
        }

        if (Microphone.devices.Length == 0)
        {
            Debug.LogError("[RealTimePitchDetector] No microphone detected!");
            if (tempDebug != null) tempDebug.text = "No Mic!";
            this.enabled = false;
            yield break;
        }

        string micNameToUse = null;
        if (this.device >= 0 && this.device < Microphone.devices.Length)
        {
            micNameToUse = Microphone.devices[this.device];
            Debug.Log($"[RealTimePitchDetector] Using microphone from 'device' field (index: {this.device}): '{micNameToUse}'");
        }
        else
        {
            if (!string.IsNullOrEmpty(selectedDeviceNameOverride))
            {
                if (System.Linq.Enumerable.Contains(Microphone.devices, selectedDeviceNameOverride))
                {
                    micNameToUse = selectedDeviceNameOverride;
                    Debug.Log($"[RealTimePitchDetector] Using 'selectedDeviceNameOverride': '{micNameToUse}'");
                }
                else Debug.LogWarning($"[RealTimePitchDetector] 'selectedDeviceNameOverride' ('{selectedDeviceNameOverride}') not found. Will try PlayerPrefs or default.");
            }

            if (string.IsNullOrEmpty(micNameToUse))
            {

                string playerPrefsKeyForIndex;
                if (PlayerPrefs.GetInt("multiplayer") == 0)
                {
                    playerPrefsKeyForIndex = gameObject.name + "Mic";
                }
                else
                {
                    playerPrefsKeyForIndex = "Player1Mic";
                }
                Debug.Log(playerPrefsKeyForIndex);
                if (PlayerPrefs.HasKey(playerPrefsKeyForIndex))
                {
                    int prefsDeviceIndex = PlayerPrefs.GetInt(playerPrefsKeyForIndex);
                    if (prefsDeviceIndex >= 0 && prefsDeviceIndex < Microphone.devices.Length)
                    {
                        micNameToUse = Microphone.devices[prefsDeviceIndex];
                        Debug.Log($"[RealTimePitchDetector] Using PlayerPrefs index from key '{playerPrefsKeyForIndex}' (index: {prefsDeviceIndex}): '{micNameToUse}'");
                    }
                    else Debug.LogWarning($"[RealTimePitchDetector] PlayerPrefs index from key '{playerPrefsKeyForIndex}' (value: {prefsDeviceIndex}) is out of bounds.");
                }
            }
        }
        if (string.IsNullOrEmpty(micNameToUse))
        {
            micNameToUse = Microphone.devices[0];
            Debug.Log($"[RealTimePitchDetector] Falling back to default device [0]: '{micNameToUse}'");
        }
        selectedDevice = micNameToUse;

        micClip = Microphone.Start(selectedDevice, true, bufferLengthSeconds, sampleRate);
        _isRecording = true;

        int delay = 0;
        while (!(Microphone.GetPosition(selectedDevice) > 0) && delay < 100)
        {
            yield return new WaitForSeconds(0.01f);
            delay++;
        }

        if (micClip == null || !(Microphone.GetPosition(selectedDevice) > 0))
        {
            Debug.LogError("[RealTimePitchDetector] Microphone did not start recording for device: " + selectedDevice);
            if (tempDebug != null) tempDebug.text = "Mic Start Fail!";
            _isRecording = false;
            this.enabled = false;
            yield break;
        }

        Debug.Log("[RealTimePitchDetector] Microphone started: " + selectedDevice + " | Sample Rate: " + sampleRate + " | Window: " + analysisWindowSize);

        // Scoring related setup post-mic start
        if (AudioClipPitchProcessor.Instance != null)
        {
            scoreIncrement = AudioClipPitchProcessor.Instance.scoreIncrement;
            Debug.Log($"[RealTimePitchDetector] StartRecordingCoroutine: scoreIncrement set to: {scoreIncrement} (from AudioClipPitchProcessor.Instance.scoreIncrement: {AudioClipPitchProcessor.Instance.scoreIncrement})");
        }
        else
        {
            scoreIncrement = 0f;
            Debug.LogWarning("[RealTimePitchDetector] StartRecordingCoroutine: AudioClipPitchProcessor.Instance is null. scoreIncrement set to 0. Scoring will effectively be disabled.");
        }
        if (scoreDisplay != null)
        { // Update display in case score was loaded or changed
            scoreDisplay.text = Mathf.RoundToInt(score).ToString("#,#");
            Debug.Log($"[RealTimePitchDetector] StartRecordingCoroutine: Score display updated to '{scoreDisplay.text}'. Current score: {score}");
        }
    }

    void UpdateBiquadCoefficients()
    {
        bool needsUpdate = !Mathf.Approximately(_previousLowShelfGainDB, lowShelfGainDB) ||
                           !Mathf.Approximately(_previousLowShelfCutoff, lowShelfCutoff) ||
                           !Mathf.Approximately(_previousLowShelfQ, lowShelfQ) ||
                           (lowShelfFilterInstance != null && !Mathf.Approximately(lowShelfFilterInstance.LastSampleRate, sampleRate));

        if (needsUpdate && lowShelfFilterInstance != null)
        {
            lowShelfFilterInstance.SetLowShelf(sampleRate, lowShelfCutoff, lowShelfGainDB, lowShelfQ);
            currentBiquadCoeffs = lowShelfFilterInstance.GetCoefficients();
            _previousLowShelfGainDB = lowShelfGainDB;
            _previousLowShelfCutoff = lowShelfCutoff;
            _previousLowShelfQ = lowShelfQ;
        }
    }

    void Update()
    {
        if (!_isInitialized || !_isRecording) return;
        if (!Mathf.Approximately(_previousLowShelfCutoff, lowShelfCutoff) ||
            !Mathf.Approximately(_previousLowShelfQ, lowShelfQ))
        {
            UpdateBiquadCoefficients();
        }
    }

    private void FixedUpdate()
    {
        if (!_isInitialized || !_isRecording || micClip == null || !nativeAudioBuffer.IsCreated || !_scoreIncrementInitialized)
        {
            return;
        }

        pitchDetectionJobHandle.Complete();



        float pitchFromJob = nativePitchResult[0];
        currentPitch2 = nativeCurrentPitch2Container[0];

        if (pitchFromJob > 0.0f)
        {
            currentPitch = Mathf.Lerp(currentPitch, pitchFromJob, 1f - smoothing);
        }
        else
        {
            currentPitch = Mathf.Lerp(currentPitch, 0f, 1f - smoothing);
        }

        int micPosition = Microphone.GetPosition(selectedDevice);
        int startPos = micPosition - analysisWindowSize;
        if (startPos < 0) startPos = 0;
        micClip.GetData(audioBuffer, startPos);
        nativeAudioBuffer.CopyFrom(audioBuffer);

        float oldGainForThisFrameCheck = lowShelfGainDB;
        if (AudioClipPitchProcessor.Instance != null)
        {
            if (currentPitch * 2 < 500f)
            {
                if (AudioClipPitchProcessor.Instance.currentPitch > 325f) lowShelfGainDB = 2f;
                else if (AudioClipPitchProcessor.Instance.currentPitch > 190f) lowShelfGainDB = 2f;
                else if (AudioClipPitchProcessor.Instance.currentPitch > 135f) lowShelfGainDB = fixedLowShelfGainDB;
                else lowShelfGainDB = fixedLowShelfGainDB;
            }
            if (debugMode && tempDebug2 != null)
            {
                //tempDebug2.text = $"EQ Gain: {lowShelfGainDB:F0}dB, AudioClipPitchProcessor.Instance.CP: {AudioClipPitchProcessor.Instance.currentPitch:F0}, Det.CP: {currentPitch:F0}";
            }
        }
        else
        {
            //if (debugMode && tempDebug2 != null) tempDebug2.text = "AudioClipPitchProcessor.Instance is null, EQ gain logic skipped.";
        }
        if (!Mathf.Approximately(oldGainForThisFrameCheck, lowShelfGainDB)) UpdateBiquadCoefficients();

        var pitchJob = new PitchDetectionJob
        {
            inputSamples = nativeAudioBuffer,
            windowedSamplesOut = nativeWindowedSamples,
            autocorrelationOut = nativeAutocorrelation,
            sampleRate = this.sampleRate,
            volumeThreshold = this.volumeThreshold,
            minFrequency = this.minFrequency,
            maxFrequency = this.maxFrequency,
            coeffs = currentBiquadCoeffs,
            currentPitch2InOut = nativeCurrentPitch2Container,
            detectedPitchOutput = nativePitchResult
        };
        pitchDetectionJobHandle = pitchJob.Schedule(pitchDetectionJobHandle);

        // --- Scoring Logic ---



        if (AudioClipPitchProcessor.Instance == null)
        {
            if (debugMode) Debug.LogWarning("[RealTimePitchDetector] FixedUpdate: AudioClipPitchProcessor.Instance is null. Skipping scoring & vocal arrow logic.");
            return;
        }

        if (currentPitch * 2 < 500f)
        {
            float baseDetectedPitch = currentPitch;
            if (AudioClipPitchProcessor.Instance.currentPitch > 135f && AudioClipPitchProcessor.Instance.currentPitch < 190f) baseDetectedPitch *= 1.05f;
            else if (AudioClipPitchProcessor.Instance.currentPitch <= 135f) baseDetectedPitch *= 1.25f;
            vocalArrow = baseDetectedPitch;
        }
        else
        {
            vocalArrow = currentPitch;
        }
        vocalArrow2 = vocalArrow * 2f;
        vocalArrow3 = vocalArrow / 2f;
        vocalArrow4 = vocalArrow * 4f;

        currentLeniencyCoefficient = leniencyCoefficient;

        // Check for grace period
        if (AudioClipPitchProcessor.Instance.currentPitch != 0f)
        {
            gracePeriodElapsed += Time.fixedDeltaTime; // Use fixedDeltaTime in FixedUpdate
            if (gracePeriodElapsed < gracePeriod)
            {
                // Apply the grace period bonus
                currentLeniencyCoefficient *= 3.5f;
            }
        }
        else
        {
            gracePeriodElapsed = 0f;
        }

        // Now, apply the difficulty modifier to the result.
        if (diffIndex == 0) // easy
        {
            currentLeniencyCoefficient *= 1.67f;
        }
        else if (diffIndex == 2)
        {
            currentLeniencyCoefficient *= 0.8f;
        }
        //Debug.Log("[GRACE] " + currentLeniencyCoefficient);

        if (diffIndex == 0) // easy
        {
            currentLeniencyCoefficient *= 1.67f;
        }
        else if (diffIndex == 2)
        {
            currentLeniencyCoefficient *= 0.8f;
        }

        bool canAttemptScore = vocalArrowS != null && vocalArrow != 30f && AudioClipPitchProcessor.Instance.currentPitch != 0f;
        if (debugMode && !canAttemptScore)
        {
            string reason = "Scoring check failed:";
            if (vocalArrowS == null) reason += " vocalArrowS is null;";
            if (vocalArrow == 30f) reason += " vocalArrow is 30f;";
            if (AudioClipPitchProcessor.Instance.currentPitch == 0f) reason += " AudioClipPitchProcessor.Instance.currentPitch is 0f;";
            //Debug.Log(reason);
        }

        bool ppJudge = (PP != null && PP.judgeInt);
        bool songNotOver = (LyricsHandler.Instance != null && !LyricsHandler.Instance.songOver);

        /*
        if (AudioClipPitchProcessor.Instance.currentPitch == 0f && currentPitch != 0f && ppJudge && songNotOver)
        {
            mashGracePeriodElapsed += Time.deltaTime;

            if(mashGracePeriodElapsed > mashGracePeriod)
            {
                score -= scoreIncrement * 5f;
                string scoreText = Mathf.RoundToInt(score).ToString("#,#");
                scoreDisplay.text = scoreText;
                if (debugMode) Debug.Log("[RealTimePitchDetector] MASHING DETECTED.");
            }
        }
        else
        {
            mashGracePeriodElapsed = 0f;
        }
        */

        if (enableAdvancedAntiMonotony && AudioClipPitchProcessor.Instance != null && AudioClipPitchProcessor.Instance.pitchOverTime != null && AudioClipPitchProcessor.Instance.pitchOverTime.Count > 0 &&
        AudioClipPitchProcessor.Instance.audioSource != null && AudioClipPitchProcessor.Instance.audioSource.isPlaying && _scoreIncrementInitialized && LyricsHandler.Instance != null && !LyricsHandler.Instance.songOver)
        {
            timeSinceLastMonotonyPenalty += Time.fixedDeltaTime;
            float referenceActivity = 0f; // <--- DECLARE referenceActivity HERE with a default value

            // --- 1. Calculate User's Current Pitch Activity ---
            if (userPitchesForActivity.Count >= userPitchActivityBufferSize)
            {
                userPitchesForActivity.Dequeue();
            }
            userPitchesForActivity.Enqueue(currentPitch > (minFrequency / 2f) ? currentPitch : 0f);
            currentUserPitchActivity = CalculatePitchActivity(userPitchesForActivity, minFrequency / 2f);

            if (debugMode && tempDebug != null)
            {

            }

            // --- 2. Calculate Dynamic Allowed Monotony Duration ([x]) based on Reference Track ---
            if (AudioClipPitchProcessor.Instance.audioClip != null && AudioClipPitchProcessor.Instance.audioClip.length > 0)
            {
                float currentTimeInSong = AudioClipPitchProcessor.Instance.audioSource.time + AudioClipPitchProcessor.Instance.AUDIO_LATENCY_COMPENSATION;
                float songLength = AudioClipPitchProcessor.Instance.audioClip.length;
                int totalRefFrames = AudioClipPitchProcessor.Instance.pitchOverTime.Count;

                int refFramesInWindow = Mathf.Max(1, Mathf.FloorToInt((referenceAnalysisWindowSecs / songLength) * totalRefFrames));
                int currentRefIdx = Mathf.Clamp(Mathf.FloorToInt((currentTimeInSong / songLength) * totalRefFrames), 0, totalRefFrames - 1);

                int startRefIdx = Mathf.Clamp(currentRefIdx, 0, totalRefFrames - refFramesInWindow);
                int endRefIdx = Mathf.Clamp(startRefIdx + refFramesInWindow - 1, startRefIdx, totalRefFrames - 1);

                List<float> referencePitchSegment = new List<float>();
                if (startRefIdx <= endRefIdx && endRefIdx < totalRefFrames)
                {
                    for (int i = startRefIdx; i <= endRefIdx; i++)
                    {
                        referencePitchSegment.Add(AudioClipPitchProcessor.Instance.pitchOverTime[i]);
                    }
                }

                referenceActivity = CalculatePitchActivity(referencePitchSegment, 32f); // <--- ASSIGN to the already declared variable

                if (debugMode && tempDebug2 != null)
                {
                    tempDebug2.text = $"Ref Act: {referenceActivity:F1}Hz | DynAllow: {currentDynamicAllowedMonotonyDuration:F2}s";
                }

                if (referenceActivity <= referenceActivitySustainThresholdHz)
                {
                    currentDynamicAllowedMonotonyDuration = maxAllowedMonotonyDuration;
                }
                else if (referenceActivity >= referenceActivityArticulationThresholdHz)
                {
                    currentDynamicAllowedMonotonyDuration = minAllowedMonotonyDuration;
                }
                else
                {
                    float t = (referenceActivity - referenceActivitySustainThresholdHz) /
                              (referenceActivityArticulationThresholdHz - referenceActivitySustainThresholdHz);
                    currentDynamicAllowedMonotonyDuration = Mathf.Lerp(maxAllowedMonotonyDuration, minAllowedMonotonyDuration, t);
                }
            }
            // Else, referenceActivity remains its default (0f) if AudioClipPitchProcessor.Instance.audioClip condition isn't met

            // --- 3. Check for User Monotony & Penalize ---
            bool userIsSingingAudibly = currentPitch > (minFrequency / 2f);
            string dbgAdd = currentUserPitchActivity.ToString();

            if (userIsSingingAudibly && currentUserPitchActivity < userMonotonyThresholdHz && ppJudge)
            {
                //tempDebug.text = "User Activity: MONOTONOUS (" + dbgAdd + ")";
                timeUserHasBeenMonotonous += Time.fixedDeltaTime;
            }
            else
            {
                //tempDebug.text = "User Activity: Ok (" + dbgAdd + ")";
                penaltyGrace = true;
                timeUserHasBeenMonotonous = 0f;
            }

            if (timeUserHasBeenMonotonous > currentDynamicAllowedMonotonyDuration &&
                timeSinceLastMonotonyPenalty >= monotonyPenaltyCooldownSecs)
            {
                if (PlayerPrefs.GetInt("multiplayer") == 0)
                {
                    score -= (scoreIncrement * monotonyPenaltyAmount);
                }
                else
                {
                    _localScore -= (scoreIncrement * monotonyPenaltyAmount);
                    if (PlayerData.LocalPlayerInstance != null)
                    {
                        PlayerData.LocalPlayerInstance.RequestUpdateCurrentScore_ServerRpc(Mathf.RoundToInt(_localScore));
                    }
                }
                if (scoreDisplay != null)
                {
                    scoreDisplay.text = Mathf.RoundToInt(score).ToString("#,#");
                }

                if (debugMode)
                {
                    // Now 'referenceActivity' is in scope here
                    Debug.Log($"[AntiMonotony PENALTY] User Monotonous for: {timeUserHasBeenMonotonous:F2}s " +
                              $"(Allowed: {currentDynamicAllowedMonotonyDuration:F2}s). " +
                              $"User Activity: {currentUserPitchActivity:F1}Hz. Ref Activity: {referenceActivity:F1}Hz. Score: {score}");
                }

                timeUserHasBeenMonotonous = 0f;
                timeSinceLastMonotonyPenalty = 0f;
            }
        }
        else if (enableAdvancedAntiMonotony)
        {
            timeUserHasBeenMonotonous = 0f;
        }

        if (canAttemptScore)
        {
            float[] vocalValues = { vocalArrow, vocalArrow2, vocalArrow3, vocalArrow4 };
            float currentAppPitch = AudioClipPitchProcessor.Instance.currentPitch;
            float bestDifference = float.MaxValue;
            float bestValue = 0f;
            foreach (float val in vocalValues)
            {
                float difference = Mathf.Abs(currentAppPitch - val);
                if (difference < bestDifference)
                {
                    bestDifference = difference;
                    bestValue = val;
                }
            }

            if (vocalArrowP != null && !vocalArrowP.isPlaying) vocalArrowP.Play();
            vocalArrowS.value = bestValue;

            if (debugMode)
            {
                if (vocalArrowSDBG != null) vocalArrowSDBG.value = vocalArrow;
                if (vocalArrowS2DBG != null) vocalArrowS2DBG.value = vocalArrow2;
                if (vocalArrowS3DBG != null) vocalArrowS3DBG.value = vocalArrow3;
                if (vocalArrowS4DBG != null) vocalArrowS4DBG.value = vocalArrow4;
            }

            float valuingCoefficient = GetOctaveLeniency(AudioClipPitchProcessor.Instance.currentPitch);
            bool scoredThisFrame = false;
            float leniencyThreshold;



            if (currentPitch * 2 < 450f)
            {
                leniencyThreshold = (AudioClipPitchProcessor.Instance.currentPitch < 170f) ? (12f * valuingCoefficient * currentLeniencyCoefficient) : (15f * valuingCoefficient * currentLeniencyCoefficient);
            }
            else
            {
                leniencyThreshold = 17f * valuingCoefficient * currentLeniencyCoefficient;
            }
            //if (debugMode && tempDebug != null) tempDebug.text = $"Leniency: {leniencyThreshold:F1}, BestDiff: {bestDifference:F1}";


            if (debugMode && (!ppJudge || !songNotOver))
            {
                string reason = "Scoring condition fail:";
                if (!ppJudge) reason += $" PP null ({PP == null}) or judgeInt false ({PP?.judgeInt});";
                if (!songNotOver) reason += $" LH null ({LyricsHandler.Instance == null}) or songOver true ({LyricsHandler.Instance?.songOver});";
                //Debug.Log(reason);
            }

            if (bestDifference < leniencyThreshold && ppJudge && songNotOver)
            {
                scoredThisFrame = true;
                if (debugMode && songDebugLeniency != null) songDebugLeniency.sizeDelta = new Vector2(leniencyThreshold * 7f, songDebugLeniency.sizeDelta.y);
            }



            if (scoredThisFrame)
            {
                if (PlayerPrefs.GetInt("multiplayer") == 0)
                {
                    score += scoreIncrement;
                }
                else
                {
                    _localScore += scoreIncrement;
                }
                if (scoreDisplay != null)
                {
                    string scoreText;
                    if (PlayerPrefs.GetInt("multiplayer") == 0)
                    {
                        scoreText = Mathf.RoundToInt(score).ToString("#,#");
                    }
                    else
                    {
                        scoreText = Mathf.RoundToInt(_localScore).ToString("#,#");
                        if (PlayerData.LocalPlayerInstance != null)
                        {
                            PlayerData.LocalPlayerInstance.RequestUpdateCurrentScore_ServerRpc(Mathf.RoundToInt(_localScore));
                        }
                    }
                    scoreDisplay.text = scoreText;
                    //if (debugMode) Debug.Log($"[RealTimePitchDetector] SCORED! Increment: {scoreIncrement}, New Score: {score}, Displayed: '{scoreText}'");
                }
                else if (debugMode)
                {
                    Debug.LogWarning($"[RealTimePitchDetector] SCORED! But scoreDisplay is null. Increment: {scoreIncrement}, New Score: {score}");
                }
            }
            else if (debugMode)
            {
                //Debug.Log($"[RealTimePitchDetector] NO SCORE. BestDiff: {bestDifference:F1}, Threshold: {leniencyThreshold:F1}, PPJudge: {ppJudge}, SongNotOver: {songNotOver}, ScoreInc: {scoreIncrement}");
            }
        }
        else
        {
            if (vocalArrowP != null && vocalArrowP.isPlaying) vocalArrowP.Stop();
        }

        if (LyricsHandler.Instance != null && LyricsHandler.Instance.songOver)
        {
            // --- THIS IS THE CHANGE ---
            if (PlayerPrefs.GetInt("multiplayer") == 0)
            {
                // This offline logic is fine, it can stay.
                PlayerPrefs.SetInt(gameObject.name + "Score", Mathf.RoundToInt(score));
                PlayerPrefs.SetInt(gameObject.name + "Placement", placement);
            }
            else
            {
                // In multiplayer, we no longer need to do anything here.
                // The final CurrentGameScore has already been synced to the server.
                // The server will calculate and sync the final placement.
                // We can disable this component now that the song is over for this player.
                this.enabled = false;
            }
        }
    }

    float GetOctaveLeniency(float frequency)
    {
        if (frequency <= 0) return 1f;
        if (frequency < 150f) return 1.0f;
        if (frequency < 300f) return 1.2f;
        return 1.5f;
    }

    void OnDisable()
    {
        if (!_isInitialized) return;
        pitchDetectionJobHandle.Complete();
        if (nativeAudioBuffer.IsCreated) nativeAudioBuffer.Dispose();
        if (nativeWindowedSamples.IsCreated) nativeWindowedSamples.Dispose();
        if (nativeAutocorrelation.IsCreated) nativeAutocorrelation.Dispose();
        if (nativePitchResult.IsCreated) nativePitchResult.Dispose();
        if (nativeCurrentPitch2Container.IsCreated) nativeCurrentPitch2Container.Dispose();

        if (_isRecording && !string.IsNullOrEmpty(selectedDevice))
        {
            Microphone.End(selectedDevice);
            Debug.Log("[RealTimePitchDetector] Microphone stopped: " + selectedDevice);
        }
        micClip = null;
        _isRecording = false;
        _isInitialized = false;
    }
}

[System.Serializable] // Can be useful for debugging if you ever need to see it
public struct BiquadCoefficients
{
    public float b0, b1, b2, a1, a2;
}

public class Biquad
{
    public float Coeff_b0 { get; private set; }
    public float Coeff_b1 { get; private set; }
    public float Coeff_b2 { get; private set; }
    public float Coeff_a1 { get; private set; } // Note: Job uses these as `y[n] = ... - a1*y[n-1] ...`
    public float Coeff_a2 { get; private set; }

    // Store last parameters to avoid redundant calculations if settings haven't changed.
    public float LastSampleRate { get; private set; } = -1;
    public float LastCutoff { get; private set; } = -1;
    public float LastGainDB { get; private set; } = -1000f; // Initialize to a value that forces first calc
    public float LastQ { get; private set; } = -1;

    // Internal state for main-thread processing (if ever used)
    private float x1_main, x2_main, y1_main, y2_main;


    public void SetLowShelf(float sampleRate, float cutoff, float gainDB, float Q)
    {
        if (Mathf.Approximately(LastSampleRate, sampleRate) &&
            Mathf.Approximately(LastCutoff, cutoff) &&
            Mathf.Approximately(LastGainDB, gainDB) &&
            Mathf.Approximately(LastQ, Q))
        {
            return; // Parameters haven't changed significantly
        }

        LastSampleRate = sampleRate;
        LastCutoff = cutoff;
        LastGainDB = gainDB;
        LastQ = Q;

        float A = Mathf.Pow(10f, gainDB / 40f); // Amplitude
        float w0 = 2f * Mathf.PI * cutoff / sampleRate; // Angular frequency
        float cosw0 = Mathf.Cos(w0);
        float sinw0 = Mathf.Sin(w0);
        // Alpha calculation based on Q for shelf filters can vary, common one for RBJ is sin(w0)/ (2*Q)
        // For shelf, sometimes alpha is related to shelf slope S.
        // Using common RBJ formulation for shelf alpha:
        float alpha = sinw0 / (2f * Q); // Or use: float alpha = sinw0/2f * Mathf.Sqrt( (A + 1f/A)*(1f/S - 1f) + 2f ); where S is shelf slope. Sticking to simple Q.


        float commonTerm1 = (A + 1f);
        float commonTerm2 = (A - 1f);

        // Coefficients for Low Shelf Filter (RBJ Cookbook)
        // Note: a0 is the normalization factor applied to all other coefficients.
        float a0_norm = commonTerm1 + commonTerm2 * cosw0 + 2f * Mathf.Sqrt(A) * alpha;
        if (Mathf.Abs(a0_norm) < 1e-9f) a0_norm = 1e-9f; // Avoid division by zero

        Coeff_b0 = A * (commonTerm1 - commonTerm2 * cosw0 + 2f * Mathf.Sqrt(A) * alpha) / a0_norm;
        Coeff_b1 = 2f * A * (commonTerm2 - commonTerm1 * cosw0) / a0_norm;
        Coeff_b2 = A * (commonTerm1 - commonTerm2 * cosw0 - 2f * Mathf.Sqrt(A) * alpha) / a0_norm;
        // a coefficients are typically negated in the difference equation y[n] = b0x[n]... -a1y[n-1]...
        // So we store them in the form ready for that subtraction.
        Coeff_a1 = -2f * (commonTerm2 + commonTerm1 * cosw0) / a0_norm; // This should be positive if used as y = ... + a1*y1 ...
                                                                        // The job expects them such that it does -a1*y1. My original biquad class had a1/=a0; which means a1 was positive.
                                                                        // RBJ has a1 =  -2 * ( (A-1) + (A+1)*cos(w0) )
                                                                        //           a2 =       ( (A+1) + (A-1)*cos(w0) - 2*sqrt(A)*alpha )
                                                                        // So job should use output = ... - (coeffs.a1 * y1_filter) ...
                                                                        // Let's re-verify the signs to match the job's usage.
                                                                        // Job: `output = ... - coeffs.a1 * y1 - coeffs.a2 * y2;`
                                                                        // So coeffs.a1 and coeffs.a2 should be the -(actual_a1/a0_norm) and -(actual_a2/a0_norm) from standard form.
                                                                        // Or, more directly, use the RBJ formulas for a1, a2 and then the job uses them with subtraction.
        float raw_a1 = -2f * ((A - 1f) + (A + 1f) * cosw0);
        float raw_a2 = (A + 1f) + (A - 1f) * cosw0 - 2f * Mathf.Sqrt(A) * alpha;

        Coeff_a1 = raw_a1 / a0_norm; // This will be used as output = ... - Coeff_a1 * y1 ...
        Coeff_a2 = raw_a2 / a0_norm; // This will be used as output = ... - Coeff_a2 * y2 ...


        // Reset main thread state if parameters change
        x1_main = x2_main = y1_main = y2_main = 0f;
    }

    public BiquadCoefficients GetCoefficients()
    {
        return new BiquadCoefficients
        {
            b0 = this.Coeff_b0,
            b1 = this.Coeff_b1,
            b2 = this.Coeff_b2,
            a1 = this.Coeff_a1,
            a2 = this.Coeff_a2
        };
    }

    // Process method for main thread usage (if any). Job uses its own internal version.
    public float ProcessMainThread(float input)
    {
        // y[n] = (b0/a0)*x[n] + (b1/a0)*x[n-1] + (b2/a0)*x[n-2]
        //               - (a1/a0)*y[n-1] - (a2/a0)*y[n-2]
        // Our stored Coeff_a1 and Coeff_a2 are already raw_a1/a0_norm and raw_a2/a0_norm
        float output = Coeff_b0 * input + Coeff_b1 * x1_main + Coeff_b2 * x2_main
                     - Coeff_a1 * y1_main - Coeff_a2 * y2_main;
        x2_main = x1_main;
        x1_main = input;
        y2_main = y1_main;
        y1_main = output;
        return output;
    }
}

[BurstCompile]
public struct PitchDetectionJob : IJob
{
    [ReadOnly] public NativeArray<float> inputSamples;
    public NativeArray<float> windowedSamplesOut; // Working buffer for windowed & filtered samples
    public NativeArray<float> autocorrelationOut; // Working buffer for autocorrelation

    public int sampleRate;
    public float volumeThreshold;
    public float minFrequency;
    public float maxFrequency;
    public BiquadCoefficients coeffs;

    public NativeArray<float> currentPitch2InOut; // Carries state for temporal smoothing
    public NativeArray<float> detectedPitchOutput;

    // Biquad filter state (local to this job execution)
    private float filter_x1, filter_x2, filter_y1, filter_y2;

    private float ProcessBiquad(float input)
    {
        // Standard difference equation: y[n] = b0*x[n] + b1*x[n-1] + b2*x[n-2] - a1*y[n-1] - a2*y[n-2]
        // Coeffs.a1 and Coeffs.a2 are stored as the -(actual_a1/a0) and -(actual_a2/a0) values from some conventions,
        // or directly as (a1/a0) and (a2/a0) from RBJ if the formula is y[n] = ... - a1*y[n-1] ...
        // The Biquad class now stores a1 and a2 from RBJ, so subtraction is correct here.
        float output = coeffs.b0 * input + coeffs.b1 * filter_x1 + coeffs.b2 * filter_x2
                     - coeffs.a1 * filter_y1 - coeffs.a2 * filter_y2;

        filter_x2 = filter_x1;
        filter_x1 = input;
        filter_y2 = filter_y1;
        filter_y1 = output;
        return output;
    }

    public void Execute()
    {
        int size = inputSamples.Length;
        if (size == 0)
        {
            detectedPitchOutput[0] = 0f;
            return;
        }

        // --- 0. Initialize Biquad state for this block ---
        filter_x1 = 0f; filter_x2 = 0f; filter_y1 = 0f; filter_y2 = 0f;

        // --- 1. Compute RMS for volume check ---
        float rmsSum = 0f;
        for (int i = 0; i < size; i++)
        {
            rmsSum += inputSamples[i] * inputSamples[i];
        }
        float rms = Mathf.Sqrt(rmsSum / size);

        if (rms < volumeThreshold)
        {
            detectedPitchOutput[0] = 0f;
            // currentPitch2InOut[0] could decay or hold here if desired
            return;
        }

        // --- 2. Apply Hann Window & Biquad Filter ---
        for (int i = 0; i < size; i++)
        {
            float window = 0.5f * (1f - Mathf.Cos(2f * Mathf.PI * i / (size - 1f)));
            float windowedSample = inputSamples[i] * window;
            windowedSamplesOut[i] = ProcessBiquad(windowedSample);
        }

        // --- 3. Compute Autocorrelation ---
        for (int lag = 0; lag < size; lag++)
        {
            float sum = 0f;
            for (int i = 0; i < size - lag; i++)
            {
                sum += windowedSamplesOut[i] * windowedSamplesOut[i + lag];
            }
            autocorrelationOut[lag] = sum;
        }

        // --- 4. Normalize Autocorrelation ---
        float normFactor = autocorrelationOut[0];
        if (Mathf.Abs(normFactor) > 1e-6f)
        {
            for (int i = 0; i < size; i++)
            {
                autocorrelationOut[i] /= normFactor;
            }
        }
        else
        { // All zero or very small signal after filtering
            detectedPitchOutput[0] = 0f; // No valid correlation
            // currentPitch2InOut[0] could decay or hold
            return;
        }

        // --- 5. Define Lag Bounds ---
        // Ensure lag is at least 1 and within array bounds for peak picking and interpolation
        int minValidLag = Mathf.Max(1, Mathf.FloorToInt((float)sampleRate / maxFrequency));
        int maxValidLag = Mathf.Min(size - 2, Mathf.FloorToInt((float)sampleRate / minFrequency));


        if (minValidLag >= maxValidLag || minValidLag <= 0 || maxValidLag >= size - 1)
        {
            detectedPitchOutput[0] = currentPitch2InOut[0]; // Not enough range or invalid
            return;
        }

        // --- 6. Find Best Lag (Peak Picking) ---
        int bestLag = -1;
        float maxCorrelation = -1f; // Autocorrelation is normalized, so peaks are <= 1
        for (int lag = minValidLag; lag <= maxValidLag; lag++)
        {
            // Check for peak: current point is greater than its immediate neighbors
            if (autocorrelationOut[lag] > autocorrelationOut[lag - 1] &&
                autocorrelationOut[lag] > autocorrelationOut[lag + 1])
            {
                if (autocorrelationOut[lag] > maxCorrelation)
                {
                    maxCorrelation = autocorrelationOut[lag];
                    bestLag = lag;
                }
            }
        }

        if (bestLag == -1)
        { // No peak found in range
            detectedPitchOutput[0] = currentPitch2InOut[0]; // Return previous smoothed pitch
            return;
        }

        // --- 7. Parabolic Interpolation ---
        // bestLag is guaranteed to be > 0 and < size - 1 due to maxValidLag constraint and peak check
        float alpha = autocorrelationOut[bestLag - 1];
        float beta = autocorrelationOut[bestLag];
        float gamma = autocorrelationOut[bestLag + 1];
        float peakOffset = 0.5f * (alpha - gamma) / (alpha - 2f * beta + gamma);
        if (float.IsNaN(peakOffset) || float.IsInfinity(peakOffset))
        { // Denominator was zero or near-zero
            peakOffset = 0f;
        }
        // Clamp peakOffset to avoid jumping too far, e.g. if peak is very flat or noisy
        peakOffset = Mathf.Clamp(peakOffset, -0.5f, 0.5f);


        float refinedLag = bestLag + peakOffset;
        if (refinedLag <= 0f)
        { // Safety check
            detectedPitchOutput[0] = currentPitch2InOut[0];
            return;
        }
        float detectedFundamental = (float)sampleRate / refinedLag;

        // --- 8. Temporal Smoothing (like original currentPitch2) ---
        float pitchChangeThreshold = 10f; // Hz; adjust as needed
        float outlierSmoothingFactor = 0.2f; // How much to lerp if change is large; adjust

        float previousPitch2 = currentPitch2InOut[0];
        if (previousPitch2 > 0f && Mathf.Abs(detectedFundamental - previousPitch2) > pitchChangeThreshold)
        {
            detectedFundamental = Mathf.Lerp(previousPitch2, detectedFundamental, outlierSmoothingFactor);
        }

        currentPitch2InOut[0] = detectedFundamental; // Update state for next job
        detectedPitchOutput[0] = detectedFundamental; // This is the pitch for this frame
    }
}