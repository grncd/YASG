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
        // 1. Stop all coroutines immediately so no background logic continues from previous scene
        StopAllCoroutines();

        // 1.5 Stop the main BG music immediately (otherwise it keeps playing if coroutine is killed)
        if (audioSource != null)
        {
            audioSource.Stop();
        }

        // 2. Force stop preview audio and clean up ONLY if we are returning to the Menu (e.g. disconnect)
        // If we are going to Main (starting game), we presumably want it to keep playing (or let Main handle it).
        if (newScene.name == "Menu")
        {
            if (previewAudioSource != null)
            {
                previewAudioSource.Stop();
                previewAudioSource.clip = null;
            }

            // 3. Clean up temporary preview file
            if (!string.IsNullOrEmpty(tempPreviewFilePath) && File.Exists(tempPreviewFilePath))
            {
                try
                {
                    File.Delete(tempPreviewFilePath);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"BGMusic: Failed to delete temp preview file: {e.Message}");
                }
                tempPreviewFilePath = null;
            }
        }

        // 4. Reset state/references for the new scene
        if (newScene.name == "Menu")
        {
            // Ensure bg volumes are sane
            if (previewAudioSource != null) previewAudioSource.volume = 0.143f;
            if (audioSource != null) audioSource.volume = bgMusicVolume;

            killSwitch = false;

            // Re-acquire references (Fragile hierarchy check, keeping as is)
            var canvas = GameObject.Find("Canvas");
            if (canvas != null)
            {
                // Verify hierarchy exists before accessing to prevent NREs
                Transform t = canvas.transform;
                if (t.childCount > 2)
                {
                    Transform audioPanel = t.GetChild(2);
                    if (audioPanel.childCount > 7)
                    {
                        Transform songInfo = audioPanel.GetChild(7);
                        if (songInfo.childCount > 2)
                        {
                            songName = songInfo.GetChild(2).GetComponent<TextMeshProUGUI>();
                        }
                        if (audioPanel.childCount > 4)
                        {
                            shuffleButton = audioPanel.GetChild(4).gameObject;
                        }
                    }
                }
            }

            // Fallback if references failed (optional, but good practice)? 
            // Existing code didn't check nulls, so it might have thrown exceptions. 
            // I'll keep it safer but if I can't find it, I proceed.
            // Actually, to be safe and closest to original:
            try
            {
                songName = GameObject.Find("Canvas").transform.GetChild(2).GetChild(7).GetChild(2).GetComponent<TextMeshProUGUI>();
                shuffleButton = songName.transform.parent.GetChild(4).gameObject;
            }
            catch { }


            StartCoroutine(MusicPlayerCoroutine());
        }
        else
        {
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
                                        // Wait until the song actually finishes playing
                                        yield return new WaitWhile(() => audioSource.isPlaying);
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
            float clipLength = previewAudioSource.clip.length;
            float fadeDuration = 1.0f; // 1 second fade
            // Wait until fadeDuration before the end, so the fade-out finishes right as the clip would end
            float playDuration = clipLength - (fadeDuration * 2f);

            if (playDuration > 0)
            {
                yield return new WaitForSeconds(playDuration);
            }

            // Fade out (audio keeps playing during fade, so we finish before clip ends)
            yield return StartCoroutine(FadeAudio(previewAudioSource, fadeDuration, 0f));

            // Restart smoothly
            previewAudioSource.time = 0f;
            previewAudioSource.Play();

            // Fade back in
            yield return StartCoroutine(FadeAudio(previewAudioSource, fadeDuration, 0.26f));
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