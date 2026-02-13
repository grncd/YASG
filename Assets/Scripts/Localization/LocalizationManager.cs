using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using System;

public class LocalizationManager : MonoBehaviour
{
    private static LocalizationManager _instance;
    public static LocalizationManager Instance => _instance;

    public static event Action OnLanguageChanged;

    // Static dictionaries that L() reads from directly — no _instance dependency.
    // This fixes builds where _instance can be null/stale when dynamic code calls L().
    private static Dictionary<string, string> _staticTranslations = new Dictionary<string, string>();
    private static Dictionary<string, string> _staticFallback = new Dictionary<string, string>();
    private static string _staticCurrentLanguage = "en";

    private Dictionary<string, string> _translations = new Dictionary<string, string>();
    private Dictionary<string, string> _fallbackTranslations = new Dictionary<string, string>();
    private string _currentLanguage = "en";
    private List<string> _availableLanguageCodes = new List<string>();

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(_instance.gameObject);
        }
        _instance = this;
        DontDestroyOnLoad(gameObject);

        DiscoverLanguages();

        // Read language directly from PlayerPrefs to avoid depending on
        // SettingsManager's initialization order (which is undefined in builds)
        string lang = PlayerPrefs.GetString("yasg_language", "en");
        if (!_availableLanguageCodes.Contains(lang))
            lang = "en";
        LoadLanguage(lang);

        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Wait one frame so all components are initialized, then force-update everything
        StartCoroutine(RefireLanguageChanged());
    }

    private IEnumerator RefireLanguageChanged()
    {
        yield return null;
        UpdateAllLocalizedText();
        FireLanguageChanged();
    }

    private void DiscoverLanguages()
    {
        _availableLanguageCodes.Clear();
        var textAssets = Resources.LoadAll<TextAsset>("Localization");
        foreach (var asset in textAssets)
        {
            _availableLanguageCodes.Add(asset.name);
        }
        if (_availableLanguageCodes.Count == 0)
            _availableLanguageCodes.Add("en");

        // Ensure "en" is first
        _availableLanguageCodes.Sort((a, b) =>
        {
            if (a == "en") return -1;
            if (b == "en") return 1;
            return string.Compare(a, b, StringComparison.Ordinal);
        });
    }

    public void LoadLanguage(string languageCode)
    {
        _currentLanguage = languageCode;
        PlayerPrefs.SetString("yasg_language", languageCode);
        PlayerPrefs.Save();
        _translations.Clear();

        // Always load English as fallback
        if (_fallbackTranslations.Count == 0 || languageCode == "en")
        {
            var enAsset = Resources.Load<TextAsset>("Localization/en");
            if (enAsset != null)
                _fallbackTranslations = JsonConvert.DeserializeObject<Dictionary<string, string>>(enAsset.text)
                                        ?? new Dictionary<string, string>();
        }

        if (languageCode == "en")
        {
            _translations = new Dictionary<string, string>(_fallbackTranslations);
        }
        else
        {
            var textAsset = Resources.Load<TextAsset>("Localization/" + languageCode);
            if (textAsset != null)
                _translations = JsonConvert.DeserializeObject<Dictionary<string, string>>(textAsset.text)
                                ?? new Dictionary<string, string>();
            else
                Debug.LogWarning($"[LocalizationManager] Language file not found: {languageCode}");
        }

        // Copy to static dictionaries so L() works without _instance
        _staticTranslations = new Dictionary<string, string>(_translations);
        _staticFallback = new Dictionary<string, string>(_fallbackTranslations);
        _staticCurrentLanguage = languageCode;

        Debug.Log($"[LocalizationManager] Loaded '{languageCode}' — {_translations.Count} keys, {_fallbackTranslations.Count} fallback keys");

        // Force-update ALL LocalizedText in the scene (including inactive objects)
        UpdateAllLocalizedText();
        FireLanguageChanged();
    }

    /// <summary>
    /// Finds every LocalizedText component in the scene (including on inactive GameObjects)
    /// and forces a text update. This bypasses the event system to guarantee translation.
    /// </summary>
    private static void UpdateAllLocalizedText()
    {
        var allTexts = FindObjectsByType<LocalizedText>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var lt in allTexts)
        {
            try
            {
                lt.UpdateText();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LocalizationManager] Failed to update LocalizedText on '{lt.gameObject.name}': {e.Message}");
            }
        }
    }

    private static void FireLanguageChanged()
    {
        if (OnLanguageChanged == null) return;
        foreach (var handler in OnLanguageChanged.GetInvocationList())
        {
            try
            {
                ((Action)handler).Invoke();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LocalizationManager] Removing dead language-changed subscriber: {e.Message}");
                OnLanguageChanged -= (Action)handler;
            }
        }
    }

    public string GetTranslation(string key, string fallback = null)
    {
        if (string.IsNullOrEmpty(key)) return fallback ?? "";
        if (_translations.TryGetValue(key, out string value))
            return value;
        if (_fallbackTranslations.TryGetValue(key, out string fbValue))
            return fbValue;
        return fallback ?? key;
    }

    /// <summary>
    /// Static shorthand for GetTranslation. Uses static dictionaries directly —
    /// does NOT depend on _instance, so it works reliably in builds regardless
    /// of MonoBehaviour lifecycle timing.
    /// </summary>
    public static string L(string key, string fallback = null)
    {
        if (string.IsNullOrEmpty(key)) return fallback ?? "";
        if (_staticTranslations.TryGetValue(key, out string value))
            return value;
        if (_staticFallback.TryGetValue(key, out string fbValue))
            return fbValue;
        return fallback ?? key;
    }

    public string CurrentLanguage => _currentLanguage;
    public static string CurrentLanguageCode => _staticCurrentLanguage;
    public List<string> AvailableLanguageCodes => _availableLanguageCodes;

    /// <summary>
    /// Gets display names for all available languages by reading _language_name from each file.
    /// </summary>
    public List<string> GetLanguageDisplayNames()
    {
        var names = new List<string>();
        foreach (var lang in _availableLanguageCodes)
        {
            var asset = Resources.Load<TextAsset>("Localization/" + lang);
            if (asset != null)
            {
                var dict = JsonConvert.DeserializeObject<Dictionary<string, string>>(asset.text);
                if (dict != null && dict.TryGetValue("_language_name", out string name))
                {
                    names.Add(name);
                    continue;
                }
            }
            names.Add(lang);
        }
        return names;
    }
}
