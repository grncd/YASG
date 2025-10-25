using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Linq; // For LINQ queries like .FirstOrDefault()

// --- Data Structures ---
[System.Serializable]
public class Profile
{
    public string name;
    public int level;
    public float progressRemaining; // Progress/♥5 towards next level (0-100 usually)
    public int difficulty; // 0, 1, or 2
    public string microphone;
    public int totalScore; // << NEW PROPERTY
    public int index;

    // Constructor for new profiles
    public Profile(string name, int level = 1, float progressRemaining = 0f, int difficulty = 1, string microphone = "Default", int totalScore = 0, int index = 0) // << ADDED
    {
        this.name = name;
        this.level = level;
        this.progressRemaining = progressRemaining;
        this.difficulty = Mathf.Clamp(difficulty, 0, 2);
        this.microphone = microphone;
        this.totalScore = totalScore; // << ADDED
        this.index = index; // 0 = disabled
    }
}

[System.Serializable]
public class ProfileDataContainer
{
    public List<Profile> profiles = new List<Profile>();
}

public class ProfileManager : MonoBehaviour
{
    // --- Singleton Instance ---
    public static ProfileManager Instance { get; private set; }

    // --- Constants ---
    private const string PROFILES_FILENAME = "profiles.json";
    private const string DATA_PATH_PREF_KEY = "dataPath"; // PlayerPrefs key for the custom data path
    private const string ACTIVE_PROFILE_PREF_KEY_PREFIX = "ActiveProfile_";
    private const int MAX_ACTIVE_PROFILES = 4;

    // --- Private Fields ---
    private ProfileDataContainer _profileDataContainer = new ProfileDataContainer();
    private List<string> _activeProfileNames = new List<string>();
    private string _profilesFilePath;

    // --- Public Properties (Read-only access to data) ---
    public IReadOnlyList<Profile> AllProfiles => _profileDataContainer.profiles.AsReadOnly();
    public IReadOnlyList<string> ActiveProfileNames => _activeProfileNames.AsReadOnly();


    void Awake()
    {
        // Singleton setup
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject); // Persist across scenes
            Initialize();
        }
        else
        {
            Destroy(gameObject); // Destroy duplicate
        }
    }

    private void Initialize()
    {
        // Determine file path
        string dataPathDir = PlayerPrefs.GetString(DATA_PATH_PREF_KEY, Application.persistentDataPath);
        if (string.IsNullOrEmpty(PlayerPrefs.GetString(DATA_PATH_PREF_KEY)))
        {
            Debug.LogWarning($"'{DATA_PATH_PREF_KEY}' not set in PlayerPrefs. Defaulting to: {Application.persistentDataPath}");
            PlayerPrefs.SetString(DATA_PATH_PREF_KEY, Application.persistentDataPath); // Set it for future reference
        }

        _profilesFilePath = Path.Combine(dataPathDir, PROFILES_FILENAME);
        Debug.Log($"Profile data path: {_profilesFilePath}");

        EnsureDirectoryExists(dataPathDir);

        LoadProfilesFromFile();
        LoadActiveProfilesFromPlayerPrefs();
    }

    public bool AddProfileTotalScore(string profileName, int score)
    {
        Profile profile = GetProfileByName(profileName);
        if (profile != null)
        {
            profile.totalScore += score;

            // Calculate total required XP for all previous levels
            int totalRequiredForPreviousLevels = 0;
            for (int lvl = 1; lvl < profile.level; lvl++)
            {
                totalRequiredForPreviousLevels += GetRequiredXPForLevel(lvl);
            }

            int requiredForThisLevel = GetRequiredXPForLevel(profile.level);

            // Handle level-ups
            while (profile.totalScore >= totalRequiredForPreviousLevels + requiredForThisLevel)
            {
                profile.level += 1;
                totalRequiredForPreviousLevels += requiredForThisLevel;
                requiredForThisLevel = GetRequiredXPForLevel(profile.level);
            }

            // Calculate progress percentage
            profile.progressRemaining = ((float)(profile.totalScore - totalRequiredForPreviousLevels) / requiredForThisLevel) * 100f;

            Debug.Log("Total score (cumulative): " + profile.totalScore);
            Debug.Log("Level: " + profile.level);
            Debug.Log("Progress towards next level (%): " + profile.progressRemaining);

            SaveProfilesToFile();
            return true;
        }

        Debug.LogWarning($"Profile '{profileName}' not found for setting total score.");
        return false;
    }

    public bool SetProfileTotalScore(string profileName, int score)
    {
        Profile profile = GetProfileByName(profileName);
        if (profile != null)
        {
            profile.totalScore = score;

            // Calculate total required XP for all previous levels
            int totalRequiredForPreviousLevels = 0;
            for (int lvl = 1; lvl < profile.level; lvl++)
            {
                totalRequiredForPreviousLevels += GetRequiredXPForLevel(lvl);
            }

            int requiredForThisLevel = GetRequiredXPForLevel(profile.level);

            // Handle level-ups
            while (profile.totalScore >= totalRequiredForPreviousLevels + requiredForThisLevel)
            {
                profile.level += 1;
                totalRequiredForPreviousLevels += requiredForThisLevel;
                requiredForThisLevel = GetRequiredXPForLevel(profile.level);
            }

            // Calculate progress percentage
            profile.progressRemaining = ((float)(profile.totalScore - totalRequiredForPreviousLevels) / requiredForThisLevel) * 100f;

            Debug.Log("Total score (cumulative): " + profile.totalScore);
            Debug.Log("Level: " + profile.level);
            Debug.Log("Progress towards next level (%): " + profile.progressRemaining);

            SaveProfilesToFile();
            return true;
        }

        Debug.LogWarning($"Profile '{profileName}' not found for setting total score.");
        return false;
    }

    private int GetRequiredXPForLevel(int level)
    {
        // First level starts at 1,750,000 XP, then increases by 750,000 per level
        return Mathf.RoundToInt(1350000f + 500000f * (level - 1));
    }


    private void EnsureDirectoryExists(string path)
    {
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
            Debug.Log($"Created directory: {path}");
        }
    }

    // --- File Operations ---
    private void LoadProfilesFromFile()
    {
        if (File.Exists(_profilesFilePath))
        {
            try
            {
                string json = File.ReadAllText(_profilesFilePath);
                _profileDataContainer = JsonUtility.FromJson<ProfileDataContainer>(json);
                if (_profileDataContainer == null || _profileDataContainer.profiles == null)
                {
                    _profileDataContainer = new ProfileDataContainer(); // Ensure it's initialized
                }
                Debug.Log($"Loaded {_profileDataContainer.profiles.Count} profiles from {_profilesFilePath}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Failed to load profiles: {e.Message}. Starting with an empty profile list.");
                _profileDataContainer = new ProfileDataContainer();
            }
        }
        else
        {
            Debug.Log("Profiles file not found. Starting with an empty profile list.");
            _profileDataContainer = new ProfileDataContainer();
        }
    }

    public void SaveProfilesToFile()
    {
        try
        {
            string json = JsonUtility.ToJson(_profileDataContainer, true); // True for pretty print
            File.WriteAllText(_profilesFilePath, json);
            Debug.Log($"Saved {_profileDataContainer.profiles.Count} profiles to {_profilesFilePath}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to save profiles: {e.Message}");
        }
    }

    // --- Profile Management ---
    public bool CreateProfile(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            Debug.LogError("Profile name cannot be empty.");
            return false;
        }
        if (_profileDataContainer.profiles.Any(p => p.name.Equals(name, System.StringComparison.OrdinalIgnoreCase)))
        {
            Debug.LogWarning($"Profile with name '{name}' already exists.");
            return false;
        }

        Profile newProfile = new Profile(name);
        _profileDataContainer.profiles.Add(newProfile);
        SaveProfilesToFile();
        Debug.Log($"Profile '{name}' created.");
        return true;
    }

    public bool RemoveProfile(string name)
    {
        Profile profileToRemove = GetProfileByName(name);
        if (profileToRemove != null)
        {
            _profileDataContainer.profiles.Remove(profileToRemove);
            DeactivateProfile(name); // Ensure it's deactivated if it was active
            SaveProfilesToFile();
            Debug.Log($"Profile '{name}' removed.");
            return true;
        }
        Debug.LogWarning($"Profile '{name}' not found for removal.");
        return false;
    }

    public Profile GetProfileByName(string name)
    {
        return _profileDataContainer.profiles.FirstOrDefault(p => p.name.Equals(name, System.StringComparison.OrdinalIgnoreCase));
    }

    // --- Change Profile Properties ---
    // You can make these more generic with reflection, but direct methods are often safer and clearer.
    public bool SetProfileLevel(string profileName, int level)
    {
        Profile profile = GetProfileByName(profileName);
        if (profile != null)
        {
            profile.level = Mathf.Max(1, level); // Level should be at least 1
            SaveProfilesToFile();
            return true;
        }
        return false;
    }

    public bool SetProfileProgress(string profileName, float progress)
    {
        Profile profile = GetProfileByName(profileName);
        if (profile != null)
        {
            profile.progressRemaining = Mathf.Max(0, progress);
            SaveProfilesToFile();
            return true;
        }
        return false;
    }

    public bool SetProfileDifficulty(string profileName, int difficulty)
    {
        Profile profile = GetProfileByName(profileName);
        if (profile != null)
        {
            profile.difficulty = Mathf.Clamp(difficulty, 0, 2);
            SaveProfilesToFile();
            return true;
        }
        return false;
    }

    public bool SetProfileIndex(string profileName, int index)
    {
        Profile profile = GetProfileByName(profileName);
        if (profile != null)
        {
            profile.index = index;
            SaveProfilesToFile();
            return true;
        }
        return false;
    }

    public bool SetProfileMicrophone(string profileName, string microphone)
    {
        Profile profile = GetProfileByName(profileName);
        if (profile != null)
        {
            profile.microphone = microphone;
            SaveProfilesToFile();
            return true;
        }
        return false;
    }

    // --- Active Profile Management ---
    private void LoadActiveProfilesFromPlayerPrefs()
    {
        _activeProfileNames.Clear();
        for (int i = 0; i < MAX_ACTIVE_PROFILES; i++)
        {
            string profileName = PlayerPrefs.GetString(ACTIVE_PROFILE_PREF_KEY_PREFIX + i, null);
            if (!string.IsNullOrEmpty(profileName) && GetProfileByName(profileName) != null) // Ensure profile still exists
            {
                _activeProfileNames.Add(profileName);
            }
        }
        Debug.Log($"Loaded {_activeProfileNames.Count} active profiles from PlayerPrefs.");
    }

    private void SaveActiveProfilesToPlayerPrefs()
    {
        for (int i = 0; i < MAX_ACTIVE_PROFILES; i++)
        {
            if (i < _activeProfileNames.Count)
            {
                //PlayerPrefs.SetString(ACTIVE_PROFILE_PREF_KEY_PREFIX + i, _activeProfileNames[i]);
            }
            else
            {
                //PlayerPrefs.DeleteKey(ACTIVE_PROFILE_PREF_KEY_PREFIX + i); // Clear unused slots
            }
        }
        //PlayerPrefs.Save(); // Persist PlayerPrefs changes
        Debug.Log($"Saved {_activeProfileNames.Count} active profiles to PlayerPrefs.");
    }

    public int ActivateProfile(string name)
    {
        if (GetProfileByName(name) == null)
        {
            Debug.LogWarning($"Cannot activate profile '{name}': Profile does not exist.");
            return 0;
        }
        if (_activeProfileNames.Contains(name))
        {
            Debug.Log($"Profile '{name}' is already active.");
            if(PlayerPrefs.GetInt("fromSettings") == 1)
            {
                AlertManager.Instance.ShowInfo("This profile is already active.", "The profile you picked is already active. Please choose/create another one by clicking the Add Profile button.", "Dismiss");
            }
            return 0;
        }
        if (_activeProfileNames.Count >= MAX_ACTIVE_PROFILES)
        {
            Debug.LogWarning($"Cannot activate profile '{name}': Maximum number of active profiles ({MAX_ACTIVE_PROFILES}) reached.");
            return 0;
        }

        _activeProfileNames.Add(name);
        GetProfileByName(name).index = _activeProfileNames.Count;
        SaveActiveProfilesToPlayerPrefs();

        // Sync PlayerPrefs for the newly activated profile
        int newIndex = _activeProfileNames.Count;
        PlayerPrefs.SetInt("Player" + newIndex, 1);
        PlayerPrefs.SetString("Player" + newIndex + "Name", name);
        PlayerPrefs.Save();

        Debug.Log($"Profile '{name}' activated at index {newIndex}.");
        return _activeProfileNames.Count;
    }

    public bool DeactivateProfile(string name)
    {
        if (!_activeProfileNames.Contains(name))
        {
            Debug.Log($"Profile '{name}' is not active.");
            return false;
        }
        GetProfileByName(name).index = 0;
        _activeProfileNames.Remove(name);

        // Reindex all remaining active profiles to maintain sequential order
        ReindexActiveProfiles();

        SaveActiveProfilesToPlayerPrefs();
        Debug.Log($"Profile '{name}' deactivated.");
        return true;
    }

    /// <summary>
    /// Reindexes all active profiles sequentially (1, 2, 3, 4).
    /// This ensures there are no gaps in the index numbers.
    /// </summary>
    private void ReindexActiveProfiles()
    {
        for (int i = 0; i < _activeProfileNames.Count; i++)
        {
            Profile profile = GetProfileByName(_activeProfileNames[i]);
            if (profile != null)
            {
                profile.index = i + 1; // 1-based indexing
                Debug.Log($"Reindexed profile '{profile.name}' to index {profile.index}");
            }
        }

        // Synchronize PlayerPrefs with new indices
        SyncPlayerPrefsWithIndices();
    }

    /// <summary>
    /// Updates PlayerPrefs to match current profile indices.
    /// Clears unused player slots and updates active ones.
    /// </summary>
    private void SyncPlayerPrefsWithIndices()
    {
        // Clear all player slots first
        for (int i = 1; i <= MAX_ACTIVE_PROFILES; i++)
        {
            PlayerPrefs.SetInt("Player" + i, 0);
            PlayerPrefs.DeleteKey("Player" + i + "Name");
            PlayerPrefs.DeleteKey("Player" + i + "Mic");
        }

        // Set active profiles
        foreach (string profileName in _activeProfileNames)
        {
            Profile profile = GetProfileByName(profileName);
            if (profile != null && profile.index > 0)
            {
                PlayerPrefs.SetInt("Player" + profile.index, 1);
                PlayerPrefs.SetString("Player" + profile.index + "Name", profile.name);
                // Note: Microphone index should be set by MicDropdownUI
                Debug.Log($"Synced PlayerPrefs for '{profile.name}' at index {profile.index}");
            }
        }

        PlayerPrefs.Save();
    }

    /// <summary>
    /// Gets the current active index of a profile (1-4), or 0 if not active.
    /// </summary>
    public int GetProfileActiveIndex(string name)
    {
        Profile profile = GetProfileByName(name);
        return profile?.index ?? 0;
    }

    public bool IsProfileActive(string name)
    {
        return _activeProfileNames.Contains(name);
    }

    public List<Profile> GetActiveProfiles()
    {
        List<Profile> activeProfiles = new List<Profile>();
        foreach (string name in _activeProfileNames)
        {
            Profile p = GetProfileByName(name);
            if (p != null) activeProfiles.Add(p);
        }
        return activeProfiles;
    }

    // --- UI / Utility Functions ---
    public List<Profile> PrintAllProfileData()
    {
        return _profileDataContainer.profiles;
    }

    public bool UpdateProfileName(string oldName, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
        {
            Debug.LogError("New profile name cannot be empty.");
            return false;
        }

        Profile profileToUpdate = GetProfileByName(oldName);
        if (profileToUpdate == null)
        {
            Debug.LogWarning($"Profile with name '{oldName}' not found for renaming.");
            return false;
        }

        // Check if newName is different from oldName AND if newName is already taken by another profile
        if (!oldName.Equals(newName, System.StringComparison.OrdinalIgnoreCase) &&
            _profileDataContainer.profiles.Any(p => p.name.Equals(newName, System.StringComparison.OrdinalIgnoreCase)))
        {
            Debug.LogWarning($"Cannot rename profile. Another profile with name '{newName}' already exists.");
            return false;
        }

        // If the name isn't actually changing, just return true (no actual update needed)
        if (oldName.Equals(newName, System.StringComparison.OrdinalIgnoreCase) && profileToUpdate.name.Equals(newName))
        {
            return true;
        }

        bool wasActive = IsProfileActive(oldName);
        List<string> tempActiveNames = new List<string>(_activeProfileNames);

        if (wasActive)
        {
            // Remove the old name if it's different and was present
            if (!oldName.Equals(newName, System.StringComparison.OrdinalIgnoreCase))
            {
                tempActiveNames.Remove(oldName);
            }
        }

        profileToUpdate.name = newName; // Update the name in the Profile object

        if (wasActive)
        {
            // Add the new name if it was active and not already in the list (or if it's different from old)
            if (!tempActiveNames.Contains(newName))
            {
                tempActiveNames.Add(newName);
            }
            _activeProfileNames = new List<string>(tempActiveNames); // Update the main list
            SaveActiveProfilesToPlayerPrefs(); // Save changes to active profiles
        }

        SaveProfilesToFile(); // Save all profile data (including the name change in the profiles list)
        Debug.Log($"Profile formerly known as '{oldName}' updated to name '{newName}'.");
        return true;
    }

    /// <summary>
    /// Gets a list of available microphone device names.
    /// Useful for populating a dropdown in the UI.
    /// </summary>
    public string[] GetAvailableMicrophones()
    {
#if !UNITY_WEBGL // Microphone.devices is not supported on WebGL
        return Microphone.devices;
#else
            Debug.LogWarning("Microphone.devices not available on WebGL. Returning empty array.");
            return new string[0];
#endif
    }

    private void Start()
    {
        
    }


    // --- Example Usage (Optional - for testing) ---
    /*
    void Start() // Start is called after Awake
    {
        // --- Test Scenario ---
        // PlayerPrefs.SetString(DATA_PATH_PREF_KEY, Path.Combine(Application.persistentDataPath, "MyGameProfiles")); // Example of setting custom path
        // PlayerPrefs.Save(); // Make sure to save if you set it programmatically before ProfileManager's Awake

        // Create some profiles if they don't exist
        if (GetProfileByName("PlayerOne") == null) CreateProfile("PlayerOne");
        if (GetProfileByName("PlayerTwo") == null) CreateProfile("PlayerTwo");
        if (GetProfileByName("Guest") == null) CreateProfile("Guest");

        // Set some properties
        SetProfileLevel("PlayerOne", 5);
        SetProfileDifficulty("PlayerOne", 2);
        SetProfileMicrophone("PlayerOne", "Realtek HD Audio");

        SetProfileProgress("PlayerTwo", 75.5f);

        // Activate profiles
        ActivateProfile("PlayerOne");
        ActivateProfile("Guest");
        // Try to activate a fifth (should fail if MAX_ACTIVE_PROFILES is 4)
        if(GetProfileByName("TestProfileMax") == null) CreateProfile("TestProfileMax");
        if(GetProfileByName("TestProfileMax2") == null) CreateProfile("TestProfileMax2");
        if(GetProfileByName("TestProfileMax3") == null) CreateProfile("TestProfileMax3");

        ActivateProfile("TestProfileMax"); // 3rd
        ActivateProfile("TestProfileMax2"); // 4th
        ActivateProfile("TestProfileMax3"); // 5th - should fail

        PrintAllProfileData();

        Debug.Log("--- Active Profiles ---");
        foreach(Profile p in GetActiveProfiles())
        {
            Debug.Log($"Active: {p.name}");
        }
    }
    */
}