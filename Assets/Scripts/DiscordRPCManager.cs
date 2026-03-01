using UnityEngine;
using UnityEngine.SceneManagement;
using System;

#if UNITY_STANDALONE
using DiscordRPC;
using DiscordRPC.Logging;
#endif

public class DiscordRPCManager : MonoBehaviour
{
    public static DiscordRPCManager Instance { get; private set; }

    private const string APPLICATION_ID = "1477705481303756810";
    private const float UPDATE_INTERVAL = 4f;

    private float _timer;
    private string _lastDetails;
    private string _lastState;
    private bool _lastHasTimestamp;
    private double _lastStartOffset;

#if UNITY_STANDALONE
    private DiscordRpcClient _client;
#endif

    private void Awake()
    {
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

#if !UNITY_STANDALONE
        enabled = false;
        return;
#else
        if (SettingsManager.Instance.GetSetting<bool>("DiscordRPC", true))
            InitializeClient();
#endif
    }

#if UNITY_STANDALONE
    private void InitializeClient()
    {
        try
        {
            _client = new DiscordRpcClient(APPLICATION_ID)
            {
                Logger = new ConsoleLogger { Level = LogLevel.Warning }
            };
            _client.OnReady += (sender, e) =>
            {
                Debug.Log($"[DiscordRPC] Connected to user {e.User.Username}");
            };
            bool loggedConnectionFail = false;
            _client.OnConnectionFailed += (sender, e) =>
            {
                if (!loggedConnectionFail)
                {
                    Debug.Log("[DiscordRPC] Connection failed — Discord not running?");
                    loggedConnectionFail = true;
                }
            };
            _client.Initialize();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[DiscordRPC] Failed to initialize: {ex.Message}");
            _client = null;
            enabled = false;
        }
    }

    private void Update()
    {
        if (_client == null || _client.IsDisposed) return;

        _timer += Time.unscaledDeltaTime;
        if (_timer < UPDATE_INTERVAL) return;
        _timer = 0f;

        UpdatePresence();
    }

    private void UpdatePresence()
    {
        string sceneName = SceneManager.GetActiveScene().name;
        string details;
        string state;
        bool hasTimestamp = false;
        double startOffset = 0;

        switch (sceneName)
        {
            case "Setup":
                details = "Setting up";
                state = "First time setup";
                break;

            case "Menu":
                if (LevelResourcesCompiler.Instance != null && LevelResourcesCompiler.Instance.compiling)
                {
                    details = "Preparing a song";
                    state = "Downloading & processing";
                }
                else
                {
                    details = "In the menu";
                    state = "Browsing songs";
                }
                break;

            case "Main":
                string songName = PlayerPrefs.GetString("currentSongDisplay", "");
                string modeStr = GetModeString();

                if (AudioClipPitchProcessor.Instance != null && AudioClipPitchProcessor.Instance.paused)
                {
                    details = string.IsNullOrEmpty(songName) ? "Paused" : $"Paused: {songName}";
                    state = modeStr;
                }
                else if (LyricsHandler.Instance != null && LyricsHandler.Instance.songOver)
                {
                    details = string.IsNullOrEmpty(songName) ? "Finishing up" : $"Finishing: {songName}";
                    state = modeStr;
                }
                else
                {
                    details = string.IsNullOrEmpty(songName) ? "Singing" : $"Singing: {songName}";
                    state = modeStr;
                    if (AudioClipPitchProcessor.Instance != null &&
                        AudioClipPitchProcessor.Instance.audioSource != null &&
                        AudioClipPitchProcessor.Instance.audioSource.isPlaying)
                    {
                        hasTimestamp = true;
                        startOffset = AudioClipPitchProcessor.Instance.audioSource.time;
                    }
                }
                break;

            case "Results":
            {
                string resultsSong = PlayerPrefs.GetString("currentSongDisplay", "");
                bool isMP = PlayerPrefs.GetInt("multiplayer") == 1;
                details = string.IsNullOrEmpty(resultsSong) ? "Viewing results" : $"Viewing results: {resultsSong}";
                state = isMP ? "Multiplayer" : "Solo";
                break;
            }

            default:
                details = "In game";
                state = "";
                break;
        }

        // Only update if something changed
        if (details == _lastDetails && state == _lastState &&
            hasTimestamp == _lastHasTimestamp &&
            (!hasTimestamp || Math.Abs(startOffset - _lastStartOffset) < 2.0))
        {
            return;
        }

        _lastDetails = details;
        _lastState = state;
        _lastHasTimestamp = hasTimestamp;
        _lastStartOffset = startOffset;

        var presence = new RichPresence
        {
            Details = details,
            State = state,
            Assets = new Assets
            {
                LargeImageKey = "yasglogo",
                LargeImageText = "YASG"
            }
        };

        if (hasTimestamp)
        {
            presence.Timestamps = new Timestamps
            {
                Start = DateTime.UtcNow - TimeSpan.FromSeconds(startOffset)
            };
        }

        try
        {
            _client.SetPresence(presence);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[DiscordRPC] Failed to set presence: {ex.Message}");
        }
    }

    private string GetModeString()
    {
        bool isMP = PlayerPrefs.GetInt("multiplayer") == 1;
        int diff = PlayerPrefs.GetInt("Player1Difficulty", 1);
        string diffStr = diff switch
        {
            0 => "Easy",
            1 => "Medium",
            2 => "Hard",
            _ => "Medium"
        };
        return isMP ? $"Multiplayer | {diffStr}" : $"Solo | {diffStr}";
    }

    public void SetEnabled(bool on)
    {
        if (on)
        {
            if (_client == null || _client.IsDisposed)
                InitializeClient();
        }
        else
        {
            DisposeClient();
        }
    }

    private void OnApplicationQuit()
    {
        DisposeClient();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            DisposeClient();
            Instance = null;
        }
    }

    private void DisposeClient()
    {
        if (_client != null && !_client.IsDisposed)
        {
            try
            {
                _client.ClearPresence();
                _client.Dispose();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DiscordRPC] Error during dispose: {ex.Message}");
            }
            _client = null;
        }
    }
#endif
}
