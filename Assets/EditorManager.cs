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

// Helper classes remain the same
[System.Serializable]
public class CorrespondenceEntry { public string key; public string value; }
[System.Serializable]
public class CorrespondenceList { public List<CorrespondenceEntry> correspondences; }

public class EditorManager : MonoBehaviour
{
    private struct LyricSyncState { public string text; public float timestamp; }

    [Header("Syncing UI")]
    public GameObject lyricItemPrefab;
    public Transform lyricsContentPanel;
    public TMP_InputField plainLyricsInputField;

    public GameObject selectorGO;
    public static EditorManager Instance { get; private set; }
    private string trackName;
    private string artistName;
    private string albumName;
    private float duration;
    public TextMeshProUGUI songInfo;
    public LyricsScroller lyricsScroller;
    private GameObject tabs;
    private MusicPlayer player;
    public AudioSource BGmusic;
    public LrcLibPublisherWithChallenge publisher;

    private List<LyricSyncItem> activeLyricItems = new List<LyricSyncItem>();
    private List<float> timestamps = new List<float>();
    private int currentSyncIndex = 0;

    // --- (Start, OnEnable, OnDisable, etc. are unchanged) ---
    #region Unchanged Setup/Teardown Methods
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
        // CHANGED: Removed the +1 offset
        lyricsScroller.CenterOnLyric(currentSyncIndex);
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
        // CHANGED: Removed the +1 offset
        lyricsScroller.CenterOnLyric(currentSyncIndex);
    }

    // --- (Tab toggling methods are unchanged) ---
    #region Unchanged Tab and List Population Methods
    public void ToggleSyncTab()
    {
        tabs.transform.GetChild(0).GetComponent<MPImage>().color = new Color(0.2169811f, 0.2169811f, 0.2169811f);
        tabs.transform.GetChild(0).GetChild(0).GetComponent<TextMeshProUGUI>().color = Color.white;
        tabs.transform.GetChild(1).GetComponent<MPImage>().color = Color.white;
        tabs.transform.GetChild(1).GetChild(0).GetComponent<TextMeshProUGUI>().color = Color.black;
        tabs.transform.parent.GetChild(2).gameObject.SetActive(false);
        tabs.transform.parent.GetChild(3).gameObject.SetActive(true);
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
        var previousSyncData = new List<LyricSyncState>();
        if (activeLyricItems.Count > 1)
        {
            for (int i = 1; i < activeLyricItems.Count; i++)
            {
                if (activeLyricItems[i] != null)
                {
                    previousSyncData.Add(new LyricSyncState
                    {
                        text = activeLyricItems[i].lyricText.text,
                        timestamp = timestamps[i]
                    });
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

        GameObject dummyGO = Instantiate(lyricItemPrefab, lyricsContentPanel);
        LyricSyncItem dummyScript = dummyGO.GetComponent<LyricSyncItem>();
        dummyScript.Setup("...", 0, this);
        dummyScript.SetDeleteButtonInteractable(false);
        activeLyricItems.Add(dummyScript);
        timestamps.Add(-1f);
        dummyGO.transform.SetAsLastSibling();

        string[] lines = plainLyricsInputField.text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < lines.Length; i++)
        {
            string currentLine = lines[i].Trim();
            if (string.IsNullOrEmpty(currentLine)) continue;

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
            newItemGO.transform.SetAsLastSibling();
        }

        Transform bottomSpacer = lyricsContentPanel.Find("BottomCenteringSpacer");
        if (bottomSpacer != null) bottomSpacer.SetAsLastSibling();

        if (currentSyncIndex >= activeLyricItems.Count)
        {
            currentSyncIndex = 0;
        }

        if (activeLyricItems.Count > 0)
        {
            UpdateAllHighlights();
            // CHANGED: Removed the +1 offset
            StartCoroutine(DelayedScroll(currentSyncIndex));
        }
    }

    private IEnumerator DelayedScroll(int targetItemIndex)
    {
        yield return new WaitForEndOfFrame();
        // The parameter passed here is now the correct index
        lyricsScroller.CenterOnLyric(targetItemIndex);
    }

    private void UpdateAllHighlights()
    {
        for (int i = 0; i < activeLyricItems.Count; i++)
        {
            activeLyricItems[i].SetHighlight(i == currentSyncIndex);
        }
    }
    #endregion

    public void SaveLyrics()
    {
        string dataPath = PlayerPrefs.GetString("dataPath");
        if (string.IsNullOrEmpty(dataPath))
        {
            Debug.LogError("dataPath is not set in PlayerPrefs!");
            return;
        }

        string workingLyricsPath = Path.Combine(dataPath, "workingLyrics");
        Directory.CreateDirectory(workingLyricsPath);

        string plainLyricsFilePath = Path.Combine(workingLyricsPath, $"{trackName}_plain.txt");
        string currentPlainLyrics = plainLyricsInputField.text;
        File.WriteAllText(plainLyricsFilePath, currentPlainLyrics);

        System.Text.StringBuilder lrcBuilder = new System.Text.StringBuilder();
        for (int i = 1; i < activeLyricItems.Count; i++)
        {
            float time = timestamps[i];
            if (time >= 0)
            {
                TimeSpan timeSpan = TimeSpan.FromSeconds(time);
                // CHANGED: Format string updated to m:ss.xx
                string timestampStr = string.Format("{0}:{1:00}.{2:00}", timeSpan.Minutes, timeSpan.Seconds, timeSpan.Milliseconds / 10);
                lrcBuilder.AppendLine($"[{timestampStr}]{activeLyricItems[i].lyricText.text}");
            }
        }
        string finalSyncedLyrics = lrcBuilder.ToString();

        string syncedLyricsFilePath = Path.Combine(workingLyricsPath, $"{trackName}_synced.txt");
        File.WriteAllText(syncedLyricsFilePath, finalSyncedLyrics);

        Debug.Log($"Lyrics saved to {workingLyricsPath}");

        if (publisher != null)
        {
            publisher.PublishWithChallenge(trackName, artistName, albumName, duration);
        }
        else
        {
            Debug.LogWarning("LrcLibPublisherWithChallenge component not assigned in the inspector.");
        }
    }

    // --- (File loading methods are unchanged) ---
    #region Unchanged File Loading & Other Helpers
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
        duration = dt / 1000f; // Use float for duration
        songInfo.text = $"{artist} - {track}";
        await LevelResourcesCompiler.Instance.DownloadSong(url, track);

        await LoadAndSetAudioClip(trackName);
    }
    private async Task LoadAndSetAudioClip(string trackKey)
    {
        string dataPath = PlayerPrefs.GetString("dataPath");
        if (string.IsNullOrEmpty(dataPath)) { Debug.LogError("dataPath is not set in PlayerPrefs!"); return; }
        string jsonPath = Path.Combine(dataPath, "corr.json");
        if (!File.Exists(jsonPath)) { Debug.LogError($"corr.json not found at path: {jsonPath}"); return; }
        try
        {
            string jsonContent = await File.ReadAllTextAsync(jsonPath);
            CorrespondenceList correspondenceList = JsonUtility.FromJson<CorrespondenceList>(jsonContent);
            CorrespondenceEntry entry = correspondenceList.correspondences.FirstOrDefault(c => c.key == trackKey);
            if (entry == null) { Debug.LogError($"Track key '{trackKey}' not found in corr.json."); return; }
            string audioFileName = entry.value;
            string audioFilePath = Path.Combine(dataPath, audioFileName);
            if (!File.Exists(audioFilePath)) { Debug.LogError($"Audio file not found at path: {audioFilePath}"); return; }
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
                        loadedClip.name = audioFileName;
                        player.SetClip(loadedClip);
                        Debug.Log($"Successfully loaded and set clip: {audioFileName}");
                    }
                }
            }
        }
        catch (System.Exception ex) { Debug.LogError($"An error occurred while loading the audio clip: {ex.Message}"); }
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
        BGmusic.Play();
        selectorGO.transform.GetChild(0).gameObject.SetActive(true);
        selectorGO.transform.GetChild(1).gameObject.SetActive(false);
        selectorGO.transform.GetChild(1).GetChild(2).gameObject.SetActive(true);
        PlayerPrefs.SetInt("editing", 0);
    }
    public void ClearTimestampFor(int index)
    {
        if (index <= 0 || index >= timestamps.Count) return;
        timestamps[index] = -1f;
        activeLyricItems[index].ClearTimestamp();
        Debug.Log($"Cleared timestamp for lyric at index {index}");
    }
    #endregion
}