using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

public class LatencyCalibrator : MonoBehaviour
{
    public enum CalibrationState
    {
        Idle,
        Verification,
        VerificationFailed,
        ToneTest,
        ToneTestComplete,
        TapTest,
        TapTestComplete,
        Complete
    }

    public static LatencyCalibrator Instance { get; private set; }

    // --- Inspector assignments ---
    [HideInInspector] public RealTimePitchDetector pitchDetector; // Auto-created by StartCalibrationWithMic
    public AudioSource toneAudioSource;
    public TMP_Text statusText;
    public TMP_Text tapStatusText;
    public Image flashImage;
    public GameObject calibrationUI;
    public GameObject startButton;
    public GameObject waitButton;
    public GameObject finishButton1;
    public GameObject finishButton2;
    public Button tapApplyButton;
    public GameObject tapRetryButton;
    public GameObject windowsOnlyObject;
    public GameObject firstStep;
    public GameObject secondStep;
    public GameObject thirdStep;

    // --- Events ---
    public event Action<CalibrationState> OnStateChanged;
    public event Action<string> OnStatusMessage;
    public event Action OnTapBeatPlayed;

    // --- Public read-only properties ---
    public CalibrationState State { get; private set; } = CalibrationState.Idle;
    public float VerificationProgress { get; private set; }
    public int ToneTestCompletedBursts { get; private set; }
    public int TapTestCompletedBeats { get; private set; }
    public float MeasuredRoundTrip { get; private set; }
    public float MeasuredOutputLatency { get; private set; }
    public float CalibratedPitchDataPadding { get; private set; }
    public float CalibratedAudioLatencyCompensation { get; private set; }

    // --- Constants ---
    private const float TONE_FREQUENCY = 220f;
    private const float DETECTION_THRESHOLD = 30f;

    // Verification
    private const float VERIFICATION_DURATION = 3f;
    private const float VERIFICATION_PASS_RATE = 0.6f;
    private const float MIC_INIT_WAIT = 0.5f;

    // Tone test
    private const int TONE_BURST_COUNT = 10;
    private const float TONE_BURST_DURATION = 0.3f;
    private const float TONE_GAP_DURATION = 1.5f;
    private const float TONE_DETECTION_TIMEOUT = 1.0f;
    private const int TONE_MIN_VALID = 5;

    // Tap test
    private const int TAP_BEAT_COUNT = 15;
    private const float TAP_BPM = 100f;
    private const int TAP_DISCARD_COUNT = 3;
    private const float TAP_GRACE_WINDOW = 1f;

    // --- Internal state ---
    private bool _advancedMode;
    private bool _windowsObjectShown;
    private int _tapCount;
    private AudioClip sineClip;
    private AudioClip clickClip;
    private Coroutine activeCoroutine;
    private List<float> toneDeltas = new List<float>();
    private List<double> scheduledBeatTimes = new List<double>();
    private List<double> tapTimes = new List<double>();
    private bool toneTestDone;
    private bool tapTestDone;

    private void SetState(CalibrationState newState)
    {
        State = newState;
        UpdateButtons();
        OnStateChanged?.Invoke(newState);
    }

    private void UpdateButtons()
    {
        bool showStart = State == CalibrationState.Idle || State == CalibrationState.VerificationFailed;
        bool showWait = State == CalibrationState.Verification || State == CalibrationState.ToneTest
                        || State == CalibrationState.TapTest;
        bool showFinish = State == CalibrationState.ToneTestComplete || State == CalibrationState.TapTestComplete
                          || State == CalibrationState.Complete;

        if (startButton != null) startButton.SetActive(showStart);
        if (waitButton != null) waitButton.SetActive(showWait);
        if (finishButton1 != null) finishButton1.SetActive(showFinish && !_advancedMode);
        if (finishButton2 != null) finishButton2.SetActive(showFinish && _advancedMode);
    }

    public void EnableAdvancedMode()
    {
        _advancedMode = true;
        UpdateButtons();
    }

    private Coroutine flashCoroutine;
    private bool _ownsPitchDetector;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        sineClip = GenerateSineWave(TONE_FREQUENCY, Mathf.Max(VERIFICATION_DURATION, TONE_BURST_DURATION) + 1f);
        clickClip = GenerateClick();

        OnStatusMessage += UpdateStatusText;
        OnTapBeatPlayed += FlashBeatIndicator;

        if (flashImage != null)
            flashImage.color = Color.black;
    }

    private void Update()
    {
        if (State == CalibrationState.TapTest && Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame)
        {
            TapButton();
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        OnStatusMessage -= UpdateStatusText;
        OnTapBeatPlayed -= FlashBeatIndicator;
        DestroyOwnedPitchDetector();
    }

    private void UpdateStatusText(string msg)
    {
        if (statusText != null)
            statusText.text = msg;
    }

    private void FlashBeatIndicator()
    {
        if (flashImage == null) return;
        if (flashCoroutine != null)
            StopCoroutine(flashCoroutine);
        flashCoroutine = StartCoroutine(FlashCoroutine());
    }

    private IEnumerator FlashCoroutine()
    {
        flashImage.color = Color.white;
        float duration = 0.15f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            flashImage.color = Color.Lerp(Color.white, Color.black, elapsed / duration);
            yield return null;
        }
        flashImage.color = Color.black;
        flashCoroutine = null;
    }

    // =========================================================================
    // PUBLIC METHODS (wire to buttons)
    // =========================================================================

    /// <summary>
    /// Prepares a temporary RealTimePitchDetector for the given microphone and shows
    /// the calibration UI. Does NOT start calibration — call StartVerification() for that.
    /// </summary>
    public void PrepareCalibrationWithMic(string micName)
    {
        if (string.IsNullOrEmpty(micName))
        {
            Debug.LogError("[LatencyCalibrator] micName is null or empty.");
            return;
        }

        // Reset to easy mode each time calibration UI opens
        _advancedMode = false;

        // Tear down any previous temp detector
        DestroyOwnedPitchDetector();

        // Create a lightweight RealTimePitchDetector on a temp child GameObject
        // Set calibration mode BEFORE adding component so Start() skips normal init
        GameObject detectorGO = new GameObject("CalibrationPitchDetector");
        detectorGO.SetActive(false);
        detectorGO.transform.SetParent(transform);
        pitchDetector = detectorGO.AddComponent<RealTimePitchDetector>();
        pitchDetector._calibrationMode = true;
        pitchDetector.selectedDeviceNameOverride = micName;
        detectorGO.SetActive(true);
        _ownsPitchDetector = true;

        // Pause all MicDropdownUI mic capture so calibration has exclusive mic access
        foreach (var mdu in FindObjectsByType<MicDropdownUI>(FindObjectsSortMode.None))
            mdu.PauseMicCapture();

        // Show calibration UI
        if (calibrationUI != null)
            calibrationUI.SetActive(true);

        // Show first step, hide the rest
        if (firstStep != null) firstStep.SetActive(true);
        if (secondStep != null) secondStep.SetActive(false);
        if (thirdStep != null) thirdStep.SetActive(false);

        // Fade out background music
        if (BGMusic.Instance != null)
            BGMusic.Instance.FadeOutAndPause();

        SetState(CalibrationState.Idle);

        if (!_windowsObjectShown && windowsOnlyObject != null
            && (Application.platform == RuntimePlatform.WindowsPlayer
            || Application.platform == RuntimePlatform.WindowsEditor))
        {
            windowsOnlyObject.SetActive(true);
            _windowsObjectShown = true;
        }
    }

    /// <summary>
    /// Closes the calibration UI and restores background music and mic capture.
    /// </summary>
    public void CloseCalibration()
    {
        // Auto-apply results if calibration was successful
        if (State == CalibrationState.ToneTestComplete || State == CalibrationState.TapTestComplete
            || State == CalibrationState.Complete)
            ApplyResults();

        Cancel();

        if (BGMusic.Instance != null)
            BGMusic.Instance.FadeInAndResume();

        // Resume all MicDropdownUI mic capture
        foreach (var mdu in FindObjectsByType<MicDropdownUI>(FindObjectsSortMode.None))
            mdu.ResumeMicCapture();
    }

    private void DestroyOwnedPitchDetector()
    {
        if (_ownsPitchDetector && pitchDetector != null)
        {
            pitchDetector.StopCalibrationMode();
            Destroy(pitchDetector.gameObject);
            pitchDetector = null;
            _ownsPitchDetector = false;
        }
    }

    /// <summary>
    /// Begin the 3-second verification phase. Plays a continuous tone and checks
    /// that the microphone picks it up via the pitch detector.
    /// </summary>
    public void StartVerification()
    {
        if (State != CalibrationState.Idle && State != CalibrationState.VerificationFailed)
        {
            Debug.LogWarning("[LatencyCalibrator] Cannot start verification in current state: " + State);
            return;
        }
        if (pitchDetector == null || toneAudioSource == null)
        {
            Debug.LogError("[LatencyCalibrator] pitchDetector and toneAudioSource must be assigned.");
            return;
        }

        StopActiveCoroutine();
        activeCoroutine = StartCoroutine(VerificationCoroutine());
    }

    /// <summary>
    /// Begin the tone burst test. Call after verification passes.
    /// </summary>
    public void StartToneTest()
    {
        if (State != CalibrationState.Idle && State != CalibrationState.ToneTestComplete
            && State != CalibrationState.VerificationFailed)
        {
            // Allow re-running tone test from various states
        }

        StopActiveCoroutine();
        activeCoroutine = StartCoroutine(ToneTestCoroutine());
    }

    /// <summary>
    /// Begin the metronome tap test (advanced mode).
    /// </summary>
    public void StartTapTest()
    {
        if (State != CalibrationState.ToneTestComplete)
        {
            Debug.LogWarning("[LatencyCalibrator] Run the tone test first before the tap test.");
            return;
        }

        StopActiveCoroutine();
        activeCoroutine = StartCoroutine(TapTestCoroutine());
    }

    /// <summary>
    /// Wire this to the tap button's onClick. Records the tap timestamp during the tap test.
    /// </summary>
    public void TapButton()
    {
        if (State == CalibrationState.TapTest && _tapCount < TAP_BEAT_COUNT)
        {
            tapTimes.Add(Time.realtimeSinceStartupAsDouble);
            _tapCount++;
            TapTestCompletedBeats = _tapCount;
            UpdateTapStatusText($"Tap {_tapCount}/{TAP_BEAT_COUNT}");
        }
    }

    /// <summary>
    /// Save the calibrated values to SettingsManager.
    /// </summary>
    public void ApplyResults()
    {
        if (State != CalibrationState.ToneTestComplete && State != CalibrationState.TapTestComplete
            && State != CalibrationState.Complete)
        {
            Debug.LogWarning("[LatencyCalibrator] No results to apply.");
            return;
        }

        ComputeResults();

        SettingsManager.Instance.SetSetting("PitchDataPadding",
            CalibratedPitchDataPadding.ToString("F3", CultureInfo.InvariantCulture));
        SettingsManager.Instance.SetSetting("AudioLatencyCompensation",
            CalibratedAudioLatencyCompensation.ToString("F3", CultureInfo.InvariantCulture));

        SetState(CalibrationState.Complete);

        string msg = $"Calibration applied. Pitch Data Padding: {CalibratedPitchDataPadding * 1000f:F0}ms";
        if (tapTestDone)
            msg += $", Audio Latency Compensation: {CalibratedAudioLatencyCompensation * 1000f:F0}ms";
        OnStatusMessage?.Invoke(msg);
        Debug.Log("[LatencyCalibrator] " + msg);

        Cleanup();
    }

    /// <summary>
    /// Abort calibration and clean up.
    /// </summary>
    public void Cancel()
    {
        StopActiveCoroutine();
        Cleanup();
        SetState(CalibrationState.Idle);
        OnStatusMessage?.Invoke("Calibration cancelled.");
    }

    // =========================================================================
    // VERIFICATION PHASE
    // =========================================================================

    private IEnumerator VerificationCoroutine()
    {
        SetState(CalibrationState.Verification);
        VerificationProgress = 0f;
        OnStatusMessage?.Invoke("Starting mic verification...");

        // Start pitch detector in calibration mode (Setup + mic recording)
        pitchDetector.StartCalibrationMode();

        // Wait for mic to initialize
        yield return new WaitForSeconds(MIC_INIT_WAIT);

        // Play continuous tone
        toneAudioSource.clip = sineClip;
        toneAudioSource.loop = true;
        toneAudioSource.Play();

        int totalFrames = 0;
        int detectedFrames = 0;
        float elapsed = 0f;

        while (elapsed < VERIFICATION_DURATION)
        {
            yield return new WaitForFixedUpdate();
            elapsed += Time.fixedDeltaTime;
            totalFrames++;

            if (Mathf.Abs(pitchDetector.currentPitchPublic - TONE_FREQUENCY) < DETECTION_THRESHOLD)
            {
                detectedFrames++;
            }

            VerificationProgress = elapsed / VERIFICATION_DURATION;
        }

        toneAudioSource.Stop();
        toneAudioSource.loop = false;

        float detectionRate = totalFrames > 0 ? (float)detectedFrames / totalFrames : 0f;
        Debug.Log($"[LatencyCalibrator] Verification: {detectedFrames}/{totalFrames} frames detected ({detectionRate:P0})");

        if (detectionRate >= VERIFICATION_PASS_RATE)
        {
            VerificationProgress = 1f;
            OnStatusMessage?.Invoke("Verification passed! Starting tone test...");

            // Brief pause so the user sees the message, then auto-start tone test
            yield return new WaitForSeconds(1f);
            activeCoroutine = StartCoroutine(ToneTestCoroutine());
        }
        else
        {
            VerificationProgress = 0f;
            OnStatusMessage?.Invoke("Mic couldn't pick up the tone. Move it closer to your speakers and try again.");
            SetState(CalibrationState.VerificationFailed);
        }
    }

    // =========================================================================
    // TONE TEST PHASE
    // =========================================================================

    private IEnumerator ToneTestCoroutine()
    {
        SetState(CalibrationState.ToneTest);
        ToneTestCompletedBursts = 0;
        toneDeltas.Clear();
        toneTestDone = false;
        OnStatusMessage?.Invoke("Playing tone bursts...");

        // Ensure pitch detector is in calibration mode
        if (State == CalibrationState.ToneTest)
        {
            // Already started from verification, or start fresh
        }

        toneAudioSource.loop = false;
        toneAudioSource.clip = sineClip;

        for (int burst = 0; burst < TONE_BURST_COUNT; burst++)
        {
            // Wait a brief silence gap before each burst (except the first)
            if (burst > 0)
            {
                yield return new WaitForSeconds(TONE_GAP_DURATION);
            }

            // Wait for pitch to settle to near-zero (silence detected)
            float settleTimeout = 0.5f;
            float settleElapsed = 0f;
            while (settleElapsed < settleTimeout)
            {
                if (pitchDetector.currentPitchPublic < 50f) break;
                yield return new WaitForFixedUpdate();
                settleElapsed += Time.fixedDeltaTime;
            }

            // Play tone burst and record start time
            double toneStartTime = Time.realtimeSinceStartupAsDouble;
            toneAudioSource.Play();

            // Wait for detection or timeout
            bool detected = false;
            float waitElapsed = 0f;

            while (waitElapsed < TONE_DETECTION_TIMEOUT)
            {
                yield return new WaitForFixedUpdate();
                waitElapsed += Time.fixedDeltaTime;

                if (Mathf.Abs(pitchDetector.currentPitchPublic - TONE_FREQUENCY) < DETECTION_THRESHOLD)
                {
                    double detectionTime = Time.realtimeSinceStartupAsDouble;
                    float delta = (float)(detectionTime - toneStartTime);
                    toneDeltas.Add(delta);
                    detected = true;
                    break;
                }
            }

            // Stop tone after burst duration or detection
            toneAudioSource.Stop();

            ToneTestCompletedBursts = burst + 1;

            if (!detected)
            {
                Debug.Log($"[LatencyCalibrator] Burst {burst + 1}: missed (timeout)");
            }
            else
            {
                Debug.Log($"[LatencyCalibrator] Burst {burst + 1}: {toneDeltas[toneDeltas.Count - 1] * 1000f:F1}ms");
            }
        }

        // Process results
        if (toneDeltas.Count < TONE_MIN_VALID)
        {
            OnStatusMessage?.Invoke($"Only {toneDeltas.Count} of {TONE_BURST_COUNT} bursts detected. Move mic closer and try again.");
            SetState(CalibrationState.VerificationFailed);
            yield break;
        }

        // Sort, discard top and bottom outlier, take median
        toneDeltas.Sort();
        List<float> trimmed = toneDeltas.GetRange(1, toneDeltas.Count - 2);
        MeasuredRoundTrip = MedianOf(trimmed);
        toneTestDone = true;

        // Compute easy-mode result
        ComputeResults();

        OnStatusMessage?.Invoke($"Tone test complete. Round-trip: {MeasuredRoundTrip * 1000f:F0}ms");
        SetState(CalibrationState.ToneTestComplete);
    }

    // =========================================================================
    // TAP TEST PHASE
    // =========================================================================

    private void UpdateTapStatusText(string msg)
    {
        if (tapStatusText != null)
            tapStatusText.text = msg;
    }

    private IEnumerator TapTestCoroutine()
    {
        SetState(CalibrationState.TapTest);
        TapTestCompletedBeats = 0;
        scheduledBeatTimes.Clear();
        tapTimes.Clear();
        tapTestDone = false;
        _tapCount = 0;
        UpdateTapStatusText("Tap the button in sync with the clicks...");

        // Disable apply button, disable retry button during test
        if (tapApplyButton != null) tapApplyButton.interactable = false;
        if (tapRetryButton != null) tapRetryButton.SetActive(false);

        float beatInterval = 60f / TAP_BPM;

        // Play clicks in a continuous loop — audio never stops during tap test
        toneAudioSource.clip = clickClip;
        toneAudioSource.loop = false;

        // Keep playing beats until user has tapped enough times
        while (_tapCount < TAP_BEAT_COUNT)
        {
            double scheduledTime = Time.realtimeSinceStartupAsDouble;
            scheduledBeatTimes.Add(scheduledTime);

            toneAudioSource.Play();
            OnTapBeatPlayed?.Invoke();

            yield return new WaitForSeconds(beatInterval);
        }

        // Stop the clicks
        toneAudioSource.Stop();

        UpdateTapStatusText("Processing...");

        // Grace window for the last tap
        yield return new WaitForSeconds(TAP_GRACE_WINDOW);

        ProcessTapResults();
    }

    private void ProcessTapResults()
    {
        float beatInterval = 60f / TAP_BPM;

        // Match taps to beats and compute offsets
        List<float> offsets = new List<float>();
        foreach (double tapTime in tapTimes)
        {
            // Find the nearest beat
            double bestDist = double.MaxValue;
            int bestBeat = -1;
            for (int b = 0; b < scheduledBeatTimes.Count; b++)
            {
                double dist = Math.Abs(tapTime - scheduledBeatTimes[b]);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestBeat = b;
                }
            }

            if (bestBeat >= 0 && bestDist < beatInterval * 0.5)
            {
                float offset = (float)(tapTime - scheduledBeatTimes[bestBeat]);
                offsets.Add(offset);
            }
        }

        // Discard first TAP_DISCARD_COUNT matched offsets (user finding rhythm)
        if (offsets.Count > TAP_DISCARD_COUNT)
        {
            offsets = offsets.GetRange(TAP_DISCARD_COUNT, offsets.Count - TAP_DISCARD_COUNT);
        }

        // Show retry button regardless of result
        if (tapRetryButton != null) tapRetryButton.SetActive(true);

        if (offsets.Count < 3)
        {
            UpdateTapStatusText("Not enough valid taps matched. Try again.");
            SetState(CalibrationState.TapTestComplete);
            return;
        }

        offsets.Sort();
        MeasuredOutputLatency = Mathf.Max(0f, MedianOf(offsets));
        tapTestDone = true;

        ComputeResults();

        UpdateTapStatusText($"Tap test complete. Output latency: {MeasuredOutputLatency * 1000f:F0}ms");

        // Enable apply button on success
        if (tapApplyButton != null) tapApplyButton.interactable = true;

        SetState(CalibrationState.TapTestComplete);
    }

    // =========================================================================
    // RESULT COMPUTATION
    // =========================================================================

    private void ComputeResults()
    {
        if (toneTestDone && tapTestDone)
        {
            // Advanced mode: split round-trip into output + input/processing
            CalibratedAudioLatencyCompensation = Mathf.Clamp(MeasuredOutputLatency, 0f, 1f);
            CalibratedPitchDataPadding = Mathf.Clamp(MeasuredRoundTrip - MeasuredOutputLatency, 0f, 1f);
        }
        else if (toneTestDone)
        {
            // Easy mode: assign full round-trip to pitch data padding
            CalibratedPitchDataPadding = Mathf.Clamp(MeasuredRoundTrip, 0f, 1f);
            CalibratedAudioLatencyCompensation = 0.1f; // Keep default
        }
    }

    // =========================================================================
    // UTILITIES
    // =========================================================================

    private void StopActiveCoroutine()
    {
        if (activeCoroutine != null)
        {
            StopCoroutine(activeCoroutine);
            activeCoroutine = null;
        }
        toneAudioSource.Stop();
    }

    private void Cleanup()
    {
        toneAudioSource.Stop();
        toneAudioSource.loop = false;
        DestroyOwnedPitchDetector();
        if (pitchDetector != null)
        {
            pitchDetector.StopCalibrationMode();
        }
    }

    private static float MedianOf(List<float> sorted)
    {
        if (sorted.Count == 0) return 0f;
        int mid = sorted.Count / 2;
        if (sorted.Count % 2 == 0)
            return (sorted[mid - 1] + sorted[mid]) / 2f;
        return sorted[mid];
    }

    private static AudioClip GenerateSineWave(float frequency, float duration, int sampleRate = 44100)
    {
        int sampleCount = (int)(sampleRate * duration);
        float[] samples = new float[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            samples[i] = Mathf.Sin(2f * Mathf.PI * frequency * i / sampleRate) * 0.8f;
        }
        AudioClip clip = AudioClip.Create("CalibrationTone", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private static AudioClip GenerateClick(float duration = 0.03f, int sampleRate = 44100)
    {
        int sampleCount = (int)(sampleRate * duration);
        float[] samples = new float[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            float t = (float)i / sampleCount;
            // 1kHz decaying blip
            samples[i] = Mathf.Sin(2f * Mathf.PI * 1000f * i / sampleRate) * (1f - t) * 0.9f;
        }
        AudioClip clip = AudioClip.Create("CalibrationClick", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }
}
