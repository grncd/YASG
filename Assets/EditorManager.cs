using MPUIKIT;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using System.IO; // Required for Path and File operations
using System.Linq; // Required for FirstOrDefault()
using System.Threading.Tasks; // Required for async/await Tasks
using UnityEngine.Networking; // Required for UnityWebRequest

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
    public GameObject selectorGO;
    public static EditorManager Instance { get; private set; }
    private string trackName;
    private string artistName;
    private string albumName;
    private int duration;
    private string plainLyrics;
    private string syncedLyrics;
    public TextMeshProUGUI songInfo;
    public LyricsScroller lyricsScroller;
    private int currentLyric;
    private GameObject tabs;
    private MusicPlayer player;
    public AudioSource BGmusic;

    private void Start()
    {
        Instance = this;
        player = GetComponent<MusicPlayer>();
        tabs = transform.GetChild(0).GetChild(1).gameObject;
    }

    void OnEnable()
    {
        selectorGO.SetActive(true);
        BGmusic.Stop();
        transform.GetChild(1).GetComponent<CanvasGroup>().alpha = 1f;
        transform.GetChild(1).GetComponent<CanvasGroup>().blocksRaycasts = false;
        transform.GetChild(0).GetComponent<CanvasGroup>().alpha = 0f;
        transform.GetChild(0).GetComponent<CanvasGroup>().blocksRaycasts = false;
        selectorGO.transform.GetChild(0).gameObject.SetActive(false);
        selectorGO.transform.GetChild(1).gameObject.SetActive(true);
        selectorGO.transform.GetChild(1).GetChild(2).gameObject.SetActive(false);
        lyricsScroller.CenterOnLyric(2);
        PlayerPrefs.SetInt("editing", 1);
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.DownArrow))
        {
            if (currentLyric != lyricsScroller.transform.GetChild(0).GetChild(0).childCount - 4)
            {
                currentLyric++;
                lyricsScroller.CenterOnLyric(currentLyric);
            }
        }

        if (Input.GetKeyDown(KeyCode.UpArrow))
        {
            if (currentLyric != 1)
            {
                currentLyric--;
                lyricsScroller.CenterOnLyric(currentLyric);
            }
        }
    }

    public void ToggleSyncTab()
    {
        tabs.transform.GetChild(0).GetComponent<MPImage>().color = new Color(0.2169811f, 0.2169811f, 0.2169811f);
        tabs.transform.GetChild(0).GetChild(0).GetComponent<TextMeshProUGUI>().color = Color.white;
        tabs.transform.GetChild(1).GetComponent<MPImage>().color = Color.white;
        tabs.transform.GetChild(1).GetChild(0).GetComponent<TextMeshProUGUI>().color = Color.black;
        tabs.transform.parent.GetChild(2).gameObject.SetActive(false);
        tabs.transform.parent.GetChild(3).gameObject.SetActive(true);
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
        await LevelResourcesCompiler.Instance.DownloadSong(url,track);

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
}