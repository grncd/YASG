using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using TMPro;
using UnityEngine;
using System.Threading.Tasks;
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
using Debug = UnityEngine.Debug; // Explicitly use UnityEngine.Debug to avoid ambiguity with System.Diagnostics.Debug

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

    private int _originalVSyncCount;

    public static LevelResourcesCompiler Instance { get; private set; }

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        dataPath = PlayerPrefs.GetString("dataPath");
        Application.targetFrameRate = -1;
        progressBar.gameObject.SetActive(false);
        initLoadingDone = true;
        RunUpdateCheckSilently(); // Run update check in background
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

        if (lyricsError)
        {
            alertManager.ShowError("This song does not have lyrics.", "The song you've selected either has no lyrics or we couldn't find any synced lyrics for it. If this song has lyrics and you'd like to add them, <b>use the Add Lyrics button</b> located in the menu.", "Dismiss");
            LoadingDone();
            mainPanel.SetActive(true);
            lyricsError = false; // Reset after showing
        }
        if (fakeLoading)
        {
            progressBar.gameObject.SetActive(true);
            elapsedFakeLoading += Time.deltaTime;
            progressBar.value = elapsedFakeLoading / 25f;
            GameObject progress2 = loadingSecond.transform.GetChild(4).GetChild(1).gameObject;
            progress2.GetComponent<Slider>().value = elapsedFakeLoading / 25f;
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
        if (splittingVocals)
        {
            GameObject progress3 = loadingSecond.transform.GetChild(4).GetChild(2).gameObject;
            progress3.GetComponent<Slider>().value = progressBar.value;
        }
        if (!string.IsNullOrEmpty(extractedFileName))
        {
            if (!processLocally)
            {
                extractedFileName = Path.GetFileNameWithoutExtension(extractedFileName);
            }
            string vocalLocation = Path.Combine(dataPath, "output", extractedFileName + " [vocals].mp3");
            UnityEngine.Debug.Log("PlayerPrefs updated with vocalLocation: " + vocalLocation);

            extractedFileName = null;
        }
        if (currentPercentage != 0f)
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

        bool isFavorite = FavoritesManager.IsFavorite(url);
        string cover = GetURLCoverFromTrack(track);

        songInfo.SetActive(true);
        songInfo.GetComponent<Animator>().Play("SongInfoIn");
        selectedTrack = track;
        lastPrecompiledTrack = track; // Cache the track object for multiplayer

        songInfo.transform.GetChild(4).GetChild(0).GetChild(0).GetComponent<TextMeshProUGUI>().text = name;
        songInfo.transform.GetChild(4).GetChild(0).GetChild(1).GetComponent<TextMeshProUGUI>().text = artist;
        songInfo.transform.GetChild(4).GetChild(0).GetChild(3).GetComponent<TextMeshProUGUI>().text = "Song Length: " + length;

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
        HighscoreEntry highscore = HighscoreManager.Instance.GetHighscore(url);

        if (highscore != null)
        {
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
        fakeLoading = true;
        bool success = await AttemptDownload(url);
        fakeLoading = false;

        if (!success)
        {
            alertManager.ShowError("An error occured downloading your song.", "This is likely due to connectivity issues, or due to some rare inconsistency. Please try again.", "Dismiss");
            LoadingDone();
            loadingFX.SetActive(false);
            mainPanel.SetActive(true);
            return;
        }

        var dataPath = PlayerPrefs.GetString("dataPath");
        var mp3Path = Directory.GetFiles(dataPath, "*.mp3").OrderByDescending(File.GetCreationTime).FirstOrDefault();

        if (mp3Path != null && PlayerPrefs.GetInt("editing") == 1)
        {
            string songName = PlayerPrefs.GetString("currentSong");
            string artistName = PlayerPrefs.GetString("currentArtist");

            string sanitizedSongName = SanitizeFileName(songName);
            string sanitizedArtistName = SanitizeFileName(artistName);

            string newFileName = $"{sanitizedArtistName} - {sanitizedSongName}.mp3";
            string newFilePath = Path.Combine(dataPath, "downloads", newFileName);
            

            if (File.Exists(newFilePath))
            {
                File.Delete(newFilePath);
            }
            File.Move(mp3Path, newFilePath);
            UnityEngine.Debug.Log($"Renamed and moved downloaded file to: {newFilePath}");
        }
        else if (mp3Path == null)
        {
            UnityEngine.Debug.LogError("No MP3 found!");
            return;
        }

        dontSave = false;
        LoadingDone();
        loadingFX.SetActive(false);
    }

    public async Task SplitSong(string filePath)
    {
        if (File.Exists(Path.Combine(PlayerPrefs.GetString("dataPath"),"output","htdemucs",Path.GetFileNameWithoutExtension(filePath)+" [vocals].mp3")))
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

    

    public async Task StartCompile(string url, string name, string artist, string length, string cover)
    {
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
                Dismiss();
                mainPanel.SetActive(false);
            }
            await Task.Delay(384);
        }
        else
        {
            transitionAnim.Play("TransitionSaved");
            await Task.Delay(1450);
        }

        PlayerPrefs.SetString("currentSong", name);
        PlayerPrefs.SetString("currentArtist", artist);

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
                PlayerPrefs.SetString("fullLocation", expectedFilePath);
                if (PlayerPrefs.GetInt("multiplayer") == 0) LoadMain();
                return;
            }
            else
            {
                UnityEngine.Debug.LogError($"Lyrics file found for '{name}', but the corresponding audio file '{expectedFileName}' is missing in the downloads folder. A re-download might be required. If the issue persists, delete the song's .txt file inside YASG's data folder.");
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

        ProcessStartInfo psi = new ProcessStartInfo
        {
            FileName = dataPath + "\\getlyrics.bat",
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
            if (args.Data.Contains("some tracks"))
            {
                lyricsError2 = true; // Set flag to try fallback
            }
        };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await Task.Run(() => process.WaitForExit());
        UnityEngine.Debug.Log("Lyrics script finished.");

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
            PlayerPrefs.SetString("fullLocation", expectedAudioPath);
            PlayerPrefs.SetString("vocalLocation", Path.Combine(dataPath, "output", "htdemucs", Path.GetFileNameWithoutExtension(expectedAudioPath) + " [vocals].mp3"));
        }
        else
        {
            fakeLoading = true;
            bool success = await AttemptDownload(url);
            fakeLoading = false;
            if (!success)
            {
                alertManager.ShowError("An error occured downloading your song.", "This is likely due to connectivity issues, or due to some rare inconsistency. Please try again.", "Dismiss");
                LoadingDone();
                loadingFX.SetActive(false);
                mainPanel.SetActive(true);
                return;
            }

            var downloadedMp3 = Directory.GetFiles(dataPath, "*.mp3").OrderByDescending(File.GetCreationTime).FirstOrDefault();
            if (downloadedMp3 != null)
            {
                if (File.Exists(expectedAudioPath)) File.Delete(expectedAudioPath);
                File.Move(downloadedMp3, expectedAudioPath);

                UnityEngine.Debug.Log($"Moved downloaded file to: {expectedAudioPath}");
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

        if(SettingsManager.Instance.GetSetting<int>("VocalProcessingMethod") == 1)
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

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            request.SetRequestHeader("User-Agent", "YASG");
            await request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                UnityEngine.Debug.LogError($"LRCLib Error: {request.error}");
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

            try
            {
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


    private async Task RunPythonDirectly(string audioFilePath)
    {
        dataPath = PlayerPrefs.GetString("dataPath");
        Debug.Log("here");
        if (string.IsNullOrEmpty(audioFilePath) || !File.Exists(audioFilePath))
        {
            UnityEngine.Debug.LogError("Audio file not found or path is invalid!");
            return;
        }

        if (processLocally)
        {
            var inputFolder = Path.Combine(dataPath, "Mel-Band-Roformer-Vocal-Model-main", "input");
            var targetPath = Path.Combine(inputFolder, Path.GetFileName(audioFilePath));
            File.Copy(audioFilePath, targetPath, true);
            await RunProcessAsync("ffmpeg", $"-i \"{audioFilePath}\" \"{Path.ChangeExtension(audioFilePath, ".wav")}\"", dataPath);

            PlayerPrefs.SetString("fullLocation", Path.ChangeExtension(audioFilePath, ".mp3"));
            PlayerPrefs.SetString("vocalLocation", Path.Combine(dataPath, "Mel-Band-Roformer-Vocal-Model-main", "output", Path.GetFileNameWithoutExtension(audioFilePath) + "_vocals.wav"));

            await RunProcessAsync("ffmpeg", $"-i \"{targetPath}\" \"{Path.ChangeExtension(targetPath, ".wav")}\"", dataPath);
            File.Delete(Path.ChangeExtension(audioFilePath, ".wav")); // saves storage
            splittingVocals = true;
            while (splittingVocals) await Task.Delay(1000);
        }
        else
        {
            Debug.Log("here");
            var inputFolder = Path.Combine(dataPath, "vocalremover", "input");
            var targetPath = Path.Combine(inputFolder, Path.GetFileName(audioFilePath));
            File.Copy(audioFilePath, targetPath, true);

            PlayerPrefs.SetString("fullLocation", audioFilePath);
            PlayerPrefs.SetString("vocalLocation", Path.Combine(dataPath, "output", "htdemucs", Path.GetFileNameWithoutExtension(audioFilePath) + " [vocals].mp3"));

            string pythonArgs;
            if(SettingsManager.Instance.GetSetting<int>("VocalProcessingMethod") == 0)
            {
                pythonArgs = $"-u \"vr.py\"";
            }
            else
            {
                pythonArgs = $"-u \"main.py\"";
            }
            string pythonExe = Path.Combine(dataPath, "venv", "Scripts", "python.exe");
            string workingDir = Path.Combine(dataPath, "vocalremover");
            await RunProcessAsync(pythonExe, pythonArgs, workingDir);
        }
        UnityEngine.Debug.Log("Python inference finished!");
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
        activeProcess.StartInfo.FileName = Path.Combine(dataPath, "venv", "Scripts", "python.exe");
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
        if (activeProcess != null && !activeProcess.HasExited)
        {
            Debug.Log("Application quitting, killing active process...");
            activeProcess.Kill();
        }
    }

    private async Task RunProcessAsync(string exePath, string arguments, string workingDirectory)
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
            await Task.Run(() => process.WaitForExit());
        }
    }

    public async Task<bool> AttemptDownload(string url)
    {
        bool fail = false;
        ProcessStartInfo psi = new ProcessStartInfo
        {
            FileName = dataPath + "\\downloadsong.bat",
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
            if (args.Data.Contains("duplicate")) dontSave = true;
        };

        process.ErrorDataReceived += (sender, args) =>
        {
            if (string.IsNullOrEmpty(args.Data)) return;
            UnityEngine.Debug.LogError("Error: " + args.Data);
            if (args.Data.Contains("Traceback"))
            {
                fail = true;
                if (!process.HasExited) process.Kill();
            }
        };


        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await Task.Run(() => process.WaitForExit());
        return !fail;
    }

    public void LoadMain()
    {
        SceneManager.LoadScene("Main");
    }

    // --- SERIALIZABLE CLASSES ---

    [Serializable]
    public class LrcLibResponse
    {
        public string syncedLyrics;
    }

    private async void RunUpdateCheckSilently()
    {
        string dataPath = PlayerPrefs.GetString("dataPath");
        string pythonExe = Path.Combine(dataPath, "venv", "Scripts", "python.exe");
        string scriptPath = Path.Combine(dataPath, "setuputilities", "updatecheck.py");
        if (!File.Exists(pythonExe) || !File.Exists(scriptPath))
        {
            // Optionally log missing files, but do nothing else
            Debug.LogWarning("Update check: Python or script not found.");
            return;
        }

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

        try
        {
            using (var process = new Process { StartInfo = psi })
            {
                process.Start();
                await Task.Run(() => process.WaitForExit());
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Update check failed: {e.Message}");
        }
    }
}