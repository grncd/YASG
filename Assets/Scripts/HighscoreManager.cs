// HighscoreManager.cs
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using System.Linq;

// Data structure for a single highscore entry
[System.Serializable]
public class HighscoreEntry
{
    public string songUrl; // Using the song's URL as a unique ID
    public string playerName;
    public int score;
    public int stars;
}

// Wrapper class for easy JSON serialization
[System.Serializable]
public class HighscoreData
{
    public List<HighscoreEntry> highscores = new List<HighscoreEntry>();
}

public class HighscoreManager
{
    private static HighscoreManager _instance;
    public static HighscoreManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = new HighscoreManager();
            }
            return _instance;
        }
    }

    private HighscoreData highscoreData;
    private readonly string savePath;

    // The constructor is called when the singleton instance is first created
    private HighscoreManager()
    {
        string dataPath = PlayerPrefs.GetString("dataPath");
        if (string.IsNullOrEmpty(dataPath))
        {
            Debug.LogError("Data path is not set in PlayerPrefs! HighscoreManager cannot initialize.");
            return;
        }
        savePath = Path.Combine(dataPath, "highscores.json");
        LoadHighscores();
    }

    private void LoadHighscores()
    {
        if (File.Exists(savePath))
        {
            string json = File.ReadAllText(savePath);
            highscoreData = JsonUtility.FromJson<HighscoreData>(json);
            if (highscoreData == null) // In case of a corrupted or empty file
            {
                highscoreData = new HighscoreData();
            }
        }
        else
        {
            highscoreData = new HighscoreData();
        }
    }

    private void SaveHighscores()
    {
        string json = JsonUtility.ToJson(highscoreData, true);
        File.WriteAllText(savePath, json);
        Debug.Log("Highscores saved to: " + savePath);
    }

    /// <summary>
    /// Gets the highscore for a specific song.
    /// </summary>
    /// <param name="songUrl">The unique URL of the song.</param>
    /// <returns>The HighscoreEntry object, or null if no highscore exists.</returns>
    public HighscoreEntry GetHighscore(string songUrl)
    {
        return highscoreData.highscores.FirstOrDefault(hs => hs.songUrl == songUrl);
    }

    /// <summary>
    /// Checks if a new score is a highscore and updates it if it is.
    /// </summary>
    /// <param name="songUrl">The unique URL of the song.</param>
    /// <param name="playerName">The name of the player who set the score.</param>
    /// <param name="score">The score achieved.</param>
    /// <param name="stars">The number of stars earned.</param>
    /// <returns>True if a new highscore was set, otherwise false.</returns>
    public bool SetHighscore(string songUrl, string playerName, int score, int stars)
    {
        HighscoreEntry existingHighscore = GetHighscore(songUrl);

        if (existingHighscore != null)
        {
            // Update if the new score is higher
            if (score > existingHighscore.score)
            {
                Debug.Log($"New highscore for {songUrl}! {playerName} beat the old score of {existingHighscore.score} with {score}.");
                existingHighscore.playerName = playerName;
                existingHighscore.score = score;
                existingHighscore.stars = stars;
                SaveHighscores();
                return true; // New highscore set
            }
        }
        else
        {
            // No existing highscore, so this is the new highscore
            Debug.Log($"First highscore for {songUrl}! Set by {playerName} with a score of {score}.");
            HighscoreEntry newHighscore = new HighscoreEntry
            {
                songUrl = songUrl,
                playerName = playerName,
                score = score,
                stars = stars
            };
            highscoreData.highscores.Add(newHighscore);
            SaveHighscores();
            return true; // New highscore set
        }

        return false; // Score was not high enough
    }
}