using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using UnityEngine.Networking;
using TMPro;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(AudioSource))]
public class BGMusic : MonoBehaviour
{
    public AudioClip menuMusicClip;
    public UnityEngine.Audio.AudioMixerGroup previewAudioMixerGroup;
    [Range(0f, 1f)]
    public float bgMusicVolume = 0.143f;
    private AudioSource audioSource;
    private int lastMenuMusicValue = -1;
    public TextMeshProUGUI songName;
    private GameObject shuffleButton;
    private bool killSwitch = false;

    private AudioSource previewAudioSource;
    private string tempPreviewFilePath;
    private Coroutine previewSongCoroutine;

    public static BGMusic Instance { get; private set; }

    void Awake()
    {
        // Make this a persistent singleton so preview audio is controlled by a single instance across scenes
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }
        previewAudioSource = gameObject.AddComponent<AudioSource>();
        previewAudioSource.loop = false;
        previewAudioSource.playOnAwake = false;
        if (previewAudioMixerGroup != null)
        {
            previewAudioSource.outputAudioMixerGroup = previewAudioMixerGroup;
        }
    }

    void Start()
    {
        songName = GameObject.Find("Canvas").transform.GetChild(2).GetChild(7).GetChild(2).GetComponent<TextMeshProUGUI>();
        shuffleButton = songName.transform.parent.GetChild(4).gameObject;
        audioSource = GetComponent<AudioSource>();
        StartCoroutine(MusicPlayerCoroutine());
    }

    private void OnEnable()
    {
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
    }

    private void OnDisable()
    {
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
    }

    private void OnDestroy()
    {
        // Clean up event subscription if this instance is being destroyed
        if (Instance == this)
        {
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
            Instance = null;
        }
    }

    private void OnActiveSceneChanged(Scene previousScene, Scene newScene)
    {
        // Always ensure any preview is stopped when scenes change to prevent overlapping audio
        if (previewAudioSource != null && previewAudioSource.isPlaying)
        {
            StopPreview();
        }

        // Only run menu setup when entering the Menu scene
        if (newScene.name == "Menu")
        {
            // Ensure bg volumes are sane
            previewAudioSource.volume = 0.143f;
            if (audioSource != null)
            {
                audioSource.volume = bgMusicVolume;
            }
            killSwitch = false;
            songName = GameObject.Find("Canvas").transform.GetChild(2).GetChild(7).GetChild(2).GetComponent<TextMeshProUGUI>();
            shuffleButton = songName.transform.parent.GetChild(4).gameObject;
            StopAllCoroutines();
            StartCoroutine(MusicPlayerCoroutine());
        }
        else
        {
            StopAllCoroutines();
            killSwitch = true;
        }
    }

    void Update()
    {
        if (PlayerPrefs.GetInt("editing") == 1)
        {
            StopAllCoroutines();
            killSwitch = true;
        }
        else
        {
            killSwitch = false;
        }

        if (!killSwitch)
        {
            int currentMenuMusicValue = SettingsManager.Instance.GetSetting<int>("MenuMusic");
            if (currentMenuMusicValue != lastMenuMusicValue)
            {
                StopAllCoroutines();
                StartCoroutine(MusicPlayerCoroutine());
            }
        }
    }

    public void Reshuffle()
    {
        if (SettingsManager.Instance.GetSetting<int>("MenuMusic") == 2)
        {
            StopAllCoroutines();
            StartCoroutine(MusicPlayerCoroutine());
        }
    }

    IEnumerator MusicPlayerCoroutine()
    {
        if (!killSwitch)
        {
            // Ensure audioSource is initialized
            if (audioSource == null)
            {
                audioSource = GetComponent<AudioSource>();
                if (audioSource == null)
                {
                    Debug.LogWarning("BGMusic: AudioSource component not found. Exiting coroutine.");
                    yield break;
                }
            }

            lastMenuMusicValue = SettingsManager.Instance.GetSetting<int>("MenuMusic");

            while (true)
            {
                switch (lastMenuMusicValue)
                {
                    case 0:
                        if (shuffleButton != null) shuffleButton.SetActive(false);
                        if (audioSource.isPlaying)
                        {
                            audioSource.Stop();
                            songName.text = "None";
                        }
                        break;
                    case 1:
                        if (shuffleButton != null) shuffleButton.SetActive(false);
                        if (!audioSource.isPlaying || audioSource.clip != menuMusicClip)
                        {
                            audioSource.clip = menuMusicClip;
                            audioSource.loop = true;
                            audioSource.Play();
                            songName.text = "grncd - YASG Menu";
                        }
                        break;
                    case 2:
                        string downloadsPath = Path.Combine(PlayerPrefs.GetString("dataPath"), "downloads");
                        if (Directory.Exists(downloadsPath))
                        {
                            string[] musicFiles = Directory.GetFiles(downloadsPath, "*.mp3");
                            if (musicFiles.Length > 0)
                            {
                                shuffleButton.SetActive(true);
                                string randomFile = musicFiles[UnityEngine.Random.Range(0, musicFiles.Length)];
                                using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip("file://" + randomFile, AudioType.MPEG))
                                {
                                    yield return www.SendWebRequest();

                                    if (www.result == UnityWebRequest.Result.Success)
                                    {
                                        AudioClip clip = DownloadHandlerAudioClip.GetContent(www);
                                        audioSource.clip = clip;
                                        audioSource.loop = false;
                                        audioSource.Play();
                                        songName.text = Path.GetFileNameWithoutExtension(randomFile);
                                        yield return new WaitForSeconds(clip.length);
                                    }
                                }
                            }
                            else
                            {
                                if (!audioSource.isPlaying)
                                {
                                    if (shuffleButton != null) shuffleButton.SetActive(false);
                                    audioSource.clip = menuMusicClip;
                                    audioSource.loop = true;
                                    audioSource.Play();
                                    songName.text = "grncd - YASG Menu";
                                }
                            }
                        }else
                        {
                            if (!audioSource.isPlaying)
                            {
                                if (shuffleButton != null) shuffleButton.SetActive(false);
                                audioSource.clip = menuMusicClip;
                                audioSource.loop = true;
                                audioSource.Play();
                                songName.text = "grncd - YASG Menu";
                            }
                        }
                        break;
                }
                yield return null;
            }
        }
    }

    public void PreviewSong(string trackIdOrUrl)
    {
        string trackId = trackIdOrUrl;
        if (trackId.Contains("spotify.com/track/"))
        {
            int trackIndex = trackId.LastIndexOf('/') + 1;
            trackId = trackId.Substring(trackIndex);
            
            int queryIndex = trackId.IndexOf('?');
            if (queryIndex != -1)
            {
                trackId = trackId.Substring(0, queryIndex);
            }
        }

        if (previewSongCoroutine != null)
        {
            StopCoroutine(previewSongCoroutine);
        }
        previewSongCoroutine = StartCoroutine(PreviewSongCoroutine(trackId));
    }

    public void StopPreview()
    {
        // Stop any preview coroutine immediately
        if (previewSongCoroutine != null)
        {
            StopCoroutine(previewSongCoroutine);
            previewSongCoroutine = null;
        }

        // Immediately stop preview audio to avoid overlapping with BG music
        if (previewAudioSource != null && previewAudioSource.isPlaying)
        {
            previewAudioSource.Stop();
        }

        // Start fade/cleanup routine (will handle nulls safely)
        StartCoroutine(StopPreviewCoroutine());
    }

    IEnumerator PreviewSongCoroutine(string trackId)
    {
        // 1. Get URL from SpotifyFetcher.
        // This assumes you have a SpotifyFetcher component in your scene
        // and a GetPreviewUrl method that takes a callback.
        string previewUrl = null;
        var spotifyFetcher = FindObjectOfType<SpotifyFetcher>();
        if (spotifyFetcher == null)
        {
            Debug.LogError("SpotifyFetcher not found in scene.");
            yield break;
        }
        yield return spotifyFetcher.GetPreviewUrl(trackId, url => previewUrl = url);

        if (string.IsNullOrEmpty(previewUrl))
        {
            Debug.LogError("Could not retrieve song preview URL.");
            yield break;
        }

        // 2. Download the content as a temporary .mp3 file.
        tempPreviewFilePath = Path.Combine(Application.temporaryCachePath, "song_preview.mp3");
        using (UnityWebRequest www = UnityWebRequest.Get(previewUrl))
        {
            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Failed to download preview song: {www.error}");
                yield break;
            }
            File.WriteAllBytes(tempPreviewFilePath, www.downloadHandler.data);
        }

        AudioClip previewClip;
        using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip("file://" + tempPreviewFilePath, AudioType.MPEG))
        {
            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Failed to load preview audio clip: {www.error}");
                yield break;
            }
            previewClip = DownloadHandlerAudioClip.GetContent(www);
        }

        // 3. Fade out BGMusic and play the preview simultaneously.
        StartCoroutine(FadeAudio(audioSource, 0.5f, 0f));

        previewAudioSource.clip = previewClip;
        previewAudioSource.volume = 0f;
        previewAudioSource.Play();
        LevelResourcesCompiler.Instance.RemoveLoadingTint();
        yield return StartCoroutine(FadeAudio(previewAudioSource, 0.5f, 0.26f));

        // Loop with fade
        while (true)
        {
            float timeToWait = previewAudioSource.clip.length - 1f;
            if (timeToWait > 0)
            {
                yield return new WaitForSeconds(timeToWait);
            }

            yield return StartCoroutine(FadeAudio(previewAudioSource, 0.5f, 0f));
            
            previewAudioSource.time = 0f;
            previewAudioSource.Play();
            
            yield return StartCoroutine(FadeAudio(previewAudioSource, 0.5f, 0.26f));
        }
    }

    IEnumerator StopPreviewCoroutine()
    {
        // Fade out the preview song and fade in the BG music simultaneously
        Coroutine fadeOutPreview = StartCoroutine(FadeAudio(previewAudioSource, 0.5f, 0f));
        StartCoroutine(FadeAudio(audioSource, 0.5f, bgMusicVolume));

        // Wait for the preview to finish fading out before we stop it and delete the file
        yield return fadeOutPreview;

        previewAudioSource.Stop();
        previewAudioSource.clip = null;

        // Delete the temporary file
        if (!string.IsNullOrEmpty(tempPreviewFilePath) && File.Exists(tempPreviewFilePath))
        {
            File.Delete(tempPreviewFilePath);
            tempPreviewFilePath = null;
        }
    }

    IEnumerator FadeAudio(AudioSource source, float duration, float targetVolume)
    {
        float currentTime = 0;
        float startVolume = source.volume;

        while (currentTime < duration)
        {
            currentTime += Time.deltaTime;
            source.volume = Mathf.Lerp(startVolume, targetVolume, currentTime / duration);
            yield return null;
        }
        source.volume = targetVolume;
    }
}