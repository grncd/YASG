using MPUIKIT;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine.Networking;
using System;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.Text;
using SFB;

public class EditorManager : MonoBehaviour
{
    private struct LyricSyncState { public string text; public float timestamp; }

    [Header("Syncing UI")]
    public GameObject lyricItemPrefab;
    public Transform lyricsContentPanel;
    public TMP_InputField plainLyricsInputField;
    public GameObject loadPrompt;
    public TextMeshProUGUI songNameText;
    [Header("Custom UI")]
    public TMP_InputField customName;
    public TMP_InputField customArtist;
    private string songPath;
    private string vocalPath;
    public Toggle automaticallyExtract;
    private bool isCustom;
    private int saveIndex = 0;

    // --- EXISTING FIELDS ---
    public GameObject selectorGO;
    public static EditorManager Instance { get; private set; }
    private string trackName;
    private string artistName;
    private string albumName;
    private float duration;
    private string trackUrl;
    public TextMeshProUGUI songInfo;
    public LyricsScroller lyricsScroller;
    private GameObject tabs;
    private MusicPlayer player;
    
    public LrcLibPublisherWithChallenge publisher;

    // --- SYNCING STATE FIELDS ---
    private List<LyricSyncItem> activeLyricItems = new List<LyricSyncItem>();
    private List<float> timestamps = new List<float>();
    private int currentSyncIndex = 0;
    private MPImage understoodFill;
    private float elapsedTime = 0f;
    private float targetTime = 22f;
    private float saveTimer = 0f;

    // --- (Unchanged Setup/Teardown Methods) ---
    #region Unchanged Setup/Teardown Methods
    private void Start()
    {
        Instance = this;
        player = GetComponent<MusicPlayer>();
        tabs = transform.GetChild(0).GetChild(1).gameObject;

        string dataPath = PlayerPrefs.GetString("dataPath");
        if (string.IsNullOrEmpty(dataPath)) { return; }
        string workingLyricsPath = Path.Combine(dataPath, "workingLyrics");
        if (Directory.Exists(workingLyricsPath) && Directory.GetFiles(workingLyricsPath).Length > 0)
        {
            loadPrompt.SetActive(true);
            string[] files = Directory.GetFiles(workingLyricsPath);
            string fileName = Path.GetFileNameWithoutExtension(files[0]);
            string[] parts = fileName.Split('_');
            songNameText.text = parts[1];
        }
    }

    private void Update()
    {
        saveTimer += Time.deltaTime;
        if (saveTimer >= 10f)
        {
            LocalSaveLyrics();
            saveTimer = 0f;
        }

        if (transform.GetChild(2).gameObject.activeInHierarchy)
        {
            if(PlayerPrefs.GetInt("lyricsDisclaimer") == 0)
            {
                elapsedTime += Time.deltaTime;
                understoodFill.fillAmount = elapsedTime / targetTime;
                if(elapsedTime / targetTime > 1f)
                {
                    PlayerPrefs.SetInt("lyricsDisclaimer", 1);
                    understoodFill.transform.parent.GetComponent<Button>().interactable = true;
                }
                else
                {
                    understoodFill.transform.parent.GetComponent<Button>().interactable = false;
                }
            }
            else
            {
                understoodFill.transform.parent.GetComponent<Button>().interactable = true;
                understoodFill.fillAmount = 1f;
            }
        }
        
    }
    void OnEnable()
    {
        if(PlayerPrefs.GetInt("lyricsDisclaimer") == 0)
        {
            understoodFill = transform.GetChild(2).GetChild(4).GetChild(0).GetComponent<MPImage>();
            transform.GetChild(2).gameObject.SetActive(true);
        }
        selectorGO.SetActive(true);
        StartCoroutine(FadeOutAndStop(GameObject.Find("Music").GetComponent<AudioSource>(), 2.0f));
        transform.GetChild(1).GetComponent<CanvasGroup>().alpha = 1f;
        transform.GetChild(1).GetComponent<CanvasGroup>().blocksRaycasts = false;
        transform.GetChild(0).GetComponent<CanvasGroup>().alpha = 0f;
        transform.GetChild(0).GetComponent<CanvasGroup>().blocksRaycasts = false;
        selectorGO.transform.GetChild(0).gameObject.SetActive(false);
        selectorGO.transform.GetChild(1).gameObject.SetActive(true);
        selectorGO.transform.GetChild(1).GetChild(2).gameObject.SetActive(false);
        PlayerPrefs.SetInt("editing", 1);
    }
    private IEnumerator FadeOutAndStop(AudioSource audioSource, float duration)
    {
        float startVolume = audioSource.volume;
        while (audioSource.volume > 0)
        {
            audioSource.volume -= startVolume * Time.deltaTime / duration;
            yield return null;
        }
        audioSource.Stop();
        audioSource.volume = startVolume;
    }
    
    void OnDisable()
    {
        GameObject.Find("Music").GetComponent<AudioSource>().Play();
        GameObject.Find("Music").GetComponent<AudioSource>().volume = 0.211f;
        selectorGO.transform.GetChild(0).gameObject.SetActive(true);
        selectorGO.transform.GetChild(1).gameObject.SetActive(false);
        selectorGO.transform.GetChild(1).GetChild(2).gameObject.SetActive(true);
        PlayerPrefs.SetInt("editing", 0);
    }
    #endregion

    public void SyncNextLyric()
    {
        int targetIndex = currentSyncIndex + 1;
        if (player.GetComponent<AudioSource>().clip == null || targetIndex >= activeLyricItems.Count)
        {
            return;
        }
        float time = player.GetComponent<AudioSource>().time;
        timestamps[targetIndex] = time;
        activeLyricItems[targetIndex].SetTimestamp(time);
        currentSyncIndex = targetIndex;
        UpdateAllHighlights();
        lyricsScroller.CenterOnLyric(currentSyncIndex + 1);
    }

    public void UndoLastSync()
    {
        if (currentSyncIndex <= 0)
        {
            return;
        }
        timestamps[currentSyncIndex] = -1f;
        activeLyricItems[currentSyncIndex].ClearTimestamp();
        currentSyncIndex--;
        UpdateAllHighlights();
        lyricsScroller.CenterOnLyric(currentSyncIndex + 1);
    }

    // --- (Unchanged Tab Toggling) ---
    #region Unchanged Tab Toggling
    public void ToggleSyncTab()
    {
        tabs.transform.GetChild(0).GetComponent<MPImage>().color = new Color(0.2169811f, 0.2169811f, 0.2169811f);
        tabs.transform.GetChild(0).GetChild(0).GetComponent<TextMeshProUGUI>().color = Color.white;
        tabs.transform.GetChild(1).GetComponent<MPImage>().color = Color.white;
        tabs.transform.GetChild(1).GetChild(0).GetComponent<TextMeshProUGUI>().color = Color.black;
        tabs.transform.parent.GetChild(2).gameObject.SetActive(false);
        tabs.transform.parent.GetChild(3).gameObject.SetActive(true);
        PopulateSyncList();

        int lastSyncedIndex = -1;
        for (int i = timestamps.Count - 1; i >= 0; i--)
        {
            if (timestamps[i] >= 0)
            {
                lastSyncedIndex = i;
                break;
            }
        }

        if (lastSyncedIndex != -1)
        {
            currentSyncIndex = lastSyncedIndex;
            UpdateAllHighlights();
            StartCoroutine(DelayedScroll(currentSyncIndex));
        }
        else
        {
            currentSyncIndex = 0;
            UpdateAllHighlights();
            StartCoroutine(DelayedScroll(currentSyncIndex));
        }
    }
    public void ToggleLyricTab()
    {
        tabs.transform.GetChild(1).GetComponent<MPImage>().color = new Color(0.2169811f, 0.2169811f, 0.2169811f);
        tabs.transform.GetChild(1).GetChild(0).GetComponent<TextMeshProUGUI>().color = Color.white;
        tabs.transform.GetChild(0).GetComponent<MPImage>().color = Color.white;
        tabs.transform.GetChild(0).GetChild(0).GetComponent<TextMeshProUGUI>().color = Color.black;
        tabs.transform.parent.GetChild(3).gameObject.SetActive(false);
        tabs.transform.parent.GetChild(2).gameObject.SetActive(true);
    }
    #endregion

    private void PopulateSyncList()
    {
        var previousSyncData = new List<LyricSyncState>();
        if (activeLyricItems.Count > 1)
        {
            for (int i = 1; i < activeLyricItems.Count; i++)
            {
                if (activeLyricItems[i] != null)
                {
                    previousSyncData.Add(new LyricSyncState { text = activeLyricItems[i].lyricText.text, timestamp = timestamps[i] });
                }
            }
        }

        foreach (Transform child in lyricsContentPanel)
        {
            if (child.name != "TopCenteringSpacer" && child.name != "BottomCenteringSpacer")
            {
                Destroy(child.gameObject);
            }
        }
        activeLyricItems.Clear();
        timestamps.Clear();

        GameObject startPaddingGO = Instantiate(lyricItemPrefab, lyricsContentPanel);
        startPaddingGO.GetComponent<LyricSyncItem>().Setup("", -1, this, false);
        if (lyricsContentPanel.Find("TopCenteringSpacer"))
            startPaddingGO.transform.SetSiblingIndex(lyricsContentPanel.Find("TopCenteringSpacer").GetSiblingIndex() + 1);

        GameObject dummyGO = Instantiate(lyricItemPrefab, lyricsContentPanel);
        dummyGO.GetComponent<LyricSyncItem>().Setup("...", 0, this);
        dummyGO.GetComponent<LyricSyncItem>().SetDeleteButtonInteractable(false);
        activeLyricItems.Add(dummyGO.GetComponent<LyricSyncItem>());
        timestamps.Add(-1f);
        dummyGO.transform.SetSiblingIndex(startPaddingGO.transform.GetSiblingIndex() + 1);

        // --- CHANGED HERE ---
        // Removed 'StringSplitOptions.RemoveEmptyEntries' to allow for blank lines.
        string[] lines = plainLyricsInputField.text.Split(new[] { '\n' });

        for (int i = 0; i < lines.Length; i++)
        {
            // We still trim to handle potential whitespace, but an empty line is now valid.
            string currentLine = lines[i].Trim();

            // NOTE: The original check 'if (string.IsNullOrEmpty(currentLine)) continue;' is removed.

            float restoredTimestamp = -1f;
            int matchIndex = previousSyncData.FindIndex(s => s.text == currentLine);
            if (matchIndex != -1)
            {
                restoredTimestamp = previousSyncData[matchIndex].timestamp;
                previousSyncData.RemoveAt(matchIndex);
            }

            GameObject newItemGO = Instantiate(lyricItemPrefab, lyricsContentPanel);
            LyricSyncItem itemScript = newItemGO.GetComponent<LyricSyncItem>();
            int logicalIndex = i + 1;
            itemScript.Setup(currentLine, logicalIndex, this);

            if (restoredTimestamp > -1f)
            {
                itemScript.SetTimestamp(restoredTimestamp);
            }

            activeLyricItems.Add(itemScript);
            timestamps.Add(restoredTimestamp);

            if (lyricsContentPanel.Find("BottomCenteringSpacer"))
            {
                newItemGO.transform.SetSiblingIndex(lyricsContentPanel.Find("BottomCenteringSpacer").GetSiblingIndex());
            }
        }

        GameObject endPaddingGO = Instantiate(lyricItemPrefab, lyricsContentPanel);
        endPaddingGO.GetComponent<LyricSyncItem>().Setup("", -1, this, false);
        if (lyricsContentPanel.Find("BottomCenteringSpacer"))
        {
            endPaddingGO.transform.SetSiblingIndex(lyricsContentPanel.Find("BottomCenteringSpacer").GetSiblingIndex());
        }

        if (currentSyncIndex >= activeLyricItems.Count)
        {
            currentSyncIndex = 0;
        }

        if (activeLyricItems.Count > 0)
        {
            UpdateAllHighlights();
            StartCoroutine(DelayedScroll(currentSyncIndex));
        }
    }

    private IEnumerator DelayedScroll(int targetItemIndex)
    {
        yield return new WaitForEndOfFrame();
        yield return new WaitForEndOfFrame();
        lyricsScroller.CenterOnLyric(targetItemIndex + 1);
    }

    private void UpdateAllHighlights()
    {
        for (int i = 0; i < activeLyricItems.Count; i++)
        {
            activeLyricItems[i].SetHighlight(i == currentSyncIndex);
        }
    }

    public async void ContinueEditing()
    {
        saveIndex = 0;
        loadPrompt.SetActive(false);
        string dataPath = PlayerPrefs.GetString("dataPath");
        if (string.IsNullOrEmpty(dataPath)) { Debug.LogError("dataPath is not set in PlayerPrefs!"); return; }

        string workingLyricsPath = Path.Combine(dataPath, "workingLyrics");
        string[] files = Directory.GetFiles(workingLyricsPath, "*_plain.txt");
        if (files.Length == 0) { Debug.LogError("No plain lyrics file found in workingLyrics."); return; }

        string plainLyricsFilePath = files[0];
        string fileName = Path.GetFileNameWithoutExtension(plainLyricsFilePath);
        string[] parts = fileName.Split('_');
        string trackId = parts[0];
        trackName = parts[1];
        trackUrl = $"https://open.spotify.com/track/{trackId}";

        // Load metadata and plain lyrics from the file
        string[] plainLines = File.ReadAllLines(plainLyricsFilePath);
        artistName = plainLines[0];
        albumName = plainLines[1];
        duration = float.Parse(plainLines[2], System.Globalization.CultureInfo.InvariantCulture);
        plainLyricsInputField.text = string.Join("\n", plainLines.Skip(3));

        // --- SETUP EDITOR STATE ---
        selectorGO.SetActive(false);
        transform.GetChild(1).GetComponent<CanvasGroup>().alpha = 0f;
        transform.GetChild(0).GetComponent<CanvasGroup>().blocksRaycasts = true;
        transform.GetChild(0).GetComponent<CanvasGroup>().alpha = 1f;
        transform.GetChild(0).GetComponent<CanvasGroup>().blocksRaycasts = true;
        songInfo.text = $"{artistName} - {trackName}";

        PlayerPrefs.SetString("currentSong", trackName);
        PlayerPrefs.SetString("currentArtist", artistName);

        // --- LOAD AUDIO ---
        if (parts[2] == "False")
        {
            isCustom = false;
            await LevelResourcesCompiler.Instance.DownloadSong(trackUrl, trackName, artistName);
            await LoadAndSetAudioClip(trackName);
        }
        else
        {
            isCustom = true;
            string pathToAudio = Path.Combine(PlayerPrefs.GetString("dataPath"), "downloads",$"{artistName} - {trackName}.mp3");
            await LoadAndSetAudioClipWithPath(pathToAudio);
        }

        // --- LOAD SYNCED LYRICS AND POPULATE UI ---
        string syncedLyricsFilePath = Path.Combine(workingLyricsPath, $"{trackId}_{trackName}_{isCustom}_synced.txt");
        if (File.Exists(syncedLyricsFilePath))
        {
            string[] syncedLines = File.ReadAllLines(syncedLyricsFilePath);

            // Clear UI and data lists
            foreach (Transform child in lyricsContentPanel)
            {
                if (child.name != "TopCenteringSpacer" && child.name != "BottomCenteringSpacer")
                    Destroy(child.gameObject);
            }
            activeLyricItems.Clear();
            timestamps.Clear();

            // Add dummy "..." item
            GameObject dummyGO = Instantiate(lyricItemPrefab, lyricsContentPanel);
            dummyGO.GetComponent<LyricSyncItem>().Setup("...", 0, this);
            dummyGO.GetComponent<LyricSyncItem>().SetDeleteButtonInteractable(false);
            activeLyricItems.Add(dummyGO.GetComponent<LyricSyncItem>());
            timestamps.Add(-1f);

            // Repopulate from loaded data
            string[] currentPlainLines = plainLyricsInputField.text.Split('\n');
            for (int i = 0; i < currentPlainLines.Length; i++)
            {
                GameObject newItemGO = Instantiate(lyricItemPrefab, lyricsContentPanel);
                LyricSyncItem itemScript = newItemGO.GetComponent<LyricSyncItem>();
                itemScript.Setup(currentPlainLines[i], i + 1, this);

                float restoredTimestamp = -1f;
                if (i < syncedLines.Length && !string.IsNullOrEmpty(syncedLines[i]))
                {
                    try
                    {
                        string[] lineParts = syncedLines[i].Split(new[] { ']' }, 2);
                        string timestampStr = lineParts[0].TrimStart('[');
                        TimeSpan timeSpan = TimeSpan.ParseExact(timestampStr, "m\\:ss\\.ff", System.Globalization.CultureInfo.InvariantCulture);
                        restoredTimestamp = (float)timeSpan.TotalSeconds;
                        itemScript.SetTimestamp(restoredTimestamp);
                    }
                    catch (Exception) { /* Ignore malformed lines */ }
                }
                
                activeLyricItems.Add(itemScript);
                timestamps.Add(restoredTimestamp);

                if (lyricsContentPanel.Find("BottomCenteringSpacer"))
                    newItemGO.transform.SetSiblingIndex(lyricsContentPanel.Find("BottomCenteringSpacer").GetSiblingIndex());
            }

            currentSyncIndex = 0;
            UpdateAllHighlights();
            StartCoroutine(DelayedScroll(currentSyncIndex));
        }
    }

    public void DiscardEditing()
    {
        loadPrompt.SetActive(false);
        string dataPath = PlayerPrefs.GetString("dataPath");
        string workingLyricsPath = Path.Combine(dataPath, "workingLyrics");
        Directory.Delete(workingLyricsPath, true);
    }

    public void LocalSaveLyrics()
    {
        if(transform.GetChild(0).GetComponent<CanvasGroup>().alpha == 1f)
        {
            if(saveIndex > 1)
            {
                string dataPath = PlayerPrefs.GetString("dataPath");
                if (string.IsNullOrEmpty(dataPath)) { Debug.LogError("dataPath is not set in PlayerPrefs!"); return; }

                string workingLyricsPath = Path.Combine(dataPath, "workingLyrics");
                Directory.CreateDirectory(workingLyricsPath);

                string trackId;
                if (trackUrl != null)
                {
                    if (trackUrl.Contains("/"))
                    {
                        trackId = trackUrl.Split('/').Last();
                    }
                    else
                    {
                        trackId = trackUrl;
                    }
                }
                else
                {
                    return;
                }
                string plainLyricsFilePath = Path.Combine(workingLyricsPath, $"{trackId}_{trackName}_{isCustom}_plain.txt");
                string header = $"{artistName}\n{albumName}\n{duration}\n";
                string currentPlainLyrics = header + plainLyricsInputField.text;
                File.WriteAllText(plainLyricsFilePath, currentPlainLyrics);
                /*
                string[] files = Directory.GetFiles(workingLyricsPath, $"{trackId}__{isCustom}_plain.txt");
                if(files.Length > 0 )
                {
                    foreach (string file in files)
                    {
                        File.Delete(file);
                    }
                }
                files = Directory.GetFiles(workingLyricsPath, $"{trackId}__{isCustom}_synced.txt");
                if (files.Length > 0)
                {
                    foreach (string file in files)
                    {
                        File.Delete(file);
                    }
                }
                files = Directory.GetFiles(workingLyricsPath, $"{trackId}__{isCustom}_synced.txt");
                if (files.Length > 0)
                {
                    foreach (string file in files)
                    {
                        File.Delete(file);
                    }
                }
                */

                System.Text.StringBuilder lrcBuilder = new System.Text.StringBuilder();
                for (int i = 1; i < activeLyricItems.Count; i++)
                {
                    float time = timestamps[i];
                    string lyricText = activeLyricItems[i].lyricText.text.Trim();

                    // Only add lines that have a timestamp and are not blank
                    if (time >= 0 && !string.IsNullOrEmpty(lyricText))
                    {
                        TimeSpan timeSpan = TimeSpan.FromSeconds(time);
                        string timestampStr = string.Format("{0}:{1:00}.{2:00}", timeSpan.Minutes, timeSpan.Seconds, timeSpan.Milliseconds / 10);

                        lrcBuilder.AppendLine($"[{timestampStr}]{lyricText}");
                    }
                }
                string finalSyncedLyrics = lrcBuilder.ToString();

                string syncedLyricsFilePath = Path.Combine(workingLyricsPath, $"{trackId}_{trackName}_{isCustom}_synced.txt");
                File.WriteAllText(syncedLyricsFilePath, finalSyncedLyrics);

                Debug.Log($"Lyrics saved to {workingLyricsPath}");
            }
            saveIndex++;
        }
    }

    public void SaveLyrics()
    {
        string dataPath = PlayerPrefs.GetString("dataPath");
        if (string.IsNullOrEmpty(dataPath)) { Debug.LogError("dataPath is not set in PlayerPrefs!"); return; }

        string workingLyricsPath = Path.Combine(dataPath, "workingLyrics");
        string localLyricsPath = Path.Combine(dataPath, "downloads");
        Directory.CreateDirectory(workingLyricsPath);

        string trackId = trackUrl.Split('/').Last();
        string plainLyricsFilePath = Path.Combine(workingLyricsPath, $"{trackId}_{trackName}_plain.txt");
        string header = $"{artistName}\n{albumName}\n{duration}\n";
        string currentPlainLyrics = header + plainLyricsInputField.text;
        File.WriteAllText(plainLyricsFilePath, currentPlainLyrics);

        System.Text.StringBuilder lrcBuilder = new System.Text.StringBuilder();
        for (int i = 1; i < activeLyricItems.Count; i++)
        {
            float time = timestamps[i];
            string lyricText = activeLyricItems[i].lyricText.text.Trim();

            // Only add lines that have a timestamp and are not blank
            if (time >= 0)
            {
                TimeSpan timeSpan = TimeSpan.FromSeconds(time);
                string timestampStr = string.Format("{0}:{1:00}.{2:00}", timeSpan.Minutes, timeSpan.Seconds, timeSpan.Milliseconds / 10);

                lrcBuilder.AppendLine($"[{timestampStr}]{lyricText}");
            }
        }
        string finalSyncedLyrics = lrcBuilder.ToString();

        if (!isCustom)
        {
            string syncedLyricsFilePath = Path.Combine(workingLyricsPath, $"{trackId}_{trackName}_synced.txt");
            File.WriteAllText(syncedLyricsFilePath, finalSyncedLyrics);
            

            if (publisher != null)
            {
                publisher.PublishWithChallenge(trackName, artistName, albumName, duration, trackUrl);
            }
            else
            {
                Debug.LogWarning("LrcLibPublisherWithChallenge component not assigned in the inspector.");
            }
        }
        else
        {
            string syncedLyricsFilePath = Path.Combine(localLyricsPath, $"{trackName}.txt");
            File.WriteAllText(syncedLyricsFilePath, finalSyncedLyrics);

            string[] syncedLines = File.ReadAllLines(syncedLyricsFilePath);
            for (int i = 0; i < syncedLines.Length; i++)
            {
                if (syncedLines[i].Contains("]"))
                {
                    syncedLines[i] = syncedLines[i].Replace("]", "] ");
                }
                if (syncedLines[i].Contains("["))
                {
                    syncedLines[i] = syncedLines[i].Replace("[", "[0");
                }
            }
            string formattedSyncedLyrics = string.Join("\n", syncedLines);
            File.Delete(syncedLyricsFilePath);
            File.WriteAllText(syncedLyricsFilePath, formattedSyncedLyrics);

            AlertManager.Instance.ShowSuccess("Lyrics successfully created.","You can now play this song by accessing your Downloaded Songs.","Dismiss");
            FavoritesManager.AddDownload(trackName,artistName,FormatTime(duration),"",GenerateRandomString(16));
        }
    }

    string FormatTime(float seconds)
    {
        int totalSeconds = Mathf.FloorToInt(seconds);
        int minutes = totalSeconds / 60;
        int remainingSeconds = totalSeconds % 60;
        return $"{minutes}:{remainingSeconds:D2}";
    }

    // --- (Unchanged File Loading & Other Helpers) ---
    #region Unchanged File Loading & Other Helpers
    public async void StartEditing(string track, string artist, string album, int dt, string url)
    {
        saveIndex = 0;
        selectorGO.SetActive(false);
        transform.GetChild(1).GetComponent<CanvasGroup>().alpha = 0f;
        transform.GetChild(0).GetComponent<CanvasGroup>().blocksRaycasts = true;
        transform.GetChild(0).GetComponent<CanvasGroup>().alpha = 1f;
        transform.GetChild(0).GetComponent<CanvasGroup>().blocksRaycasts = true;
        trackName = track;
        artistName = artist;
        albumName = album;
        duration = dt / 1000f;
        trackUrl = url;
        songInfo.text = $"{artist} - {track}";
        isCustom = false;

        PlayerPrefs.SetString("currentSong", track);
        PlayerPrefs.SetString("currentArtist", artist);

        await LevelResourcesCompiler.Instance.DownloadSong(url, track, artist);
        await LoadAndSetAudioClip(trackName);
    }

    public string GenerateRandomString(int length)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
        StringBuilder randomString = new StringBuilder();

        for (int i = 0; i < length; i++)
        {
            randomString.Append(chars[UnityEngine.Random.Range(0, chars.Length)]);
        }

        return randomString.ToString();
    }

    public void CreateEditCustomButton()
    {
        StartEditingCustom(customName.text,customArtist.text,songPath,vocalPath);
    }

    public void SelectSongPath()
    {
        var paths = StandaloneFileBrowser.OpenFilePanel("Select .mp3 file", "", "mp3",false);
        if (paths.Length > 0)
        {
            songPath = paths[0];
        }
    }

    public void SelectVocalPath()
    {
        var paths = StandaloneFileBrowser.OpenFilePanel("Select .mp3 file", "", "mp3", false);
        if (paths.Length > 0)
        {
            vocalPath = paths[0];
        }
    }

    public async void StartEditingCustom(string track, string artist, string songPath, string vocalPath)
    {
        saveIndex = 0;
        selectorGO.SetActive(false);
        isCustom = true;
        transform.GetChild(1).GetComponent<CanvasGroup>().alpha = 0f;
        transform.GetChild(0).GetComponent<CanvasGroup>().blocksRaycasts = true;
        transform.GetChild(0).GetComponent<CanvasGroup>().alpha = 1f;
        transform.GetChild(0).GetComponent<CanvasGroup>().blocksRaycasts = true;
        trackUrl = GenerateRandomString(16);
        await LoadAndSetAudioClipWithPath(songPath);
        File.Copy(songPath, Path.Combine(PlayerPrefs.GetString("dataPath"), "downloads", $"{artist} - {track}.mp3"));

        if(string.IsNullOrEmpty(vocalPath))
        {
            await LevelResourcesCompiler.Instance.SplitSong(Path.Combine(PlayerPrefs.GetString("dataPath"), "downloads", $"{artist} - {track}.mp3"));
        }
        else
        {
            File.Copy(vocalPath, Path.Combine(PlayerPrefs.GetString("dataPath"), "output", "htdemucs",Path.GetFileNameWithoutExtension(vocalPath)+" [vocals].mp3"));
        }

        trackName = track;
        artistName = artist;
        albumName = "";
        duration = player.audioSource.clip.length;
        
        songInfo.text = $"{artist} - {track}";

        PlayerPrefs.SetString("currentSong", track);
        PlayerPrefs.SetString("currentArtist", artist);
    }

    private async Task LoadAndSetAudioClip(string trackKey)
    {
        string dataPath = PlayerPrefs.GetString("dataPath");
        if (string.IsNullOrEmpty(dataPath)) { Debug.LogError("dataPath is not set in PlayerPrefs!"); return; }

        string sanitizedTrackName = SanitizeFileName(trackName);
        string sanitizedArtistName = SanitizeFileName(artistName);
        string expectedFileName = $"{sanitizedArtistName} - {sanitizedTrackName}.mp3";
        string audioFilePath = Path.Combine(dataPath, "downloads", expectedFileName);

        if (!File.Exists(audioFilePath))
        {
            Debug.LogError($"Audio file not found at path: {audioFilePath}");
            return;
        }

        try
        {
            string uri = "file://" + audioFilePath;
            AudioType audioType = AudioType.MPEG;
            using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(uri, audioType))
            {
                var asyncOp = www.SendWebRequest();
                while (!asyncOp.isDone) { await Task.Yield(); }
                if (www.result == UnityWebRequest.Result.ConnectionError || www.result == UnityWebRequest.Result.ProtocolError)
                {
                    Debug.LogError($"Error loading AudioClip from {uri}: {www.error}");
                }
                else
                {
                    AudioClip loadedClip = DownloadHandlerAudioClip.GetContent(www);
                    if (loadedClip != null)
                    {
                        loadedClip.name = expectedFileName;
                        player.SetClip(loadedClip);
                        Debug.Log($"Successfully loaded and set clip: {expectedFileName}");
                    }
                }
            }
        }
        catch (System.Exception ex) { Debug.LogError($"An error occurred while loading the audio clip: {ex.Message}"); }
    }

    private async Task LoadAndSetAudioClipWithPath(string path)
    {
        string audioFilePath = path;

        if (!File.Exists(audioFilePath))
        {
            Debug.LogError($"Audio file not found at path: {audioFilePath}");
            return;
        }

        try
        {
            string uri = "file://" + audioFilePath;
            AudioType audioType = AudioType.MPEG;
            using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(uri, audioType))
            {
                var asyncOp = www.SendWebRequest();
                while (!asyncOp.isDone) { await Task.Yield(); }
                if (www.result == UnityWebRequest.Result.ConnectionError || www.result == UnityWebRequest.Result.ProtocolError)
                {
                    Debug.LogError($"Error loading AudioClip from {uri}: {www.error}");
                }
                else
                {
                    AudioClip loadedClip = DownloadHandlerAudioClip.GetContent(www);
                    if (loadedClip != null)
                    {
                        player.SetClip(loadedClip);
                    }
                }
            }
        }
        catch (System.Exception ex) { Debug.LogError($"An error occurred while loading the audio clip: {ex.Message}"); }
    }

    public void ReloadScene()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public void ClearTimestampFor(int index)
    {
        if (index <= 0 || index >= timestamps.Count) return;
        timestamps[index] = -1f;
        activeLyricItems[index].ClearTimestamp();
        Debug.Log($"Cleared timestamp for lyric at index {index}");
    }

    private string SanitizeFileName(string name)
    {
        string charactersToRemovePattern = @"[/\\:*?""<>|]";
        return System.Text.RegularExpressions.Regex.Replace(name, charactersToRemovePattern, string.Empty);
    }
    #endregion
}