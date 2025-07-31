
using UnityEngine;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using System.Linq;
using System;

/// <summary>
/// Defines the categories for settings.
/// </summary>
public enum SettingCategory
{
    Gameplay,
    Processing,
    Misc
}

/// <summary>
/// Defines the UI element type for a setting.
/// </summary>
public enum UIType
{
    TextInput,
    Dropdown,
    Toggle
}

/// <summary>
/// Represents a single setting, including its value and category.
/// </summary>
public class Setting
{
    public object Value { get; set; }
    public SettingCategory Category { get; set; }
    public bool IsHidden { get; set; }
    public UIType UIType { get; set; }
    public string FormalName { get; set; }
    public string Description { get; set; }
    public List<string> DropdownOptions { get; set; } = new List<string>();
}

/// <summary>
/// Manages game settings, persisting them to a JSON file.
/// This is a Singleton and should be placed on a persistent GameObject in your initial scene.
/// </summary>
public class SettingsManager : MonoBehaviour
{
    // Singleton instance
    private static SettingsManager _instance;
    public static SettingsManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindObjectOfType<SettingsManager>();
                if (_instance == null)
                {
                    GameObject singletonObject = new GameObject("SettingsManager");
                    _instance = singletonObject.AddComponent<SettingsManager>();
                }
            }
            return _instance;
        }
    }

    private Dictionary<string, Setting> _settings = new Dictionary<string, Setting>();
    public string _settingsFilePath;

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(this.gameObject);
            return;
        }
        _instance = this;
        DontDestroyOnLoad(this.gameObject);

        string dataPath = PlayerPrefs.GetString("dataPath", Application.persistentDataPath);
        _settingsFilePath = Path.Combine(dataPath, "settings.json");
        LoadSettings();
    }

    /// <summary>
    /// Loads settings from the JSON file. Handles migration from old format if necessary.
    /// </summary>
    private void LoadSettings()
    {
        if (File.Exists(_settingsFilePath))
        {
            string json = File.ReadAllText(_settingsFilePath);
            try
            {
                // Try to deserialize to the new format
                var newSettings = JsonConvert.DeserializeObject<Dictionary<string, Setting>>(json);
                if (newSettings != null && newSettings.Count > 0)
                {
                    _settings = newSettings;
                }
                else
                {
                    // Handle case where file is empty or invalid
                    InitializeDefaultSettings();
                }
            }
            catch (JsonSerializationException)
            {
                // This likely means it's the old format.
                Debug.Log("Old settings format detected. Migrating to new format.");
                var oldSettings = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                InitializeDefaultSettings(); // Start with default structure
                if (oldSettings != null)
                {
                    foreach (var setting in oldSettings)
                    {
                        // Update value if the key exists in the new structure
                        if (_settings.ContainsKey(setting.Key))
                        {
                            _settings[setting.Key].Value = setting.Value;
                        }
                    }
                }
                SaveSettings(); // Save the migrated settings
            }
        }
        else
        {
            Debug.Log("Settings file not found. Creating a new one with default values.");
            InitializeDefaultSettings();
            SaveSettings();
        }
    }

    /// <summary>
    /// Initializes the settings with default values and categories.
    /// </summary>
    private void InitializeDefaultSettings()
    {
        _settings = new Dictionary<string, Setting>
        {
            // Gameplay
            // DONE
            { "ShowDetectedPitch", new Setting { Value = true, Category = SettingCategory.Gameplay, IsHidden = false, UIType = UIType.Toggle, FormalName = "Show Detected Pitch", Description = "If toggled on, shows the current detected pitch of the song's vocals and the user's microphone."  } },
            // DONE
            { "AudioReactivePlayerCircle", new Setting { Value = true, Category = SettingCategory.Gameplay, IsHidden = false, UIType = UIType.Toggle, FormalName = "Audio-Reactive Player Circle", Description = "If toggled on, adds a reactive glow around the judgment circle."  } },

            // Processing
            // DONE
            { "PitchProcessingQuality", new Setting { Value = 2, Category = SettingCategory.Processing, IsHidden = false, UIType = UIType.Dropdown, FormalName = "Pitch Processing Quality", Description = "Higher means more accurate, but the pitch processing stage will take longer.", DropdownOptions = new List<string> { "Low", "Medium", "High" } } },
            // DONE
            { "PitchDetectionQuality", new Setting { Value = 2, Category = SettingCategory.Processing, IsHidden = false, UIType = UIType.Dropdown, FormalName = "Real-Time Pitch Detection Quality", Description = "Turn this down if your FPS is dropping when singing in game. Only recommended to turn this up if you have a very low pitched voice or want precise pitch detection.", DropdownOptions = new List<string> { "Low", "Medium", "High", "Very High" } } },
            // DONE
            { "VocalProcessingMethod", new Setting { Value = 0, Category = SettingCategory.Processing, IsHidden = false, UIType = UIType.Dropdown, FormalName = "Vocal Processing Method", Description = "Method used to extract vocals from the song. Only use vocalremover.org if you don't have a (good) GPU. Otherwise, use Demucs.", DropdownOptions = new List<string> { "VocalRemover.org", "Demucs" } } },

            // Misc

            { "MenuMusic", new Setting { Value = 1, Category = SettingCategory.Misc, IsHidden = false, UIType = UIType.Dropdown, FormalName = "Menu Music", Description = "Defines the song that will be played in the menu.", DropdownOptions = new List<string> { "None","Default","Random selection from downloaded songs" } } },
            // DONE
            { "MenuBG", new Setting { Value = 3, Category = SettingCategory.Misc, IsHidden = false, UIType = UIType.Dropdown, FormalName = "Menu Background", Description = "Defines the background that will be displayed in the menu.", DropdownOptions = new List<string> { "Rainbow Vortex", "Abstract", "Rainbow Tunnel", "Landing Planet" } } },
            // Done
            { "InGameBG", new Setting { Value = 3, Category = SettingCategory.Misc, IsHidden = false, UIType = UIType.Dropdown, FormalName = "In-Game Background", Description = "Defines the background that will be displayed in-game.", DropdownOptions = new List<string> { "None", "Rainbow Vortex", "Abstract", "Rainbow Tunnel", "Landing Planet" } } },
            // DONE
            { "AudioReactiveBGInGame", new Setting { Value = true, Category = SettingCategory.Misc, IsHidden = false, UIType = UIType.Toggle, FormalName = "Audio-Reactive Background", Description = "Defines if the background will be audio-reactive or not. Currently, this only works if you are using the Rainbow Tunnel BG."  } },
            // DONE
            { "SpotifySpDc", new Setting { Value = "", Category = SettingCategory.Misc, IsHidden = true, UIType = UIType.TextInput, FormalName = "Spotify sp_dc Cookie", Description = "Spotify cookie used to extract lyrics."  } },
            // DONE
            { "SpotifyApiKey", new Setting { Value = "", Category = SettingCategory.Misc, IsHidden = true, UIType = UIType.TextInput, FormalName = "Spotify API Key", Description = "Spotify API key used to retrieve search results and song information."  } },
        };
    }


    /// <summary>
    /// Saves the current settings to the settings.json file.
    /// </summary>
    private void SaveSettings()
    {
        string json = JsonConvert.SerializeObject(_settings, Formatting.Indented);
        File.WriteAllText(_settingsFilePath, json);
    }

    /// <summary>
    /// Retrieves a setting value by its key.
    /// </summary>
    public T GetSetting<T>(string key, T defaultValue = default)
    {
        if (_settings.TryGetValue(key, out Setting setting))
        {
            try
            {
                return (T)System.Convert.ChangeType(setting.Value, typeof(T));
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Failed to cast setting '{key}' to type {typeof(T)}. Error: {ex.Message}");
                return defaultValue;
            }
        }
        return defaultValue;
    }

    /// <summary>
    /// Sets a setting value by its key and saves the changes.
    /// If the setting does not exist, it will be added with the 'Misc' category.
    /// </summary>
    public void SetSetting(string key, object value)
    {
        if (_settings.TryGetValue(key, out Setting setting))
        {
            if(key == "VocalProcessingMethod")
            {
                if(PlayerPrefs.GetInt("demucsInstalled") != 1 && Convert.ToInt32(value) == 1 && PlayerPrefs.GetInt("setupDone") == 1)
                {
                    LevelResourcesCompiler.Instance.RunFullInstall();
                }
            }
            setting.Value = value;
        }
        else
        {
            _settings.Add(key, new Setting { Value = value, Category = SettingCategory.Misc, UIType = UIType.TextInput, FormalName = key });
        }
        SaveSettings();
    }

    /// <summary>
    /// Retrieves the category of a specific setting.
    /// </summary>
    public SettingCategory? GetSettingCategory(string key)
    {
        if (_settings.TryGetValue(key, out Setting setting))
        {
            return setting.Category;
        }
        return null;
    }

    /// <summary>
    /// Retrieves all settings belonging to a specific category.
    /// </summary>
    public Dictionary<string, Setting> GetSettingsByCategory(SettingCategory category, bool includeHidden = false)
    {
        return _settings.Where(s => s.Value.Category == category && (includeHidden || !s.Value.IsHidden))
                        .ToDictionary(s => s.Key, s => s.Value);
    }

    /// <summary>
    /// Checks if a setting is marked as hidden.
    /// </summary>
    public bool IsSettingHidden(string key)
    {
        if (_settings.TryGetValue(key, out Setting setting))
        {
            return setting.IsHidden;
        }
        return false; // Default to not hidden if key not found
    }

    /// <summary>
    /// Sets the visibility of a specific setting.
    /// </summary>
    public void SetSettingVisibility(string key, bool isHidden)
    {
        if (_settings.TryGetValue(key, out Setting setting))
        {
            setting.IsHidden = isHidden;
            SaveSettings();
        }
    }

    /// <summary>
    /// Retrieves the UI type of a specific setting.
    /// </summary>
    public UIType? GetSettingUIType(string key)
    {
        if (_settings.TryGetValue(key, out Setting setting))
        {
            return setting.UIType;
        }
        return null;
    }

    /// <summary>
    /// Retrieves the formal name of a specific setting.
    /// </summary>
    public string GetSettingFormalName(string key)
    {
        if (_settings.TryGetValue(key, out Setting setting))
        {
            return setting.FormalName;
        }
        return null;
    }

    /// <summary>
    /// Sets the formal name of a specific setting.
    /// </summary>
    public void SetSettingFormalName(string key, string formalName)
    {
        if (_settings.TryGetValue(key, out Setting setting))
        {
            setting.FormalName = formalName;
            SaveSettings();
        }
    }

    /// <summary>
    /// Retrieves the dropdown options for a specific setting.
    /// </summary>
    public List<string> GetSettingDropdownOptions(string key)
    {
        if (_settings.TryGetValue(key, out Setting setting) && setting.UIType == UIType.Dropdown)
        {
            return setting.DropdownOptions;
        }
        return new List<string>();
    }

    /// <summary>
    /// Sets the dropdown options for a specific setting.
    /// </summary>
    public void SetSettingDropdownOptions(string key, List<string> options)
    {
        if (_settings.TryGetValue(key, out Setting setting) && setting.UIType == UIType.Dropdown)
        {
            setting.DropdownOptions = options;
            SaveSettings();
        }
    }
}
