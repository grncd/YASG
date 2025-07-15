using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using TMPro;
using FishNet.Connection; // Required for OnStartServer
using System.IO;
using UnityEngine.Networking;
using System.Collections;
using System.Threading.Tasks;
using System.Collections.Generic;
using System;
using System.Linq;

public class PlayerData : NetworkBehaviour
{
    #region Static and Inspector References
    public static PlayerData LocalPlayerInstance { get; private set; }

    [Tooltip("Optional: A TextMeshProUGUI to display the player's name above their character.")]
    public TextMeshProUGUI playerNameDisplay;
    #endregion

    #region SyncVars
    public readonly SyncVar<string> PlayerName = new SyncVar<string>(""); 
    public readonly SyncVar<int> Level = new SyncVar<int>(1);
    public readonly SyncVar<float> PercentageToNextLevel = new SyncVar<float>();
    public readonly SyncVar<int> TotalScore = new SyncVar<int>();
    public readonly SyncVar<int> Difficulty = new SyncVar<int>();
    public readonly SyncVar<bool> IsHost = new SyncVar<bool>();
    public readonly SyncVar<bool> IsMasterProcessor = new SyncVar<bool>();
    public readonly SyncVar<bool> IsReady = new SyncVar<bool>(false);
    public readonly SyncVar<bool> IsGameReady = new SyncVar<bool>(false);
    public readonly SyncVar<int> CurrentGameScore = new SyncVar<int>();
    public readonly SyncVar<int> Perfects = new SyncVar<int>();
    public readonly SyncVar<int> Greats = new SyncVar<int>();
    public readonly SyncVar<int> Mehs = new SyncVar<int>();
    public readonly SyncVar<int> Stars = new SyncVar<int>();
    public readonly SyncVar<int> Placement = new SyncVar<int>();
    #endregion

    private SimpleHttpFileServer _httpServer;

    #region Network Callbacks (OnStart/OnStop)
    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        // Subscribe to all SyncVar changes to trigger update methods
        PlayerName.OnChange += OnPlayerNameChanged;
        Level.OnChange += OnLevelChanged;
        PercentageToNextLevel.OnChange += OnPercentageChanged;
        TotalScore.OnChange += OnTotalScoreChanged;
        Difficulty.OnChange += OnDifficultyChanged;
        IsHost.OnChange += OnIsHostChanged;
        IsMasterProcessor.OnChange += OnIsMasterProcessorChanged;
        IsReady.OnChange += OnIsReadyChanged;
        IsGameReady.OnChange += OnIsGameReadyChanged;
        CurrentGameScore.OnChange += OnCurrentGameScoreChanged;
        Perfects.OnChange += OnPerfectsChanged;
        Greats.OnChange += OnGreatsChanged;
        Mehs.OnChange += OnMehsChanged;
        Stars.OnChange += OnStarsChanged;
        Placement.OnChange += OnPlacementChanged;
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        // Unsubscribe from all SyncVar changes to prevent memory leaks
        PlayerName.OnChange -= OnPlayerNameChanged;
        Level.OnChange -= OnLevelChanged;
        PercentageToNextLevel.OnChange -= OnPercentageChanged;
        TotalScore.OnChange -= OnTotalScoreChanged;
        Difficulty.OnChange -= OnDifficultyChanged;
        IsHost.OnChange -= OnIsHostChanged;
        IsMasterProcessor.OnChange -= OnIsMasterProcessorChanged;
        IsReady.OnChange -= OnIsReadyChanged;
        IsGameReady.OnChange -= OnIsGameReadyChanged;
        CurrentGameScore.OnChange -= OnCurrentGameScoreChanged;
        Perfects.OnChange -= OnPerfectsChanged;
        Greats.OnChange -= OnGreatsChanged;
        Mehs.OnChange -= OnMehsChanged;
        Stars.OnChange -= OnStarsChanged;
        Placement.OnChange -= OnPlacementChanged;
    }

    private void Awake()
    {
        // Ensure the player has the file server component
        _httpServer = GetComponent<SimpleHttpFileServer>();
        if (_httpServer == null)
        {
            _httpServer = gameObject.AddComponent<SimpleHttpFileServer>();
        }
    }

    private void OnCurrentGameScoreChanged(int oldScore, int newScore, bool asServer)
    {
        // This event is useful for triggering UI updates on all clients.
        // We will use a manager to handle this, but it's good to have.
        Debug.Log($"Player '{PlayerName.Value}' current game score changed to {newScore}");
    }

    [ServerRpc(RequireOwnership = true)]
    public void RequestUpdateCurrentScore_ServerRpc(int newScore)
    {
        // The server receives the new total score from the client and updates the SyncVar.
        // For a competitive game, you might add anti-cheat validation here,
        // but for now, we trust the client's calculation.
        CurrentGameScore.Value = newScore;
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        if (Owner.IsHost)
        {
            Debug.Log($"Player {Owner.ClientId} is the host. Assigning Host and Master Processor roles.");
            IsHost.Value = true;
            IsMasterProcessor.Value = true;
        }
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        if (base.IsOwner)
        {
            LocalPlayerInstance = this;
            Debug.Log("PlayerData: Local PlayerData spawned and ready. Setting static instance.");

            // --- THIS IS THE MAJOR CHANGE ---
            // Request initialization using the full profile data.
            if (ProfileManager.Instance != null && ProfileManager.Instance.GetActiveProfiles().Count > 0)
            {
                // Get the local profile
                var localProfile = ProfileManager.Instance.GetActiveProfiles()[0];

                Debug.Log($"PlayerData: Requesting initialization with local profile data for '{localProfile.name}'.");

                // Call the new, more comprehensive RPC with all the data
                RequestInitializeData_ServerRpc(
                    localProfile.name,
                    localProfile.level,
                    localProfile.progressRemaining / 100f, // Assuming 'progressRemaining' is your 'percentage to next level'
                    localProfile.totalScore
                );
            }
            else
            {
                Debug.LogWarning("PlayerData: ProfileManager or active profiles not found. Requesting default initialization.");
                // Initialize with default values if no profile is found
                RequestInitializeData_ServerRpc("UnnamedPlayer", 1, 0f, 0);
            }
        }
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        if (LocalPlayerInstance == this)
        {
            LocalPlayerInstance = null;
        }
    }
    #endregion

    #region OnChange Callbacks
    // These methods are called on all clients whenever their corresponding SyncVar changes.
    // This is where you would update UI elements.

    private void OnPlayerNameChanged(string oldName, string newName, bool asServer)
    {
        gameObject.name = "Player_" + newName;
        if (playerNameDisplay != null)
        {
            playerNameDisplay.text = newName;
        }
        Debug.Log($"Player name changed to {newName}");
    }

    private void OnIsGameReadyChanged(bool oldVal, bool newVal, bool asServer)
    {
        Debug.Log($"Player '{PlayerName.Value}' GameReady status changed to {newVal}");
        // You could potentially use this for UI, but for now it's just for server logic.
    }

    private void OnLevelChanged(int oldLevel, int newLevel, bool asServer)
    {
        Debug.Log($"Player level changed to {newLevel}");
        // Example: Find a UI element and update it: UIManager.Instance.UpdatePlayerLevel(this, newLevel);
    }

    private void OnPercentageChanged(float oldPercent, float newPercent, bool asServer)
    {
        Debug.Log($"Player level percentage changed to {newPercent * 100f}%");
        // Example: UIManager.Instance.UpdatePlayerXPBar(this, newPercent);
    }

    private void OnTotalScoreChanged(int oldScore, int newScore, bool asServer)
    {
        Debug.Log($"Player total score changed to {newScore}");
        // Example: UIManager.Instance.UpdatePlayerScore(this, newScore);
    }

    private void OnDifficultyChanged(int oldDiff, int newDiff, bool asServer)
    {
        Debug.Log($"Player difficulty changed to {newDiff}");
        // Example: UIManager.Instance.UpdatePlayerScore(this, newScore);
    }

    private void OnIsHostChanged(bool oldStatus, bool newStatus, bool asServer)
    {
        Debug.Log($"Player host status changed to {newStatus}");
        // Example: Show/hide a "Host" crown icon next to the player's name
    }

    private void OnIsMasterProcessorChanged(bool oldStatus, bool newStatus, bool asServer)
    {
        Debug.Log($"Player master processor status changed to {newStatus}");
        // This is mostly for internal logic, but you could show a UI icon for it.
    }

    private void OnPlacementChanged(int oldPlacement, int newPlacement, bool asServer)
    {
        Debug.Log($"Player '{PlayerName.Value}' placement changed to {newPlacement}");
    }


    private void OnIsReadyChanged(bool oldStatus, bool newStatus, bool asServer)
    {
        Debug.Log($"Player '{PlayerName.Value}' ready status changed to {newStatus}");
        // Example: Update the UI to show a green checkmark or change the color of the player's nameplate.
        // UIManager.Instance.UpdatePlayerReadyIndicator(this, newStatus);
    }

    private void OnPerfectsChanged(int oldPerfects, int newPerfects, bool asServer)
    {
        Debug.Log($"Player perfects changed to {newPerfects}");
        // Example: UIManager.Instance.UpdatePlayerScore(this, newScore);
    }

    private void OnGreatsChanged(int oldPerfects, int newPerfects, bool asServer)
    {
        Debug.Log($"Player greats changed to {newPerfects}");
        // Example: UIManager.Instance.UpdatePlayerScore(this, newScore);
    }

    private void OnMehsChanged(int oldPerfects, int newPerfects, bool asServer)
    {
        Debug.Log($"Player mehs changed to {newPerfects}");
        // Example: UIManager.Instance.UpdatePlayerScore(this, newScore);
    }

    private void OnStarsChanged(int oldPerfects, int newPerfects, bool asServer)
    {
        Debug.Log($"Player stars changed to {newPerfects}");
        // Example: UIManager.Instance.UpdatePlayerScore(this, newScore);
    }
    #endregion

    #region ServerRPCs (Client-to-Server Requests)

    // --- RENAMED and EXPANDED RPC for initialization ---
    [ServerRpc(RequireOwnership = true)]
    public void RequestInitializeData_ServerRpc(string name, int level, float progress, int score)
    {
        Debug.Log($"Server: Received initialization data for client {Owner.ClientId}. Name: {name}, Level: {level}, Score: {score}");

        // The server now sets all the initial values for this player.
        // This is authoritative; the server is the source of truth.
        PlayerName.Value = name;
        Level.Value = level;
        PercentageToNextLevel.Value = progress;
        TotalScore.Value = score;
    }

    [ServerRpc(RequireOwnership = true)]
    public void RequestReportGameReady_ServerRpc()
    {
        Debug.Log($"Server received GameReady report from client {Owner.ClientId}.");

        // Set this player's status
        this.IsGameReady.Value = true;

        // Tell the RoomManager to check if everyone is ready now.
        if (RoomManager.Instance != null)
        {
            RoomManager.Instance.CheckIfAllPlayersAreGameReady_Server();
        }
    }

    [ObserversRpc]
    public void StartCountdown_ObserversRpc()
    {
        Debug.Log("Received RPC to start the game countdown.");
        // Find our local audio processor and tell it to start the final countdown.
        AudioClipPitchProcessor.Instance?.StartFinalCountdown();
    }


    [ServerRpc(RequireOwnership = true)]
    public void RequestSetFinalPlacements_ServerRpc()
    {
        // SECURITY: Only the host should be able to trigger final placement calculation.
        if (!this.IsHost.Value) return;

        Debug.Log("Host is setting final placements for all players.");

        // Find all players and sort them by their final game score.
        List<PlayerData> sortedPlayers = FindObjectsOfType<PlayerData>()
            .OrderByDescending(p => p.CurrentGameScore.Value)
            .ToList();

        // Iterate through the sorted list and set the Placement SyncVar for each player.
        for (int i = 0; i < sortedPlayers.Count; i++)
        {
            PlayerData rankedPlayer = sortedPlayers[i];
            int placement = i + 1; // 1st, 2nd, 3rd...
            rankedPlayer.Placement.Value = placement;
        }
    }

    [ServerRpc(RequireOwnership = true)]
    public void RequestSetRoomName_ServerRpc(string newRoomName)
    {
        if (RoomManager.Instance != null)
        {
            RoomManager.Instance.SetRoomName_Server(newRoomName, this.PlayerName.Value);
        }
    }

    [ServerRpc(RequireOwnership = true)]
    public void RequestAddScore_ServerRpc(int amount)
    {
        TotalScore.Value += amount;
        // You could add logic here to update Level and PercentageToNextLevel based on the new score.
    }

    [ServerRpc(RequireOwnership = true)]
    public void RequestChangeDiff_ServerRpc(int diff)
    {
        Difficulty.Value = diff;
    }

    [ServerRpc(RequireOwnership = true)]
    public void RequestUpdatePerformanceStats_ServerRpc(int perfects, int greats, int mehs, int stars)
    {
        Debug.Log($"Server receiving final stats for player {PlayerName.Value}: P:{perfects}, G:{greats}, M:{mehs}, S:{stars}");
        this.Perfects.Value = perfects;
        this.Greats.Value = greats;
        this.Mehs.Value = mehs;
        this.Stars.Value = stars;
    }

    private int GetRequiredXPForLevel(int level)
    {
        return Mathf.RoundToInt(1350000f + 500000f * (level - 1));
    }


    // --- Add this NEW ServerRpc to the RPC region ---
    // This will be called by the host at the end of a song.
    [ServerRpc(RequireOwnership = true)]
    public void RequestFinalXPCalculation_ServerRpc()
    {
        // SECURITY: Only the host should be able to trigger the end-of-game calculations.
        if (!this.IsHost.Value)
        {
            Debug.LogWarning($"Client {Owner.ClientId} tried to finalize XP, but is not the host.");
            return;
        }

        Debug.Log("Host is finalizing XP and levels for all players.");

        // The host iterates through EVERY connected player on the server.
        foreach (var playerConn in ServerManager.Clients.Values)
        {
            if (playerConn.FirstObject == null) continue;

            PlayerData targetPlayer = playerConn.FirstObject.GetComponent<PlayerData>();
            if (targetPlayer != null)
            {
                // --- This is your level-up logic, now running authoritatively on the server ---

                // Add the game score to the player's cumulative total score.
                targetPlayer.TotalScore.Value += targetPlayer.CurrentGameScore.Value;

                // Calculate total required XP for all levels UP TO the player's current level.
                int totalRequiredForPreviousLevels = 0;
                for (int lvl = 1; lvl < targetPlayer.Level.Value; lvl++)
                {
                    totalRequiredForPreviousLevels += GetRequiredXPForLevel(lvl);
                }

                int requiredForThisLevel = GetRequiredXPForLevel(targetPlayer.Level.Value);

                // Handle multiple level-ups in a loop.
                while (targetPlayer.TotalScore.Value >= totalRequiredForPreviousLevels + requiredForThisLevel)
                {
                    targetPlayer.Level.Value++; // Level up!
                    totalRequiredForPreviousLevels += requiredForThisLevel;
                    requiredForThisLevel = GetRequiredXPForLevel(targetPlayer.Level.Value);
                }

                // Calculate progress percentage for the new current level.
                int xpIntoCurrentLevel = targetPlayer.TotalScore.Value - totalRequiredForPreviousLevels;
                targetPlayer.PercentageToNextLevel.Value = ((float)xpIntoCurrentLevel / requiredForThisLevel); // Store as 0.0-1.0

                Debug.Log($"Finalized stats for {targetPlayer.PlayerName.Value}: Level {targetPlayer.Level.Value}, Progress {targetPlayer.PercentageToNextLevel.Value * 100f}%");
            }
        }
    }


    [ServerRpc(RequireOwnership = true)]
    public void RequestSetReadyStatus_ServerRpc(bool isReady)
    {
        IsReady.Value = isReady;
    }

    [ServerRpc(RequireOwnership = true)]
    public void RequestTransferHost_ServerRpc(NetworkObject newHostPlayer)
    {
        if (!this.IsHost.Value) return;
        PlayerData targetPlayerData = newHostPlayer.GetComponent<PlayerData>();
        if (targetPlayerData == null) return;
        this.IsHost.Value = false;
        targetPlayerData.IsHost.Value = true;
    }

    [ServerRpc(RequireOwnership = true)]
    public void RequestTransferMasterProcessor_ServerRpc(NetworkObject newMasterPlayer)
    {
        if (!this.IsMasterProcessor.Value) return;
        PlayerData targetPlayerData = newMasterPlayer.GetComponent<PlayerData>();
        if (targetPlayerData == null) return;
        this.IsMasterProcessor.Value = false;
        targetPlayerData.IsMasterProcessor.Value = true;
    }

    [ServerRpc(RequireOwnership = true)]
    public void RequestChangeSong_ServerRpc(SongData newSongData)
    {
        // SECURITY CHECK: Only the host can change the song.
        if (!this.IsHost.Value)
        {
            Debug.LogWarning($"Client {Owner.ClientId} tried to change the song, but they are not the host.");
            return;
        }

        if (RoomManager.Instance != null)
        {
            RoomManager.Instance.SetSelectedSong_Server(newSongData);
        }
    }

    #endregion

    #region Game Start Logic

    // [ClientRpc] - The server tells all clients to prepare for loading.
    [ObserversRpc]
    public void PrepareToLoad_ClientRpc()
    {
        Debug.Log("Received PrepareToLoad RPC from server.");

        // Everyone calls BeginLoading()
        if (LevelResourcesCompiler.Instance != null)
        {
            LevelResourcesCompiler.Instance.BeginLoading();
        }
        else
        {
            Debug.LogError("LevelResourcesCompiler.Instance is null! Cannot begin loading.");
            return;
        }

        // Check if WE are the master processor
        if (base.IsOwner && this.IsMasterProcessor.Value) // More robust check: must be our own object
        {
            Debug.Log("I am the Master Processor. Starting compile process.");
            SongData songToCompile = RoomManager.Instance.SelectedSong.Value;

            // Start the compile process. We no longer need a coroutine here
            // if the background task is handled correctly.
            CompileAndReport(songToCompile);
        }
        else
        {
            Debug.Log("I am not the Master Processor. Waiting for download info.");
            if (LevelResourcesCompiler.Instance.status != null)
            {
                LevelResourcesCompiler.Instance.loadingCanvas.SetActive(true);
                LevelResourcesCompiler.Instance.loadingSecond.SetActive(true);
                LevelResourcesCompiler.Instance.loadingFirst.SetActive(false);
                LevelResourcesCompiler.Instance.status.text = "Waiting for Master Processor to finish compiling...";
            }
        }
    }

    // This method runs the background task and reports back.
    private async void CompileAndReport(SongData song)
    {
        // --- STEP 1: Run compilation (no change) ---
        await LevelResourcesCompiler.Instance.StartCompile(song.Link, song.Title, song.Artist, song.Length, song.CoverUrl);

        // --- STEP 2: Get all three file paths ---
        string fullLocation = PlayerPrefs.GetString("fullLocation");
        string vocalLocation = PlayerPrefs.GetString("vocalLocation");

        // --- NEW: Construct the LRC file path ---
        string charactersToRemovePattern = @"[/\\:*?""<>|]";
        string currentSong = PlayerPrefs.GetString("currentSong");
        string cleanSongName = System.Text.RegularExpressions.Regex.Replace(currentSong, charactersToRemovePattern, string.Empty);
        string lrcLocation = Path.Combine(PlayerPrefs.GetString("dataPath"), "downloads", cleanSongName + ".txt");

        // Basic validation
        if (string.IsNullOrEmpty(fullLocation) || string.IsNullOrEmpty(vocalLocation) || !File.Exists(lrcLocation))
        {
            Debug.LogError($"Compilation finished, but one or more required files are missing! Full: {fullLocation}, Vocal: {vocalLocation}, LRC: {lrcLocation}");
            return;
        }

        // Add all three files to the list to be served.
        List<string> filesToServe = new List<string> { fullLocation, vocalLocation, lrcLocation };

        // --- STEP 3: Start the server (no change) ---
        bool serverStarted = _httpServer.StartServer(filesToServe);
        if (!serverStarted) { /* ... */ return; }

        // --- STEP 4: Report all three filenames to the main server ---
        string fullFileName = Path.GetFileName(fullLocation);
        string vocalFileName = Path.GetFileName(vocalLocation);
        string lrcFileName = Path.GetFileName(lrcLocation); // Get the LRC filename

        Debug.Log("CompileAndReport: Reporting all three file names to server.");
        RequestReportCompilationFinished_ServerRpc(fullFileName, vocalFileName, lrcFileName);
        Debug.Log("Master Processor finished its tasks and is reporting ready status.");
        RequestReportReadyForSceneChange_ServerRpc();
    }


    [ServerRpc(RequireOwnership = true)]
    public void RequestReportCompilationFinished_ServerRpc(string fullFileName, string vocalFileName, string lrcFileName) // Add lrcFileName
    {
        if (!this.IsMasterProcessor.Value) return;

        Debug.Log($"Server received compilation finished report from {Owner.ClientId}.");
        string masterProcessorIp = Owner.GetAddress();

        // Pass the lrcFileName to the broadcast method
        RoomManager.Instance.BroadcastDownloadInfo_Server(masterProcessorIp, fullFileName, vocalFileName, lrcFileName);
    }


    // [ClientRpc] - The server tells non-master clients to download the files.
    [ObserversRpc]
    public void DownloadFiles_ObserversRpc(string masterIp, string fullFileName, string vocalFileName, string lrcFileName) // Add lrcFileName
    {
        if (this.IsMasterProcessor.Value) return;

        Debug.Log($"Received RPC to download files from {masterIp}.");
        StartCoroutine(DownloadFiles_Coroutine(fullFileName, vocalFileName, lrcFileName)); // Pass it to the coroutine
    }

    private IEnumerator DownloadFiles_Coroutine(string fullFileName, string vocalFileName, string lrcFileName)
    {
        string dataPath = PlayerPrefs.GetString("dataPath");
        string masterIp = PlayerPrefs.GetString("masterIp");

        // --- Construct Save Paths ---
        string fullSavePath = Path.Combine(dataPath, fullFileName);

        string vocalSaveDir = Path.Combine(dataPath, "output", "htdemucs");
        Directory.CreateDirectory(vocalSaveDir);
        string vocalSavePath = Path.Combine(vocalSaveDir, vocalFileName);

        string lrcSaveDir = Path.Combine(dataPath, "downloads");
        Directory.CreateDirectory(lrcSaveDir);
        string lrcSavePath = Path.Combine(lrcSaveDir, lrcFileName);

        // --- NEW: Check if the .lrc file exists as a .txt file ---
        string lrcAsTxtPath = Path.ChangeExtension(lrcSavePath, ".txt");

        // --- NEW: Check if all files already exist ---
        if (File.Exists(fullSavePath) && File.Exists(vocalSavePath) && (File.Exists(lrcSavePath) || File.Exists(lrcAsTxtPath)))
        {
            Debug.Log("All necessary files already exist locally. Skipping download.");
            PlayerPrefs.SetInt("saved", 1);
            // --- FIX: Do not overwrite PlayerPrefs here. The correct paths are already set by the single-player download process. ---
            // PlayerPrefs.SetString("vocalLocation", vocalSavePath);
            // PlayerPrefs.SetString("fullLocation", fullSavePath);
            // Immediately report ready status
            if (PlayerData.LocalPlayerInstance != null)
            {
                PlayerData.LocalPlayerInstance.RequestReportReadyForSceneChange_ServerRpc();
            }
            else
            {
                Debug.LogError("Could not report ready status because LocalPlayerInstance was null!");
            }

            // Exit the coroutine since no download is needed.
            yield break;
        }
        PlayerPrefs.SetInt("saved", 0);
        // If we reach here, at least one file is missing, so we proceed with downloading.

        // --- Construct Download URLs ---
        string fullUrl = $"http://{masterIp}:8080/{Uri.EscapeDataString(fullFileName)}";
        string vocalUrl = $"http://{masterIp}:8080/{Uri.EscapeDataString(vocalFileName)}";
        string lrcUrl = $"http://{masterIp}:8080/{Uri.EscapeDataString(lrcFileName)}";

        // --- Download Full Song (only if it doesn't exist) ---
        if (!File.Exists(fullSavePath))
        {
            LevelResourcesCompiler.Instance.status.text = $"Downloading full song...";
            UnityWebRequest fullRequest = new UnityWebRequest(fullUrl, UnityWebRequest.kHttpVerbGET)
            {
                downloadHandler = new DownloadHandlerFile(fullSavePath)
            };
            yield return fullRequest.SendWebRequest();
            if (fullRequest.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Failed to download full song: {fullRequest.error}");
                yield break;
            }
        }

        // --- Download Vocal Track (only if it doesn't exist) ---
        if (!File.Exists(vocalSavePath))
        {
            LevelResourcesCompiler.Instance.status.text = $"Downloading vocal track...";
            UnityWebRequest vocalRequest = new UnityWebRequest(vocalUrl, UnityWebRequest.kHttpVerbGET)
            {
                downloadHandler = new DownloadHandlerFile(vocalSavePath)
            };
            yield return vocalRequest.SendWebRequest();
            if (vocalRequest.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Failed to download vocal track: {vocalRequest.error}");
                yield break;
            }
        }

        // --- Download LRC file (only if it doesn't exist as .lrc or .txt) ---
        if (!File.Exists(lrcSavePath) && !File.Exists(lrcAsTxtPath))
        {
            Debug.Log($"Downloading LRC file from {lrcUrl}");
            LevelResourcesCompiler.Instance.status.text = $"Downloading lyrics...";
            UnityWebRequest lrcRequest = new UnityWebRequest(lrcUrl, UnityWebRequest.kHttpVerbGET)
            {
                downloadHandler = new DownloadHandlerFile(lrcSavePath)
            };
            yield return lrcRequest.SendWebRequest();
            if (lrcRequest.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Failed to download LRC file: {lrcRequest.error}");
                yield break;
            }
            Debug.Log($"Successfully downloaded LRC file to {lrcSavePath}");
        }

        // --- Report Ready (This part is unchanged) ---

        PlayerPrefs.SetString("vocalLocation", vocalSavePath);
        PlayerPrefs.SetString("fullLocation", fullSavePath);

        Debug.Log("All files downloaded or verified. Reporting ready status to server via LocalPlayerInstance.");
        LevelResourcesCompiler.Instance.status.text = "Finished!";

        if (PlayerData.LocalPlayerInstance != null)
        {
            PlayerData.LocalPlayerInstance.RequestReportReadyForSceneChange_ServerRpc();
        }
        else
        {
            Debug.LogError("Could not report ready status because LocalPlayerInstance was null!");
        }
    }



    // [ServerRpc] - A client tells the server it has finished downloading and is ready.
    [ServerRpc(RequireOwnership = true)]
    public void RequestReportReadyForSceneChange_ServerRpc()
    {
        this.IsReady.Value = true;
        RoomManager.Instance.CheckIfAllPlayersAreReadyToLoadScene_Server();
    }

    // [ServerRpc] - Host requests to start the game.
    [ServerRpc(RequireOwnership = true)]
    public void RequestStartGame_ServerRpc()
    {
        if (!this.IsHost.Value) return;
        RoomManager.Instance.StartGame_Server();
    }

    [ServerRpc(RequireOwnership = true)]
    public void RequestReturnToLobby_ServerRpc()
    {
        if (!this.IsHost.Value) return;
        RoomManager.Instance.ReturnToLobby_Server();
    }
    #endregion

}