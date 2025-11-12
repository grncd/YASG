using UnityEngine;
using System.IO;
using System.IO.Compression;
using System;
using SFB;

/// <summary>
/// Handles file imports for custom songs via keyboard shortcut.
/// Press Ctrl+I to open a file browser and select a .zip file to import.
/// </summary>
public class FileDropHandler : MonoBehaviour
{
    private static bool hasProcessedArgs = false;

    private void Start()
    {
        Debug.Log("FileDropHandler initialized.");
        // Process command line arguments on start (in case app was launched with a file)
        if (!hasProcessedArgs)
        {
            ProcessCommandLineArgs();
            hasProcessedArgs = true;
        }
    }

    private void Update()
    {
        // Keyboard shortcut: Ctrl+I to import a zip file
        if (Input.GetKeyDown(KeyCode.I) && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)))
        {
            Debug.Log("Ctrl+I detected!");
            OpenFileDialogAndImport();
        }
    }

    private void OpenFileDialogAndImport()
    {
        var paths = StandaloneFileBrowser.OpenFilePanel("Select Custom Song ZIP", "", "zip", false);
        if (paths.Length > 0 && !string.IsNullOrEmpty(paths[0]))
        {
            ImportCustomSongZip(paths[0]);
        }
    }

    private void ProcessCommandLineArgs()
    {
        string[] args = System.Environment.GetCommandLineArgs();

        // Skip the first argument (it's the executable path)
        for (int i = 1; i < args.Length; i++)
        {
            string arg = args[i];

            // Check if it's a zip file
            if (arg.EndsWith(".zip", System.StringComparison.OrdinalIgnoreCase) && File.Exists(arg))
            {
                Debug.Log($"Detected zip file from command line: {arg}");
                StartCoroutine(WaitAndImport(arg));
            }
        }
    }

    private System.Collections.IEnumerator WaitAndImport(string zipPath)
    {
        // Small delay to ensure systems are ready
        yield return new UnityEngine.WaitForSeconds(0.5f);
        ImportCustomSongZip(zipPath);
    }

    private void ImportCustomSongZip(string zipFilePath)
    {
        if (!File.Exists(zipFilePath))
        {
            Debug.LogError($"Zip file not found: {zipFilePath}");
            if (AlertManager.Instance != null)
                AlertManager.Instance.ShowError("Import Failed", "The selected zip file could not be found.", "OK");
            return;
        }

        string dataPath = PlayerPrefs.GetString("dataPath");
        if (string.IsNullOrEmpty(dataPath))
        {
            Debug.LogError("dataPath is not set in PlayerPrefs!");
            if (AlertManager.Instance != null)
                AlertManager.Instance.ShowError("Import Failed", "Data path is not configured.", "OK");
            return;
        }

        try
        {
            // Create a temporary extraction directory
            string tempExtractPath = Path.Combine(Path.GetTempPath(), $"YASG_Import_{System.Guid.NewGuid()}");
            Directory.CreateDirectory(tempExtractPath);

            // Extract the zip file
            ZipFile.ExtractToDirectory(zipFilePath, tempExtractPath);

            // Find the files in the extracted content
            string[] mp3Files = Directory.GetFiles(tempExtractPath, "*.mp3", SearchOption.AllDirectories);
            string[] txtFiles = Directory.GetFiles(tempExtractPath, "*.txt", SearchOption.AllDirectories);

            if (mp3Files.Length < 2)
            {
                throw new Exception("Zip file must contain at least 2 MP3 files (full song and vocals).");
            }

            if (txtFiles.Length < 1)
            {
                throw new Exception("Zip file must contain a lyrics file (.txt).");
            }

            // Identify files based on naming convention
            string fullSongFile = null;
            string vocalFile = null;
            string lyricsFile = txtFiles[0];

            foreach (string mp3 in mp3Files)
            {
                string fileName = Path.GetFileName(mp3);
                if (fileName.Contains("[vocals]") || fileName.Contains("vocals"))
                {
                    vocalFile = mp3;
                }
                else
                {
                    fullSongFile = mp3;
                }
            }

            if (string.IsNullOrEmpty(fullSongFile) || string.IsNullOrEmpty(vocalFile))
            {
                throw new Exception("Could not identify full song and vocal files. Ensure one file contains '[vocals]' in its name.");
            }

            // Parse song information from filename
            string fullSongFileName = Path.GetFileNameWithoutExtension(fullSongFile);
            string[] parts = fullSongFileName.Split(new[] { " - " }, StringSplitOptions.None);

            string artistName;
            string trackName;

            if (parts.Length >= 2)
            {
                artistName = parts[0].Trim();
                trackName = parts[1].Trim();
            }
            else
            {
                // Fallback if format is different
                artistName = "Unknown Artist";
                trackName = fullSongFileName;
            }

            // Copy files to appropriate locations
            string downloadsPath = Path.Combine(dataPath, "downloads");
            string vocalsOutputPath = Path.Combine(dataPath, "output", "htdemucs");

            Directory.CreateDirectory(downloadsPath);
            Directory.CreateDirectory(vocalsOutputPath);

            string destinationFullSong = Path.Combine(downloadsPath, $"{artistName} - {trackName}.mp3");
            string destinationVocals = Path.Combine(vocalsOutputPath, $"{artistName} - {trackName} [vocals].mp3");
            string destinationLyrics = Path.Combine(downloadsPath, $"{trackName}.txt");

            // Copy the files
            File.Copy(fullSongFile, destinationFullSong, true);
            File.Copy(vocalFile, destinationVocals, true);
            File.Copy(lyricsFile, destinationLyrics, true);

            // Calculate duration from lyrics file
            float duration = CalculateDurationFromLyrics(destinationLyrics);
            string formattedDuration = FormatTime(duration);

            // Add to downloaded songs
            string randomUrl = GenerateRandomString(16);
            FavoritesManager.AddDownload(trackName, artistName, formattedDuration, "", randomUrl, (int)(duration * 1000));

            // Cleanup temp directory
            Directory.Delete(tempExtractPath, true);

            Debug.Log($"Successfully imported custom song: {artistName} - {trackName}");
            if (AlertManager.Instance != null)
                AlertManager.Instance.ShowSuccess("Import Successful", $"Successfully imported '{trackName}' by {artistName}.", "OK");
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error importing zip file: {ex.Message}");
            if (AlertManager.Instance != null)
                AlertManager.Instance.ShowError("Import Failed", $"Failed to import custom song: {ex.Message}", "OK");
        }
    }

    private float CalculateDurationFromLyrics(string lyricsFilePath)
    {
        try
        {
            if (!File.Exists(lyricsFilePath))
                return 180f; // Default 3 minutes

            string[] lines = File.ReadAllLines(lyricsFilePath);
            float maxTime = 0f;

            foreach (string line in lines)
            {
                if (line.Contains("[") && line.Contains("]"))
                {
                    try
                    {
                        string[] lineParts = line.Split(new[] { ']' }, 2);
                        string timestampStr = lineParts[0].TrimStart('[').Replace("[0", "[");

                        // Parse timestamp in format [m:ss.ff]
                        TimeSpan timeSpan = TimeSpan.ParseExact(timestampStr, "m\\:ss\\.ff", System.Globalization.CultureInfo.InvariantCulture);
                        float time = (float)timeSpan.TotalSeconds;

                        if (time > maxTime)
                            maxTime = time;
                    }
                    catch
                    {
                        // Ignore malformed timestamps
                    }
                }
            }

            // Add 10 seconds buffer to the last timestamp
            return maxTime > 0 ? maxTime + 10f : 180f;
        }
        catch
        {
            return 180f; // Default 3 minutes
        }
    }

    private string FormatTime(float seconds)
    {
        int totalSeconds = UnityEngine.Mathf.FloorToInt(seconds);
        int minutes = totalSeconds / 60;
        int remainingSeconds = totalSeconds % 60;
        return $"{minutes}:{remainingSeconds:D2}";
    }

    private string GenerateRandomString(int length)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
        System.Text.StringBuilder randomString = new System.Text.StringBuilder();

        for (int i = 0; i < length; i++)
        {
            randomString.Append(chars[UnityEngine.Random.Range(0, chars.Length)]);
        }

        return randomString.ToString();
    }
}

/*
 * USAGE NOTES:
 *
 * This component handles custom song .zip file imports in two ways:
 *
 * 1. KEYBOARD SHORTCUT: Press Ctrl+I to open a file browser and select a .zip file
 *    - Works in Editor and all builds
 *
 * 2. COMMAND LINE: You can right-click a .zip file > Open With > your app,
 *    or launch the app with the zip file as a command line argument
 *
 * Attach this component to a GameObject in your scene (like the EditorManager).
 */
