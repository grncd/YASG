using UnityEngine;
using System.Collections.Generic;
using Newtonsoft.Json;
using System;

public class LocalizationManager : MonoBehaviour
{
    private static LocalizationManager _instance;
    public static LocalizationManager Instance => _instance;

    public static event Action OnLanguageChanged;

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

        string lang = "en";
        if (SettingsManager.Instance != null)
        {
            int langIndex = SettingsManager.Instance.GetSetting<int>("Language", 0);
            if (langIndex >= 0 && langIndex < _availableLanguageCodes.Count)
                lang = _availableLanguageCodes[langIndex];
        }
        LoadLanguage(lang);
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

        OnLanguageChanged?.Invoke();
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
    /// Static shorthand for GetTranslation. Safe to call even if Instance is null.
    /// </summary>
    public static string L(string key, string fallback = null)
    {
        if (_instance == null) return fallback ?? key;
        return _instance.GetTranslation(key, fallback);
    }

    public string CurrentLanguage => _currentLanguage;
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
