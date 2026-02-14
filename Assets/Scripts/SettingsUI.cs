using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Threading.Tasks;
using UnityEngine.UI;
using TMPro;
using MPUIKIT;
using System.IO;
using System.Text;
using System.Linq;
using FishNet.Managing.Scened;
using UnityEngine.InputSystem;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

public class SettingsUI : MonoBehaviour
{
    public static SettingsUI Instance { get; private set; }
    public MPImage searchGradient;
    private bool onSettings = false;
    public GameObject settingsContainer;
    public GameObject playerPrefab;
    public Transform playerContainer;
    private bool canClick = true;
    public GameObject settingsButton;
    public GameObject menuSettingsButton;
    public AudioClip settingsIn;
    public AudioClip settingsOut;
    public GameObject menuGO;
    public GameObject mainGO;
    public GameObject backButtonMenu;
    public ParticleSystem mainMenuParticles;
    public GameObject multiplayerSet;
    public AudioSource clickFX;
    private bool fromSettings = false;
    public Transform settingsTabs;
    public List<Material> backgrounds;
    public List<Color> backgroundsDarken;
    public Image darken;
    public RawImage BG;
    public AudioSource tabSwitchFX;
    public GameObject onboardingOverlay;

    // Background rendering optimization
    private RenderTexture bgRenderTexture;
    private Material currentBgMaterial;
    private Coroutine bgUpdateCoroutine;
    private int currentBgIndex = -1;
    private float currentResolutionScale = -1f;


    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }

    }

    public void SelectTabSettings(int value)
    {
        foreach (Transform child in settingsTabs)
        {
            child.GetComponent<MPImage>().color = new Color(0.2169811f, 0.2169811f, 0.2169811f);
            child.GetChild(0).GetComponent<TextMeshProUGUI>().color = Color.white;
            settingsTabs.parent.GetChild(child.GetSiblingIndex() + 2).gameObject.SetActive(false);
        }

        settingsTabs.GetChild(value).GetComponent<MPImage>().color = Color.white;
        settingsTabs.GetChild(value).GetChild(0).GetComponent<TextMeshProUGUI>().color = Color.black;
        settingsTabs.parent.GetChild(value + 2).gameObject.SetActive(true);
        tabSwitchFX.Play();
    }


    private void Start()
    {
        if (Application.platform == RuntimePlatform.Android)
        {
            Application.targetFrameRate = 60;
        }

        // Show onboarding overlay on first launch after setup
        bool isOnboarding = onboardingOverlay != null && PlayerPrefs.GetInt("onboarding") == 1;
        if (isOnboarding)
        {
            onboardingOverlay.SetActive(true);
            StartCoroutine(MonitorFPSDuringOnboarding());
        }

        // Check mic permission on Android every scene start
#if UNITY_ANDROID
        if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
        {
            if (isOnboarding)
            {
                // During onboarding, use the retry flow so permission is granted before profile creation
                RequestMicPermission();
            }
            else
            {
                // Outside onboarding, show a warning and direct to app settings on dismiss
                AlertManager.Instance.ShowWarning(
                    LocalizationManager.L("alert.mic_permission.title", "Microphone Permission Required"),
                    LocalizationManager.L("alert.mic_permission.info", "YASG needs microphone access to work. Please enable it in your device's app settings."),
                    LocalizationManager.L("alert.dismiss", "Dismiss"),
                    OpenAndroidAppSettings
                );
            }
        }
#endif

        for (int i = 1; i < 5; i++)
        {
            if (PlayerPrefs.GetInt("Player" + i) == 1)
            {
                AddProfile(PlayerPrefs.GetString("Player" + i + "Name"));
            }
        }
        settingsContainer.SetActive(false);

        // Disable particle system on mobile for better performance
        if (mainMenuParticles != null && (Application.platform == RuntimePlatform.Android || Application.platform == RuntimePlatform.IPhonePlayer))
        {
            mainMenuParticles.gameObject.SetActive(false);
        }

        // Initialize background render texture
        if (!LevelResourcesCompiler.Instance.compiling)
        {
            currentBgIndex = SettingsManager.Instance.GetSetting<int>("MenuBG");
            currentResolutionScale = SettingsManager.Instance.GetBGResolutionScale();
            currentBgMaterial = backgrounds[currentBgIndex];
            darken.color = backgroundsDarken[currentBgIndex];
            SetupBackgroundRenderTexture();
        }
    }
    public async void ToggleSettings()
    {
        if (canClick)
        {
            if (!onSettings)
            {
                canClick = false;
                ProfileDisplay.Instance.hasSettingsBeenOpened = true;
                onSettings = !onSettings;
                //SelectorOutline.Instance.defaultObject = settingsContainer.transform.GetChild(2).GetChild(0).gameObject;
                settingsContainer.SetActive(true);
                settingsContainer.GetComponent<Animator>().Play("FadeIn");
                settingsButton.GetComponent<Animator>().Play("SettingsIn");
                settingsButton.GetComponent<AudioSource>().clip = settingsIn;
                settingsButton.GetComponent<AudioSource>().Play();
                await Task.Delay(TimeSpan.FromSeconds(0.3f));
                menuGO.SetActive(false);
                canClick = true;
            }
            else
            {

                List<Profile> profiles = ProfileManager.Instance.GetActiveProfiles();
                foreach (Profile profile in profiles)
                {
                    if (profile.microphone == "Default")
                    {
                        AlertManager.Instance.ShowWarning(LocalizationManager.L("alert.micless_profiles.title", "There are micless profiles."), LocalizationManager.L("alert.micless_profiles.info", "One of your profiles has no microphone selected. If you aren't going to use said profile, please disable it."), LocalizationManager.L("alert.dismiss", "Dismiss"));
                        return;
                    }
                }
                //SelectorOutline.Instance.defaultObject = settingsButton.gameObject;
                canClick = false;
                menuGO.SetActive(true);
                onSettings = !onSettings;
                settingsButton.GetComponent<AudioSource>().clip = settingsOut;
                settingsButton.GetComponent<AudioSource>().Play();
                settingsContainer.GetComponent<Animator>().Play("FadeOut");
                settingsButton.GetComponent<Animator>().Play("SettingsOut");
                await Task.Delay(TimeSpan.FromSeconds(0.3f));
                settingsContainer.SetActive(false);
                canClick = true;
            }
        }
    }

    public async void ToggleSettingsMenu()
    {
        if (canClick)
        {
            if (!onSettings)
            {
                canClick = false;
                StartCoroutine(FadeParticleSystem(mainMenuParticles, 0.06666667f, 0f, 0.2f));
                mainGO.SetActive(true);
                settingsButton.SetActive(false);
                backButtonMenu.SetActive(false);
                ProfileDisplay.Instance.hasSettingsBeenOpened = true;
                onSettings = !onSettings;
                //SelectorOutline.Instance.defaultObject = settingsContainer.transform.GetChild(2).GetChild(0).gameObject;
                settingsContainer.SetActive(true);
                settingsContainer.GetComponent<Animator>().Play("FadeIn");
                menuSettingsButton.GetComponent<Animator>().Play("SettingsIn");
                menuSettingsButton.GetComponent<AudioSource>().clip = settingsIn;
                menuSettingsButton.GetComponent<AudioSource>().Play();
                await Task.Delay(TimeSpan.FromSeconds(0.3f));
                menuGO.SetActive(false);
                canClick = true;
            }
            else
            {

                List<Profile> profiles = ProfileManager.Instance.GetActiveProfiles();
                foreach (Profile profile in profiles)
                {
                    if (profile.microphone == "Default")
                    {
                        AlertManager.Instance.ShowWarning(LocalizationManager.L("alert.micless_profiles.title", "There are micless profiles."), LocalizationManager.L("alert.micless_profiles.info", "One of your profiles has no microphone selected. If you aren't going to use said profile, please disable it."), LocalizationManager.L("alert.dismiss", "Dismiss"));
                        return;
                    }
                }
                //SelectorOutline.Instance.defaultObject = settingsButton.gameObject;
                StartCoroutine(FadeParticleSystem(mainMenuParticles, 0f, 0.06666667f, 0.2f));
                canClick = false;
                mainGO.SetActive(false);
                menuGO.SetActive(true);
                onSettings = !onSettings;
                menuSettingsButton.GetComponent<AudioSource>().clip = settingsOut;
                menuSettingsButton.GetComponent<AudioSource>().Play();
                settingsContainer.GetComponent<Animator>().Play("FadeOut");
                menuSettingsButton.GetComponent<Animator>().Play("SettingsOut");
                await Task.Delay(TimeSpan.FromSeconds(0.3f));
                settingsContainer.SetActive(false);
                settingsButton.SetActive(true);
                backButtonMenu.SetActive(true);
                canClick = true;
            }
        }
    }

    private IEnumerator FadeParticleSystem(ParticleSystem ps, float startAlpha, float endAlpha, float duration)
    {
        var main = ps.main;
        ParticleSystem.Particle[] particles = new ParticleSystem.Particle[main.maxParticles];

        Color originalColor = main.startColor.color;

        float timeStartedLerping = Time.time;

        while (Time.time < timeStartedLerping + duration)
        {
            float timeSinceStarted = Time.time - timeStartedLerping;
            float percentageComplete = timeSinceStarted / duration;
            float currentAlpha = Mathf.Lerp(startAlpha, endAlpha, percentageComplete);

            main.startColor = new Color(originalColor.r, originalColor.g, originalColor.b, currentAlpha);

            int particleCount = ps.GetParticles(particles);
            for (int i = 0; i < particleCount; i++)
            {
                Color32 particleColor = particles[i].startColor;
                particleColor.a = (byte)(currentAlpha * 255);
                particles[i].startColor = particleColor;
            }
            ps.SetParticles(particles, particleCount);

            yield return new WaitForEndOfFrame();
        }

        main.startColor = new Color(originalColor.r, originalColor.g, originalColor.b, endAlpha);

        int finalParticleCount = ps.GetParticles(particles);
        for (int i = 0; i < finalParticleCount; i++)
        {
            Color32 particleColor = particles[i].startColor;
            particleColor.a = (byte)(endAlpha * 255);
            particles[i].startColor = particleColor;
        }
        ps.SetParticles(particles, finalParticleCount);
    }

    private void Update()
    {
        if (Keyboard.current.f11Key.wasPressedThisFrame)
        {
            Screen.fullScreen = !Screen.fullScreen;
        }
        if (!LevelResourcesCompiler.Instance.compiling)
        {
            int bgIndex = SettingsManager.Instance.GetSetting<int>("MenuBG");
            float resolutionScale = SettingsManager.Instance.GetBGResolutionScale();

            // Check if background or resolution changed
            if (bgIndex != currentBgIndex || resolutionScale != currentResolutionScale)
            {
                currentBgIndex = bgIndex;
                currentResolutionScale = resolutionScale;
                currentBgMaterial = backgrounds[bgIndex];
                darken.color = backgroundsDarken[bgIndex];
                SetupBackgroundRenderTexture();
            }
        }
    }

    private void SetupBackgroundRenderTexture()
    {
        // Stop existing coroutine
        if (bgUpdateCoroutine != null)
        {
            StopCoroutine(bgUpdateCoroutine);
            bgUpdateCoroutine = null;
        }

        // Release old render texture
        if (bgRenderTexture != null)
        {
            bgRenderTexture.Release();
            bgRenderTexture = null;
        }

        // Get resolution scale (0.25, 0.5, 0.75, or 1.0)
        float resolutionScale = SettingsManager.Instance.GetBGResolutionScale();

        // Calculate render texture size
        int screenWidth = Screen.width;
        int screenHeight = Screen.height;
        int rtWidth = Mathf.RoundToInt(screenWidth * resolutionScale);
        int rtHeight = Mathf.RoundToInt(screenHeight * resolutionScale);

        // Create new render texture with no alpha channel to avoid transparency issues
        bgRenderTexture = new RenderTexture(rtWidth, rtHeight, 0, RenderTextureFormat.RGB111110Float);
        bgRenderTexture.filterMode = FilterMode.Bilinear; // Blurry upscaling
        bgRenderTexture.useMipMap = false;
        bgRenderTexture.autoGenerateMips = false;
        bgRenderTexture.Create();

        // Set target FPS on the material
        currentBgMaterial.SetFloat("_TargetFPS", 60.0f);

        // Display the render texture
        BG.texture = bgRenderTexture;
        BG.material = null; // Don't use material directly

        // Start update coroutine
        bgUpdateCoroutine = StartCoroutine(UpdateBackgroundRenderTexture());
    }

    private IEnumerator UpdateBackgroundRenderTexture()
    {
        WaitForEndOfFrame wait = new WaitForEndOfFrame();
        int frameSkip = 0;

        bool isMobile = Application.platform == RuntimePlatform.Android ||
                       Application.platform == RuntimePlatform.IPhonePlayer;

        while (bgRenderTexture != null && currentBgMaterial != null)
        {
            // Adaptive frame skip: target ~30fps background regardless of refresh rate
            // At 120Hz: skip 3 → 30fps. At 60Hz: skip 1 → 30fps.
            int framesToSkip;
            if (isMobile)
            {
                int fps = Mathf.Max(30, Application.targetFrameRate);
                framesToSkip = Mathf.Max(0, Mathf.RoundToInt((float)fps / 30f) - 1);
            }
            else
            {
                framesToSkip = 1;
            }

            if (frameSkip % (framesToSkip + 1) == 0)
            {
                Graphics.Blit(null, bgRenderTexture, currentBgMaterial);
            }
            frameSkip++;
            yield return wait;
        }
    }

    private void OnDestroy()
    {
        // Clean up render texture
        if (bgRenderTexture != null)
        {
            bgRenderTexture.Release();
            bgRenderTexture = null;
        }
    }

    public void CompleteOnboarding()
    {
        PlayerPrefs.SetInt("onboarding", 0);
        PlayerPrefs.Save();
    }

    private IEnumerator MonitorFPSDuringOnboarding()
    {
        float lowFpsTimer = 0f;

        while (PlayerPrefs.GetInt("onboarding") == 1)
        {
            // Skip frames with huge deltaTime (app was suspended by permission dialog, etc.)
            if (Time.unscaledDeltaTime > 0.5f)
            {
                lowFpsTimer = 0f;
                yield return null;
                continue;
            }

            float fps = 1f / Time.unscaledDeltaTime;

            if (fps < 45f)
            {
                lowFpsTimer += Time.unscaledDeltaTime;
                if (lowFpsTimer >= 2f)
                {
                    int currentBG = SettingsManager.Instance.GetSetting<int>("BGResolution", 1);
                    if (currentBG > 0)
                    {
                        SettingsManager.Instance.SetSetting("BGResolution", 0);
                        Debug.Log($"[Onboarding] FPS was below 45 for 3s (last: {fps:F0}), auto-reduced BG quality to 0.25x");
                        NotificationCenter.Info(
                            LocalizationManager.L("notification.bg_quality_reduced.title", "Background Quality Reduced"),
                            LocalizationManager.L("notification.bg_quality_reduced.info", "Background quality was automatically reduced to improve performance.")
                        );
                    }
                    yield break;
                }
            }
            else
            {
                lowFpsTimer = 0f;
            }

            yield return null;
        }
    }

#if UNITY_ANDROID
    private int _micPermissionAttempts;

    private void RequestMicPermission()
    {
        _micPermissionAttempts++;
        Debug.Log($"[Onboarding] Requesting mic permission (attempt {_micPermissionAttempts}/2)");

        var callbacks = new PermissionCallbacks();
        callbacks.PermissionGranted += _ =>
        {
            Debug.Log("[Onboarding] Microphone permission granted");
        };
        callbacks.PermissionDenied += _ =>
        {
            Debug.Log($"[Onboarding] Microphone permission denied (attempt {_micPermissionAttempts}/2)");
            if (_micPermissionAttempts < 2)
            {
                StartCoroutine(RetryMicPermission());
            }
            else
            {
                ShowMicPermissionWarning();
            }
        };
        callbacks.PermissionDeniedAndDontAskAgain += _ =>
        {
            Debug.Log("[Onboarding] Microphone permission denied permanently");
            ShowMicPermissionWarning();
        };
        Permission.RequestUserPermission(Permission.Microphone, callbacks);
    }

    private IEnumerator RetryMicPermission()
    {
        yield return new WaitForSeconds(0.5f);
        RequestMicPermission();
    }

    private void ShowMicPermissionWarning()
    {
        AlertManager.Instance.ShowWarning(
            LocalizationManager.L("alert.mic_permission.title", "Microphone Permission Required"),
            LocalizationManager.L("alert.mic_permission.info", "YASG needs microphone access to work. Please enable it in your device's app settings."),
            LocalizationManager.L("alert.dismiss", "Dismiss"),
            OpenAndroidAppSettings
        );
    }

    private static void OpenAndroidAppSettings()
    {
        try
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var intent = new AndroidJavaObject("android.content.Intent", "android.settings.APPLICATION_DETAILS_SETTINGS"))
            using (var uri = new AndroidJavaClass("android.net.Uri"))
            using (var uriData = uri.CallStatic<AndroidJavaObject>("parse", "package:" + Application.identifier))
            {
                intent.Call<AndroidJavaObject>("setData", uriData);
                activity.Call("startActivity", intent);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Onboarding] Failed to open app settings: {e.Message}");
        }
    }
#endif

    public void FromSettings()
    {
        PlayerPrefs.SetInt("fromSettings", 1);
    }

    public void AddProfile(string name)
    {
        Debug.Log(ProfileManager.Instance.GetProfileByName(name));
        if (ProfileManager.Instance.GetProfileByName(name) == null)
        {
            ProfileManager.Instance.CreateProfile(name);
        }
        //ProfileManager.Instance.AddProfileTotalScore(name, 750000);

        // During onboarding, auto-assign the default microphone
        if (PlayerPrefs.GetInt("onboarding") == 1)
        {
            string[] devices = Microphone.devices;
            if (devices.Length > 0)
            {
                ProfileManager.Instance.SetProfileMicrophone(name, devices[0]);
                Debug.Log($"[Onboarding] Auto-assigned microphone '{devices[0]}' to profile '{name}'");
            }
        }

        ProfileDisplay.Instance.InstantiateProfile(ProfileManager.Instance.GetProfileByName(name));
        GameObject temp = Instantiate(playerPrefab, playerContainer);
        temp.name = name;
    }

    public void CheckMultiplayer()
    {
        if (ProfileManager.Instance.GetActiveProfiles().Count == 0)
        {
            AlertManager.Instance.ShowError(LocalizationManager.L("alert.no_active_profiles.title", "You don't have any active profiles!"), LocalizationManager.L("alert.no_active_profiles.info", "Please go to the Settings (cogwheel on the bottom right) and either create a new profile or activate an existing one."), LocalizationManager.L("alert.dismiss", "Dismiss"));
            return;
        }

        if (ProfileManager.Instance.GetActiveProfiles().Count > 1)
        {
            AlertManager.Instance.ShowError(LocalizationManager.L("alert.too_many_profiles.title", "You have more than one active profile!"), LocalizationManager.L("alert.too_many_profiles.info", "Multiplayer supports only one profile per client. Please deactivate profiles that are not going to be used."), LocalizationManager.L("alert.dismiss", "Dismiss"));
            return;
        }

        foreach (var profile in ProfileManager.Instance.GetActiveProfiles())
        {
            if (profile.microphone == "Default")
            {
                AlertManager.Instance.ShowWarning(LocalizationManager.L("alert.micless_profiles.title", "There are micless profiles."), LocalizationManager.L("alert.micless_profiles.info", "One of your profiles has no microphone selected. If you aren't going to use said profile, please disable it."), LocalizationManager.L("alert.dismiss", "Dismiss"));
                return;
            }
        }

        // Close settings panel when opening multiplayer UI
        if (onSettings)
        {
            onSettings = false;
            settingsContainer.SetActive(false);
            settingsButton.GetComponent<Animator>().Play("SettingsOut");
            menuGO.SetActive(true);
        }

        multiplayerSet.SetActive(true);
        clickFX.Play();
    }

    public void SaveSettingToJson(string key, string value)
    {
        string dataPath = PlayerPrefs.GetString("dataPath");
        string filePath = Path.Combine(dataPath, "settings.json");
        Dictionary<string, string> settings = new Dictionary<string, string>();

        // Read existing settings if file exists
        if (File.Exists(filePath))
        {
            string json = File.ReadAllText(filePath, Encoding.UTF8);
            settings = JsonUtility.FromJson<SerializableDictionary>(json)?.ToDictionary() ?? new Dictionary<string, string>();
        }

        // Update or add the key-value pair
        settings[key] = value;

        // Serialize and write back to file
        string newJson = JsonUtility.ToJson(new SerializableDictionary(settings), true);
        File.WriteAllText(filePath, newJson, Encoding.UTF8);
    }

    public string GetSettingFromJson(string key)
    {
        string dataPath = PlayerPrefs.GetString("dataPath");
        string filePath = Path.Combine(dataPath, "settings.json");
        if (!File.Exists(filePath))
            return null;

        string json = File.ReadAllText(filePath, Encoding.UTF8);
        var settings = JsonUtility.FromJson<SerializableDictionary>(json)?.ToDictionary();
        if (settings != null && settings.ContainsKey(key))
            return settings[key];
        return null;
    }

    [System.Serializable]
    private class SerializableDictionary
    {
        public List<string> keys = new List<string>();
        public List<string> values = new List<string>();

        public SerializableDictionary() { }

        public SerializableDictionary(Dictionary<string, string> dict)
        {
            keys = dict.Keys.ToList();
            values = dict.Values.ToList();
        }

        public Dictionary<string, string> ToDictionary()
        {
            var dict = new Dictionary<string, string>();
            for (int i = 0; i < Mathf.Min(keys.Count, values.Count); i++)
            {
                dict[keys[i]] = values[i];
            }
            return dict;
        }
    }

}
