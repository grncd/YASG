using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using TMPro;
using UnityEngine;
using System.Threading.Tasks;
using System.Threading;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine.SceneManagement;
using System.Linq;
using JetBrains.Annotations;
using UnityEngine.UI;
using Unity.VisualScripting;
using UnityEngine.Audio;
using MPUIKIT;
using static SearchHandler;
using UnityEngine.Networking;
using System.Text.RegularExpressions;
using Debug = UnityEngine.Debug;
using GameKit.Dependencies.Utilities;
using FishNet.Demo.AdditiveScenes;

public class LevelResourcesCompiler : MonoBehaviour
{
    public static Track lastPrecompiledTrack;
    public GameObject loadingCanvas;
    public TextMeshProUGUI status;
    public string dataPath;
    private string extractedFileName = null;
    private float totalETA = 0f;
    private float currentETA = 0f;
    private bool splittingVocals = false;
    public Slider progressBar;
    private Process vocalSplitProc;
    public Animator blurAnim;
    public Animator loadingAnim;
    public GameObject loadingFirst;
    public GameObject loadingSecond;
    private bool initLoadingDone;
    public AudioMixerGroup musicControl;
    private Coroutine transitionCoroutine;
    public GameObject songInfo;
    private Track selectedTrack;
    public Animator transitionAnim;
    public GameObject mainPanel;
    private bool processLocally = false;
    public AlertManager alertManager;
    private bool lyricsError = false;
    private bool lyricsError2 = false; // Flag for initial lyrics script failure
    public DifficultyHover DIH;
    public DifficultySelector DIH2;
    private float currentPercentage = 0f;
    public UnityEngine.UI.Image bgDarken;
    public GameObject bgGM;
    private bool fakeLoading = false;
    private float elapsedFakeLoading = 0f;
    public GameObject starHSPrefab;
    public AudioClip stage1FX;
    public AudioClip stage2FX;
    public AudioClip stage3FX;
    public GameObject loadingFX;
    private bool dontSave = false;
    public bool compiling = false;
    public Button startCompileButton;
    public bool partyMode = false;

    public TextMeshProUGUI currentStatusText;
    private bool partyModeStartAllowed = true;
    public Transform partyModeAdvisors;
    public GameObject advisorPrefab;
    public GameObject partyModeUI;
    public WebServerManager webServerManager;

    public List<bool> playersReady;

    public TextMeshProUGUI nextSongName;
    public TextMeshProUGUI nextSongArtist;
    public MPImage nextSongCover;
    public MPImage nextSongBackdrop;
    public TextMeshProUGUI nextSongLength;
    public GameObject menuGO;
    public GameObject profileDisplay;
    public Sprite placeholder;
    public CanvasGroup textAdvisors;
    public AudioSource playFX;

    private Process currentBGProcess;
    private CancellationTokenSource backgroundCompileCancellationSource;

    private VocalRemoverAPI _vocalRemoverAPI;

    private int _originalVSyncCount;
    private static bool _hasRunUpdateCheck = false;

    public static LevelResourcesCompiler Instance { get; private set; }

    private void Awake()
    {
        // Singleton setup
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject); // Persist across scenes
        }
        else
        {
            Destroy(Instance.gameObject); // Destroy previous instance
            Instance = this;
            DontDestroyOnLoad(gameObject); // Persist across scenes
        }
    }



    private void Start()
    {
        dataPath = PlayerPrefs.GetString("dataPath");
        Application.targetFrameRate = -1;
        progressBar.gameObject.SetActive(false);
        initLoadingDone = true;

        // Only run update check once per game session (delayed by 1 second)
        if (!_hasRunUpdateCheck)
        {
            _hasRunUpdateCheck = true;
            StartCoroutine(DelayedUpdateCheck());
        }

        if (PlayerPrefs.GetInt("partyMode") == 1)
        {
            partyMode = true;
            partyModeUI.SetActive(true);
            menuGO.SetActive(false);
            profileDisplay.SetActive(false);
            GameObject.Find("QRCodeCanvas").GetComponent<CanvasGroup>().alpha = 1f;
            UpdatePartyModeUI();
        }

        // T Mode: Automatically start compile for testing
        if (Application.version.StartsWith("T"))
        {
            Debug.Log("[T Mode] Auto-compile starting...");
            StartCoroutine(TModeAutoCompile());
        }
    }

    private IEnumerator TModeAutoCompile()
    {
        // Wait a frame for everything to initialize
        yield return new WaitForSeconds(1f);

        Debug.Log("[T Mode] Starting automated test sequence...");

        // Create a mock Track object so LRCLib fallback works
        selectedTrack = new SearchHandler.Track
        {
            name = "Don't Forget",
            artists = new List<SearchHandler.Artist> { new SearchHandler.Artist { name = "Toby Fox" } },
            duration_ms = 51000,
            album = new SearchHandler.Album
            {
                name = "DELTARUNE Chapter 1 OST",
                images = new List<SearchHandler.Image> { new SearchHandler.Image { url = "https://i.scdn.co/image/ab67616d0000b2732f2eeee9b405f4d00428d84c", width = 640, height = 640 } }
            }
        };

        string testUrl = "https://open.spotify.com/track/7y6NbGuLZuNW0lVaPnYx22";
        string testName = "Don't Forget";
        string testArtist = "Toby Fox";
        string testLength = "0:51";
        string testCover = "https://i.scdn.co/image/ab67616d0000b2732f2eeee9b405f4d00428d84c";

        // --- PHASE 1: Test with Demucs ---
        Debug.Log("[T Mode] Phase 1: Setting VocalProcessingMethod to Demucs (1)...");
        SettingsManager.Instance.SetSetting("VocalProcessingMethod", 1);

        Debug.Log("[T Mode] Phase 1: Starting compile with Demucs...");
        Task compileTask = TModeCompile(testUrl, testName, testArtist, testLength, testCover);
        yield return new WaitUntil(() => compileTask.IsCompleted);

        if (compileTask.IsFaulted)
        {
            Debug.LogError($"[T Mode] Phase 1 compile failed: {compileTask.Exception?.InnerException?.Message}");
            TModeOutputFail("Phase 1 (Demucs) compile task failed: " + compileTask.Exception?.InnerException?.Message);
            yield break;
        }

        Debug.Log("[T Mode] Phase 1: Compile complete. Checking files...");
        bool phase1FilesOk = TModeCheckFiles(testArtist, testName);

        if (!phase1FilesOk)
        {
            Debug.LogError("[T Mode] Phase 1 (Demucs) failed: Required files not found.");
            TModeOutputFail("Phase 1 (Demucs) failed: Required files not found.");
            yield break;
        }

        Debug.Log("[T Mode] Phase 1: All files present. Deleting files for Phase 2...");
        TModeDeleteFiles(testArtist, testName);

        // --- PHASE 2: Test with VocalRemover.org ---
        Debug.Log("[T Mode] Phase 2: Setting VocalProcessingMethod to VocalRemover.org (0)...");
        SettingsManager.Instance.SetSetting("VocalProcessingMethod", 0);

        Debug.Log("[T Mode] Phase 2: Starting compile with VocalRemover.org...");
        Task compileTask2 = TModeCompile(testUrl, testName, testArtist, testLength, testCover);
        yield return new WaitUntil(() => compileTask2.IsCompleted);

        if (compileTask2.IsFaulted)
        {
            Debug.LogError($"[T Mode] Phase 2 compile failed: {compileTask2.Exception?.InnerException?.Message}");
            TModeOutputFail("Phase 2 (VocalRemover.org) compile task failed: " + compileTask2.Exception?.InnerException?.Message);
            yield break;
        }

        Debug.Log("[T Mode] Phase 2: Compile complete. Checking files...");
        bool phase2FilesOk = TModeCheckFiles(testArtist, testName);

        if (!phase2FilesOk)
        {
            Debug.LogError("[T Mode] Phase 2 (VocalRemover.org) failed: Required files not found.");
            TModeOutputFail("Phase 2 (VocalRemover.org) failed: Required files not found.");
            yield break;
        }

        Debug.Log("[T Mode] All tests passed! Generating success.txt...");
        TModeOutputSuccess();
    }

    /// <summary>
    /// Simplified compile for T Mode testing. Does not load Main scene.
    /// </summary>
    private async Task TModeCompile(string url, string name, string artist, string length, string cover)
    {
        dataPath = PlayerPrefs.GetString("dataPath");
        Debug.Log($"[T Mode] Compiling: {name} by {artist}");

        string sanitizedName = SanitizeFileName(name);
        string safeArtist = SanitizeFileName(artist);
        string expectedFileName = $"{safeArtist} - {sanitizedName}.mp3";
        string expectedAudioPath = Path.Combine(dataPath, "downloads", expectedFileName);

        // Fetch lyrics using LRCLib directly for simplicity
        Debug.Log("[T Mode] Fetching lyrics via LRCLib...");
        bool lyricsSuccess = await FetchLyricsFromLrcLib(selectedTrack);
        if (!lyricsSuccess)
        {
            throw new Exception("Failed to fetch lyrics from LRCLib.");
        }

        // Download song if not present
        if (!File.Exists(expectedAudioPath))
        {
            Debug.Log("[T Mode] Downloading song...");
            TimeSpan spotifyDuration = TimeSpan.FromMilliseconds(selectedTrack.duration_ms);
            string albumName = selectedTrack.album?.name;
            await SpotifyToYoutubeDownloader.DownloadClosestMatch(artist, name, spotifyDuration, expectedAudioPath, albumName);
        }
        else
        {
            Debug.Log("[T Mode] Audio file already exists. Skipping download.");
        }

        // Split vocals
        Debug.Log("[T Mode] Splitting vocals...");
        await RunPythonDirectly(expectedAudioPath);

        Debug.Log("[T Mode] Compile complete.");
    }

    /// <summary>
    /// Checks if all required files are present for T Mode testing.
    /// </summary>
    private bool TModeCheckFiles(string artist, string songName)
    {
        string sanitizedName = SanitizeFileName(songName);
        string safeArtist = SanitizeFileName(artist);
        string expectedFileName = $"{safeArtist} - {sanitizedName}";

        // Expected files:
        // 1. Audio file: downloads/{artist} - {name}.mp3
        string audioPath = Path.Combine(dataPath, "downloads", expectedFileName + ".mp3");
        // 2. Lyrics file: downloads/{name}.lrc or downloads/{name}.txt
        string lyricsPathLrc = Path.Combine(dataPath, "downloads", sanitizedName + ".lrc");
        string lyricsPathTxt = Path.Combine(dataPath, "downloads", sanitizedName + ".txt");
        // 3. Vocal file: output/htdemucs/{artist} - {name} [vocals].mp3
        string vocalPath = Path.Combine(dataPath, "output", "htdemucs", expectedFileName + " [vocals].mp3");
        // 4. Instrumental file: output/htdemucs/{artist} - {name} [no_vocals].mp3
        string instrumentalPath = Path.Combine(dataPath, "output", "htdemucs", expectedFileName + " [no_vocals].mp3");

        Debug.Log($"[T Mode] Checking audio: {audioPath} - Exists: {File.Exists(audioPath)}");
        Debug.Log($"[T Mode] Checking lyrics (lrc): {lyricsPathLrc} - Exists: {File.Exists(lyricsPathLrc)}");
        Debug.Log($"[T Mode] Checking lyrics (txt): {lyricsPathTxt} - Exists: {File.Exists(lyricsPathTxt)}");
        Debug.Log($"[T Mode] Checking vocals: {vocalPath} - Exists: {File.Exists(vocalPath)}");
        Debug.Log($"[T Mode] Checking instrumental: {instrumentalPath} - Exists: {File.Exists(instrumentalPath)}");

        bool audioExists = File.Exists(audioPath);
        bool lyricsExists = File.Exists(lyricsPathLrc) || File.Exists(lyricsPathTxt);
        bool vocalExists = File.Exists(vocalPath);
        bool instrumentalExists = File.Exists(instrumentalPath);

        return audioExists && lyricsExists && vocalExists && instrumentalExists;
    }

    /// <summary>
    /// Deletes all generated files for a song (used between T Mode phases).
    /// </summary>
    private void TModeDeleteFiles(string artist, string songName)
    {
        string sanitizedName = SanitizeFileName(songName);
        string safeArtist = SanitizeFileName(artist);
        string expectedFileName = $"{safeArtist} - {sanitizedName}";

        string audioPath = Path.Combine(dataPath, "downloads", expectedFileName + ".mp3");
        string lyricsPathLrc = Path.Combine(dataPath, "downloads", sanitizedName + ".lrc");
        string lyricsPathTxt = Path.Combine(dataPath, "downloads", sanitizedName + ".txt");
        string vocalPath = Path.Combine(dataPath, "output", "htdemucs", expectedFileName + " [vocals].mp3");
        string instrumentalPath = Path.Combine(dataPath, "output", "htdemucs", expectedFileName + " [no_vocals].mp3");

        try { if (File.Exists(audioPath)) File.Delete(audioPath); Debug.Log($"[T Mode] Deleted: {audioPath}"); } catch (Exception e) { Debug.LogWarning($"[T Mode] Failed to delete {audioPath}: {e.Message}"); }
        try { if (File.Exists(lyricsPathLrc)) File.Delete(lyricsPathLrc); Debug.Log($"[T Mode] Deleted: {lyricsPathLrc}"); } catch (Exception e) { Debug.LogWarning($"[T Mode] Failed to delete {lyricsPathLrc}: {e.Message}"); }
        try { if (File.Exists(lyricsPathTxt)) File.Delete(lyricsPathTxt); Debug.Log($"[T Mode] Deleted: {lyricsPathTxt}"); } catch (Exception e) { Debug.LogWarning($"[T Mode] Failed to delete {lyricsPathTxt}: {e.Message}"); }
        try { if (File.Exists(vocalPath)) File.Delete(vocalPath); Debug.Log($"[T Mode] Deleted: {vocalPath}"); } catch (Exception e) { Debug.LogWarning($"[T Mode] Failed to delete {vocalPath}: {e.Message}"); }
        try { if (File.Exists(instrumentalPath)) File.Delete(instrumentalPath); Debug.Log($"[T Mode] Deleted: {instrumentalPath}"); } catch (Exception e) { Debug.LogWarning($"[T Mode] Failed to delete {instrumentalPath}: {e.Message}"); }
    }

    /// <summary>
    /// Outputs success.txt and quits the application.
    /// </summary>
    private void TModeOutputSuccess()
    {
        string executableDir = Path.GetDirectoryName(Application.dataPath);
        string successPath = Path.Combine(executableDir, "success.txt");
        try
        {
            File.WriteAllText(successPath, $"T Mode test completed successfully at {DateTime.Now}");
            Debug.Log($"[T Mode] SUCCESS! Written to: {successPath}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[T Mode] Failed to write success.txt: {e.Message}");
        }
        Application.Quit();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    /// <summary>
    /// Outputs fail.txt with reason and quits the application.
    /// </summary>
    private void TModeOutputFail(string reason)
    {
        string executableDir = Path.GetDirectoryName(Application.dataPath);
        string failPath = Path.Combine(executableDir, "fail.txt");
        try
        {
            File.WriteAllText(failPath, $"T Mode test failed at {DateTime.Now}\nReason: {reason}");
            Debug.LogError($"[T Mode] FAILED! Reason: {reason}. Written to: {failPath}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[T Mode] Failed to write fail.txt: {e.Message}");
        }
        Application.Quit();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }



    public void TogglePartyMode()
    {
        if (!partyMode)
        {
            partyMode = true;
            partyModeUI.SetActive(true);
            profileDisplay.SetActive(false);
            UpdatePartyModeUI();
            PlayerPrefs.SetInt("partyMode", 1);
            GameObject.Find("QRCodeCanvas").GetComponent<CanvasGroup>().alpha = 1f;
            // Start web server for party mode
            if (webServerManager != null)
            {
                webServerManager.StartWebServer();
                webServerManager.player1Old = PlayerPrefs.GetInt("Player1");
                webServerManager.player1NameOld = PlayerPrefs.GetString("Player1Name");
                webServerManager.player2Old = PlayerPrefs.GetInt("Player2");
                webServerManager.player2NameOld = PlayerPrefs.GetString("Player2Name");
                webServerManager.player3Old = PlayerPrefs.GetInt("Player3");
                webServerManager.player3NameOld = PlayerPrefs.GetString("Player3Name");
                webServerManager.player4Old = PlayerPrefs.GetInt("Player4");
                webServerManager.player4NameOld = PlayerPrefs.GetString("Player4Name");
            }
            else
            {
                partyMode = false;
                partyModeUI.SetActive(false);
                profileDisplay.SetActive(true);
                menuGO.SetActive(true);
                PlayerPrefs.SetInt("partyMode", 0);
                GameObject.Find("QRCodeCanvas").GetComponent<CanvasGroup>().alpha = 0f;
                // Stop web server
                if (webServerManager != null)
                {
                    webServerManager.StopWebServer();
                    PlayerPrefs.SetInt("Player1", webServerManager.player1Old);
                    PlayerPrefs.SetString("Player1Name", webServerManager.player1NameOld);
                    PlayerPrefs.SetInt("Player2", webServerManager.player2Old);
                    PlayerPrefs.SetString("Player2Name", webServerManager.player2NameOld);
                    PlayerPrefs.SetInt("Player3", webServerManager.player3Old);
                    PlayerPrefs.SetString("Player3Name", webServerManager.player3NameOld);
                    PlayerPrefs.SetInt("Player4", webServerManager.player4Old);
                    PlayerPrefs.SetString("Player4Name", webServerManager.player4NameOld);
                }
            }
        }
    }

    public void UpdatePartyModeUI()
    {
        if (WebServerManager.Instance != null && WebServerManager.Instance.mainQueue.Count != 0)
        {
            playersReady.Clear();
            if (partyModeAdvisors != null)
            {
                foreach (Transform child in partyModeAdvisors)
                {
                    Destroy(child.gameObject);
                }
            }
            var i = 0;
            foreach (string player in WebServerManager.Instance.mainQueue[0].players)
            {
                i++;
                playersReady.Add(false);
                GameObject advisor = Instantiate(advisorPrefab, partyModeAdvisors);
                advisor.name = $"Player{i}";
                advisor.transform.GetChild(1).GetChild(1).GetComponent<TextMeshProUGUI>().text = player + ",";
                advisor.transform.GetChild(2).GetChild(1).GetComponent<TextMeshProUGUI>().text = player + ",";
                advisor.transform.GetChild(3).GetChild(1).GetComponent<TextMeshProUGUI>().text = player + ",";

            }
            if (i == 4)
            {
                GameObject.Find("QRCodeCanvas").GetComponent<CanvasGroup>().alpha = 0.5f;
            }
        }
        else
        {
            foreach (Transform child in partyModeAdvisors)
            {
                Destroy(child.gameObject);
            }
        }
    }

    public async void PartyModeStart()
    {
        partyModeStartAllowed = false;
        playersReady.Clear();
        var firstTrack = WebServerManager.Instance.mainQueue[0].track;
        playFX.Play();
        await Task.Delay(1010);
        StartPartyMode(firstTrack.url, firstTrack.name, firstTrack.artist, firstTrack.length, firstTrack.cover);
    }

    void Update()
    {
        lock (executionQueue)
        {
            while (executionQueue.Count > 0)
            {
                executionQueue.Dequeue().Invoke();
            }
        }
        if (partyMode)
        {
            if (SceneManager.GetActiveScene().name == "Menu")
            {
                if (WebServerManager.Instance != null)
                {
                    currentStatusText.text = WebServerManager.Instance.GetCurrentStatus();
                    if (WebServerManager.Instance.mainQueue.Count != 0)
                    {
                        textAdvisors.alpha = 1f;
                        if (playersReady.All(ready => ready) && playersReady.Count != 0 && WebServerManager.Instance.mainQueue[0].processed)
                        {
                            PartyModeStart();
                        }
                        if (Input.GetKeyDown(KeyCode.Backspace))
                        {
                            WebServerManager.Instance.mainQueue.RemoveAt(0);
                            BGMusic.Instance.StopPreview();
                            playersReady.Clear();
                            WebServerManager.Instance.SetCurrentStatus("");
                            UpdatePartyModeUI();
                            if (WebServerManager.Instance.IsProcessing())
                            {
                                WebServerManager.Instance.SetProcessing(false);
                                if (currentBGProcess != null && !currentBGProcess.HasExited)
                                {
                                    try
                                    {
                                        currentBGProcess.Kill();
                                        Debug.Log("Killed background process from BackgroundCompile.");
                                    }
                                    catch (Exception ex)
                                    {
                                        Debug.LogWarning($"Failed to kill background process: {ex.Message}");
                                    }
                                }
                                // Cancel the BackgroundCompile task
                                if (backgroundCompileCancellationSource != null)
                                {
                                    backgroundCompileCancellationSource.Cancel();
                                    Debug.Log("Cancelled BackgroundCompile task.");
                                }
                            }
                        }
                        if (WebServerManager.Instance.mainQueue[0].track.name != nextSongName.text)
                        {
                            nextSongName.text = WebServerManager.Instance.mainQueue[0].track.name;
                            nextSongArtist.text = WebServerManager.Instance.mainQueue[0].track.artist;
                            nextSongLength.text = "Song Length: " + WebServerManager.Instance.mainQueue[0].track.length;
                            StartCoroutine(DownloadAlbumCover(WebServerManager.Instance.mainQueue[0].track.cover, nextSongCover, nextSongBackdrop));
                            BGMusic.Instance.PreviewSong(WebServerManager.Instance.mainQueue[0].track.url);
                        }
                    }
                    else
                    {
                        nextSongName.text = "----------------------";
                        nextSongArtist.text = "Escaneie o QR Code abaixo para adicionar músicas!";
                        nextSongLength.text = "Song Length: X:XX";
                        nextSongCover.sprite = placeholder;
                        nextSongBackdrop.sprite = placeholder;
                        textAdvisors.alpha = 0.2f;
                    }
                }
            }
            if (WebServerManager.Instance != null && WebServerManager.Instance.mainQueue.Count != 0)
            {
                if (!WebServerManager.Instance.mainQueue[0].processed && !WebServerManager.Instance.IsProcessing())
                {
                    ProcessFirstOnQueue();
                }
            }
        }

        if (lyricsError)
        {
            if (BGMusic.Instance != null) BGMusic.Instance.StopPreview();
            alertManager.ShowError("This song does not have lyrics.", "The song you've selected either has no lyrics or we couldn't find any synced lyrics for it. If this song has lyrics and you'd like to add them, <b>use the Add Lyrics button</b> located in the menu.", "Dismiss");
            LoadingDone();
            mainPanel.SetActive(true);
            lyricsError = false; // Reset after showing
        }
        if (fakeLoading)
        {
            progressBar.gameObject.SetActive(true);
            elapsedFakeLoading += Time.deltaTime;
            progressBar.value = elapsedFakeLoading / 32f;
            GameObject progress2 = loadingSecond.transform.GetChild(4).GetChild(1).gameObject;
            progress2.GetComponent<Slider>().value = elapsedFakeLoading / 32f;
        }
        else if (!fakeLoading && elapsedFakeLoading != 0f)
        {
            elapsedFakeLoading = 0f;
            progressBar.value = 0f;
            progressBar.gameObject.SetActive(false);
        }
        if (initLoadingDone)
        {
            initLoadingDone = false;
            LoadingDone();
        }
        if (splittingVocals && !partyMode)
        {
            GameObject progress3 = loadingSecond.transform.GetChild(4).GetChild(2).gameObject;
            progress3.GetComponent<Slider>().value = progressBar.value;
        }
        if (!string.IsNullOrEmpty(extractedFileName))
        {
            if (!processLocally)
            {
                UnityEngine.Debug.Log("Extracted file name: " + extractedFileName);
                extractedFileName = Path.GetFileNameWithoutExtension(extractedFileName);
            }
            string vocalLocation = Path.Combine(dataPath, "output", extractedFileName + " [vocals].mp3");
            UnityEngine.Debug.Log("PlayerPrefs updated with vocalLocation: " + vocalLocation);

            extractedFileName = null;
        }
        if (currentPercentage != 0f && !partyMode)
        {
            progressBar.gameObject.SetActive(true);
            progressBar.value = currentPercentage;
        }

    }

    public bool CheckFile(string name)
    {
        string folderPath = Path.Combine(dataPath, "downloads");
        if (Directory.Exists(folderPath))
        {
            string[] files = Directory.GetFiles(folderPath);
            foreach (string file in files)
            {
                if (Path.GetFileName(file) == name)
                {
                    UnityEngine.Debug.Log("File exists: " + file);
                    return true;
                }
            }
        }
        return false;
    }

    public void SetAlbumCoverFromTrack(Track track, MPImage image, MPImage image2)
    {
        if (track == null || track.album == null || track.album.images == null || track.album.images.Count == 0)
        {
            UnityEngine.Debug.LogWarning("No album cover found for this track.");
            return;
        }

        var highestResImage = track.album.images.OrderByDescending(img => img.width).FirstOrDefault();
        if (highestResImage != null)
        {
            StartCoroutine(DownloadAlbumCover(highestResImage.url, image, image2));
        }
    }

    public string GetURLCoverFromTrack(Track track)
    {
        if (track == null || track.album == null || track.album.images == null || track.album.images.Count == 0)
        {
            UnityEngine.Debug.LogWarning("No album cover found for this track.");
            return "";
        }

        var highestResImage = track.album.images.OrderByDescending(img => img.width).FirstOrDefault();
        return highestResImage?.url ?? "";
    }

    private IEnumerator DownloadAlbumCover(string imageUrl, MPImage image, MPImage image2)
    {
        using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(imageUrl))
        {
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                Texture2D texture = DownloadHandlerTexture.GetContent(request);
                Sprite albumCover = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                image.sprite = albumCover;
                image2.sprite = albumCover;
            }
            else
            {
                UnityEngine.Debug.LogError("Failed to download album cover: " + request.error);
            }
        }
    }

    public void SetSelectedTrack(Track track)
    {
        selectedTrack = track;
    }

    public void RemoveLoadingTint()
    {
        var loadingTint = songInfo.transform.GetChild(4).GetChild(0).GetChild(2).GetChild(0).GetComponent<CanvasGroup>();
        StartCoroutine(FadeCanvasGroupAlpha(loadingTint, 0.5f));
    }

    private IEnumerator FadeCanvasGroupAlpha(CanvasGroup canvasGroup, float duration)
    {
        float startAlpha = canvasGroup.alpha;
        float time = 0f;
        while (time < duration)
        {
            time += Time.deltaTime;
            canvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, time / duration);
            yield return null;
        }
        canvasGroup.alpha = 0f;
    }

    public void PreCompile(string url, string name, string artist, string length, Track track)
    {
        if (ProfileManager.Instance.GetActiveProfiles().Count == 0)
        {
            alertManager.ShowError("You don't have any active profiles!", "Please go to the Settings (cogwheel on the bottom right) and either create a new profile or activate an existing one.", "Dismiss");
            return;
        }

        foreach (var profile in ProfileManager.Instance.GetActiveProfiles())
        {
            if (profile.microphone == "Default")
            {
                AlertManager.Instance.ShowWarning("There are micless profiles.", "One of your profiles has no microphone selected. If you aren't going to use said profile, please disable it.", "Dismiss");
                return;
            }
        }

        BGMusic.Instance.PreviewSong(url);
        var loadingTint = songInfo.transform.GetChild(4).GetChild(0).GetChild(2).GetChild(0).GetComponent<CanvasGroup>();
        loadingTint.alpha = 1f;

        bool isFavorite = FavoritesManager.IsFavorite(url);
        string cover = GetURLCoverFromTrack(track);

        songInfo.SetActive(true);
        songInfo.GetComponent<Animator>().Play("SongInfoIn");
        selectedTrack = track;
        lastPrecompiledTrack = track; // Cache the track object for multiplayer

        songInfo.transform.GetChild(4).GetChild(0).GetChild(0).GetComponent<TextMeshProUGUI>().text = name;
        songInfo.transform.GetChild(4).GetChild(0).GetChild(1).GetComponent<TextMeshProUGUI>().text = artist;
        string lengthText = "Song Length: " + length;
        if (FavoritesManager.IsDownloaded(url))
        {
            lengthText += " <color=#5dff4d>(Downloaded)</color>";
        }
        songInfo.transform.GetChild(4).GetChild(0).GetChild(3).GetComponent<TextMeshProUGUI>().text = lengthText;

        var playButton = songInfo.transform.GetChild(4).GetChild(0).GetChild(4).GetComponent<Button>();
        var favButton = songInfo.transform.GetChild(4).GetChild(0).GetChild(5).GetComponent<Button>();
        var unfavButton = songInfo.transform.GetChild(4).GetChild(0).GetChild(6).GetComponent<Button>();

        playButton.onClick.RemoveAllListeners();
        favButton.onClick.RemoveAllListeners();
        unfavButton.onClick.RemoveAllListeners();

        playButton.onClick.AddListener(() => StartCompile(url, name, artist, length, cover));

        favButton.gameObject.SetActive(!isFavorite);
        unfavButton.gameObject.SetActive(isFavorite);

        favButton.onClick.AddListener(() =>
        {
            AddFavorite(name, artist, length, cover, url);
            favButton.gameObject.SetActive(false);
            unfavButton.gameObject.SetActive(true);
        });

        unfavButton.onClick.AddListener(() =>
        {
            RemoveFavorite(url);
            favButton.gameObject.SetActive(true);
            unfavButton.gameObject.SetActive(false);
        });

        SetAlbumCoverFromTrack(track, songInfo.transform.GetChild(4).GetChild(0).GetComponent<MPImage>(), songInfo.transform.GetChild(4).GetChild(0).GetChild(2).GetComponent<MPImage>());

        Transform highscorePanel = songInfo.transform.GetChild(4).GetChild(0).GetChild(7);
        Transform placeholderPanel = songInfo.transform.GetChild(4).GetChild(0).GetChild(8);
        HighscoreEntry highscore = HighscoreManager.Instance.GetHighscore(url);

        if (highscore != null)
        {
            placeholderPanel.gameObject.SetActive(false);
            highscorePanel.gameObject.SetActive(true);

            highscorePanel.GetChild(0).GetComponent<TextMeshProUGUI>().text = highscore.score.ToString("#,#");
            highscorePanel.GetChild(2).GetComponent<TextMeshProUGUI>().text = "By: " + highscore.playerName;

            Transform starsLocation = highscorePanel.GetChild(1).transform;
            foreach (Transform child in starsLocation) Destroy(child.gameObject);

            if (starHSPrefab != null)
            {
                for (int i = 0; i < highscore.stars; i++)
                {
                    Instantiate(starHSPrefab, starsLocation);
                }
            }
            else
            {
                UnityEngine.Debug.LogWarning("starHSPrefab is not assigned!");
            }
        }
        else
        {
            highscorePanel.gameObject.SetActive(false);
            placeholderPanel.gameObject.SetActive(true);
        }
    }

    public void AddFavorite(string name, string artist, string length, string cover, string url)
    {
        FavoritesManager.AddFavorite(name, artist, length, cover, url);
        songInfo.transform.GetChild(5).GetComponent<AudioSource>().Play();
    }

    public void RemoveFavorite(string url)
    {
        FavoritesManager.RemoveFavoriteByUrl(url);
        songInfo.transform.GetChild(6).GetComponent<AudioSource>().Play();
    }

    public async void Dismiss()
    {
        BGMusic.Instance.StopPreview();
        DIH.Close();
        DIH2.Close();
        songInfo.GetComponent<Animator>().Play("SongInfoOut");
        await Task.Delay(200);
        songInfo.SetActive(false);
    }

    public async void Dismiss(bool plc)
    {
        DIH.Close();
        DIH2.Close();
        songInfo.GetComponent<Animator>().Play("SongInfoOut");
        await Task.Delay(200);
        songInfo.SetActive(false);
    }

    public void ChallengeBegin()
    {
        loadingCanvas.SetActive(true);
        loadingSecond.SetActive(true);
        loadingSecond.transform.GetChild(4).gameObject.SetActive(false);
        loadingFirst.SetActive(false);
        BeginLoading();
        loadingFX.SetActive(true);
        status.text = "Solving API Challenge... (can take a while)";
    }

    public void ChallengeEnd()
    {
        LoadingDone();
        loadingFX.SetActive(false);
    }

    public async Task DownloadSong(string url, string name, string artist)
    {
        string expectedAudioPath = GetExpectedAudioFilePath(artist, name);
        if (File.Exists(expectedAudioPath))
        {
            UnityEngine.Debug.Log("Audio file already exists. Skipping download.");
            return;
        }

        loadingCanvas.SetActive(true);
        loadingSecond.SetActive(true);
        loadingSecond.transform.GetChild(4).gameObject.SetActive(false);
        loadingFirst.SetActive(false);
        BeginLoading();
        loadingFX.SetActive(true);
        status.text = "Downloading song for playback...";

        try
        {
            // Get duration and album from selectedTrack for better matching
            TimeSpan spotifyDuration = TimeSpan.Zero;
            string albumName = null;

            if (selectedTrack != null)
            {
                spotifyDuration = TimeSpan.FromMilliseconds(selectedTrack.duration_ms);
                albumName = selectedTrack.album?.name;
            }
            else if (lastPrecompiledTrack != null)
            {
                spotifyDuration = TimeSpan.FromMilliseconds(lastPrecompiledTrack.duration_ms);
                albumName = lastPrecompiledTrack.album?.name;
            }

            await SpotifyToYoutubeDownloader.DownloadClosestMatch(artist, name, spotifyDuration, expectedAudioPath, albumName);
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogError($"YouTube download failed: {ex.Message}");
            alertManager.ShowError("An error occurred downloading your song.", "This is likely due to connectivity issues, or due to some rare inconsistency. Please try again.", "Dismiss");
            LoadingDone();
            loadingFX.SetActive(false);
            mainPanel.SetActive(true);
            return;
        }

        dontSave = false;
        LoadingDone();
        loadingFX.SetActive(false);
    }

    public async Task SplitSong(string filePath)
    {
        if (File.Exists(Path.Combine(PlayerPrefs.GetString("dataPath"), "output", "htdemucs", Path.GetFileNameWithoutExtension(filePath) + " [vocals].mp3")))
        {
            return;
        }

        loadingCanvas.SetActive(true);
        loadingSecond.SetActive(true);
        loadingSecond.transform.GetChild(4).gameObject.SetActive(false);
        loadingFirst.SetActive(false);
        BeginLoading();
        loadingFX.SetActive(true);
        status.text = "Splitting vocals for use...";

        splittingVocals = true;
        Color prev = bgDarken.color;

        if (SettingsManager.Instance.GetSetting<int>("VocalProcessingMethod") == 1)
        {
            StartCoroutine(FadeImageAlpha(bgDarken, 1f, 1f));
            await Task.Delay(1010);
            bgGM.SetActive(false);
        }

        _originalVSyncCount = QualitySettings.vSyncCount;
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 30;

        await RunPythonDirectly(filePath);

        Application.targetFrameRate = -1;
        QualitySettings.vSyncCount = _originalVSyncCount;

        splittingVocals = false;
        currentPercentage = 0f;

        bgGM.SetActive(true);
        bgDarken.color = prev;

        dontSave = false;
        LoadingDone();
        loadingFX.SetActive(false);
    }

    private string SanitizeFileName(string name)
    {
        string charactersToRemovePattern = @"[/\\:*?""<>|]";
        return Regex.Replace(name, charactersToRemovePattern, string.Empty);
    }

    private string GetExpectedAudioFilePath(string artist, string songName)
    {
        string sanitizedSongName = SanitizeFileName(songName);
        string sanitizedArtistName = SanitizeFileName(artist);
        string fileName = $"{sanitizedArtistName} - {sanitizedSongName}.mp3";
        return Path.Combine(dataPath, "downloads", fileName);
    }

    private async void ProcessFirstOnQueue()
    {
        if (WebServerManager.Instance != null)
        {
            WebServerManager.Instance.SetProcessing(true);
            var currentQueue = WebServerManager.Instance.mainQueue.FirstOrDefault(q => !q.processed);
            var currentTrack = WebServerManager.Instance.mainQueue.FirstOrDefault(q => !q.processed)?.track;
            if (currentTrack == null)
            {
                WebServerManager.Instance.SetProcessing(false);
                return;
            }

            // Create a new cancellation token source for this background compile operation
            backgroundCompileCancellationSource = new CancellationTokenSource();

            try
            {
                await BackgroundCompile(currentTrack.url, currentTrack.name, currentTrack.artist, currentTrack.length, currentTrack.cover, backgroundCompileCancellationSource.Token);
                currentQueue.processed = true;
            }
            catch (OperationCanceledException)
            {
                Debug.Log("BackgroundCompile was cancelled.");
            }
            finally
            {
                backgroundCompileCancellationSource?.Dispose();
                backgroundCompileCancellationSource = null;
                WebServerManager.Instance.SetProcessing(false);
            }
        }
    }

    public async void StartPartyMode(string url, string name, string artist, string length, string cover)
    {
        var i = 0;
        PlayerPrefs.SetInt("Player1", 0);
        PlayerPrefs.SetInt("Player2", 0);
        PlayerPrefs.SetInt("Player3", 0);
        PlayerPrefs.SetInt("Player4", 0);
        foreach (string playerName in WebServerManager.Instance.mainQueue[0].players)
        {
            i++;
            PlayerPrefs.SetString($"Player{i}Name", playerName);
            PlayerPrefs.SetInt($"Player{i}Difficulty", WebServerManager.Instance.mainQueue[0].playerDifficulties[i - 1]);
            PlayerPrefs.SetInt($"Player{i}", 1);
        }



        dataPath = PlayerPrefs.GetString("dataPath");
        UnityEngine.Debug.Log($"STARTING: {url}, {name}, {artist}, {length}, {cover}");
        PlayerPrefs.SetString("currentSongURL", url);
        name = name.Replace("\\", " ");
        songInfo.transform.GetChild(4).GetComponent<AudioSource>().Play();

        string sanitizedName = SanitizeFileName(name);
        if (!CheckFile(sanitizedName + ".txt"))
        {
            File.Move(Path.Combine(dataPath, "downloads", sanitizedName + ".lrc"), Path.Combine(dataPath, "downloads", sanitizedName + ".txt"));
        }

        transitionAnim.Play("TransitionSaved");
        await Task.Delay(1450);
        if (WebServerManager.Instance != null && WebServerManager.Instance.mainQueue.Count > 0)
        {
            WebServerManager.Instance.mainQueue.RemoveAt(0);
        }

        PlayerPrefs.SetString("currentSong", name);
        PlayerPrefs.SetString("currentArtist", artist);
        if (CheckFile(sanitizedName + ".txt"))
        {
            PlayerPrefs.SetInt("saved", 1);

            string safeArtist = SanitizeFileName(artist);
            string expectedFileName = $"{safeArtist} - {sanitizedName}.mp3";
            string expectedFilePath = Path.Combine(dataPath, "downloads", expectedFileName);

            if (File.Exists(expectedFilePath))
            {
                UnityEngine.Debug.Log("Found corresponding file: " + expectedFilePath);
                string vocalLocation = Path.Combine(dataPath, "output", "htdemucs", Path.GetFileNameWithoutExtension(expectedFilePath) + " [vocals].mp3");
                PlayerPrefs.SetString("vocalLocation", vocalLocation);
                if (SettingsManager.Instance.GetSetting<bool>("PlayInstrumental"))
                {
                    string instrumentalLocation = Path.Combine(dataPath, "output", "htdemucs", Path.GetFileNameWithoutExtension(expectedFilePath) + " [no_vocals].mp3");
                    PlayerPrefs.SetString("fullLocation", instrumentalLocation);
                }
                else
                {
                    PlayerPrefs.SetString("fullLocation", expectedFilePath);
                }
                partyModeStartAllowed = true;
                if (PlayerPrefs.GetInt("multiplayer") == 0) LoadMain();
                return;
            }
            else
            {
                UnityEngine.Debug.LogError($"Lyrics file found for '{name}', but the corresponding audio file '{expectedFileName}' is missing in the downloads folder. A re-download might be required. If the issue persists, delete the song's .txt file inside YASG's data folder.");
                return;
            }
        }
    }

    public async Task StartCompile(string url, string name, string artist, string length, string cover)
    {
        startCompileButton.interactable = false;
        dataPath = PlayerPrefs.GetString("dataPath");
        UnityEngine.Debug.Log($"CALLED: {url}, {name}, {artist}, {length}, {cover}");
        PlayerPrefs.SetString("currentSongURL", url);
        name = name.Replace("\\", " ");
        songInfo.transform.GetChild(4).GetComponent<AudioSource>().Play();

        string sanitizedName = SanitizeFileName(name);

        if (!CheckFile(sanitizedName + ".txt"))
        {
            transitionAnim.Play("Transition");
            await Task.Delay(1350);
            if (songInfo.activeSelf)
            {
                Dismiss(true);
                mainPanel.SetActive(false);
            }
            await Task.Delay(384);
        }
        else
        {
            transitionAnim.Play("TransitionSaved");
            await Task.Delay(1450);
            Dismiss(true);
            await Task.Delay(600);
        }

        PlayerPrefs.SetString("currentSong", name);
        PlayerPrefs.SetString("currentArtist", artist);
        startCompileButton.interactable = true;
        if (CheckFile(sanitizedName + ".txt"))
        {
            status.text = "Already downloaded. Loading main scene...";
            PlayerPrefs.SetInt("saved", 1);

            string safeArtist = SanitizeFileName(artist);
            string expectedFileName = $"{safeArtist} - {sanitizedName}.mp3";
            string expectedFilePath = Path.Combine(dataPath, "downloads", expectedFileName);

            if (File.Exists(expectedFilePath))
            {
                UnityEngine.Debug.Log("Found corresponding file: " + expectedFilePath);
                string vocalLocation = Path.Combine(dataPath, "output", "htdemucs", Path.GetFileNameWithoutExtension(expectedFilePath) + " [vocals].mp3");
                PlayerPrefs.SetString("vocalLocation", vocalLocation);
                if (SettingsManager.Instance.GetSetting<bool>("PlayInstrumental"))
                {
                    string instrumentalLocation = Path.Combine(dataPath, "output", "htdemucs", Path.GetFileNameWithoutExtension(expectedFilePath) + " [no_vocals].mp3");
                    PlayerPrefs.SetString("fullLocation", instrumentalLocation);
                }
                else
                {
                    PlayerPrefs.SetString("fullLocation", expectedFilePath);
                }
                if (PlayerPrefs.GetInt("multiplayer") == 0) LoadMain();
                return;
            }
            else
            {
                UnityEngine.Debug.LogError($"Lyrics file found for '{name}', but the corresponding audio file '{expectedFileName}' is missing in the downloads folder. A re-download might be required. If the issue persists, delete the song's .txt file inside YASG's data folder.");
                PlayerPrefs.SetInt("ERR", 1);
                UnityEngine.SceneManagement.SceneManager.LoadScene("Menu");
                return;
            }
        }

        PlayerPrefs.SetInt("saved", 0);
        loadingCanvas.SetActive(true);
        loadingSecond.SetActive(true);
        loadingSecond.transform.GetChild(4).gameObject.SetActive(true);
        loadingFirst.SetActive(false);
        BeginLoading();
        loadingFX.SetActive(true);
        status.text = "Fetching lyrics...";
        compiling = true;

        GameObject stage1 = loadingSecond.transform.GetChild(4).GetChild(3).GetChild(0).gameObject;
        GameObject stage2 = loadingSecond.transform.GetChild(4).GetChild(3).GetChild(1).gameObject;
        GameObject stage3 = loadingSecond.transform.GetChild(4).GetChild(3).GetChild(2).gameObject;
        GameObject stage4 = loadingSecond.transform.GetChild(4).GetChild(3).GetChild(3).gameObject;
        GameObject progress1 = loadingSecond.transform.GetChild(4).GetChild(0).gameObject;
        GameObject progress2 = loadingSecond.transform.GetChild(4).GetChild(1).gameObject;
        GameObject progress3 = loadingSecond.transform.GetChild(4).GetChild(2).gameObject;

        stage1.transform.GetChild(1).gameObject.SetActive(true);

        // Check LyricsSource setting: 0 = Syrics (with LRCLib fallback), 1 = LRCLib only
        int lyricsSource = SettingsManager.Instance?.GetSetting<int>("LyricsSource") ?? 0;

        if (lyricsSource == 0)
        {
            // --- SYRICS LOGIC ---
            bool isLinux = Application.platform == RuntimePlatform.LinuxPlayer || Application.platform == RuntimePlatform.LinuxEditor;
            string scriptName = isLinux ? "getlyrics.sh" : "getlyrics.bat";
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = Path.Combine(dataPath, scriptName),
                Arguments = url + " " + dataPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            Process process = new Process { StartInfo = psi };
            process.ErrorDataReceived += (sender, args) =>
            {
                if (string.IsNullOrEmpty(args.Data)) return;
                UnityEngine.Debug.Log("Error Output: " + args.Data);
                if (args.Data.Contains("some tracks") || args.Data.Contains("Your application has"))
                {
                    lyricsError2 = true; // Set flag to try fallback
                }
            };
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Wait for process completion or cancellation with timeout
            int timeoutSeconds = 7;
            if (SettingsManager.Instance != null)
            {
                if (!int.TryParse(SettingsManager.Instance.GetSetting<string>("SyricsTimeout", "7"), out timeoutSeconds)) UnityEngine.Debug.LogError("Error parsing SyricsTimeout");
            }
            UnityEngine.Debug.Log($"Lyrics Process Limit (Main): {timeoutSeconds} seconds.");
            var timeoutLimit = DateTime.Now.AddSeconds(timeoutSeconds);

            await Task.Run(() =>
            {
                while (!process.HasExited)
                {
                    if (DateTime.Now > timeoutLimit)
                    {
                        try
                        {
                            process.Kill();
                            process.WaitForExit(5000); // Wait up to 5 seconds for process to fully terminate
                            UnityEngine.Debug.LogWarning($"Lyrics process (Main) timed out HARD after {timeoutSeconds}s.");
                            lyricsError2 = true;
                        }
                        catch (Exception e) { UnityEngine.Debug.LogError($"Error killing timed-out process: {e.Message}"); }
                        break;
                    }
                    Thread.Sleep(500); // Check every 0.5s
                }
            });

            // Ensure process is fully disposed
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit(5000);
                }
                process.Dispose();
            }
            catch (Exception e) { UnityEngine.Debug.LogWarning($"Error disposing lyrics process: {e.Message}"); }

            UnityEngine.Debug.Log("Lyrics script finished.");

            // Check if a lyrics file was actually created recently
            string downloadsCheckPath = Path.Combine(dataPath, "downloads");
            bool recentLyricsFound = false;
            if (Directory.Exists(downloadsCheckPath))
            {
                var directoryInfo = new DirectoryInfo(downloadsCheckPath);
                var recentFiles = directoryInfo.GetFiles("*.*")
                                               .Where(f => (f.Extension == ".lrc" || f.Extension == ".txt") &&
                                                           (DateTime.Now - f.CreationTime).TotalSeconds < 20)
                                               .ToArray();
                if (recentFiles.Length > 0)
                {
                    recentLyricsFound = true;
                }
            }

            if (!recentLyricsFound && !lyricsError2)
            {
                UnityEngine.Debug.LogWarning("Syrics finished but no new lyrics file found. Forcing fallback.");
                lyricsError2 = true;
            }
            // --- END SYRICS LOGIC ---
        }
        else
        {
            // LRCLib only mode - skip Syrics entirely
            UnityEngine.Debug.Log("LyricsSource set to LRCLib only. Skipping Syrics.");
            lyricsError2 = true; // Force LRCLib path
        }

        // --- FALLBACK LOGIC ---
        if (lyricsError2)
        {
            lyricsError2 = false; // Reset flag
            status.text = "Primary source failed. Trying LRCLib...";
            UnityEngine.Debug.Log("Initial lyric fetch failed. Attempting LRCLib fallback.");

            // In multiplayer, selectedTrack might be null. Use the cached track.
            Track trackForLrcLib = selectedTrack;
            if (PlayerPrefs.GetInt("multiplayer") == 1 && trackForLrcLib == null)
            {
                if (lastPrecompiledTrack != null && lastPrecompiledTrack.name == name && lastPrecompiledTrack.artists[0].name == artist)
                {
                    trackForLrcLib = lastPrecompiledTrack;
                    UnityEngine.Debug.Log("Using cached track for LRCLib fallback in multiplayer.");
                }
                else
                {
                    UnityEngine.Debug.LogError("LRCLib fallback in multiplayer failed: No valid cached track found.");
                    lyricsError = true;
                    loadingFX.SetActive(false);
                    return;
                }
            }

            bool fallbackSuccess = await FetchLyricsFromLrcLib(trackForLrcLib);

            if (!fallbackSuccess)
            {
                UnityEngine.Debug.LogError("LRCLib fallback also failed. No lyrics found.");
                lyricsError = true; // Trigger the final error UI
                loadingFX.SetActive(false);
                return;
            }

            UnityEngine.Debug.Log("LRCLib fallback successful!");
        }
        // --- END FALLBACK LOGIC ---

        // Mark Stage 1 as complete
        stage1.transform.GetChild(1).gameObject.SetActive(false);
        stage1.transform.GetChild(0).GetComponent<TextMeshProUGUI>().color = new Color(0.3443396f, 1f, 0.3759922f);
        stage1.transform.GetChild(2).GetComponent<TextMeshProUGUI>().color = new Color(0.3443396f, 1f, 0.3759922f);
        stage1.GetComponent<MPImage>().color = new Color(0.1116768f, 0.333f, 0.1218292f);
        stage1.GetComponent<MPImage>().OutlineColor = new Color(0.313548f, 0.901f, 0.3404953f);
        stage1.GetComponent<Animator>().Play("Done");
        progress1.GetComponent<Slider>().value = 1f;
        progress1.transform.GetChild(1).GetChild(0).GetComponent<MPImage>().color = new Color(0.313548f, 0.901f, 0.3404953f);
        GetComponent<AudioSource>().clip = stage1FX;
        GetComponent<AudioSource>().Play();

        status.text = "Downloading song...";
        stage2.transform.GetChild(1).gameObject.SetActive(true);

        string expectedAudioPath = GetExpectedAudioFilePath(artist, name);

        if (File.Exists(expectedAudioPath))
        {
            UnityEngine.Debug.Log("Audio file already exists. Skipping download.");
            PlayerPrefs.SetString("vocalLocation", Path.Combine(dataPath, "output", "htdemucs", Path.GetFileNameWithoutExtension(expectedAudioPath) + " [vocals].mp3"));
            if (SettingsManager.Instance.GetSetting<bool>("PlayInstrumental"))
            {
                string instrumentalLocation = Path.Combine(dataPath, "output", "htdemucs", Path.GetFileNameWithoutExtension(expectedAudioPath) + " [no_vocals].mp3");
                PlayerPrefs.SetString("fullLocation", instrumentalLocation);
            }
            else
            {
                PlayerPrefs.SetString("fullLocation", expectedAudioPath);
            }
        }
        else
        {
            // Use YoutubeExplode-based downloader instead of spotdl
            try
            {
                // Get duration and album from selectedTrack for better matching
                TimeSpan spotifyDuration = TimeSpan.Zero;
                string albumName = null;

                if (selectedTrack != null)
                {
                    spotifyDuration = TimeSpan.FromMilliseconds(selectedTrack.duration_ms);
                    albumName = selectedTrack.album?.name;
                }
                else if (lastPrecompiledTrack != null)
                {
                    spotifyDuration = TimeSpan.FromMilliseconds(lastPrecompiledTrack.duration_ms);
                    albumName = lastPrecompiledTrack.album?.name;
                }

                // Subscribe to progress events
                Action<double> progressHandler = null;
                progressHandler = (progress) =>
                {
                    // Update currentPercentage (0.0 to 1.0 from downloader)
                    currentPercentage = (float)progress;

                    // Update stage 2 progress bar
                    progress2.GetComponent<Slider>().value = (float)progress;
                };

                SpotifyToYoutubeDownloader.OnProgress += progressHandler;

                try
                {
                    await SpotifyToYoutubeDownloader.DownloadClosestMatch(artist, name, spotifyDuration, expectedAudioPath, albumName);
                }
                finally
                {
                    // Always unsubscribe to prevent memory leaks
                    SpotifyToYoutubeDownloader.OnProgress -= progressHandler;
                }
            }
            catch (Exception ex)
            {
                if (BGMusic.Instance != null) BGMusic.Instance.StopPreview();
                UnityEngine.Debug.LogError($"YouTube download failed: {ex.Message}");
                alertManager.ShowError("An error occurred downloading your song.", "This is likely due to connectivity issues, or due to some rare inconsistency. Please try again.", "Dismiss");
                LoadingDone();
                loadingFX.SetActive(false);
                mainPanel.SetActive(true);
                return;
            }
        }

        stage2.transform.GetChild(1).gameObject.SetActive(false);
        stage2.transform.GetChild(0).GetComponent<TextMeshProUGUI>().color = new Color(0.3443396f, 1f, 0.3759922f);
        stage2.transform.GetChild(2).GetComponent<TextMeshProUGUI>().color = new Color(0.3443396f, 1f, 0.3759922f);
        stage2.GetComponent<MPImage>().color = new Color(0.1116768f, 0.333f, 0.1218292f);
        stage2.GetComponent<MPImage>().OutlineColor = new Color(0.313548f, 0.901f, 0.3404953f);
        stage2.GetComponent<Animator>().Play("Done");
        progress2.GetComponent<Slider>().value = 1f;
        progress2.transform.GetChild(1).GetChild(0).GetComponent<MPImage>().color = new Color(0.313548f, 0.901f, 0.3404953f);
        GetComponent<AudioSource>().clip = stage2FX;
        GetComponent<AudioSource>().Play();

        status.text = "Splitting vocals...";
        splittingVocals = true;
        stage3.transform.GetChild(1).gameObject.SetActive(true);

        if (SettingsManager.Instance.GetSetting<int>("VocalProcessingMethod") == 1)
        {
            StartCoroutine(FadeImageAlpha(bgDarken, 1f, 1f));
            await Task.Delay(1010);
            bgGM.SetActive(false);
        }

        _originalVSyncCount = QualitySettings.vSyncCount;
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 30;

        await RunPythonDirectly(expectedAudioPath);

        Application.targetFrameRate = -1;
        QualitySettings.vSyncCount = _originalVSyncCount;

        splittingVocals = false;
        currentPercentage = 0f;

        FavoritesManager.AddDownload(name, artist, length, cover, url);

        stage3.transform.GetChild(1).gameObject.SetActive(false);
        stage3.transform.GetChild(0).GetComponent<TextMeshProUGUI>().color = new Color(0.3443396f, 1f, 0.3759922f);
        stage3.transform.GetChild(2).GetComponent<TextMeshProUGUI>().color = new Color(0.3443396f, 1f, 0.3759922f);
        stage3.GetComponent<MPImage>().color = new Color(0.1116768f, 0.333f, 0.1218292f);
        stage3.GetComponent<MPImage>().OutlineColor = new Color(0.313548f, 0.901f, 0.3404953f);
        stage3.GetComponent<Animator>().Play("Done");
        progress3.GetComponent<Slider>().value = 1f;
        progress3.transform.GetChild(1).GetChild(0).GetComponent<MPImage>().color = new Color(0.313548f, 0.901f, 0.3404953f);
        GetComponent<AudioSource>().clip = stage3FX;
        GetComponent<AudioSource>().Play();

        status.text = "Loading Main Scene...";
        stage4.transform.GetChild(1).gameObject.SetActive(true);
        await Task.Delay(1010);
        if (PlayerPrefs.GetInt("multiplayer") == 0) LoadMain();
    }


    public async Task BackgroundCompile(string url, string name, string artist, string length, string cover, CancellationToken cancellationToken = default)
    {
        dataPath = PlayerPrefs.GetString("dataPath");
        UnityEngine.Debug.Log($"Now processing: {url}, {name}, {artist}, {length}, {cover}");
        PlayerPrefs.SetString("currentSongURL", url);
        name = name.Replace("\\", " ");

        string sanitizedName = SanitizeFileName(name);

        if (CheckFile(sanitizedName + ".txt"))
        {
            if (WebServerManager.Instance != null)
            {
                WebServerManager.Instance.SetCurrentStatus("Ready to play!");
            }
            return; // No need to proceed if the file already exists
        }

        // Check for cancellation before proceeding
        cancellationToken.ThrowIfCancellationRequested();

        if (WebServerManager.Instance != null)
        {
            WebServerManager.Instance.SetCurrentStatus("Fetching lyrics...");
        }
        compiling = true;

        // Check LyricsSource setting: 0 = Syrics (with LRCLib fallback), 1 = LRCLib only
        int lyricsSource = SettingsManager.Instance?.GetSetting<int>("LyricsSource") ?? 0;

        if (lyricsSource == 0)
        {
            // --- SYRICS LOGIC ---
            bool isLinux = Application.platform == RuntimePlatform.LinuxPlayer || Application.platform == RuntimePlatform.LinuxEditor;
            string scriptName = isLinux ? "getlyrics.sh" : "getlyrics.bat";

            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = Path.Combine(dataPath, scriptName),
                Arguments = url + " " + dataPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            Process process = new Process { StartInfo = psi };
            process.OutputDataReceived += (sender, args) =>
            {
                if (string.IsNullOrEmpty(args.Data)) return;
                UnityEngine.Debug.Log("Output: " + args.Data);
                if (args.Data.Contains("some tracks") || args.Data.Contains("Your application has"))
                {
                    lyricsError2 = true; // Set flag to try fallback
                }
            };
            process.ErrorDataReceived += (sender, args) =>
            {
                if (string.IsNullOrEmpty(args.Data)) return;
                UnityEngine.Debug.Log("Error Output: " + args.Data);
                if (args.Data.Contains("some tracks") || args.Data.Contains("Your application has"))
                {
                    lyricsError2 = true; // Set flag to try fallback
                }
            };
            process.Start();
            currentBGProcess = process;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Wait for process completion or cancellation with timeout
            int timeoutSeconds = 7;
            if (SettingsManager.Instance != null)
            {
                if (!int.TryParse(SettingsManager.Instance.GetSetting<string>("SyricsTimeout", "7"), out timeoutSeconds)) UnityEngine.Debug.LogError("Error parsing SyricsTimeout");
            }
            UnityEngine.Debug.Log($"Lyrics Process Limit: {timeoutSeconds} seconds.");
            var timeoutLimit = DateTime.Now.AddSeconds(timeoutSeconds);

            await Task.Run(() =>
            {
                while (!process.HasExited)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        try
                        {
                            process.Kill();
                            process.WaitForExit(5000);
                        }
                        catch { }
                        break;
                    }

                    if (DateTime.Now > timeoutLimit)
                    {
                        try
                        {
                            process.Kill();
                            process.WaitForExit(5000); // Wait up to 5 seconds for process to fully terminate
                            UnityEngine.Debug.LogWarning($"Lyrics process timed out HARD after {timeoutSeconds}s.");
                            lyricsError2 = true;
                        }
                        catch (Exception e) { UnityEngine.Debug.LogError($"Error killing timed-out process: {e.Message}"); }
                        break;
                    }
                    Thread.Sleep(500); // Check every 0.5s
                }
            }, cancellationToken);

            // Ensure process is fully disposed
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit(5000);
                }
                process.Dispose();
            }
            catch (Exception e) { UnityEngine.Debug.LogWarning($"Error disposing lyrics process: {e.Message}"); }

            UnityEngine.Debug.Log("Lyrics script finished.");

            // Check if a lyrics file was actually created recently
            string downloadsCheckPath = Path.Combine(dataPath, "downloads");
            bool recentLyricsFound = false;
            if (Directory.Exists(downloadsCheckPath))
            {
                var directoryInfo = new DirectoryInfo(downloadsCheckPath);
                var recentFiles = directoryInfo.GetFiles("*.*")
                                               .Where(f => (f.Extension == ".lrc" || f.Extension == ".txt") &&
                                                           (DateTime.Now - f.CreationTime).TotalSeconds < 20)
                                               .ToArray();
                if (recentFiles.Length > 0)
                {
                    recentLyricsFound = true;
                }
            }

            if (!recentLyricsFound && !lyricsError2)
            {
                UnityEngine.Debug.LogWarning("Syrics finished but no new lyrics file found. Forcing fallback.");
                lyricsError2 = true;
            }
            // --- END SYRICS LOGIC ---
        }
        else
        {
            // LRCLib only mode - skip Syrics entirely
            UnityEngine.Debug.Log("LyricsSource set to LRCLib only. Skipping Syrics.");
            lyricsError2 = true; // Force LRCLib path
        }

        // Check for cancellation after lyrics fetch
        cancellationToken.ThrowIfCancellationRequested();

        // --- FALLBACK LOGIC ---
        if (lyricsError2)
        {
            lyricsError2 = false; // Reset flag
            status.text = "Primary source failed. Trying LRCLib...";
            UnityEngine.Debug.Log("Initial lyric fetch failed. Attempting LRCLib fallback.");

            // In multiplayer, selectedTrack might be null. Use the cached track.
            Track trackForLrcLib = selectedTrack;
            if (PlayerPrefs.GetInt("multiplayer") == 1 && trackForLrcLib == null)
            {
                if (lastPrecompiledTrack != null && lastPrecompiledTrack.name == name && lastPrecompiledTrack.artists[0].name == artist)
                {
                    trackForLrcLib = lastPrecompiledTrack;
                    UnityEngine.Debug.Log("Using cached track for LRCLib fallback in multiplayer.");
                }
                else
                {
                    UnityEngine.Debug.LogError("LRCLib fallback in multiplayer failed: No valid cached track found.");
                    lyricsError = true;
                    loadingFX.SetActive(false);
                    return;
                }
            }

            bool fallbackSuccess = await FetchLyricsFromLrcLib(trackForLrcLib);

            if (!fallbackSuccess)
            {
                UnityEngine.Debug.LogError("LRCLib fallback also failed. No lyrics found.");
                lyricsError = true; // Trigger the final error UI
                loadingFX.SetActive(false);
                return;
            }

            UnityEngine.Debug.Log("LRCLib fallback successful!");
        }

        if (WebServerManager.Instance != null)
        {
            WebServerManager.Instance.SetCurrentStatus("Downloading song...");
        }

        // Check for cancellation before download
        cancellationToken.ThrowIfCancellationRequested();

        bool success = false;
        string expectedAudioPath = GetExpectedAudioFilePath(artist, name);

        while (!success)
        {
            if (File.Exists(expectedAudioPath))
            {
                UnityEngine.Debug.Log("Audio file already exists. Skipping download.");
                success = true;
            }
            else
            {
                try
                {
                    // Get duration and album from lastPrecompiledTrack for better matching
                    TimeSpan spotifyDuration = TimeSpan.Zero;
                    string albumName = null;

                    if (lastPrecompiledTrack != null)
                    {
                        spotifyDuration = TimeSpan.FromMilliseconds(lastPrecompiledTrack.duration_ms);
                        albumName = lastPrecompiledTrack.album?.name;
                    }

                    await SpotifyToYoutubeDownloader.DownloadClosestMatch(artist, name, spotifyDuration, expectedAudioPath, albumName);
                    success = true;
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError($"YouTube download failed: {ex.Message}");
                    success = false;
                }
            }
            if (!success) await Task.Delay(1500, cancellationToken);
        }

        if (WebServerManager.Instance != null)
        {
            WebServerManager.Instance.SetCurrentStatus("Splitting vocals...");
        }

        // Check for cancellation before vocal splitting
        cancellationToken.ThrowIfCancellationRequested();

        splittingVocals = true;

        await RunPythonDirectly(expectedAudioPath, cancellationToken);

        splittingVocals = false;
        currentPercentage = 0f;

        FavoritesManager.AddDownload(name, artist, length, cover, url);

        if (WebServerManager.Instance != null)
        {
            WebServerManager.Instance.SetCurrentStatus("Ready to play!");
        }
    }

    private async Task<bool> FetchLyricsFromLrcLib(Track track)
    {
        if (track == null)
        {
            UnityEngine.Debug.LogError("Cannot fetch from LRCLib: selectedTrack is null.");
            return false;
        }

        string trackName = UnityWebRequest.EscapeURL(track.name);
        string artistName = UnityWebRequest.EscapeURL(track.artists[0].name);
        string albumName = UnityWebRequest.EscapeURL(track.album.name);
        int duration = track.duration_ms / 1000;

        string url = $"https://lrclib.net/api/get?track_name={trackName}&artist_name={artistName}&album_name={albumName}&duration={duration}";

        // In T Mode, use more retries to handle potential network instability
        int maxRetries = Application.version.StartsWith("T") ? 200 : 10;
        int attempt = 0;

        while (attempt < maxRetries)
        {
            attempt++;
            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                request.SetRequestHeader("User-Agent", "YASG");
                await request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    string error = request.error ?? "";
                    string errorLower = error.ToLowerInvariant();

                    // Check for transient/retryable errors
                    bool isRetryable = errorLower.Contains("curl error 52") ||
                                       errorLower.Contains("empty reply") ||
                                       errorLower.Contains("connection reset") ||
                                       errorLower.Contains("connection refused") ||
                                       errorLower.Contains("timed out") ||
                                       errorLower.Contains("timeout") ||
                                       request.result == UnityWebRequest.Result.ConnectionError;

                    if (isRetryable && attempt < maxRetries)
                    {
                        UnityEngine.Debug.LogWarning($"LRCLib transient error: {error}, retrying... ({attempt}/{maxRetries})");
                        status.text = $"Retrying LRCLib... ({attempt}/{maxRetries})";
                        await Task.Delay(1000 * attempt); // Exponential backoff
                        continue;
                    }

                    UnityEngine.Debug.LogError($"LRCLib Error: {error}");
                    return false;
                }

                string jsonResponse = request.downloadHandler.text;
                if (string.IsNullOrEmpty(jsonResponse) || jsonResponse.Trim() == "null")
                {
                    UnityEngine.Debug.Log("LRCLib: No entry found for this track.");
                    return false;
                }

                LrcLibResponse response = JsonUtility.FromJson<LrcLibResponse>(jsonResponse);

                if (string.IsNullOrEmpty(response.syncedLyrics))
                {
                    UnityEngine.Debug.Log("LRCLib: Found an entry, but it has no synced lyrics.");
                    return false;
                }

                // Save the lyrics to file, mimicking the original script's output
                string sanitizedName = SanitizeFileName(track.name);
                string lyricsFilePath = Path.Combine(dataPath, "downloads", $"{sanitizedName}.txt");
                string directoryPath = Path.GetDirectoryName(lyricsFilePath);

                try
                {
                    if (!Directory.Exists(directoryPath))
                    {
                        Directory.CreateDirectory(directoryPath);
                    }
                    await File.WriteAllTextAsync(lyricsFilePath, response.syncedLyrics);
                    UnityEngine.Debug.Log($"Successfully fetched and saved lyrics from LRCLib to {lyricsFilePath}");
                    return true;
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogError($"Failed to write lyrics file from LRCLib: {e.Message}");
                    return false;
                }
            }
        }

        UnityEngine.Debug.LogError($"LRCLib failed after {maxRetries} retries due to server overload.");
        return false;
    }


    private async Task RunPythonDirectly(string audioFilePath, CancellationToken cancellationToken = default)
    {
        dataPath = PlayerPrefs.GetString("dataPath");

        if (string.IsNullOrEmpty(audioFilePath) || !File.Exists(audioFilePath))
        {
            UnityEngine.Debug.LogError("Audio file not found or path is invalid!");
            return;
        }

        var outputDir = Path.Combine(dataPath, "output", "htdemucs");
        Directory.CreateDirectory(outputDir);

        PlayerPrefs.SetString("vocalLocation", Path.Combine(outputDir, Path.GetFileNameWithoutExtension(audioFilePath) + " [vocals].mp3"));
        if (SettingsManager.Instance.GetSetting<bool>("PlayInstrumental"))
        {
            string instrumentalLocation = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(audioFilePath) + " [no_vocals].mp3");
            PlayerPrefs.SetString("fullLocation", instrumentalLocation);
        }
        else
        {
            PlayerPrefs.SetString("fullLocation", audioFilePath);
        }

        // VocalProcessingMethod 0 = VocalRemover.org API (online)
        // VocalProcessingMethod 1 = Demucs (local Python)
        if (SettingsManager.Instance.GetSetting<int>("VocalProcessingMethod") == 0)
        {
            // Use VocalRemover API directly
            await RunVocalRemoverAPIAsync(audioFilePath, outputDir, cancellationToken);
            UnityEngine.Debug.Log("VocalRemover API processing finished!");
        }
        else
        {
            // Use local Python/Demucs
            var inputFolder = Path.Combine(dataPath, "vocalremover", "input");
            Directory.CreateDirectory(inputFolder);
            var targetPath = Path.Combine(inputFolder, Path.GetFileName(audioFilePath));
            File.Copy(audioFilePath, targetPath, true);

            string pythonArgs = $"-u \"main.py\"";
            string pythonExe;
            bool isLinux = Application.platform == RuntimePlatform.LinuxPlayer || Application.platform == RuntimePlatform.LinuxEditor;
            if (isLinux)
            {
                pythonExe = Path.Combine(dataPath, "venv", "bin", "python3");
            }
            else
            {
                pythonExe = Path.Combine(dataPath, "venv", "Scripts", "python.exe");
            }
            string workingDir = Path.Combine(dataPath, "vocalremover");
            await RunProcessAsync(pythonExe, pythonArgs, workingDir, cancellationToken);
            UnityEngine.Debug.Log("Python inference finished!");
        }
    }

    private Task RunVocalRemoverAPIAsync(string audioFilePath, string outputDir, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<bool>();

        // Create or get VocalRemoverAPI component
        if (_vocalRemoverAPI == null)
        {
            _vocalRemoverAPI = gameObject.AddComponent<VocalRemoverAPI>();
        }

        // Set up completion handler
        void OnComplete(bool success, string message)
        {
            _vocalRemoverAPI.OnProcessingComplete -= OnComplete;
            _vocalRemoverAPI.OnProgressChanged -= OnProgress;

            if (success)
            {
                tcs.TrySetResult(true);
            }
            else
            {
                tcs.TrySetException(new Exception($"VocalRemover API failed: {message}"));
            }
        }

        void OnProgress(int percent)
        {
            // Update progress bar and currentPercentage
            float normalizedProgress = percent / 100f;
            currentPercentage = normalizedProgress;

            if (progressBar != null)
            {
                progressBar.value = normalizedProgress;
            }
        }

        _vocalRemoverAPI.OnProcessingComplete += OnComplete;
        _vocalRemoverAPI.OnProgressChanged += OnProgress;

        // Handle cancellation
        cancellationToken.Register(() =>
        {
            _vocalRemoverAPI.OnProcessingComplete -= OnComplete;
            _vocalRemoverAPI.OnProgressChanged -= OnProgress;
            _vocalRemoverAPI.StopAllCoroutines();
            tcs.TrySetCanceled();
        });

        // Start processing
        _vocalRemoverAPI.ProcessFile(audioFilePath, outputDir);

        return tcs.Task;
    }



    public float ParseTime(string timeString)
    {
        string[] parts = timeString.Split(' ');
        if (float.TryParse(parts[0], out float result)) return result;

        UnityEngine.Debug.LogError("Invalid time format");
        return 0f;
    }
    public void LowpassTransition(bool inOut) // true = in / false = out
    {
        if (transitionCoroutine != null) StopCoroutine(transitionCoroutine);
        transitionCoroutine = StartCoroutine(inOut ? ChangeCutoffOverTime(20000f, 650f, 0.5f) : ChangeCutoffOverTime(650f, 20000f, 1f));
    }

    private IEnumerator ChangeCutoffOverTime(float startFreq, float endFreq, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float newFreq = Mathf.Lerp(startFreq, endFreq, elapsed / duration);
            musicControl.audioMixer.SetFloat("LowpassCutoff", newFreq);
            yield return null;
        }
        musicControl.audioMixer.SetFloat("LowpassCutoff", endFreq);
    }
    private IEnumerator FadeImageAlpha(UnityEngine.UI.Image image, float targetAlpha, float duration)
    {
        float time = 0;
        Color startColor = image.color;
        while (time < duration)
        {
            time += Time.deltaTime;
            float newAlpha = Mathf.Lerp(startColor.a, targetAlpha, time / duration);
            image.color = new Color(startColor.r, startColor.g, startColor.b, newAlpha);
            yield return null;
        }
        image.color = new Color(startColor.r, startColor.g, startColor.b, targetAlpha);
    }

    public async void LoadingDone()
    {
        LowpassTransition(false);
        blurAnim.Play("BlurOut");
        loadingAnim.Play("LoadingOut");
        await Task.Delay(500);
        loadingCanvas.SetActive(false);
    }

    public void BeginLoading()
    {
        LowpassTransition(true);
        blurAnim.Play("BlurIn");
        loadingAnim.Play("LoadingIn");
    }

    private Process activeProcess;
    private bool processIsRunning = false;
    private readonly static Queue<Action> executionQueue = new Queue<Action>();

    private void QueueForMainThread(Action action)
    {
        if (action == null) return;
        lock (executionQueue)
        {
            executionQueue.Enqueue(action);
        }
    }

    public void RunFullInstall()
    {
        if (processIsRunning)
        {
            Debug.LogWarning("A process is already running.");
            return;
        }

        status.text = "Starting...";
        progressBar.value = 0;

        loadingCanvas.SetActive(true);
        loadingSecond.SetActive(true);
        loadingSecond.transform.GetChild(4).gameObject.SetActive(true);
        loadingFirst.SetActive(false);
        BeginLoading();
        loadingFX.SetActive(true);

        StartCoroutine(RunFullInstallCoroutine());
    }

    private IEnumerator RunFullInstallCoroutine()
    {
        processIsRunning = true;

        activeProcess = new Process();
        string dataPath = PlayerPrefs.GetString("dataPath");

        string scriptPath = Path.Combine(dataPath, "setuputilities", "fullinstall.py");
        if (!File.Exists(scriptPath))
        {
            Debug.LogError($"Script not found at: {scriptPath}");
            QueueForMainThread(() => status.text = "Error: Script not found.");
            processIsRunning = false;
            yield break;
        }
        bool isLinux = Application.platform == RuntimePlatform.LinuxPlayer || Application.platform == RuntimePlatform.LinuxEditor;
        if (isLinux)
        {
            activeProcess.StartInfo.FileName = Path.Combine(dataPath, "venv", "bin", "python3");
        }
        else
        {
            activeProcess.StartInfo.FileName = Path.Combine(dataPath, "venv", "Scripts", "python.exe");
        }
        activeProcess.StartInfo.Arguments = $" -u \"{scriptPath}\" true";

        activeProcess.StartInfo.UseShellExecute = false;
        activeProcess.StartInfo.CreateNoWindow = true;
        activeProcess.StartInfo.RedirectStandardOutput = true;
        activeProcess.StartInfo.RedirectStandardError = true;
        activeProcess.EnableRaisingEvents = true;

        activeProcess.OutputDataReceived += (sender, args) =>
        {
            if (!string.IsNullOrEmpty(args.Data))
            {
                QueueForMainThread(() => ParseFinalInstallOutputLine(args.Data));
            }
        };

        activeProcess.ErrorDataReceived += (sender, args) =>
        {
            if (!string.IsNullOrEmpty(args.Data))
            {
                QueueForMainThread(() => ProcessErrorLine(args.Data));
            }
        };

        try
        {
            activeProcess.Start();
            activeProcess.BeginOutputReadLine();
            activeProcess.BeginErrorReadLine();
            Debug.Log($"Process '{Path.GetFileName(activeProcess.StartInfo.FileName)}' started successfully.");
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to start process: {e.Message}");
            QueueForMainThread(() =>
            {
                status.text = "Error: Failed to start.";
            });
            processIsRunning = false;
            yield break;
        }

        while (!activeProcess.HasExited)
        {
            yield return null;
        }

        Debug.Log($"Process finished with exit code: {activeProcess.ExitCode}.");

        CleanUpProcess();
    }

    private void ParseFinalInstallOutputLine(string line)
    {
        Debug.Log($"[FinalInstall] {line}");
        Match match = Regex.Match(line, @"^\s*\[\s*(\d{1,3})%\s*\]\s*(.*)");

        if (match.Success)
        {
            string percentageStr = match.Groups[1].Value;
            string message = match.Groups[2].Value.Trim();

            Debug.Log($"[FinalInstall] Matched! Percentage: {percentageStr}, Message: {message}");

            if (status != null && !string.IsNullOrEmpty(message))
            {
                status.text = Regex.Replace(message, "-+", "").Trim();
            }
            if (progressBar != null && int.TryParse(percentageStr, out int percentage))
            {
                progressBar.value = percentage / 100.0f;
            }
        }
        else
        {
            Debug.LogWarning($"[FinalInstall] No match for line: {line}");
        }
        if (line.Contains("Setup Complete!"))
        {
            LoadingDone();
            loadingFX.SetActive(false);
            mainPanel.SetActive(true);
            PlayerPrefs.SetInt("demucsInstalled", 1);
            Debug.Log("Final installation completed successfully.");
        }
    }

    private void ProcessErrorLine(string line)
    {
        if (line.Contains("WARNING: You are using pip version") || line.Contains("install --upgrade pip") || line.Contains("A new release of pip available"))
        {
            Debug.Log($"[Ignored Warning] {line}");
            return;
        }

        Debug.LogError($"[Process Error] {line}");
        if (status != null)
        {
            status.text = "An error occurred. Check console.";
        }
    }

    private void CleanUpProcess()
    {
        if (activeProcess != null)
        {
            activeProcess.Close();
            activeProcess = null;
        }
        processIsRunning = false;
    }

    void OnApplicationQuit()
    {
        if (partyMode)
        {
            TogglePartyMode();
        }
        if (activeProcess != null && !activeProcess.HasExited)
        {
            Debug.Log("Application quitting, killing active process...");
            activeProcess.Kill();
        }
    }

    private async Task RunProcessAsync(string exePath, string arguments, string workingDirectory, CancellationToken cancellationToken = default)
    {
        if (exePath == "python" && processLocally)
        {
            loadingCanvas.SetActive(true);
            loadingSecond.SetActive(false);
            loadingFirst.SetActive(true);
        }
        var tempCPU = Process.GetCurrentProcess().ProcessorAffinity;
        var done = false;
        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        var process = new Process { StartInfo = psi };
        currentBGProcess = process;

        process.OutputDataReceived += (sender, args) =>
        {
            if (string.IsNullOrEmpty(args.Data)) return;

            UnityEngine.Debug.Log($"[STDOUT] {args.Data}");
            if (args.Data.Contains("Estimated total processing time for this track:"))
            {
                totalETA = ParseTime(args.Data.Substring(48));
                UnityEngine.Debug.Log("Extracted ETA: " + totalETA);
            }
            if (args.Data.Contains("Processing track") && exePath == "python" && processLocally)
            {
                Process.GetCurrentProcess().ProcessorAffinity = (IntPtr)0b0001;
                Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.Idle;
                process.PriorityClass = ProcessPriorityClass.RealTime;
            }
            if (args.Data.Contains("Progress:"))
            {
                string progressStr = Regex.Match(args.Data, @"\d+").Value;
                if (int.TryParse(progressStr, out int temp))
                {
                    currentPercentage = (float)temp / 100;
                }
            }

            if (args.Data.Contains(processLocally ? "Processing track 1/1:" : "Uploading file:"))
            {
                int startIndex = processLocally ? 22 : 15;
                int endIndexTrim = processLocally ? 4 : 4;
                if (args.Data.Length >= startIndex + endIndexTrim)
                {
                    extractedFileName = args.Data.Substring(startIndex, args.Data.Length - (startIndex + endIndexTrim)).Trim();
                    UnityEngine.Debug.Log("Extracted file name: " + extractedFileName);
                }
            }
        };
        process.ErrorDataReceived += (sender, args) =>
        {
            if (!string.IsNullOrEmpty(args.Data)) UnityEngine.Debug.Log($"[STDERR] {args.Data}");
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (exePath == "python" && processLocally)
        {
            vocalSplitProc = process;
            while (true)
            {
                while (!done) await Task.Delay(1000);

                UnityEngine.Debug.Log("end detected");
                splittingVocals = false;
                Process.GetCurrentProcess().ProcessorAffinity = tempCPU;
                Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.Normal;
                done = false;
            }
        }
        else
        {
            // Wait for process completion or cancellation
            await Task.Run(() =>
            {
                while (!process.HasExited && !cancellationToken.IsCancellationRequested)
                {
                    Thread.Sleep(100);
                }

                if (cancellationToken.IsCancellationRequested && !process.HasExited)
                {
                    try
                    {
                        process.Kill();
                        Debug.Log("Killed vocal processing due to cancellation.");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"Failed to kill vocal processing: {ex.Message}");
                    }
                }
            }, cancellationToken);
        }
    }


    public void LoadMain()
    {
        // In T mode, verify outputs instead of loading Main scene
        if (Application.version.StartsWith("T"))
        {
            VerifyTModeOutputs();
            return;
        }
        SceneManager.LoadScene("Main");
    }

    private void VerifyTModeOutputs()
    {
        string dataPath = PlayerPrefs.GetString("dataPath");
        string executableDir = Path.GetDirectoryName(Application.dataPath);
        bool allFilesExist = true;

        Debug.Log("[T Mode Verification] Starting output verification...");

        // Check 1: .mp3 under dataPath/downloads
        string downloadsPath = Path.Combine(dataPath, "downloads");
        bool hasMp3InDownloads = Directory.Exists(downloadsPath) && Directory.GetFiles(downloadsPath, "*.mp3").Length > 0;
        Debug.Log($"[T Mode] Check 1 - MP3 in downloads: {hasMp3InDownloads}");
        if (!hasMp3InDownloads) allFilesExist = false;

        // Check 2: .lrc or .txt under dataPath/downloads
        bool hasLyricsInDownloads = Directory.Exists(downloadsPath) &&
            (Directory.GetFiles(downloadsPath, "*.lrc").Length > 0 || Directory.GetFiles(downloadsPath, "*.txt").Length > 0);
        Debug.Log($"[T Mode] Check 2 - Lyrics in downloads: {hasLyricsInDownloads}");
        if (!hasLyricsInDownloads) allFilesExist = false;

        // Check 3: .mp3 under dataPath/output/htdemucs
        string htdemucsPath = Path.Combine(dataPath, "output", "htdemucs");
        bool hasMp3InHtdemucs = Directory.Exists(htdemucsPath) && Directory.GetFiles(htdemucsPath, "*.mp3").Length > 0;
        Debug.Log($"[T Mode] Check 3 - MP3 in htdemucs: {hasMp3InHtdemucs}");
        if (!hasMp3InHtdemucs) allFilesExist = false;

        // Create result file
        string resultFile = allFilesExist ? "success.txt" : "fail.txt";
        string resultPath = Path.Combine(executableDir, resultFile);

        try
        {
            string content = $"=== YASG T Mode Verification Result ===\n";
            content += $"Time: {DateTime.Now}\n\n";
            content += $"MP3 in downloads: {hasMp3InDownloads}\n";
            content += $"Lyrics in downloads: {hasLyricsInDownloads}\n";
            content += $"MP3 in htdemucs: {hasMp3InHtdemucs}\n\n";
            content += $"Overall Result: {(allFilesExist ? "SUCCESS" : "FAIL")}\n";

            File.WriteAllText(resultPath, content);
            Debug.Log($"[T Mode] Created result file: {resultPath}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[T Mode] Failed to create result file: {e.Message}");
        }

        Debug.Log($"[T Mode Verification] Complete. Result: {(allFilesExist ? "SUCCESS" : "FAIL")}");

        // Quit the application after verification
        Application.Quit();
    }

    // --- SERIALIZABLE CLASSES ---

    [Serializable]
    public class LrcLibResponse
    {
        public string syncedLyrics;
    }

    private IEnumerator DelayedUpdateCheck()
    {
        yield return new WaitForSeconds(1f);
        RunUpdateCheckSilently();
    }

    private async void RunUpdateCheckSilently()
    {
        // Check for updates by downloading README.md and comparing versions
        try
        {
            using (UnityWebRequest request = UnityWebRequest.Get("https://raw.githubusercontent.com/grncd/YASG/refs/heads/main/README.md"))
            {
                await request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"Update check failed: {request.error}");
                    return;
                }

                string readmeContent = request.downloadHandler.text;

                // Extract version using regex: v\d+\.\d+\.\d+[a-z]?
                Match versionMatch = Regex.Match(readmeContent, @"v\d+\.\d+\.\d+[a-z]?");

                if (!versionMatch.Success)
                {
                    Debug.LogWarning("Update check: Could not find version in README.md");
                    return;
                }

                // Remove the "v" prefix
                string latestVersion = versionMatch.Value.Substring(1);
                string currentVersion = Application.version;

                Debug.Log($"Update check: Current version = {currentVersion}, Latest version = {latestVersion}");

                // Compare versions
                if (latestVersion != currentVersion)
                {
                    AlertManager.Instance.ShowInfo(
                        "There is a new version of YASG available!",
                        "Please visit <link=\"https://github.com/grncd/YASG/releases\"><u>https://github.com/grncd/YASG/releases</u></link> to download it.",
                        "Dismiss"
                    );
                }
            }

            string dataPath = PlayerPrefs.GetString("dataPath");
            string pythonExe;
            bool isLinux = Application.platform == RuntimePlatform.LinuxPlayer || Application.platform == RuntimePlatform.LinuxEditor;
            if (isLinux)
            {
                pythonExe = Path.Combine(dataPath, "venv", "bin", "python3");
            }
            else
            {
                pythonExe = Path.Combine(dataPath, "venv", "Scripts", "python.exe");
            }
            string scriptPath = Path.Combine(dataPath, "setuputilities", "updatechecker.py");
            if (!File.Exists(pythonExe) || !File.Exists(scriptPath))
            {
                // Optionally log missing files, but do nothing else
                Debug.LogWarning("Update check: Python or script not found.");
                return;
            }

            // Send notification that update check is starting
            NotificationCenter.Info("Update Check", "Checking for script updates...");

            var psi = new ProcessStartInfo
            {
                FileName = pythonExe,
                Arguments = $" -u \"{scriptPath}\"",
                WorkingDirectory = Path.GetDirectoryName(scriptPath),
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            bool hadUpdates = false;
            List<string> updatedFiles = new List<string>();

            using (var process = new Process { StartInfo = psi })
            {
                process.OutputDataReceived += (sender, args) =>
                {
                    if (!string.IsNullOrEmpty(args.Data))
                    {
                        Debug.Log($"[UpdateChecker] {args.Data}");
                        // Check if any file was updated
                        if (args.Data.Contains("updated."))
                        {
                            hadUpdates = true;
                            // Extract filename from message like "filename.py updated."
                            string filename = args.Data.Replace(" updated.", "").Trim();
                            updatedFiles.Add(filename);
                        }
                    }
                };

                process.ErrorDataReceived += (sender, args) =>
                {
                    if (!string.IsNullOrEmpty(args.Data) && !args.Data.Contains("pip"))
                        Debug.LogError($"[UpdateChecker STDERR] {args.Data}");
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                await Task.Run(() => process.WaitForExit());
            }

            // Send appropriate notification based on results
            if (hadUpdates)
            {
                string updateInfo = updatedFiles.Count == 1
                    ? $"{updatedFiles[0]} was updated."
                    : $"{updatedFiles.Count} files were updated.";
                NotificationCenter.Success("Scripts Updated", updateInfo);
            }
            else
            {
                NotificationCenter.Info("Update Check Complete", "All scripts are up to date.");
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Update check failed: {e.Message}");
            NotificationCenter.Error("Update Check Failed", e.Message);
        }
    }

    public void DeletePitch()
    {
        if (selectedTrack == null)
        {
            alertManager.ShowError("No Song Selected", "Please select a song first.", "Dismiss");
            return;
        }
        string artistName = (selectedTrack.artists != null && selectedTrack.artists.Count > 0) ? selectedTrack.artists[0].name : "Unknown Artist";
        DeletePitch(selectedTrack.name, artistName);
    }

    public void DeletePitch(string name, string artist)
    {
        string expectedAudioPath = GetExpectedAudioFilePath(artist, name);
        // Pitch data is hashed from the VOCALS path, not the original download path.
        // We must reconstruct the vocal path to find the correct cache file.
        string baseName = Path.GetFileNameWithoutExtension(expectedAudioPath);
        string vocalPath = Path.Combine(dataPath, "output", "htdemucs", baseName + " [vocals].mp3");

        string cachePath = AudioClipPitchProcessor.GetCacheFilePath(vocalPath);
        if (string.IsNullOrEmpty(cachePath) || !File.Exists(cachePath))
        {
            alertManager.ShowError("Pitch Data Not Found", "This song hasn't been processed yet or has no pitch data.", "Dismiss");
            return;
        }

        // Use static method directly - no need for Instance check
        AudioClipPitchProcessor.DeletePitchData(vocalPath);
        alertManager.ShowSuccess($"Pitch data deleted for {name}", "Reprocess the song to regenerate it.", "OK", true);
    }

    public void DeleteSong()
    {
        if (selectedTrack == null)
        {
            alertManager.ShowError("No Song Selected", "Please select a song first.", "Dismiss");
            return;
        }
        string artistName = (selectedTrack.artists != null && selectedTrack.artists.Count > 0) ? selectedTrack.artists[0].name : "Unknown Artist";
        string url = (selectedTrack.external_urls != null) ? selectedTrack.external_urls.spotify : "";

        DeleteSong(selectedTrack.name, artistName, url);
    }

    public void DeleteSong(string name, string artist, string url)
    {
        if (!FavoritesManager.IsDownloaded(url))
        {
            alertManager.ShowError("Song Not Found", "You haven't downloaded this song yet.", "Dismiss");
            return;
        }

        try
        {
            string expectedAudioPath = GetExpectedAudioFilePath(artist, name);
            string baseName = Path.GetFileNameWithoutExtension(expectedAudioPath);

            // 1. Delete Audio File
            if (File.Exists(expectedAudioPath)) File.Delete(expectedAudioPath);

            // 2. Delete Lyrics (.txt and .lrc)
            // Try "SongName.txt" (Current standard)
            string sanitizedSongName = SanitizeFileName(name);
            string simpleLyricsTxt = Path.Combine(dataPath, "downloads", sanitizedSongName + ".txt");
            string simpleLyricsLrc = Path.Combine(dataPath, "downloads", sanitizedSongName + ".lrc");
            if (File.Exists(simpleLyricsTxt)) File.Delete(simpleLyricsTxt);
            if (File.Exists(simpleLyricsLrc)) File.Delete(simpleLyricsLrc);

            // Try "Artist - SongName.txt" (Potential legacy or alternative format)
            string lyricsTxt = Path.Combine(dataPath, "downloads", baseName + ".txt");
            string lyricsLrc = Path.Combine(dataPath, "downloads", baseName + ".lrc");
            if (File.Exists(lyricsTxt)) File.Delete(lyricsTxt);
            if (File.Exists(lyricsLrc)) File.Delete(lyricsLrc);

            // 3. Delete Vocals
            string vocalsPath = Path.Combine(dataPath, "output", "htdemucs", baseName + " [vocals].mp3");
            if (File.Exists(vocalsPath)) File.Delete(vocalsPath);

            // 4. Delete Pitch Data (Silent delete, no alert)
            AudioClipPitchProcessor.DeletePitchData(vocalsPath);

            // 5. Remove from Downloads List
            FavoritesManager.RemoveDownloadByUrl(url);

            // 6. Update UI: PreCompile Window
            if (songInfo != null && songInfo.activeInHierarchy)
            {
                // Verify if this is the song we just deleted (by checking if the text has the tag)
                var lengthTextObj = songInfo.transform.GetChild(4).GetChild(0).GetChild(3).GetComponent<TextMeshProUGUI>();
                if (lengthTextObj != null)
                {
                    string currentText = lengthTextObj.text;
                    if (currentText.Contains("<color=#5dff4d>(Downloaded)</color>"))
                    {
                        currentText = currentText.Replace(" <color=#5dff4d>(Downloaded)</color>", "");
                        lengthTextObj.text = currentText;
                    }
                }
            }

            // 7. Update UI: Search Handler
            SearchHandler sh = FindObjectOfType<SearchHandler>();
            if (sh != null)
            {
                sh.Refresh();
            }

            alertManager.ShowSuccess($"Deleted {name}", "Song data and files have been removed.", "OK", true);
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogError($"Error deleting song {name}: {ex.Message}");
            alertManager.ShowError("Deletion Failed", $"Could not delete all files: {ex.Message}", "Dismiss");
        }
    }
}