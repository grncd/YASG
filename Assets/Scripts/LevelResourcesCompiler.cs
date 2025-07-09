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

public class LevelResourcesCompiler : MonoBehaviour
{
    public GameObject loadingCanvas;
    public TextMeshProUGUI status;
    public string dataPath;
    private string extractedFileName = null;
    private string filePath;
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
    private bool lyricsError2 = false;
    public DifficultyHover DIH;
    public DifficultySelector DIH2;
    private float currentPercentage = 0f;
    public Animator bgDarken;
    public GameObject bgGM;
    private bool fakeLoading = false;
    private float elapsedFakeLoading = 0f;
    public GameObject starHSPrefab;
    public AudioClip stage1FX;
    public AudioClip stage2FX;
    public AudioClip stage3FX;
    public GameObject loadingFX;

    private int _originalVSyncCount;

    public static LevelResourcesCompiler Instance { get; private set; }

    private void Awake()
    {
        Instance = this;
        //DontDestroyOnLoad(this.gameObject);
    }

    private void Start() 
    {
        if (Application.isEditor)
        {
            PlayerPrefs.SetString("dataPath", "C:\\YASGdata");
        }
        else
        {
            PlayerPrefs.SetString("dataPath", "C:\\YASGdataTesting");
        }
        Application.targetFrameRate = -1;
        filePath = Path.Combine(dataPath, "corr.json");
        progressBar.gameObject.SetActive(false);
        if (processLocally)
        {
            System.IO.Directory.Delete(Path.Combine(dataPath, "Mel-Band-Roformer-Vocal-Model-main", "input"), true);
            System.IO.Directory.CreateDirectory(Path.Combine(dataPath, "Mel-Band-Roformer-Vocal-Model-main", "input"));
            InitVocalSplit();
        }
        else
        {
            initLoadingDone = true;
        }
    }



    private async void InitVocalSplit()
    {
        string pythonArgs = $"-u \"inference.py\" --config_path big_beta5e.yaml --model_path big_beta5e.ckpt --input_folder input --store_dir output";
        string pythonExe = "python"; // or full path to python if needed
        string workingDir = Path.Combine(dataPath, "Mel-Band-Roformer-Vocal-Model-main");
        RunProcessAsync(pythonExe, pythonArgs, workingDir);
    }

    void Update()
    {
        if (lyricsError)
        {
            alertManager.ShowError("This song does not have lyrics.", "The song you've selected either has no lyrics or Spotify didn't provide synced lyrics for it. Please choose another song.", "Dismiss");
            LoadingDone();
            mainPanel.SetActive(true);
            lyricsError = false;
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
            //PlayerPrefs.SetString("vocalLocation", vocalLocation);
            UnityEngine.Debug.Log("PlayerPrefs updated with vocalLocation: " + vocalLocation);

            // Reset the flag so we don't update repeatedly
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

        // Find the highest resolution image (largest width)
        var highestResImage = track.album.images[0];
        foreach (var img in track.album.images)
        {
            if (img.width > highestResImage.width)
            {
                highestResImage = img;
            }
        }

        // Start the coroutine to download and apply the image
        StartCoroutine(DownloadAlbumCover(highestResImage.url, image, image2));
    }

    public string GetURLCoverFromTrack(Track track)
    {
        if (track == null || track.album == null || track.album.images == null || track.album.images.Count == 0)
        {
            UnityEngine.Debug.LogWarning("No album cover found for this track.");
            return "";
        }

        // Find the highest resolution image (largest width)
        var highestResImage = track.album.images[0];
        foreach (var img in track.album.images)
        {
            if (img.width > highestResImage.width)
            {
                highestResImage = img;
            }
        }

        return highestResImage.url;        
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


    public void PreCompile(string url, string name,string artist,string length, Track track)
    {


        if(ProfileManager.Instance.GetActiveProfiles().Count == 0)
        {
            alertManager.ShowError("You don't have any active profiles!", "Please go to the Settings (cogwheel on the bottom right) and either create a new profile or activate an existing one.", "Dismiss");
            return;
        }
        List<Profile> profiles = ProfileManager.Instance.GetActiveProfiles();
        foreach (Profile profile in profiles)
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
        songInfo.transform.GetChild(4).GetChild(0).GetChild(0).GetComponent<TextMeshProUGUI>().text = name;
        songInfo.transform.GetChild(4).GetChild(0).GetChild(1).GetComponent<TextMeshProUGUI>().text = artist;
        songInfo.transform.GetChild(4).GetChild(0).GetChild(3).GetComponent<TextMeshProUGUI>().text = "Song Length: "+length;

        songInfo.transform.GetChild(4).GetChild(0).GetChild(4).GetComponent<Button>().onClick.RemoveAllListeners();
        songInfo.transform.GetChild(4).GetChild(0).GetChild(5).GetComponent<Button>().onClick.RemoveAllListeners();
        songInfo.transform.GetChild(4).GetChild(0).GetChild(6).GetComponent<Button>().onClick.RemoveAllListeners();

        songInfo.transform.GetChild(4).GetChild(0).GetChild(4).GetComponent<Button>().onClick.AddListener(delegate { StartCompile(url, name,artist,length,cover); });

        if (!isFavorite)
        {
            songInfo.transform.GetChild(4).GetChild(0).GetChild(5).gameObject.SetActive(true);
            songInfo.transform.GetChild(4).GetChild(0).GetChild(6).gameObject.SetActive(false);
        }
        else
        {
            songInfo.transform.GetChild(4).GetChild(0).GetChild(5).gameObject.SetActive(false);
            songInfo.transform.GetChild(4).GetChild(0).GetChild(6).gameObject.SetActive(true);
        }

        songInfo.transform.GetChild(4).GetChild(0).GetChild(5).GetComponent<Button>().onClick.AddListener(delegate { AddFavorite(name,artist,length,cover,url); });
        songInfo.transform.GetChild(4).GetChild(0).GetChild(5).GetComponent<Button>().onClick.AddListener(delegate { songInfo.transform.GetChild(4).GetChild(0).GetChild(5).gameObject.SetActive(false); });
        songInfo.transform.GetChild(4).GetChild(0).GetChild(5).GetComponent<Button>().onClick.AddListener(delegate { songInfo.transform.GetChild(4).GetChild(0).GetChild(6).gameObject.SetActive(true); });

        songInfo.transform.GetChild(4).GetChild(0).GetChild(6).GetComponent<Button>().onClick.AddListener(delegate { RemoveFavorite(url); });
        songInfo.transform.GetChild(4).GetChild(0).GetChild(6).GetComponent<Button>().onClick.AddListener(delegate { songInfo.transform.GetChild(4).GetChild(0).GetChild(5).gameObject.SetActive(true); });
        songInfo.transform.GetChild(4).GetChild(0).GetChild(6).GetComponent<Button>().onClick.AddListener(delegate { songInfo.transform.GetChild(4).GetChild(0).GetChild(6).gameObject.SetActive(false); });

        SetAlbumCoverFromTrack(track, songInfo.transform.GetChild(4).GetChild(0).GetComponent<MPImage>(), songInfo.transform.GetChild(4).GetChild(0).GetChild(2).GetComponent<MPImage>());

        Transform highscorePanel = songInfo.transform.GetChild(4).GetChild(0).GetChild(7);
        HighscoreEntry highscore = HighscoreManager.Instance.GetHighscore(url);

        if (highscore != null)
        {
            highscorePanel.gameObject.SetActive(true);

            // Get UI components based on the paths you provided
            TextMeshProUGUI scoreText = highscorePanel.GetChild(0).GetComponent<TextMeshProUGUI>();
            Transform starsLocation = highscorePanel.GetChild(1).transform;
            TextMeshProUGUI byText = highscorePanel.GetChild(2).GetComponent<TextMeshProUGUI>();

            // Set text values
            scoreText.text = highscore.score.ToString("#,#");
            byText.text = "By: " + highscore.playerName;

            // Clear any old stars that might be there from a previous selection
            foreach (Transform child in starsLocation)
            {
                Destroy(child.gameObject);
            }

            // Instantiate the correct number of stars
            if (starHSPrefab != null)
            {
                for (int i = 0; i < highscore.stars; i++)
                {
                    Instantiate(starHSPrefab, starsLocation);
                }
            }
            else
            {
                UnityEngine.Debug.LogWarning("starHSPrefab is not assigned in the LevelResourcesCompiler Inspector!");
            }
        }
        else
        {
            // If no highscore exists for this song, hide the panel
            highscorePanel.gameObject.SetActive(false);
        }
    }

    public void AddFavorite(string name, string artist, string length, string cover, string url)
    {
        FavoritesManager.AddFavorite(name, artist, length, cover, url);
        songInfo.transform.GetChild(5).GetComponent<AudioSource>().Play();
        FavoritesManager.PrintAllFavorites();
    }

    public void RemoveFavorite(string url)
    {
        FavoritesManager.RemoveFavoriteByUrl(url);
        songInfo.transform.GetChild(6).GetComponent<AudioSource>().Play();
        FavoritesManager.PrintAllFavorites();
    }

    public async void Dismiss()
    {
        DIH.Close();
        DIH2.Close();
        songInfo.GetComponent<Animator>().Play("SongInfoOut");
        await Task.Delay(TimeSpan.FromMilliseconds(200));
        songInfo.SetActive(false);
    }

    public async Task DownloadSong(string url)
    {
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
        LoadingDone();
        loadingFX.SetActive(false);
    }

    public async Task StartCompile(string url,string name,string artist,string length,string cover)
    {
        if (Application.isEditor)
        {
            PlayerPrefs.SetString("dataPath", "C:\\YASGdata");
        }
        else
        {
            PlayerPrefs.SetString("dataPath", "C:\\YASGdataTesting");
        }
        dataPath = PlayerPrefs.GetString("dataPath");
        UnityEngine.Debug.Log($"CALLED: {url}, {name}, {artist}, {length}, {cover}");
        PlayerPrefs.SetString("currentSongURL", url);
        name = name.Replace("\\", " ");
        songInfo.transform.GetChild(4).GetComponent<AudioSource>().Play();
        if (!CheckFile(name + ".txt"))
        {
            transitionAnim.Play("Transition");
            await Task.Delay(TimeSpan.FromMilliseconds(1350));
            if (songInfo.activeSelf)
            {
                Dismiss();
                mainPanel.SetActive(false);
            }
            await Task.Delay(TimeSpan.FromMilliseconds(384));
        }
        else
        {
            transitionAnim.Play("TransitionSaved");
            await Task.Delay(TimeSpan.FromMilliseconds(1450));
        }
        PlayerPrefs.SetString("currentSong", name);
        string charactersToRemovePattern = @"[/\\:*?""<>|]";
        name = Regex.Replace(name, charactersToRemovePattern, string.Empty);
        UnityEngine.Debug.Log(CheckFile(name+".txt"));
        if (CheckFile(name + ".txt")) // Checking if this process has already been done for the song
        {
            status.text = "Already downloaded. Loading main scene...";
            PlayerPrefs.SetInt("saved",1);
            if (!File.Exists(filePath))
            {
                UnityEngine.Debug.LogWarning("corr.json not found. Creating a new one.");
                var newData = new CorrespondenceData2();
                string newJsonContent = JsonUtility.ToJson(newData, true);
                File.WriteAllText(filePath, newJsonContent);
            }
            string jsonContent = File.ReadAllText(filePath);
            CorrespondenceData2 data = JsonUtility.FromJson<CorrespondenceData2>(jsonContent);

            if (data != null && data.correspondences != null)
            {
                string key = name;
                bool found = false;
                foreach (var item in data.correspondences)
                {
                    if (item.key == key)
                    {
                        string value = item.value;
                        UnityEngine.Debug.Log("Corresponding value: " + value);
                        string vocalLocation = Path.Combine(dataPath, "output","htdemucs", value.Substring(0, value.Length - 4) + " [vocals].mp3");
                        PlayerPrefs.SetString("vocalLocation", vocalLocation);
                        PlayerPrefs.SetString("fullLocation", Path.Combine(dataPath, value));
                        //if (PlayerPrefs.GetInt("multiplayer") == 1)
                        //{
                        //status.text = "Sending files to players...";
                        //await Task.Delay(TimeSpan.FromMilliseconds(2500));
                        //}
                        if (PlayerPrefs.GetInt("multiplayer") == 0)
                        {
                            LoadMain();
                        }
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    UnityEngine.Debug.Log("Key not found: " + key);
                }
            }
            else
            {
                UnityEngine.Debug.LogError("Failed to parse JSON or empty correspondences.");
            }
            return;
        }
        PlayerPrefs.SetInt("saved", 0);
        loadingCanvas.SetActive(true);
        loadingSecond.SetActive(true);
        loadingSecond.transform.GetChild(4).gameObject.SetActive(true);
        loadingFirst.SetActive(false);
        BeginLoading();
        loadingFX.SetActive(true);
        status.text = "Fetching lyrics...";

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
            FileName = dataPath+"\\getlyrics.bat",
            Arguments = url+ " "+dataPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        Process process = new Process { StartInfo = psi };

        // Capture output and error asynchronously
        process.OutputDataReceived += (sender, args) =>
        {
            if (!string.IsNullOrEmpty(args.Data))
                UnityEngine.Debug.Log("Output: " + args.Data);
            if (args.Data.Contains("some tracks"))
            {
                loadingFX.SetActive(false);
                lyricsError = true;
                lyricsError2 = true;
                return;
            }
        };


        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // Wait asynchronously without freezing the game
        await Task.Run(() => process.WaitForExit());
        UnityEngine.Debug.Log("DONE");
        if (lyricsError2)
        {
            loadingFX.SetActive(false);
            lyricsError2 = false;
            return;
        }

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

        bgDarken.Play("Darken");
        await Task.Delay(TimeSpan.FromMilliseconds(1010));
        bgGM.SetActive(false);

        // Store original VSync setting and disable it, then set target FPS
        _originalVSyncCount = QualitySettings.vSyncCount;
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 30;

        await RunPythonDirectly();

        // Restore original settings
        Application.targetFrameRate = -1;
        QualitySettings.vSyncCount = _originalVSyncCount;

        splittingVocals = false;
        currentPercentage = 0f;

        FavoritesManager.AddDownload(name, artist, length, cover, url);
        //if(PlayerPrefs.GetInt("multiplayer") == 1)
        //{
        //status.text = "Sending files to players...";
        //await Task.Delay(TimeSpan.FromMilliseconds(2500));
        //}

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
        await Task.Delay(TimeSpan.FromMilliseconds(1010));
        if (PlayerPrefs.GetInt("multiplayer") == 0)
        {
            LoadMain();
        }
    }

    private async Task RunPythonDirectly()
    {
        if (processLocally)
        {
            // 1. Find your mp3 file in the folder
            var mp3Path = Directory.GetFiles(dataPath, "*.mp3").OrderByDescending(File.GetCreationTime).FirstOrDefault();
            if (mp3Path == null)
            {
                UnityEngine.Debug.LogError("No MP3 found!");
                return;
            }

            // 2. Move it into input folder
            var inputFolder = Path.Combine(dataPath, "Mel-Band-Roformer-Vocal-Model-main", "input");
            var mp3FileName = Path.GetFileName(mp3Path);
            var targetPath = Path.Combine(inputFolder, mp3FileName);
            File.Copy(mp3Path, targetPath);
            await RunProcessAsync("ffmpeg", $"-i \"" + mp3Path + "\" \"" + Path.ChangeExtension(mp3Path, ".wav") + "\"", dataPath);

            PlayerPrefs.SetString("fullLocation", Path.ChangeExtension(mp3Path, ".mp3"));
            PlayerPrefs.SetString("vocalLocation", Path.Combine(dataPath, "Mel-Band-Roformer-Vocal-Model-main", "output", Path.GetFileNameWithoutExtension(mp3Path) + "_vocals.wav"));
            AppendToJson(Path.Combine(dataPath, "corr.json"), PlayerPrefs.GetString("currentSong"), Path.GetFileName(Path.ChangeExtension(mp3Path, ".mp3")));

            // 3. Convert to .wav with ffmpeg
            await RunProcessAsync("ffmpeg", $"-i \"{targetPath}\" \"{Path.ChangeExtension(targetPath, ".wav")}\"", dataPath);
            // 4. Run python inference
            File.Delete(Path.ChangeExtension(mp3Path,".wav")); // saves storage
            splittingVocals = true;
            while (splittingVocals)
            {
                await Task.Delay(1000);
            }
        }
        else
        {
            dataPath = PlayerPrefs.GetString("dataPath");
            // 1. Find your mp3 file in the folder
            var mp3Path = Directory.GetFiles(dataPath, "*.mp3").OrderByDescending(File.GetCreationTime).FirstOrDefault();
            UnityEngine.Debug.Log(mp3Path);
            if (mp3Path == null)
            {
                UnityEngine.Debug.LogError("No MP3 found!");
                return;
            }

            // 2. Move it into input folder
            var inputFolder = Path.Combine(dataPath, "vocalremover", "input");
            var mp3FileName = Path.GetFileName(mp3Path);
            var targetPath = Path.Combine(inputFolder, mp3FileName);
            File.Copy(mp3Path, targetPath);
            //await RunProcessAsync("ffmpeg", $"-i \"" + mp3Path + "\" \"" + Path.ChangeExtension(mp3Path, ".wav") + "\"", dataPath);
            PlayerPrefs.SetString("fullLocation", mp3Path);
            PlayerPrefs.SetString("vocalLocation", Path.Combine(dataPath, "output","htdemucs", Path.GetFileNameWithoutExtension(mp3Path) + " [vocals].mp3"));
            AppendToJson(Path.Combine(dataPath, "corr.json"), PlayerPrefs.GetString("currentSong"), Path.GetFileName(Path.ChangeExtension(mp3Path, ".mp3")));

            // 4. Run python inference
            //File.Delete(Path.ChangeExtension(mp3Path, ".wav")); // saves storage
            string pythonArgs = $"-u \"main.py\" "+Path.Combine(dataPath,"output");
            string pythonExe = "python"; // or full path to python if needed
            string workingDir = Path.Combine(dataPath, "vocalremover");
            await RunProcessAsync(pythonExe,pythonArgs,workingDir);
        }
        UnityEngine.Debug.Log("Python inference finished!");
    }

    public void AppendToJson(string path, string newKey, string newValue)
    {
        // Load the existing JSON data
        if (!File.Exists(path))
        {
            UnityEngine.Debug.LogError("JSON file not found: " + path);
            return;
        }

        string json = File.ReadAllText(path);
        CorrespondenceData2 data = JsonUtility.FromJson<CorrespondenceData2>(json);

        // Append new entry
        KeyValuePair newEntry = new KeyValuePair { key = newKey, value = newValue };
        data.correspondences.Add(newEntry);

        // Save updated JSON back to file
        string updatedJson = JsonUtility.ToJson(data, true);
        File.WriteAllText(path, updatedJson);

        UnityEngine.Debug.Log("Added new entry: " + newKey + " -> " + newValue);
    }

    public float ParseTime(string timeString)
    {
        // Split the string by the space to separate the number and the "seconds" part
        string[] parts = timeString.Split(' ');

        // Try to parse the first part as a float
        if (float.TryParse(parts[0], out float result))
        {
            return result;
        }
        else
        {
            UnityEngine.Debug.LogError("Invalid time format");
            return 0f;
        }
    }
    public void LowpassTransition(bool inOut) // true = in / false = out
    {
        if (transitionCoroutine != null)
            StopCoroutine(transitionCoroutine);

        if (inOut)
        {
            transitionCoroutine = StartCoroutine(ChangeCutoffOverTime(20000f, 650f, 0.5f));
        }
        else
        {
            transitionCoroutine = StartCoroutine(ChangeCutoffOverTime(650f, 20000f, 1f));
        }
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
        musicControl.audioMixer.SetFloat("LowpassCutoff", endFreq); // Ensure it reaches exactly 20000Hz
    }
    public async void LoadingDone()
    {
        LowpassTransition(false);
        blurAnim.Play("BlurOut");
        loadingAnim.Play("LoadingOut");
        await Task.Delay(TimeSpan.FromSeconds(0.5f));
        loadingCanvas.SetActive(false);
    }

    public void BeginLoading()
    {
        LowpassTransition(true);
        blurAnim.Play("BlurIn");
        loadingAnim.Play("LoadingIn");
    }


    private async Task RunProcessAsync(string exePath, string arguments, string workingDirectory)
    {
        if(exePath == "python" && processLocally)
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
            if (!string.IsNullOrEmpty(args.Data))
            {
                UnityEngine.Debug.Log($"[STDOUT] {args.Data}");
                if (args.Data.Contains("Estimated total processing time for this track:"))
                {
                    var temp = args.Data.Substring(48, args.Data.Length - 48);
                    var tempf = ParseTime(temp);
                    totalETA = tempf;
                    UnityEngine.Debug.Log("Extracted ETA: " + tempf.ToString());
                }
                if (args.Data.Contains("Processing track") && exePath == "python" && processLocally)
                {
                    Process.GetCurrentProcess().ProcessorAffinity = (IntPtr)0b0001;
                    Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.Idle;
                    process.PriorityClass = ProcessPriorityClass.RealTime;
                }
                if (args.Data.Contains("Model is ready.") && exePath == "python" && processLocally)
                {
                    initLoadingDone = true;
                }
                if (args.Data.Contains("Processing finished.") && exePath == "python" && processLocally)
                {
                    done = true;
                }
                if (args.Data.Contains("Progress:"))
                {
                    int temp = Convert.ToInt32(args.Data.Substring(10, args.Data.Length - 11).Replace("%",""));
                    currentPercentage = (float)temp/100;
                }
                if (processLocally)
                {
                    if (args.Data.Contains("Processing track 1/1:"))
                    {
                        // Correct the substring calculation:
                        // Starting at 22, length = args.Data.Length - 26 to remove first 22 and last 4 characters.
                        if (args.Data.Length >= 26)
                        {
                            extractedFileName = args.Data.Substring(22, args.Data.Length - 26).Trim();
                            UnityEngine.Debug.Log("Extracted file name: " + extractedFileName);
                        }
                        else
                        {
                            UnityEngine.Debug.LogError("Data too short for substring extraction: " + args.Data);
                        }
                    }
                }
                else
                {
                    if (args.Data.Contains("Uploading file:"))
                    {
                        extractedFileName = args.Data.Substring(15, args.Data.Length - 19).Trim();
                        UnityEngine.Debug.Log("Extracted file name: " + extractedFileName);
                    }
                }
            }
        };
        process.ErrorDataReceived += (sender, args) =>
        {
            if (!string.IsNullOrEmpty(args.Data))
                UnityEngine.Debug.Log($"[STDERR] {args.Data}");
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (exePath == "python" && processLocally)
        {
            vocalSplitProc = process;
            while (true)
            {
                while (!done)
                {
                    await Task.Delay(1000);
                }
                UnityEngine.Debug.Log("end detected");
                splittingVocals = false;
                Process.GetCurrentProcess().ProcessorAffinity = tempCPU;
                Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.Normal;
                done = false;
            }
        } else
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

        // Capture output and error asynchronously
        process.OutputDataReceived += (sender, args) =>
        {
            if (!string.IsNullOrEmpty(args.Data))
                UnityEngine.Debug.Log("Output: " + args.Data);
        };

        process.ErrorDataReceived += (sender, args) =>
        {
            if (!string.IsNullOrEmpty(args.Data))
            {
                UnityEngine.Debug.LogError("Error: " + args.Data);
                if (args.Data.Contains("Traceback"))
                {
                    fail = true;
                    process.Kill();
                }
            }
        };


        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // Wait asynchronously without freezing the game
        await Task.Run(() => process.WaitForExit());
        if (fail)
        {
            return false;
        }
        return true;
    }

    public void LoadMain()
    {
        SceneManager.LoadScene("Main");
    }

    [Serializable]
    public class KeyValuePair
    {
        public string key;
        public string value;
    }

    [Serializable]
    public class CorrespondenceData
    {
        public KeyValuePair[] correspondences;
    }

    [System.Serializable]
    public class CorrespondenceData2
    {
        public List<KeyValuePair> correspondences = new List<KeyValuePair>();
    }

    

}
