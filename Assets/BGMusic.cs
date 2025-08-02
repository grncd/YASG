using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using UnityEngine.Networking;
using TMPro;
using UnityEngine.SceneManagement;

public class BGMusic : MonoBehaviour
{
    public AudioClip menuMusicClip;
    private AudioSource audioSource;
    private int lastMenuMusicValue = -1;
    public TextMeshProUGUI songName;
    private GameObject shuffleButton;

    void Start()
    {
        songName = GameObject.Find("CurrentlyPlayingSong").GetComponent<TextMeshProUGUI>();
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

    private void OnActiveSceneChanged(Scene preivousScene, Scene newScene)
    {
        if(newScene.name == "Menu")
        {
            songName = GameObject.Find("CurrentlyPlayingSong").GetComponent<TextMeshProUGUI>();
            shuffleButton = songName.transform.parent.GetChild(4).gameObject;
            StopAllCoroutines();
            StartCoroutine(MusicPlayerCoroutine());
        }
    }

    void Update()
    {
        int currentMenuMusicValue = SettingsManager.Instance.GetSetting<int>("MenuMusic");
        if (currentMenuMusicValue != lastMenuMusicValue)
        {
            StopAllCoroutines();
            StartCoroutine(MusicPlayerCoroutine());
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
        lastMenuMusicValue = SettingsManager.Instance.GetSetting<int>("MenuMusic");

        while (true)
        {
            switch (lastMenuMusicValue)
            {
                case 0:
                    if(shuffleButton != null) shuffleButton.SetActive(false);
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
                        songName.text = "ivvys - unfinished 2";
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
                            string randomFile = musicFiles[Random.Range(0, musicFiles.Length)];
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
                                songName.text = "ivvys - unfinished 2";
                            }
                        }
                    }
                    break;
            }
            yield return null;
        }
    }
}

