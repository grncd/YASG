using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using System.IO;
using UnityEngine.UI;
using System.Runtime.CompilerServices;
using System;
using System.Threading.Tasks;
using UnityEngine.SceneManagement;
using FishNet.Managing.Scened;

public class LyricsHandler : MonoBehaviour
{
    public string lyrics;
    public TextMeshProUGUI prevLyricsText;
    public TextMeshProUGUI lyricsText;
    public Animator lyricsAnimator;
    private int prevIndex = -1;
    public GameObject pausePanel;
    private float lyricsDelay = 1f;

    public List<(float time, string line)> parsedLyrics = new List<(float, string)>();
    private List<float> lineDurations = new List<float>();
    public AudioClipPitchProcessor APP;
    private float processedAudioLength;
    public Slider progressTime;
    private string songName;
    public TextMeshProUGUI songText;
    private bool firstLine = false;

    private float startTime;
    private bool isPlaying = false;
    public event System.Action<int, float, bool> OnNewLyricLine; // Event for players
    public GameObject playersParent;
    public bool songOver = false;
    public Animator fadeOut;
    private bool fadeOutDone = false;
    private bool paused = false;
    private bool canPause = false;
    public AudioSource pauseFX;
    public AudioSource unpauseFX;
    public GameObject stagesGO;

    private int currentLineIndex = 0;

    public static LyricsHandler Instance { get; private set; }

    private void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        lyricsDelay = Mathf.Clamp(float.Parse(SettingsManager.Instance.GetSetting<string>("LyricDisplayOffset")), 0f, 5f);
        string charactersToRemovePattern = @"[/\\:*?""<>|]";
        string currentSong = PlayerPrefs.GetString("currentSong");
        currentSong = Regex.Replace(currentSong, charactersToRemovePattern, string.Empty);

        PlayerPrefs.SetString("currentSong", PlayerPrefs.GetString("currentSong"));
        Application.targetFrameRate = -1;
        songName = Path.GetFileNameWithoutExtension(PlayerPrefs.GetString("fullLocation"));
        songText.text = songName;
        PlayerPrefs.SetString("currentSongDisplay", songName);
        Debug.Log(PlayerPrefs.GetInt("MicPlayer1"));
        Debug.Log(PlayerPrefs.GetInt("MicPlayer2"));
        Debug.Log(PlayerPrefs.GetInt("MicPlayer3"));
        Debug.Log(PlayerPrefs.GetInt("MicPlayer4"));
        if (PlayerPrefs.GetInt("Player1") == 1)
        {
            var temp = playersParent.transform.GetChild(0);
            temp.gameObject.SetActive(true);
            temp.GetChild(3).GetComponent<TextMeshProUGUI>().text = PlayerPrefs.GetString("Player1Name");
            //temp.GetComponent<RealTimePitchDetector>().device = PlayerPrefs.GetInt("MicPlayer1");
        }
        if (PlayerPrefs.GetInt("Player2") == 1)
        {
            var temp = playersParent.transform.GetChild(1);
            temp.gameObject.SetActive(true);
            temp.GetChild(3).GetComponent<TextMeshProUGUI>().text = PlayerPrefs.GetString("Player2Name");
            //temp.GetComponent<RealTimePitchDetector>().device = PlayerPrefs.GetInt("MicPlayer2");
        }
        if (PlayerPrefs.GetInt("Player3") == 1)
        {
            var temp = playersParent.transform.GetChild(2);
            temp.gameObject.SetActive(true);
            temp.GetChild(3).GetComponent<TextMeshProUGUI>().text = PlayerPrefs.GetString("Player3Name");
            //temp.GetComponent<RealTimePitchDetector>().device = PlayerPrefs.GetInt("MicPlayer3");
        }
        if (PlayerPrefs.GetInt("Player4") == 1)
        {
            var temp = playersParent.transform.GetChild(3);
            temp.gameObject.SetActive(true);
            temp.GetChild(3).GetComponent<TextMeshProUGUI>().text = PlayerPrefs.GetString("Player4Name");
            //temp.GetComponent<RealTimePitchDetector>().device = PlayerPrefs.GetInt("MicPlayer4");
        }
        if (!File.Exists($"{PlayerPrefs.GetString("dataPath")}\\downloads\\" + currentSong + ".txt"))
        {
            System.IO.File.Move($"{PlayerPrefs.GetString("dataPath")}\\downloads\\" + currentSong + ".lrc", $"{PlayerPrefs.GetString("dataPath")}\\downloads\\" + currentSong + ".txt");
        }
        if(PlayerPrefs.GetInt("saved") == 1)
        {
            stagesGO.SetActive(false);
        }
        LoadTextFile($"{PlayerPrefs.GetString("dataPath")}\\downloads\\" + currentSong + ".txt");
        ParseLyrics();
    }

    private void OnApplicationFocus(bool focus)
    {
        if (!focus)
        {
            Pause();
        }
    }

    public void Pause()
    {
        if (canPause && PlayerPrefs.GetInt("multiplayer") == 0)
        {
            paused = !paused;
            PlayerPerformance[] allPlayers = FindObjectsOfType<PlayerPerformance>();
            foreach (PlayerPerformance player in allPlayers)
            {
                player.Pause();
            }
            APP.Pause();
            if (paused)
            {
                pauseFX.Play();
                pausePanel.SetActive(true);
                Time.timeScale = 0f;
            }
            else
            {
                unpauseFX.Play();
                pausePanel.SetActive(false);
                Time.timeScale = 1f;
            }
        }
    }

    public void BackToMenu()
    {
        Time.timeScale = 1f;
        UnityEngine.SceneManagement.SceneManager.LoadScene("Menu", LoadSceneMode.Single);
    }

    public void Retry()
    {
        Time.timeScale = 1f;
        UnityEngine.SceneManagement.SceneManager.LoadScene("Main");
    }

    public void LoadTextFile(string path)
    {
        if (File.Exists(path))
        {
            lyrics = File.ReadAllText(path);
            Debug.Log("File Loaded Successfully:\n" + lyrics);
        }
        else
        {
            Debug.LogError("File not found at path: " + path);
        }
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape) && canPause && PlayerPrefs.GetInt("multiplayer") == 0)
        {
            Pause();
        }
        float elapsedTime = Time.time - startTime;
        
        if (isPlaying)
        {
            progressTime.value = Math.Clamp(elapsedTime / processedAudioLength, 0.005f, 1f);
            UpdateLyricsDisplay(elapsedTime);
            UpdateLyricsDisplayOffset(elapsedTime);
            if (elapsedTime > processedAudioLength) // songs over
            {
                songOver = true;
                if (!fadeOutDone)
                {
                    fadeOutDone = true;
                    FadeOut();
                }
            }
        }
    }

    private async void FadeOut()
    {
        fadeOut.Play("FadeOut");
        await Task.Delay(TimeSpan.FromSeconds(1));

        if (PlayerPrefs.GetInt("multiplayer") == 1)
        {
            // Online logic
            if (PlayerData.LocalPlayerInstance != null && PlayerData.LocalPlayerInstance.IsHost.Value)
            {
                // 1. Tell the server to calculate and set the final placements for everyone.
                PlayerData.LocalPlayerInstance.RequestSetFinalPlacements_ServerRpc();

                // 2. Tell the server to calculate final XP and Level for everyone.
                PlayerData.LocalPlayerInstance.RequestFinalXPCalculation_ServerRpc();

                // 3. Wait a brief moment to ensure the SyncVars have time to propagate.
                await Task.Delay(TimeSpan.FromMilliseconds(250));

                SceneLoadData sld = new SceneLoadData("Results");
                sld.ReplaceScenes = ReplaceOption.All;
                FishNet.InstanceFinder.NetworkManager.SceneManager.LoadGlobalScenes(sld);
            }
        }
        else
        {
            // Offline logic
            UnityEngine.SceneManagement.SceneManager.LoadScene("Results");
        }
    }

    public void StartLyrics()
    {
        canPause = true;
        processedAudioLength = APP.ProcessedAudioLength;
        startTime = Time.time;
        isPlaying = true;

        if (parsedLyrics.Count > 0)
        {
            currentLineIndex = 0;
            OnNewLyricLine?.Invoke(currentLineIndex, parsedLyrics[currentLineIndex].time, false);
        }
    }

    private void ParseLyrics()
    {
        if (lyrics == null) return;
        parsedLyrics.Clear();
        lineDurations.Clear();

        string[] lines = lyrics.Split('\n');
        Regex regex = new Regex(@"\[(\d{2}):(\d{2}\.\d{2})\](.*)");

        float previousTime = 0f;
        foreach (string line in lines)
        {
            Match match = regex.Match(line);
            if (match.Success)
            {
                Debug.Log("Parsing line: " + line);
                int minutes = int.Parse(match.Groups[1].Value);
                float seconds = float.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                string lyricLine = match.Groups[3].Value.Trim();

                float time = minutes * 60 + seconds;
                parsedLyrics.Add((time, lyricLine));

                if (parsedLyrics.Count > 1)
                {
                    lineDurations.Add(time - previousTime);
                }
                previousTime = time;
            }
        }

        if (parsedLyrics.Count > 1)
        {
            lineDurations.Add(3f); // Default duration for the last line
        }
    }

    private void UpdateLyricsDisplay(float elapsedTime)
    {
        for (int i = parsedLyrics.Count - 1; i >= 0; i--)
        {
            if (elapsedTime >= parsedLyrics[i].time)
            {
                
                if (i != currentLineIndex || (firstLine == false && i == 0))
                {
                    firstLine = true;
                    currentLineIndex = i;
                    if (parsedLyrics[i].line != "♪" && parsedLyrics[i].line != "")
                    {
                        OnNewLyricLine?.Invoke(currentLineIndex, parsedLyrics[i].time, true);
                    }
                    else
                    {
                        OnNewLyricLine?.Invoke(currentLineIndex, parsedLyrics[i].time, false);
                    }
                }
                break;
            }
        }
    }
    private void UpdateLyricsDisplayOffset(float elapsedTime)
    {
        for (int i = parsedLyrics.Count - 1; i >= 0; i--)
        {
            if (elapsedTime+lyricsDelay >= parsedLyrics[i].time)
            {
                if (prevIndex != i)
                {
                    prevIndex = i;
                    lyricsAnimator.Play("NextLine");
                    if (i != 0)
                    {
                        prevLyricsText.text = parsedLyrics[i - 1].line;
                    }
                    lyricsText.text = parsedLyrics[i].line;
                }
                break;
            }
        }

    }

    public float GetLineDuration(int index)
    {
        return index < lineDurations.Count ? lineDurations[index] : 3f;
    }

    public string GetLyricForTime(float time)
    {
        // Iterate from the end for efficiency
        string activeLine = "";
        for (int i = parsedLyrics.Count - 1; i >= 0; i--)
        {
            if (time >= parsedLyrics[i].time)
            {
                activeLine = parsedLyrics[i].line;
                break;
            }
        }
        return activeLine;
    }
}
