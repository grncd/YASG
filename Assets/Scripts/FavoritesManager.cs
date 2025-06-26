using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public static class FavoritesManager
{
    private static string dataPath = Path.Combine(PlayerPrefs.GetString("dataPath"), "user.json");

    private static UserMusicData LoadMusicDataFromFile()
    {
        if (File.Exists(dataPath))
        {
            try
            {
                string json = File.ReadAllText(dataPath);
                UserMusicData data = JsonUtility.FromJson<UserMusicData>(json);
                if (data == null) data = new UserMusicData();
                if (data.favorites == null) data.favorites = new List<Song>();
                if (data.downloads == null) data.downloads = new List<Song>();
                return data;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Error loading music data: {ex.Message}");
                return new UserMusicData();
            }
        }
        return new UserMusicData();
    }

    private static void SaveMusicDataToFile(UserMusicData data)
    {
        try
        {
            string json = JsonUtility.ToJson(data, true);
            File.WriteAllText(dataPath, json);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error saving music data: {ex.Message}");
        }
    }

    // --- FAVORITES Management (methods like AddFavorite, RemoveFavoriteByUrl, GetAllFavorites, etc.) ---
    // ... (These methods remain as previously defined without spotifyTrackId) ...

    public static bool AddFavorite(SearchHandler.Track trackData, string formattedLength)
    {
        if (trackData == null || trackData.external_urls == null || string.IsNullOrEmpty(trackData.external_urls.spotify))
        {
            Debug.LogError("Invalid track data provided for AddFavorite.");
            return false;
        }
        UserMusicData data = LoadMusicDataFromFile();
        if (IsFavorite(trackData.external_urls.spotify))
        {
            Debug.LogWarning($"Song '{trackData.name}' (URL: {trackData.external_urls.spotify}) already in favorites.");
            return false;
        }
        Song newFavorite = new Song(trackData, formattedLength);
        data.favorites.Add(newFavorite);
        SaveMusicDataToFile(data);
        Debug.Log($"Added to favorites: {newFavorite.name}");
        return true;
    }

    public static bool AddFavorite(string name, string artist, string length, string coverUrl, string songUrl, int durationMs = 0)
    {
        UserMusicData data = LoadMusicDataFromFile();
        if (IsFavorite(songUrl, name, artist))
        {
            Debug.LogWarning($"Song '{name}' by '{artist}' (or URL: {songUrl}) already in favorites. Not adding again.");
            return false;
        }
        if (durationMs == 0 && !string.IsNullOrEmpty(length))
        {
            string[] parts = length.Split(':');
            if (parts.Length == 2 && int.TryParse(parts[0], out int minutes) && int.TryParse(parts[1], out int seconds))
            {
                durationMs = (minutes * 60 + seconds) * 1000;
            }
        }
        Song newFavorite = new Song(name, artist, length, durationMs, coverUrl, songUrl);
        data.favorites.Add(newFavorite);
        SaveMusicDataToFile(data);
        Debug.Log($"Added to favorites: {name} by {artist}");
        return true;
    }

    public static bool RemoveFavoriteByUrl(string songUrl)
    {
        if (string.IsNullOrEmpty(songUrl)) return false;
        UserMusicData data = LoadMusicDataFromFile();
        Song songToRemove = data.favorites.FirstOrDefault(s => s.url == songUrl);
        if (songToRemove != null) { data.favorites.Remove(songToRemove); SaveMusicDataToFile(data); Debug.Log($"Removed from fav by URL: {songToRemove.name}"); return true; }
        return false;
    }
    public static List<Song> GetAllFavorites() => LoadMusicDataFromFile().favorites;
    public static bool IsFavorite(string songUrl, string name = null, string artist = null)
    {
        UserMusicData data = LoadMusicDataFromFile();
        if (data.favorites == null || data.favorites.Count == 0) return false;
        if (!string.IsNullOrEmpty(songUrl)) return data.favorites.Any(s => s.url == songUrl);
        if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(artist)) return data.favorites.Any(s => string.IsNullOrEmpty(s.url) && s.name == name && s.artist == artist);
        return false;
    }
    public static void PrintAllFavorites()
    {
        List<Song> allFavorites = GetAllFavorites();
        if (allFavorites.Count == 0) { Debug.Log("No favorite songs found."); return; }
        Debug.Log("--- All Favorite Songs ---");
        foreach (Song song in allFavorites) Debug.Log(song.ToString());
        Debug.Log("--------------------------");
    }


    // --- DOWNLOADS Management ---
    public static bool AddDownload(SearchHandler.Track trackData, string formattedLength)
    {
        if (trackData == null || trackData.external_urls == null || string.IsNullOrEmpty(trackData.external_urls.spotify))
        {
            Debug.LogError("Invalid track data provided for AddDownload.");
            return false;
        }
        UserMusicData data = LoadMusicDataFromFile();
        if (IsDownloaded(trackData.external_urls.spotify))
        {
            Debug.LogWarning($"Song '{trackData.name}' (URL: {trackData.external_urls.spotify}) already in downloads.");
            return false;
        }
        Song newDownload = new Song(trackData, formattedLength);
        data.downloads.Add(newDownload);
        SaveMusicDataToFile(data);
        Debug.Log($"Added to downloads: {newDownload.name} (URL: {newDownload.url})");
        return true;
    }

    public static bool AddDownload(string name, string artist, string length, string coverUrl, string songSourceUrl, int durationMs = 0)
    {
        UserMusicData data = LoadMusicDataFromFile();
        if (IsDownloaded(songSourceUrl))
        {
            Debug.LogWarning($"Song with source URL '{songSourceUrl}' already in downloads. Not adding again.");
            return false;
        }
        if (durationMs == 0 && !string.IsNullOrEmpty(length))
        {
            string[] parts = length.Split(':');
            if (parts.Length == 2 && int.TryParse(parts[0], out int minutes) && int.TryParse(parts[1], out int seconds))
            {
                durationMs = (minutes * 60 + seconds) * 1000;
            }
        }
        Song newDownload = new Song(name, artist, length, durationMs, coverUrl, songSourceUrl);
        data.downloads.Add(newDownload);
        SaveMusicDataToFile(data);
        Debug.Log($"Added to downloads: {name} (URL: {songSourceUrl})");
        return true;
    }

    /// <summary>
    /// Removes a song from the downloaded list based on its original source URL.
    /// </summary>
    /// <param name="songSourceUrl">The original source URL of the song to remove from downloads.</param>
    /// <returns>True if the song was found and removed, false otherwise.</returns>
    public static bool RemoveDownloadByUrl(string songSourceUrl)
    {
        if (string.IsNullOrEmpty(songSourceUrl))
        {
            Debug.LogWarning("Cannot remove download: songSourceUrl is null or empty.");
            return false;
        }

        UserMusicData data = LoadMusicDataFromFile();
        Song songToRemove = data.downloads.FirstOrDefault(s => s.url == songSourceUrl);

        if (songToRemove != null)
        {
            data.downloads.Remove(songToRemove);
            SaveMusicDataToFile(data);
            Debug.Log($"Removed '{songToRemove.name}' from downloads (URL: {songSourceUrl}).");
            return true;
        }
        else
        {
            Debug.LogWarning($"Song with URL '{songSourceUrl}' not found in downloads.");
            return false;
        }
    }

    public static List<Song> GetAllDownloads() => LoadMusicDataFromFile().downloads;

    public static void PrintAllDownloads()
    {
        List<Song> allDownloads = GetAllDownloads();
        if (allDownloads.Count == 0) { Debug.Log("No downloaded songs found."); return; }
        Debug.Log("--- All Downloaded Songs ---");
        foreach (Song song in allDownloads) Debug.Log(song.ToString());
        Debug.Log("----------------------------");
    }

    public static bool IsDownloaded(string songSourceUrl)
    {
        if (string.IsNullOrEmpty(songSourceUrl)) return false;
        UserMusicData data = LoadMusicDataFromFile();
        if (data.downloads == null || data.downloads.Count == 0) return false;
        return data.downloads.Any(s => s.url == songSourceUrl);
    }
}