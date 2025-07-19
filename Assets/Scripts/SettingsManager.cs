
using UnityEngine;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

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
                // Find existing instance
                _instance = FindObjectOfType<SettingsManager>();

                // Create new instance if one doesn't exist
                if (_instance == null)
                {
                    GameObject singletonObject = new GameObject("SettingsManager");
                    _instance = singletonObject.AddComponent<SettingsManager>();
                }
            }
            return _instance;
        }
    }

    private Dictionary<string, object> _settings = new Dictionary<string, object>();
    private string _settingsFilePath;

    private void Awake()
    {
        // Singleton pattern implementation
        if (_instance != null && _instance != this)
        {
            Destroy(this.gameObject);
            return;
        }
        _instance = this;
        DontDestroyOnLoad(this.gameObject);

        // Set the file path and load settings
        string dataPath = PlayerPrefs.GetString("dataPath", Application.persistentDataPath);
        _settingsFilePath = Path.Combine(dataPath, "settings.json");
        LoadSettings();
    }

    /// <summary>
    /// Loads settings from the settings.json file.
    /// If the file doesn't exist, it creates a default one.
    /// </summary>
    private void LoadSettings()
    {
        if (File.Exists(_settingsFilePath))
        {
            string json = File.ReadAllText(_settingsFilePath);
            // Using Newtonsoft.Json for robust deserialization
            _settings = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
            if (_settings == null)
            {
                _settings = new Dictionary<string, object>();
            }
        }
        else
        {
            Debug.Log("Settings file not found. Creating a new one with default values.");
            // Initialize with default settings if the file doesn't exist
            _settings = new Dictionary<string, object>
            {
                { "MasterVolume", 1f },
                { "MusicVolume", 1.0f },
                { "SFXVolume", 1.0f },
                { "PitchProcessingQuality", 2 },
                { "PitchDetectionQuality", 3 },
                { "MenuBG", 3 },
                { "InGameBG", 3 },
                { "ShowDetectedPitch", true },
                { "AudioReactivePlayerCircle", true },
                { "AudioReactiveBGInGame", true },
                { "VocalProcessingMethod", 1 },
                { "SpotifySpDc", "" },
                { "SpotifyApiKey", "" },
            };
            SaveSettings();
        }
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
    /// <typeparam name="T">The type of the value to retrieve.</typeparam>
    /// <param name="key">The key of the setting.</param>
    /// <param name="defaultValue">The default value to return if the key is not found.</param>
    /// <returns>The setting value, or the default value if not found.</returns>
    public T GetSetting<T>(string key, T defaultValue = default)
    {
        if (_settings.TryGetValue(key, out object value))
        {
            try
            {
                // Convert the object to the desired type
                return (T)System.Convert.ChangeType(value, typeof(T));
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
    /// </summary>
    /// <param name="key">The key of the setting.</param>
    /// <param name="value">The value to set.</param>
    public void SetSetting(string key, object value)
    {
        if (_settings.ContainsKey(key))
        {
            _settings[key] = value;
        }
        else
        {
            _settings.Add(key, value);
        }
        SaveSettings();
    }
}
