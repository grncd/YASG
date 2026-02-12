using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using System.Linq; // For LINQ methods like .SequenceEqual()
using TMPro; // Make sure you have TextMeshPro imported
using MPUIKIT;
using System;
using UnityEngine.Audio;

public class MicDropdownUI : MonoBehaviour
{
    [Header("UI Elements (Assign in Inspector or ensure child order)")]
    public TMP_InputField nameInput; // Child 0
    public TMP_Dropdown micDropdown;   // Child 1 (after activeToggle removal)
    public MPImage progressImage;       // Child 2
    public TextMeshProUGUI levelText; // Child 2 -> Child 0 -> Child 0
    public TextMeshProUGUI totalScoreText; // Child 6

    [Header("Microphone Visualizer")]
    [Tooltip("Slider that visualizes microphone amplitude")]
    public Slider micAmplitudeSlider;

    [Header("Microphone Feedback")]
    [Tooltip("Assign the FX AudioMixerGroup to route mic feedback through the SFX volume control.")]
    public AudioMixerGroup fxMixerGroup;

    [Header("Profile Configuration")]
    [Tooltip("The name of the profile this UI element will manage...")]
    public string profileIdentifier;

    private Profile _currentProfile;
    private List<string> _availableMicsCache = new List<string>();
    private float _micCheckInterval = 0.5f;
    private float _micCheckTimer = 0f;
    private bool _isInitialized = false;
    private bool _listenersAttached = false;
    private string _playerPrefsKeyForProfileName;

    // Microphone visualization
    private Coroutine _micVisualizerCoroutine;
    private AudioClip _micClip;
    private int _micSampleRate = 0;
    private const int MIC_SAMPLE_LENGTH = 512; // Number of samples to read
    private float _currentAmplitudeValue = 0f; // Current smoothed amplitude value
    private const float AMPLITUDE_SMOOTHING = 10f; // Speed of smoothing (higher = faster)
    private string _currentMicName = ""; // Track which mic is currently open
    private GameObject _feedbackGO;
    private MicFeedbackFilter _feedbackFilter;

    // Dynamic property that always gets the current index from ProfileManager
    private int PlayerIndex
    {
        get
        {
            if (ProfileManager.Instance != null && _currentProfile != null)
            {
                return ProfileManager.Instance.GetProfileActiveIndex(_currentProfile.name);
            }
            return 0;
        }
    }

    private void OnEnable()
    {
        // Restart visualizer when GameObject is re-enabled (menu reopens)
        if (_isInitialized && _currentProfile != null && micDropdown != null && micDropdown.value > 0 && micAmplitudeSlider != null)
        {
            string selectedMic = micDropdown.options[micDropdown.value].text;
            if (selectedMic != "--- NONE ---")
            {
                StartMicVisualizer(selectedMic);
            }
        }
    }

    void Start()
    {
        _playerPrefsKeyForProfileName = gameObject.name + "Name";
        string prefsProfileName = PlayerPrefs.GetString(_playerPrefsKeyForProfileName, string.Empty);

        // --- Initialize profileIdentifier ---
        if (string.IsNullOrEmpty(profileIdentifier))
        {
            profileIdentifier = !string.IsNullOrEmpty(prefsProfileName) ? prefsProfileName : gameObject.name;
            if (string.IsNullOrEmpty(prefsProfileName) && profileIdentifier == gameObject.name)
                Debug.Log($"'{gameObject.name}': Profile identifier not set. Defaulting to GO name: '{profileIdentifier}'");
        }
        else if (profileIdentifier != prefsProfileName)
        {
            //PlayerPrefs.SetString(_playerPrefsKeyForProfileName, profileIdentifier);
            //PlayerPrefs.Save();
        }

        // --- Attempt to find UI components ---
        // Child indices need to be precise based on your hierarchy
        if (nameInput == null && transform.childCount > 0) nameInput = transform.GetChild(0)?.GetComponent<TMP_InputField>();
        if (micDropdown == null && transform.childCount > 1) micDropdown = transform.GetChild(1)?.GetComponent<TMP_Dropdown>();

        if (progressImage == null && transform.childCount > 2) progressImage = transform.GetChild(2)?.GetComponent<MPImage>(); // Or MPImage
        if (levelText == null && transform.childCount > 2 && transform.GetChild(2).childCount > 0 && transform.GetChild(2).GetChild(0).childCount > 0)
        {
            levelText = transform.GetChild(2).GetChild(0).GetChild(0)?.GetComponent<TextMeshProUGUI>();
        }
        if (totalScoreText == null && transform.childCount > 6) totalScoreText = transform.GetChild(6)?.GetComponent<TextMeshProUGUI>();


        // --- Validate essential UI components ---
        if (nameInput == null || micDropdown == null) // Essential for core functionality
        {
            Debug.LogError($"'{gameObject.name}': NameInput or MicDropdown not assigned/found. Disabling script.", this);
            SetUIInteractable(false, false, false, false, false);
            enabled = false;
            return;
        }
        // Log warnings for optional UI elements if not found, but don't disable script
        if (progressImage == null) Debug.LogWarning($"'{gameObject.name}': ProgressImage not assigned/found. Progress UI will not update.", this);
        if (levelText == null) Debug.LogWarning($"'{gameObject.name}': LevelText not assigned/found. Level UI will not update.", this);
        if (totalScoreText == null) Debug.LogWarning($"'{gameObject.name}': TotalScoreText not assigned/found. Score UI will not update.", this);


        if (ProfileManager.Instance == null)
        {
            Debug.LogWarning($"'{gameObject.name}': ProfileManager not ready. Will attempt initialization in Update.");
            return;
        }
        InitializeWithProfile();
    }

    void Update()
    {
        if (!_isInitialized && ProfileManager.Instance != null)
        {
            InitializeWithProfile();
        }

        if (!_isInitialized || _currentProfile == null) return;

        _micCheckTimer += Time.deltaTime;
        if (_micCheckTimer >= _micCheckInterval)
        {
            _micCheckTimer = 0f;
            CheckForMicChangesAndUpdateDropdown();
        }
    }

    private void InitializeWithProfile()
    {
        if (ProfileManager.Instance == null) return;
        if (string.IsNullOrWhiteSpace(profileIdentifier))
        {
            Debug.LogError($"'{gameObject.name}': profileIdentifier is empty. Cannot initialize.", this);
            SetUIInteractable(false, false, false, false, false);
            if (nameInput != null) nameInput.text = "(Error: No ID)";
            return;
        }

        _currentProfile = ProfileManager.Instance.GetProfileByName(profileIdentifier);

        if (_currentProfile == null)
        {
            Debug.LogWarning($"'{gameObject.name}': Profile '{profileIdentifier}' not found. Attempting to create.", this);
            bool created = ProfileManager.Instance.CreateProfile(profileIdentifier);
            if (created)
            {
                _currentProfile = ProfileManager.Instance.GetProfileByName(profileIdentifier);
                if (_currentProfile != null)
                {
                    Debug.Log($"'{gameObject.name}': Profile '{profileIdentifier}' created.", this);
                    //PlayerPrefs.SetString(_playerPrefsKeyForProfileName, profileIdentifier);
                    PlayerPrefs.Save();
                }
                else { /* Error handling as before */ }
            }
            else { /* Error handling as before */ }
            if (_currentProfile == null) // If still null after attempt
            {
                Debug.LogError($"'{gameObject.name}': Failed to find or create profile '{profileIdentifier}'.", this);
                SetUIInteractable(false, false, false, false, false);
                if (nameInput != null) nameInput.text = $"{profileIdentifier} (Error)";
                _isInitialized = true; // Prevent loop, but it's an error state
                return;
            }
        }

        if (_currentProfile != null)
        {
            Debug.Log($"'{gameObject.name}': Initializing UI for profile '{_currentProfile.name}'. Activating.", this);
            int succ = ProfileManager.Instance.ActivateProfile(_currentProfile.name);
            Debug.Log(succ);
            if (succ == 0 && PlayerPrefs.GetInt("fromSettings") == 1)
            {
                ProfileDisplay.Instance.RemoveProfile(_currentProfile.name);
                Destroy(gameObject);
            }
            else
            {
                succ = ProfileManager.Instance.GetProfileByName(_currentProfile.name).index;
                PlayerPrefs.SetString("Player" + succ + "Name", _currentProfile.name);
                PlayerPrefs.SetInt("Player" + succ, 1);
                // playerIndex is now a property that queries ProfileManager, so no assignment needed
            }
        }

        SetUIInteractable(true, true,
            progressImage != null,
            levelText != null,
            totalScoreText != null);
        PopulateUIFromProfile(); // This will now update all UI elements
        PopulateMicDropdown();
        if (!_listenersAttached)
        {
            SetupEventListeners();
            _listenersAttached = true;
        }
        _isInitialized = true;
    }

    void SetUIInteractable(bool nameInteractable, bool micInteractable, bool progressInteractable, bool levelInteractable, bool scoreInteractable)
    {
        if (nameInput != null) nameInput.interactable = nameInteractable;
        if (micDropdown != null) micDropdown.interactable = micInteractable;
        // For display elements, interactable might not be relevant, but you could disable the GameObject
        if (progressImage != null) progressImage.gameObject.SetActive(progressInteractable);
        if (levelText != null) levelText.gameObject.SetActive(levelInteractable);
        if (totalScoreText != null) totalScoreText.gameObject.SetActive(scoreInteractable);
    }

    void PopulateUIFromProfile()
    {

        if (nameInput != null) nameInput.text = _currentProfile.name;

        if (levelText != null)
        {
            levelText.text = $"{_currentProfile.level}";
        }
        if (progressImage != null)
        {
            progressImage.fillAmount = Mathf.Clamp01(_currentProfile.progressRemaining / 100f);
        }
        if (totalScoreText != null)
        {
            if(_currentProfile.totalScore != 0)
            {
                totalScoreText.text = $"Total Score: {_currentProfile.totalScore.ToString("#,#")}";
            }
            else
            {
                totalScoreText.text = $"Total Score: 0";
            }
        }
    }

    void SetUIInteractable(bool interactable)
    {
        if (nameInput != null) nameInput.interactable = interactable;
        if (micDropdown != null) micDropdown.interactable = interactable;
    }


    void SetupEventListeners()
    {
        nameInput.onEndEdit.AddListener(OnNameInputEndEdit);
        micDropdown.onValueChanged.AddListener(OnMicDropdownChanged);
    }

    void PopulateMicDropdown()
    {
        StopMicVisualizer();

        if (ProfileManager.Instance == null || _currentProfile == null)
        {
            if (micDropdown != null)
            {
                micDropdown.ClearOptions();
                micDropdown.AddOptions(new List<string> { "N/A" });
            }
            return;
        }

        micDropdown.ClearOptions();
        _availableMicsCache = new List<string>(ProfileManager.Instance.GetAvailableMicrophones());

        List<string> micOptions = new List<string>(_availableMicsCache);
        micOptions.Insert(0, "--- NONE ---");
        micDropdown.AddOptions(micOptions);

        if (_listenersAttached) micDropdown.onValueChanged.RemoveListener(OnMicDropdownChanged);

        int currentIndex = 0;
        if (!string.IsNullOrEmpty(_currentProfile.microphone))
        {
            int foundIndex = micOptions.IndexOf(_currentProfile.microphone);
            if (foundIndex != -1)
            {
                currentIndex = foundIndex;
            }
        }
        micDropdown.value = currentIndex;
        PlayerPrefs.SetInt("Player" + PlayerIndex + "Mic", currentIndex-1);

        if (_listenersAttached) micDropdown.onValueChanged.AddListener(OnMicDropdownChanged);

        // Start visualizer if a mic is selected
        if (currentIndex > 0 && micAmplitudeSlider != null)
        {
            string selectedMic = micOptions[currentIndex];
            StartMicVisualizer(selectedMic);
        }
    }

    void OnNameInputEndEdit(string newName)
    {
        if (_currentProfile == null || ProfileManager.Instance == null) return;
        if (string.IsNullOrWhiteSpace(newName))
        {
            Debug.LogWarning($"'{gameObject.name}': Profile name cannot be empty. Reverting to '{_currentProfile.name}'.", this);
            nameInput.text = _currentProfile.name;
            return;
        }

        if (_currentProfile.name.Equals(newName, System.StringComparison.OrdinalIgnoreCase)) return;

        ProfileDisplay.Instance.ChangeName(_currentProfile.name, newName);

        bool success = ProfileManager.Instance.UpdateProfileName(_currentProfile.name, newName);
        if (success)
        {
            Debug.Log($"'{gameObject.name}': Profile name successfully updated from '{profileIdentifier}' to '{newName}'.", this);
            profileIdentifier = newName;
            _currentProfile = ProfileManager.Instance.GetProfileByName(newName);

            PlayerPrefs.SetString("Player"+PlayerIndex+"Name", newName);
            PlayerPrefs.Save();
        }
        else
        {
            Debug.LogWarning($"'{gameObject.name}': Failed to update profile name to '{newName}'. Reverting input field.", this);
            nameInput.text = _currentProfile.name;
        }
    }

    void OnMicDropdownChanged(int index)
    {
        if (_currentProfile == null || ProfileManager.Instance == null || index < 0 || index >= micDropdown.options.Count) return;

        string selectedMicName = (index == 0) ? string.Empty : micDropdown.options[index].text;
        ProfileManager.Instance.SetProfileMicrophone(_currentProfile.name, selectedMicName);
        PlayerPrefs.SetInt("Player" + PlayerIndex + "Mic", index-1);

        // Update microphone visualizer
        StopMicVisualizer();

        if (index > 0 && micAmplitudeSlider != null)
        {
            StartMicVisualizer(selectedMicName);
        }
    }

    private void StartMicVisualizer(string micName)
    {
        if (micAmplitudeSlider == null) return;

        _micVisualizerCoroutine = StartCoroutine(MicVisualizerCoroutine(micName));
    }

    private void StopMicVisualizer()
    {
        StopMicFeedback();

        if (_micVisualizerCoroutine != null)
        {
            StopCoroutine(_micVisualizerCoroutine);
            _micVisualizerCoroutine = null;
        }

        // Only stop the specific microphone we started
        if (!string.IsNullOrEmpty(_currentMicName) && Microphone.IsRecording(_currentMicName))
        {
            Microphone.End(_currentMicName);
        }
        _currentMicName = "";
        _micClip = null;

        _currentAmplitudeValue = 0f;

        if (micAmplitudeSlider != null)
        {
            micAmplitudeSlider.value = 0f;
        }
    }

    private void StartMicFeedback(AudioClip clip, string micName)
    {
        if (clip == null) return;

        if (_feedbackGO == null)
        {
            _feedbackGO = new GameObject("MicFeedback");
            _feedbackGO.transform.SetParent(transform);
            _feedbackGO.AddComponent<AudioSource>();
            _feedbackFilter = _feedbackGO.AddComponent<MicFeedbackFilter>();
        }

        _feedbackFilter.Activate(clip, micName, fxMixerGroup);
    }

    private void StopMicFeedback()
    {
        if (_feedbackFilter != null)
        {
            _feedbackFilter.Deactivate();
        }
    }

    private IEnumerator MicVisualizerCoroutine(string micName)
    {
        if (micAmplitudeSlider == null) yield break;

        _currentMicName = micName;

        // Get the minimum frequency supported by the microphone
        int minFreq;
        int maxFreq;
        Microphone.GetDeviceCaps(micName, out minFreq, out maxFreq);

        // Use 44100Hz if available, otherwise use max supported frequency
        _micSampleRate = (maxFreq >= 44100) ? 44100 : maxFreq;
        if (_micSampleRate == 0) _micSampleRate = 44100; // Fallback

        // Start recording
        _micClip = Microphone.Start(micName, true, 1, _micSampleRate);

        // Wait for microphone to start recording (with timeout)
        float startTime = Time.time;
        while (Microphone.GetPosition(micName) <= 0)
        {
            if (Time.time - startTime > 3f)
            {
                Debug.LogError($"MicDropdownUI: Timeout waiting for microphone '{micName}' to start.");
                StopMicVisualizer();
                yield break;
            }
            yield return null;
        }

        // Start low-latency mic audio feedback
        StartMicFeedback(_micClip, micName);

        float[] samples = new float[MIC_SAMPLE_LENGTH];
        _currentAmplitudeValue = 0f; // Reset smoothed value

        // Optimize: Update visualizer at 15 FPS instead of every frame
        WaitForSeconds updateInterval = new WaitForSeconds(1f / 15f); // 15 FPS

        while (Microphone.IsRecording(micName) && _currentMicName == micName && _micClip != null)
        {
            // Read the most recent samples from the microphone clip
            int micPosition = Microphone.GetPosition(micName);
            int readPosition = micPosition - MIC_SAMPLE_LENGTH;
            if (readPosition < 0)
            {
                // Handle wrap-around for circular buffer
                _micClip.GetData(samples, _micClip.samples - MIC_SAMPLE_LENGTH);
            }
            else
            {
                _micClip.GetData(samples, readPosition);
            }

            // Calculate RMS (Root Mean Square) amplitude
            float sum = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                sum += samples[i] * samples[i];
            }
            float rms = Mathf.Sqrt(sum / samples.Length);

            // Amplify the signal for better visualization (microphones can be quiet)
            float amplified = rms * 10f;

            // Smooth the value using Lerp (use fixed time since we're not updating every frame)
            _currentAmplitudeValue = Mathf.Lerp(_currentAmplitudeValue, amplified, AMPLITUDE_SMOOTHING / 15f);

            // Clamp to 0-1 range for slider
            micAmplitudeSlider.value = Mathf.Clamp01(_currentAmplitudeValue);

            yield return updateInterval; // Update at 15 FPS instead of every frame
        }
    }

    void CheckForMicChangesAndUpdateDropdown()
    {
        if (ProfileManager.Instance == null) return;

        var newMicsArray = ProfileManager.Instance.GetAvailableMicrophones();
        bool listChanged = newMicsArray.Length != _availableMicsCache.Count || !_availableMicsCache.SequenceEqual(newMicsArray);

        if (listChanged)
        {
            Debug.Log($"'{gameObject.name}': Microphone list changed. Refreshing dropdown.", this);
            PopulateMicDropdown();
        }
    }



    public void NotifyProfileWasDeleted(string deletedProfileName)
    {
        if (_currentProfile != null && deletedProfileName.Equals(_currentProfile.name, System.StringComparison.OrdinalIgnoreCase))
        {
            Debug.LogWarning($"'{gameObject.name}': Profile '{deletedProfileName}' this UI was managing has been deleted.", this);
            _currentProfile = null;
            SetUIInteractable(false);
            StopMicVisualizer();
            if (nameInput != null) nameInput.text = $"{deletedProfileName} (Deleted)";
            if (micDropdown != null)
            {
                micDropdown.ClearOptions();
                micDropdown.AddOptions(new List<string> { "N/A" });
            }
        }
    }

    public void DeactivateProfileAndDestroyGameObject()
    {
        StopMicVisualizer();

        if (ProfileManager.Instance != null && _currentProfile != null)
        {
            int currentIndex = PlayerIndex;
            Debug.Log($"'{gameObject.name}': Attempting to deactivate profile '{_currentProfile.name}' (index {currentIndex}) before destroying UI element.");

            // DeactivateProfile will handle reindexing and PlayerPrefs synchronization
            bool deactivated = ProfileManager.Instance.DeactivateProfile(_currentProfile.name);
            if (deactivated)
            {
                Debug.Log($"'{gameObject.name}': Profile '{_currentProfile.name}' deactivated successfully. ProfileManager has reindexed remaining profiles.");
            }
            else
            {
                Debug.LogWarning($"'{gameObject.name}': Deactivation of profile '{_currentProfile.name}' reported as unsuccessful or profile was not active.");
            }
        }
        else if (_currentProfile == null)
        {
            Debug.LogWarning($"'{gameObject.name}': No current profile associated. Skipping deactivation. Destroying GameObject.");
        }
        else if (ProfileManager.Instance == null)
        {
            Debug.LogWarning($"'{gameObject.name}': ProfileManager instance not found. Skipping deactivation. Destroying GameObject.");
        }

        Debug.Log($"'{gameObject.name}': Destroying GameObject.");
        ProfileDisplay.Instance.RemoveProfile(gameObject.name);

        // No need to manually clear PlayerPrefs - ProfileManager.DeactivateProfile() handles all reindexing and syncing

        Destroy(gameObject);
    }

    public void RefreshUIForProfile(string newProfileIdentifier = null)
    {
        StopMicVisualizer();

        if (!string.IsNullOrEmpty(newProfileIdentifier))
        {
            profileIdentifier = newProfileIdentifier;
            PlayerPrefs.SetString(_playerPrefsKeyForProfileName, profileIdentifier);
            PlayerPrefs.Save();
        }
        Debug.Log($"'{gameObject.name}': Refreshing UI for profile '{profileIdentifier}'.", this);
        _isInitialized = false;
        _listenersAttached = false;
        if (nameInput != null) nameInput.onEndEdit.RemoveAllListeners();
        if (micDropdown != null) micDropdown.onValueChanged.RemoveAllListeners();

        // PopulateUIFromProfile will be called within InitializeWithProfile
    }

    // Call this if an external action changes profile data (e.g., level up, score change)
    public void UpdateDisplayedProfileData()
    {
        if (_isInitialized && _currentProfile != null)
        {
            // Optional: Re-fetch profile data in case it changed in ProfileManager's list
            // _currentProfile = ProfileManager.Instance.GetProfileByName(profileIdentifier);
            // This might not be necessary if _currentProfile is a reference to the object in ProfileManager's list
            // and ProfileManager modifies that object directly.

            Debug.Log($"'{gameObject.name}': Externally triggered UI data update for profile '{_currentProfile.name}'.");
            PopulateUIFromProfile();
        }
    }

    private void OnDestroy()
    {
        StopMicVisualizer();
    }

}