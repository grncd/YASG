using MPUIKIT;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using System.IO; // Required for Path and File operations
using System.Linq; // Required for FirstOrDefault()
using System.Threading.Tasks; // Required for async/await Tasks
using UnityEngine.Networking; // Required for UnityWebRequest
using System;

// Place these helper classes here or in their own file
[System.Serializable]
public class CorrespondenceEntry
{
    public string key;
    public string value;
}

[System.Serializable]
public class CorrespondenceList
{
    public List<CorrespondenceEntry> correspondences;
}


public class EditorManager : MonoBehaviour
{
    // --- ADD THESE NEW PUBLIC FIELDS ---
    [Header("Syncing UI")]
    public GameObject lyricItemPrefab; // The prefab for a single lyric line
    public Transform lyricsContentPanel; // The 'Content' object in your ScrollRect
    public TMP_InputField plainLyricsInputField; // The input field from the 'Lyrics' tab

    // --- EXISTING FIELDS ---
    public GameObject selectorGO;
    public static EditorManager Instance { get; private set; }
    private string trackName;
    private string artistName;
    private string albumName;
    private int duration;
    private string plainLyrics;
    // private string syncedLyrics; // We will build this at the end
    public TextMeshProUGUI songInfo;
    public LyricsScroller lyricsScroller;
    // private int currentLyric; // We will use a new variable for syncing
    private GameObject tabs;
    private MusicPlayer player;
    public AudioSource BGmusic;

    // --- ADD THESE NEW PRIVATE FIELDS ---
    private List<LyricSyncItem> activeLyricItems = new List<LyricSyncItem>();
    private List<float> timestamps = new List<float>();
    private int currentSyncIndex = 1;

    private void Start()
    {
        Instance = this;
        player = GetComponent<MusicPlayer>();
        tabs = transform.GetChild(0).GetChild(1).gameObject;
    }

    void OnEnable()
    {
        selectorGO.SetActive(true);
        StartCoroutine(FadeOutAndStop(BGmusic, 2.0f));
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

    public void SyncNextLyric()
    {
        // The lyric to sync is the one *after* the currently selected one.
        int targetIndex = currentSyncIndex + 1;

        if (player.GetComponent<AudioSource>().clip == null || targetIndex >= activeLyricItems.Count)
        {
            Debug.LogWarning("Cannot sync: No more lyrics or audio not loaded.");
            return;
        }

        // Get current time and apply to the target
        float time = player.GetComponent<AudioSource>().time;
        timestamps[targetIndex] = time;
        activeLyricItems[targetIndex].SetTimestamp(time);

        // Now, the new "current" is the one we just synced.
        currentSyncIndex = targetIndex;

        // Update highlights
        UpdateAllHighlights(); // This will highlight the new currentSyncIndex and unhighlight others.
        Debug.Log(currentSyncIndex + 1);
        lyricsScroller.CenterOnLyric(currentSyncIndex + 1);
    }

    public void UndoLastSync()
    {
        // We want to undo the timestamp on the `currentSyncIndex` lyric.
        // We can only undo if the current lyric is not the dummy lyric.
        if (currentSyncIndex <= 0)
        {
            Debug.LogWarning("Cannot undo: At the start of the list.");
            return;
        }

        // Clear the timestamp of the current lyric
        timestamps[currentSyncIndex] = -1f;
        activeLyricItems[currentSyncIndex].ClearTimestamp();

        // Move the selection back to the *previous* lyric.
        currentSyncIndex--;

        // Update highlights and scroll position
        UpdateAllHighlights();
        lyricsScroller.CenterOnLyric(currentSyncIndex + 1);
    }

    void Update()
    {

    }

    void OnDisable()
    {
        BGmusic.Play();
        selectorGO.transform.GetChild(0).gameObject.SetActive(true);
        selectorGO.transform.GetChild(1).gameObject.SetActive(false);
        selectorGO.transform.GetChild(1).GetChild(2).gameObject.SetActive(true);
        PlayerPrefs.SetInt("editing", 0);
    }

    public async void StartEditing(string track, string artist, string album, int dt, string url)
    {
        selectorGO.SetActive(false);
        transform.GetChild(1).GetComponent<CanvasGroup>().alpha = 0f;
        transform.GetChild(0).GetComponent<CanvasGroup>().blocksRaycasts = true;
        transform.GetChild(0).GetComponent<CanvasGroup>().alpha = 1f;
        transform.GetChild(0).GetComponent<CanvasGroup>().blocksRaycasts = true;
        trackName = track;
        artistName = artist;
        albumName = album;
        duration = dt / 1000;
        songInfo.text = $"{artist} - {track}";
        await LevelResourcesCompiler.Instance.DownloadSong(url, track);

        await LoadAndSetAudioClip(trackName);
    }

    /// <summary>
    /// Finds the audio filename from corr.json, loads the AudioClip from disk, and sets it in the player.
    /// </summary>
    /// <param name="trackKey">The name of the track to look up in the JSON file.</param>
    private async Task LoadAndSetAudioClip(string trackKey)
    {
        string dataPath = PlayerPrefs.GetString("dataPath");
        if (string.IsNullOrEmpty(dataPath))
        {
            Debug.LogError("dataPath is not set in PlayerPrefs!");
            return;
        }

        string jsonPath = Path.Combine(dataPath, "corr.json");
        if (!File.Exists(jsonPath))
        {
            Debug.LogError($"corr.json not found at path: {jsonPath}");
            return;
        }

        try
        {
            // 1. Read and parse the JSON file
            string jsonContent = await File.ReadAllTextAsync(jsonPath);
            CorrespondenceList correspondenceList = JsonUtility.FromJson<CorrespondenceList>(jsonContent);

            // 2. Find the entry for our track name
            CorrespondenceEntry entry = correspondenceList.correspondences.FirstOrDefault(c => c.key == trackKey);
            if (entry == null)
            {
                Debug.LogError($"Track key '{trackKey}' not found in corr.json.");
                return;
            }

            string audioFileName = entry.value;
            string audioFilePath = Path.Combine(dataPath, audioFileName);

            if (!File.Exists(audioFilePath))
            {
                Debug.LogError($"Audio file not found at path: {audioFilePath}");
                return;
            }

            // 3. Load the AudioClip asynchronously
            // We must use "file://" protocol for local files with UnityWebRequest
            string uri = "file://" + audioFilePath;

            // Determine the audio type. MPEG is for .mp3 files.
            AudioType audioType = AudioType.MPEG;

            using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(uri, audioType))
            {
                var asyncOp = www.SendWebRequest();

                while (!asyncOp.isDone)
                {
                    await Task.Yield(); // Wait for the next frame
                }

                if (www.result == UnityWebRequest.Result.ConnectionError || www.result == UnityWebRequest.Result.ProtocolError)
                {
                    Debug.LogError($"Error loading AudioClip from {uri}: {www.error}");
                }
                else
                {
                    // 4. If successful, get the clip and set it in the player
                    AudioClip loadedClip = DownloadHandlerAudioClip.GetContent(www);
                    if (loadedClip != null)
                    {
                        loadedClip.name = audioFileName; // Good practice to name the clip
                        player.SetClip(loadedClip);
                        Debug.Log($"Successfully loaded and set clip: {audioFileName}");
                    }
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"An error occurred while loading the audio clip: {ex.Message}");
        }
    }



    /// <summary>
    /// Called by the 'X' button on a LyricSyncItem.
    /// </summary>
    public void ClearTimestampFor(int index)
    {
        // Cannot clear the dummy item
        if (index <= 0 || index >= timestamps.Count) return;

        timestamps[index] = -1f;
        activeLyricItems[index].ClearTimestamp();
        Debug.Log($"Cleared timestamp for lyric at index {index}");
    }

    // --- MODIFY YOUR TAB TOGGLING METHODS ---

    public void ToggleSyncTab()
    {
        tabs.transform.GetChild(0).GetComponent<MPImage>().color = new Color(0.2169811f, 0.2169811f, 0.2169811f);
        tabs.transform.GetChild(0).GetChild(0).GetComponent<TextMeshProUGUI>().color = Color.white;
        tabs.transform.GetChild(1).GetComponent<MPImage>().color = Color.white;
        tabs.transform.GetChild(1).GetChild(0).GetComponent<TextMeshProUGUI>().color = Color.black;
        tabs.transform.parent.GetChild(2).gameObject.SetActive(false);
        tabs.transform.parent.GetChild(3).gameObject.SetActive(true);

        // --- ADD THIS ---
        // Populate the list when switching to this tab
        PopulateSyncList();
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



    private void PopulateSyncList()
    {
        plainLyrics = plainLyricsInputField.text;

        // 1. Clear any existing items from the content panel
        foreach (Transform child in lyricsContentPanel)
        {
            if (child.name != "TopCenteringSpacer" && child.name != "BottomCenteringSpacer")
            {
                Destroy(child.gameObject);
            }
        }
        activeLyricItems.Clear();
        timestamps.Clear();

        // --- PADDING AND DUMMY SETUP ---

        // 2. Instantiate and configure the START padding item
        GameObject startPaddingGO = Instantiate(lyricItemPrefab, lyricsContentPanel);
        startPaddingGO.name = "StartPadding";
        LyricSyncItem startPaddingScript = startPaddingGO.GetComponent<LyricSyncItem>();
        startPaddingScript.Setup("", -1, this, false); // Setup as non-interactive
        startPaddingGO.transform.SetSiblingIndex(1); // After top spacer

        // 3. Add the VISIBLE DUMMY lyric item (Index 0 in our lists)
        GameObject dummyGO = Instantiate(lyricItemPrefab, lyricsContentPanel);
        dummyGO.name = "Dummy Lyric";
        LyricSyncItem dummyScript = dummyGO.GetComponent<LyricSyncItem>();
        dummyScript.Setup("...", 0, this); // Regular setup
        dummyScript.SetDeleteButtonInteractable(false);
        activeLyricItems.Add(dummyScript);
        timestamps.Add(-1f);
        dummyGO.transform.SetSiblingIndex(2); // After start padding

        // 4. Split the real lyrics and instantiate a prefab for each
        string[] lines = plainLyrics.Split(new[] { '\n' }, System.StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < lines.Length; i++)
        {
            GameObject newItemGO = Instantiate(lyricItemPrefab, lyricsContentPanel);
            LyricSyncItem itemScript = newItemGO.GetComponent<LyricSyncItem>();

            int logicalIndex = i + 1;
            itemScript.Setup(lines[i].Trim(), logicalIndex, this);
            activeLyricItems.Add(itemScript);
            timestamps.Add(-1f);

            if (lyricsContentPanel.Find("BottomCenteringSpacer"))
            {
                int bottomSpacerIndex = lyricsContentPanel.Find("BottomCenteringSpacer").GetSiblingIndex();
                newItemGO.transform.SetSiblingIndex(bottomSpacerIndex);
            }
        }

        // 5. Instantiate and configure the END padding item
        GameObject endPaddingGO = Instantiate(lyricItemPrefab, lyricsContentPanel);
        endPaddingGO.name = "EndPadding";
        LyricSyncItem endPaddingScript = endPaddingGO.GetComponent<LyricSyncItem>();
        endPaddingScript.Setup("", -1, this, false); // Setup as non-interactive
        if (lyricsContentPanel.Find("BottomCenteringSpacer"))
        {
            int bottomSpacerIndex = lyricsContentPanel.Find("BottomCenteringSpacer").GetSiblingIndex();
            endPaddingGO.transform.SetSiblingIndex(bottomSpacerIndex);
        }

        // 6. Reset sync state
        currentSyncIndex = 0;

        // 7. Set initial view and highlights
        if (activeLyricItems.Count > 0)
        {
            lyricsScroller.CenterOnLyric(0 + 1);
            UpdateAllHighlights();
            Fix();
        }
    }

    public async void Fix()
    {
        await Task.Delay(TimeSpan.FromMilliseconds(15));
        lyricsScroller.CenterOnLyric(0 + 1);
    }

    private void UpdateAllHighlights()
    {
        for (int i = 0; i < activeLyricItems.Count; i++)
        {
            // Only highlight the lyric at the currentSyncIndex
            if (i == currentSyncIndex)
            {
                activeLyricItems[i].SetAsCurrent();
            }
            else
            {
                activeLyricItems[i].ClearAsCurrent();
            }
        }
    }

    public void SaveLyrics()
    {
        string dataPath = PlayerPrefs.GetString("dataPath");
        if (string.IsNullOrEmpty(dataPath))
        {
            Debug.LogError("dataPath is not set in PlayerPrefs!");
            return;
        }

        string workingLyricsPath = Path.Combine(dataPath, "workingLyrics");
        Directory.CreateDirectory(workingLyricsPath); // Ensure the directory exists

        // Save plain lyrics
        string plainLyricsFilePath = Path.Combine(workingLyricsPath, $"lyricsPlain.txt");
        File.WriteAllText(plainLyricsFilePath, plainLyricsInputField.text);

        // Save synced lyrics
        string syncedLyricsFilePath = Path.Combine(workingLyricsPath, $"lyricsSynced.txt");
        using (StreamWriter writer = new StreamWriter(syncedLyricsFilePath))
        {
            for (int i = 1; i < activeLyricItems.Count; i++) // Start from 1 to skip the dummy lyric
            {
                string timestampStr = MusicPlayer.Instance.FormatTime(timestamps[i]);
                writer.WriteLine($"[{timestampStr}] {activeLyricItems[i].lyricText.text}");
            }
        }

        Debug.Log($"Lyrics saved to {workingLyricsPath}");
    }
}