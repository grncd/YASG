using FishNet;
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
    public static bool isIntentionalDisconnect = false;

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
    private bool _areRoleNotificationsEnabled = false; // Flag to suppress initial spawn notifications

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
            gameObject.name = "Player1";
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

            // Start coroutine to enable role notifications after a short delay
            // This prevents "You are the Host" popping up immediately upon room creation
            StartCoroutine(EnableRoleNotificationsAfterDelay());
        }
    }

    private IEnumerator EnableRoleNotificationsAfterDelay()
    {
        yield return new WaitForSeconds(1.0f);
        _areRoleNotificationsEnabled = true;
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
        if (!base.IsOwner)
        {
            gameObject.name = "Player_" + newName;
        }
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

        // Only show notification for the local player
        if (base.IsOwner && _areRoleNotificationsEnabled)
        {
            if (newStatus)
            {
                NotificationCenter.Info("You are the Host!", "You have been promoted to Host.");
            }
            else
            {
                NotificationCenter.Info("Role Changed", "You are no longer the Host.");
            }
        }
    }

    private void OnIsMasterProcessorChanged(bool oldStatus, bool newStatus, bool asServer)
    {
        Debug.Log($"Player master processor status changed to {newStatus}");

        // Only show notification for the local player
        if (base.IsOwner && _areRoleNotificationsEnabled)
        {
            if (newStatus)
            {
                NotificationCenter.Info("You are the Master Processor!", "You are now responsible for downloading and processing songs for the room.");
            }
            else
            {
                NotificationCenter.Info("Role Changed", "You are no longer the Master Processor.");
            }
        }
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

        // Broadcast ready sound to all clients when a player becomes ready
        if (isReady && RoomManager.Instance != null)
        {
            RoomManager.Instance.PlayPlayerReadySound_ObserversRpc();
        }
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
        // SECURITY CHECK: Only the CURRENT host can assign master processor.
        if (!this.IsHost.Value) return;
        PlayerData targetPlayerData = newMasterPlayer.GetComponent<PlayerData>();
        if (targetPlayerData == null) return;

        // Remove master processor role from whoever currently has it
        foreach (var playerConn in ServerManager.Clients.Values)
        {
            if (playerConn.FirstObject == null) continue;
            PlayerData player = playerConn.FirstObject.GetComponent<PlayerData>();
            if (player != null && player.IsMasterProcessor.Value)
            {
                player.IsMasterProcessor.Value = false;
                Debug.Log($"Removed master processor role from {player.PlayerName.Value}");
            }
        }

        // Assign master processor role to the new player
        targetPlayerData.IsMasterProcessor.Value = true;
        Debug.Log($"Assigned master processor role to {targetPlayerData.PlayerName.Value}");
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
        try
        {
            Debug.Log("[CompileAndReport] Starting compilation...");

            // --- STEP 1: Run compilation (no change) ---
            await LevelResourcesCompiler.Instance.StartCompile(song.Link, song.Title, song.Artist, song.Length, song.CoverUrl);

            Debug.Log("[CompileAndReport] Compilation finished, gathering file paths...");

            // --- STEP 2: Get all three file paths ---
            string vocalLocation = PlayerPrefs.GetString("vocalLocation");

            // --- IMPORTANT: For HTTP server, ALWAYS use the original downloaded file, not the instrumental!
            // The instrumental path in PlayerPrefs might be set based on host's playback preference,
            // but we need to serve the original file for all clients to use correctly.
            string dataPath = PlayerPrefs.GetString("dataPath");
            string expectedFileName = $"{song.Artist} - {song.Title}.mp3";
            expectedFileName = string.Join("_", expectedFileName.Split(Path.GetInvalidFileNameChars()));
            string fullLocation = Path.Combine(dataPath, "downloads", expectedFileName);

            Debug.Log($"[CompileAndReport] fullLocation (original): {fullLocation}, exists: {File.Exists(fullLocation)}");
            Debug.Log($"[CompileAndReport] vocalLocation: {vocalLocation}");

            // --- NEW: Construct the LRC file path ---
            string charactersToRemovePattern = @"[/\\:*?""<>|]";
            string currentSong = PlayerPrefs.GetString("currentSong");
            string cleanSongName = System.Text.RegularExpressions.Regex.Replace(currentSong, charactersToRemovePattern, string.Empty);
            string lrcLocation = Path.Combine(dataPath, "downloads", cleanSongName + ".txt");

            Debug.Log($"[CompileAndReport] lrcLocation: {lrcLocation}, exists: {File.Exists(lrcLocation)}");

            // --- NEW: Construct the Instrumental file path (always, regardless of host setting) ---
            // It is always in the same folder as vocalLocation, with [no_vocals] suffix.
            // Assuming vocalLocation ends with " [vocals].mp3"
            string instrumentalLocation = vocalLocation.Replace(" [vocals].mp3", " [no_vocals].mp3");

            Debug.Log($"[CompileAndReport] instrumentalLocation: {instrumentalLocation}, exists: {File.Exists(instrumentalLocation)}");

            // Basic validation
            if (string.IsNullOrEmpty(fullLocation) || string.IsNullOrEmpty(vocalLocation) || !File.Exists(lrcLocation))
            {
                Debug.LogError($"[CompileAndReport] VALIDATION FAILED! Full: '{fullLocation}' (empty: {string.IsNullOrEmpty(fullLocation)}), Vocal: '{vocalLocation}' (empty: {string.IsNullOrEmpty(vocalLocation)}), LRC exists: {File.Exists(lrcLocation)}");
                return;
            }

            Debug.Log("[CompileAndReport] Validation passed, preparing file server...");

            // Add all FOUR files to the list to be served.
            // Note: fullLocation might be the same as instrumentalLocation if host has PlayInstrumental ON.
            // SimpleHttpFileServer handles duplicate paths gracefully (or we can distinct them).
            List<string> filesToServe = new List<string> { fullLocation, vocalLocation, lrcLocation, instrumentalLocation }.Distinct().ToList();

            Debug.Log($"[CompileAndReport] Files to serve: {string.Join(", ", filesToServe)}");

            // --- STEP 3: Start the server (no change) ---
            if (_httpServer == null)
            {
                Debug.LogError("[CompileAndReport] _httpServer is NULL!");
                return;
            }

            bool serverStarted = _httpServer.StartServer(filesToServe);
            Debug.Log($"[CompileAndReport] HTTP server started: {serverStarted}");
            if (!serverStarted)
            {
                Debug.LogError("[CompileAndReport] Failed to start HTTP server!");
                return;
            }

            // --- STEP 4: Report all four filenames to the main server ---
            string fullFileName = Path.GetFileName(fullLocation);
            string vocalFileName = Path.GetFileName(vocalLocation);
            string lrcFileName = Path.GetFileName(lrcLocation);
            string instrumentalFileName = Path.GetFileName(instrumentalLocation);

            Debug.Log($"[CompileAndReport] File names - Full: {fullFileName}, Vocal: {vocalFileName}, LRC: {lrcFileName}, Instrumental: {instrumentalFileName}");

            // Get all possible local IPs (LAN, VPN, etc.) on the CLIENT side before calling the ServerRpc
            var masterProcessorIps = GetLocalIPAddresses();
            Debug.Log($"[CompileAndReport] Master processor found {masterProcessorIps.Count} IP addresses: {string.Join(", ", masterProcessorIps)}");

            Debug.Log("[CompileAndReport] Calling RequestReportCompilationFinished_ServerRpc...");
            RequestReportCompilationFinished_ServerRpc(masterProcessorIps.ToArray(), fullFileName, vocalFileName, lrcFileName, instrumentalFileName);
            Debug.Log("[CompileAndReport] ServerRpc called successfully.");

            Debug.Log("[CompileAndReport] Calling RequestReportReadyForSceneChange_ServerRpc...");
            RequestReportReadyForSceneChange_ServerRpc();
            Debug.Log("[CompileAndReport] Master Processor finished all tasks successfully.");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[CompileAndReport] EXCEPTION CAUGHT: {ex.GetType().Name}: {ex.Message}");
            Debug.LogError($"[CompileAndReport] Stack trace: {ex.StackTrace}");
        }
    }


    [ServerRpc(RequireOwnership = true)]
    public void RequestReportCompilationFinished_ServerRpc(string[] masterProcessorIps, string fullFileName, string vocalFileName, string lrcFileName, string instrumentalFileName)
    {
        if (!this.IsMasterProcessor.Value) return;

        Debug.Log($"Server received compilation finished report from {Owner.ClientId}.");
        Debug.Log($"[CompileAndReport] Using master processor IPs from client: {string.Join(", ", masterProcessorIps)}");

        // Pass the lrcFileName and instrumentalFileName to the broadcast method
        RoomManager.Instance.BroadcastDownloadInfo_Server(masterProcessorIps, fullFileName, vocalFileName, lrcFileName, instrumentalFileName);
    }

    /// <summary>
    /// Gets all local IPv4 addresses (excluding loopback).
    /// Used for HTTP server so clients can try multiple IPs (LAN, VPN, etc.).
    /// </summary>
    private System.Collections.Generic.List<string> GetLocalIPAddresses()
    {
        var ips = new System.Collections.Generic.List<string>();
        try
        {
            // Get all network interfaces
            var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                // We want IPv4, not IPv6, and not loopback (127.0.0.1)
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                    !System.Net.IPAddress.IsLoopback(ip))
                {
                    ips.Add(ip.ToString());
                }
            }

            if (ips.Count == 0)
            {
                Debug.LogWarning("[GetLocalIPAddresses] No non-loopback IPv4 addresses found");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[GetLocalIPAddresses] Error getting local IPs: {ex.Message}");
        }

        return ips;
    }


    // [ClientRpc] - The server tells non-master clients to download the files.
    [ObserversRpc]
    public void DownloadFiles_ObserversRpc(string[] masterIps, string fullFileName, string vocalFileName, string lrcFileName, string instrumentalFileName)
    {
        if (!IsOwner) return; // Only the actual client who owns this object should run the download logic
        if (this.IsMasterProcessor.Value) return;

        Debug.Log($"Received RPC to download files from {masterIps.Length} possible IPs: {string.Join(", ", masterIps)}");
        StartCoroutine(DownloadFiles_Coroutine(masterIps, fullFileName, vocalFileName, lrcFileName, instrumentalFileName));
    }

    private IEnumerator DownloadFiles_Coroutine(string[] masterIps, string fullFileName, string vocalFileName, string lrcFileName, string instrumentalFileName)
    {
        string dataPath = PlayerPrefs.GetString("dataPath");

        // Try each IP until we find one that works
        string workingIp = null;
        int gamePort = 8080;
        int maxRetries = 10;

        // Retry all IPs multiple times with delays (server might need time to fully start)
        for (int retry = 1; retry <= maxRetries; retry++)
        {
            Debug.Log($"DownloadFiles_Coroutine: Retry attempt {retry}/{maxRetries}");

            foreach (string testIp in masterIps)
            {
                Debug.Log($"DownloadFiles_Coroutine: Testing connection to {testIp}:{gamePort}...");
                bool ipWorks = false;

                // Quick TCP connection test
                try
                {
                    using (var client = new System.Net.Sockets.TcpClient())
                    {
                        var result = client.BeginConnect(testIp, gamePort, null, null);
                        bool success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(2));
                        if (success)
                        {
                            client.EndConnect(result);
                            ipWorks = true;
                            Debug.Log($"DownloadFiles_Coroutine: Successfully connected to {testIp}:{gamePort}");
                        }
                    }
                }
                catch
                {
                    Debug.Log($"DownloadFiles_Coroutine: Cannot connect to {testIp}:{gamePort}");
                }

                if (ipWorks)
                {
                    workingIp = testIp;
                    break;
                }
            }

            if (!string.IsNullOrEmpty(workingIp))
            {
                break;
            }

            // Wait before next retry (but not after the last attempt)
            if (retry < maxRetries)
            {
                Debug.Log($"DownloadFiles_Coroutine: All IPs failed on retry {retry}, waiting 2 seconds before next retry...");
                yield return new WaitForSeconds(2f);
            }
        }

        if (string.IsNullOrEmpty(workingIp))
        {
            Debug.LogError($"DownloadFiles_Coroutine: Could not connect to ANY of the provided IPs after {maxRetries} retries: {string.Join(", ", masterIps)}");
            // Show error to user
            if (AlertManager.Instance != null)
            {
                AlertManager.Instance.ShowError(
                    LocalizationManager.L("alert.connection_failed.title", "Connection failed"),
                    string.Format(LocalizationManager.L("alert.master_connection_failed.info", "Could not connect to the master processor at any of the available IPs after {0} attempts. Please check your network connection."), maxRetries),
                    LocalizationManager.L("alert.dismiss", "Dismiss")
                );
            }
            yield break;
        }

        Debug.Log($"DownloadFiles_Coroutine: Using working IP: {workingIp}");

        // --- Construct Save Paths ---
        string downloadDir = Path.Combine(dataPath, "downloads");
        Directory.CreateDirectory(downloadDir);
        string fullSavePath = Path.Combine(downloadDir, fullFileName);

        string vocalSaveDir = Path.Combine(dataPath, "output", "htdemucs");
        Directory.CreateDirectory(vocalSaveDir);
        string vocalSavePath = Path.Combine(vocalSaveDir, vocalFileName);

        string lrcSavePath = Path.Combine(downloadDir, lrcFileName);

        // --- NEW: Instrumental Save Path ---
        string instrumentalSavePath = Path.Combine(vocalSaveDir, instrumentalFileName);

        // --- NEW: Check if the .lrc file exists as a .txt file ---
        string lrcAsTxtPath = Path.ChangeExtension(lrcSavePath, ".txt");

        // --- CLEANUP: Delete files in wrong location (root instead of downloads) ---
        string wrongFullPath = Path.Combine(dataPath, fullFileName);
        if (File.Exists(wrongFullPath) && wrongFullPath != fullSavePath)
        {
            Debug.Log($"Deleting file in wrong location: {wrongFullPath}");
            File.Delete(wrongFullPath);
        }

        // --- FILE SIZE VALIDATION ---
        // Check if existing files are valid (not empty/corrupted)
        long minAudioFileSize = 10000; // 10KB minimum for audio files
        if (File.Exists(fullSavePath) && new FileInfo(fullSavePath).Length < minAudioFileSize)
        {
            Debug.LogWarning($"Full song file is too small ({new FileInfo(fullSavePath).Length} bytes), deleting: {fullSavePath}");
            File.Delete(fullSavePath);
        }
        if (File.Exists(vocalSavePath) && new FileInfo(vocalSavePath).Length < minAudioFileSize)
        {
            Debug.LogWarning($"Vocal track file is too small ({new FileInfo(vocalSavePath).Length} bytes), deleting: {vocalSavePath}");
            File.Delete(vocalSavePath);
        }
        if (File.Exists(instrumentalSavePath) && new FileInfo(instrumentalSavePath).Length < minAudioFileSize)
        {
            Debug.LogWarning($"Instrumental track file is too small ({new FileInfo(instrumentalSavePath).Length} bytes), deleting: {instrumentalSavePath}");
            File.Delete(instrumentalSavePath);
        }

        // --- STRICT VERIFICATION ---
        // Using LevelResourcesCompiler's strict integrity check.
        // If files are missing (or partial), they are deleted to force a clean download.
        bool filesVerified = LevelResourcesCompiler.Instance.VerifySongIntegrity(lrcAsTxtPath, fullSavePath, vocalSavePath, instrumentalSavePath);

        if (filesVerified)
        {
            Debug.Log("Strict Integrity Check Passed. All files present. Skipping download.");
            PlayerPrefs.SetInt("saved", 1);

            // Set PlayerPrefs for paths
            PlayerPrefs.SetString("vocalLocation", vocalSavePath);
            if (SettingsManager.Instance.GetSetting<bool>("PlayInstrumental"))
            {
                PlayerPrefs.SetString("fullLocation", instrumentalSavePath);
            }
            else
            {
                PlayerPrefs.SetString("fullLocation", fullSavePath);
            }

            // Add to downloaded songs list
            if (RoomManager.Instance != null)
            {
                SongData song = RoomManager.Instance.SelectedSong.Value;
                FavoritesManager.AddDownload(song.Title, song.Artist, song.Album, song.Length, song.CoverUrl, song.Link);
            }

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
        string fullUrl = $"http://{workingIp}:8080/{Uri.EscapeDataString(fullFileName)}";
        string vocalUrl = $"http://{workingIp}:8080/{Uri.EscapeDataString(vocalFileName)}";
        string lrcUrl = $"http://{workingIp}:8080/{Uri.EscapeDataString(lrcFileName)}";
        string instrumentalUrl = $"http://{workingIp}:8080/{Uri.EscapeDataString(instrumentalFileName)}";

        // --- Download Full Song (only if it doesn't exist) ---
        if (!File.Exists(fullSavePath))
        {
            LevelResourcesCompiler.Instance.status.text = $"Downloading full song...";
            yield return DownloadFileReliably(fullUrl, fullSavePath, "full song");
            if (!File.Exists(fullSavePath) || new FileInfo(fullSavePath).Length < minAudioFileSize)
            {
                Debug.LogError($"Full song download failed or file is invalid: {fullSavePath}");
                yield break;
            }
        }

        // --- Download Vocal Track (only if it doesn't exist) ---
        if (!File.Exists(vocalSavePath))
        {
            LevelResourcesCompiler.Instance.status.text = $"Downloading vocal track...";
            yield return DownloadFileReliably(vocalUrl, vocalSavePath, "vocal track");
            if (!File.Exists(vocalSavePath) || new FileInfo(vocalSavePath).Length < minAudioFileSize)
            {
                Debug.LogError($"Vocal track download failed or file is invalid: {vocalSavePath}");
                yield break;
            }
        }

        // --- Download LRC file (only if it doesn't exist as .lrc or .txt) ---
        if (!File.Exists(lrcSavePath) && !File.Exists(lrcAsTxtPath))
        {
            Debug.Log($"Downloading LRC file from {lrcUrl}");
            LevelResourcesCompiler.Instance.status.text = $"Downloading lyrics...";
            yield return DownloadFileReliably(lrcUrl, lrcSavePath, "lyrics", minSizeBytes: 10); // LRC can be small
            if (!File.Exists(lrcSavePath))
            {
                Debug.LogError($"Failed to download LRC file: {lrcSavePath}");
                yield break;
            }
        }

        // --- Download Instrumental Track (Required for strict verification) ---
        if (!File.Exists(instrumentalSavePath))
        {
            LevelResourcesCompiler.Instance.status.text = $"Downloading instrumental...";
            yield return DownloadFileReliably(instrumentalUrl, instrumentalSavePath, "instrumental");
            // We proceed even if instrumental fails, but next verification might fail.
        }

        // --- VALIDATE AUDIO FILES BEFORE PROCEEDING ---
        LevelResourcesCompiler.Instance.status.text = "Validating audio files...";

        // Validate vocal track
        bool vocalValid = false;
        yield return ValidateAudioFile(vocalSavePath, (isValid) => vocalValid = isValid);
        if (!vocalValid)
        {
            Debug.LogError($"Vocal track failed validation, retrying download: {vocalSavePath}");
            if (File.Exists(vocalSavePath)) File.Delete(vocalSavePath);

            // Retry download using reliable method
            yield return DownloadFileReliably(vocalUrl, vocalSavePath, "vocal track (retry)");

            // Validate again
            yield return ValidateAudioFile(vocalSavePath, (isValid) => vocalValid = isValid);
            if (!vocalValid)
            {
                Debug.LogError("Vocal track still invalid after re-download. Aborting.");
                yield break;
            }
        }

        // Validate full track
        bool fullValid = false;
        yield return ValidateAudioFile(fullSavePath, (isValid) => fullValid = isValid);
        if (!fullValid)
        {
            Debug.LogError($"Full track failed validation, retrying download: {fullSavePath}");
            if (File.Exists(fullSavePath)) File.Delete(fullSavePath);

            // Retry download using reliable method
            yield return DownloadFileReliably(fullUrl, fullSavePath, "full song (retry)");

            // Validate again
            yield return ValidateAudioFile(fullSavePath, (isValid) => fullValid = isValid);
            if (!fullValid)
            {
                Debug.LogError("Full track still invalid after re-download. Aborting.");
                yield break;
            }
        }

        Debug.Log("All audio files validated successfully!");

        // --- Set PlayerPrefs for Playback ---
        bool needInstrumental = SettingsManager.Instance.GetSetting<bool>("PlayInstrumental");
        if (needInstrumental && File.Exists(instrumentalSavePath))
        {
            PlayerPrefs.SetString("fullLocation", instrumentalSavePath);
        }
        else
        {
            PlayerPrefs.SetString("fullLocation", fullSavePath);
        }

        PlayerPrefs.SetString("vocalLocation", vocalSavePath);

        Debug.Log("All files downloaded and validated. Reporting ready status to server.");
        LevelResourcesCompiler.Instance.status.text = "Ready!";

        // Add to downloaded songs list
        if (RoomManager.Instance != null)
        {
            SongData song = RoomManager.Instance.SelectedSong.Value;
            FavoritesManager.AddDownload(song.Title, song.Artist, song.Album, song.Length, song.CoverUrl, song.Link);
        }

        if (PlayerData.LocalPlayerInstance != null)
        {
            PlayerData.LocalPlayerInstance.RequestReportReadyForSceneChange_ServerRpc();
        }
        else
        {
            Debug.LogError("Could not report ready status because LocalPlayerInstance was null!");
        }
    }

    /// <summary>
    /// Downloads a file reliably using DownloadHandlerBuffer with Content-Length verification.
    /// Uses buffer instead of DownloadHandlerFile to ensure complete downloads.
    /// </summary>
    private IEnumerator DownloadFileReliably(string url, string savePath, string fileDescription, int maxRetries = 3, long minSizeBytes = 10000)
    {
        int attempt = 0;
        bool success = false;

        while (attempt < maxRetries && !success)
        {
            attempt++;
            if (attempt > 1)
            {
                Debug.Log($"Retry attempt {attempt}/{maxRetries} for {fileDescription}...");
                LevelResourcesCompiler.Instance.status.text = $"Retrying {fileDescription} ({attempt}/{maxRetries})...";
                yield return new WaitForSeconds(1f); // Wait before retry
            }

            // Delete any partial file from previous attempt
            if (File.Exists(savePath))
            {
                try { File.Delete(savePath); }
                catch (Exception e) { Debug.LogWarning($"Could not delete partial file: {e.Message}"); }
            }

            using (UnityWebRequest www = UnityWebRequest.Get(url))
            {
                // Use buffer handler instead of file handler for more control
                www.downloadHandler = new DownloadHandlerBuffer();
                www.timeout = 120; // 2 minute timeout

                var operation = www.SendWebRequest();

                // Show progress while downloading
                while (!operation.isDone)
                {
                    if (www.downloadProgress > 0)
                    {
                        LevelResourcesCompiler.Instance.status.text = $"Downloading {fileDescription}... {(www.downloadProgress * 100f):F0}%";
                    }
                    yield return null;
                }

                if (www.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"Download failed for {fileDescription} (attempt {attempt}): {www.error}");
                    continue; // Retry
                }

                byte[] data = www.downloadHandler.data;
                if (data == null || data.Length == 0)
                {
                    Debug.LogWarning($"Downloaded data is empty for {fileDescription} (attempt {attempt})");
                    continue; // Retry
                }

                // Verify Content-Length if server provided it
                string contentLengthHeader = www.GetResponseHeader("Content-Length");
                if (!string.IsNullOrEmpty(contentLengthHeader))
                {
                    if (long.TryParse(contentLengthHeader, out long expectedLength))
                    {
                        if (data.Length != expectedLength)
                        {
                            Debug.LogWarning($"Content-Length mismatch for {fileDescription}: expected {expectedLength}, got {data.Length} (attempt {attempt})");
                            continue; // Retry - download was incomplete
                        }
                    }
                }

                // Check minimum size
                if (data.Length < minSizeBytes)
                {
                    Debug.LogWarning($"Downloaded {fileDescription} is too small: {data.Length} bytes (min: {minSizeBytes}) (attempt {attempt})");
                    continue; // Retry
                }

                // Write to disk with explicit flush
                try
                {
                    // Ensure directory exists
                    string directory = Path.GetDirectoryName(savePath);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    using (FileStream fs = new FileStream(savePath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        fs.Write(data, 0, data.Length);
                        fs.Flush(true); // Flush to disk, not just OS buffer
                    }

                    // Verify the file was written correctly
                    if (File.Exists(savePath))
                    {
                        long writtenSize = new FileInfo(savePath).Length;
                        if (writtenSize == data.Length)
                        {
                            Debug.Log($"Successfully downloaded {fileDescription}: {savePath} ({writtenSize} bytes)");
                            success = true;
                        }
                        else
                        {
                            Debug.LogWarning($"File size mismatch after write for {fileDescription}: wrote {data.Length}, file is {writtenSize}");
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"File does not exist after write for {fileDescription}: {savePath}");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"Failed to write {fileDescription} to disk: {e.Message}");
                }
            }
        }

        if (!success)
        {
            Debug.LogError($"Failed to download {fileDescription} after {maxRetries} attempts: {url}");
        }
    }

    /// <summary>
    /// Validates an audio file by loading it and checking if it has valid samples.
    /// </summary>
    private IEnumerator ValidateAudioFile(string filePath, System.Action<bool> callback)
    {
        // Check file exists and has reasonable size
        if (!File.Exists(filePath))
        {
            Debug.LogWarning($"ValidateAudioFile: File does not exist: {filePath}");
            callback(false);
            yield break;
        }

        long fileSize = new FileInfo(filePath).Length;
        if (fileSize < 10000) // Less than 10KB
        {
            Debug.LogWarning($"ValidateAudioFile: File too small ({fileSize} bytes): {filePath}");
            callback(false);
            yield break;
        }

        // Try to load the audio file
        string url = new Uri(filePath).AbsoluteUri;
        using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.MPEG))
        {
            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"ValidateAudioFile: Failed to load audio: {www.error}");
                callback(false);
                yield break;
            }

            AudioClip clip = DownloadHandlerAudioClip.GetContent(www);
            if (clip == null)
            {
                Debug.LogWarning($"ValidateAudioFile: AudioClip is null after loading: {filePath}");
                callback(false);
                yield break;
            }

            if (clip.samples <= 0 || clip.length <= 0)
            {
                Debug.LogWarning($"ValidateAudioFile: AudioClip has invalid data (samples: {clip.samples}, length: {clip.length}s): {filePath}");
                callback(false);
                yield break;
            }

            Debug.Log($"ValidateAudioFile: Valid audio - {filePath} ({clip.samples} samples, {clip.length}s)");
            callback(true);
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

    [ServerRpc(RequireOwnership = true)]
    public void RequestLoadResultsScene_ServerRpc()
    {
        if (!this.IsHost.Value)
        {
            Debug.LogWarning($"Client {Owner.ClientId} tried to load Results scene, but is not the host.");
            return;
        }

        Debug.Log("Host is requesting Results scene load via server.");
        RoomManager.Instance?.LoadResultsScene_Server();
    }

    [ServerRpc(RequireOwnership = true)]
    public void RequestReportLyricsError_ServerRpc()
    {
        // Only the master processor should report lyrics errors
        if (!this.IsMasterProcessor.Value)
        {
            Debug.LogWarning($"Client {Owner.ClientId} tried to report lyrics error, but is not the master processor.");
            return;
        }

        Debug.Log("Master processor is reporting lyrics error to all clients.");
        RoomManager.Instance?.HandleLyricsError_Server();
    }

    [ServerRpc(RequireOwnership = true)]
    public void ReportDownloadError_ServerRpc()
    {
        // Only the master processor should report download errors
        if (!this.IsMasterProcessor.Value)
        {
            Debug.LogWarning($"Client {Owner.ClientId} tried to report download error, but is not the master processor.");
            return;
        }

        Debug.Log("Master processor is reporting download error to all clients.");
        RoomManager.Instance?.HandleDownloadError_Server();
    }

    [ObserversRpc]
    public void ShowLyricsError_ObserversRpc()
    {
        Debug.Log("Received lyrics error notification from server.");

        // Show the error alert
        if (AlertManager.Instance != null)
        {
            AlertManager.Instance.ShowError(
                LocalizationManager.L("alert.no_lyrics_mp.title", "This song does not have lyrics."),
                LocalizationManager.L("alert.no_lyrics_mp.info", "The song you've selected either has no lyrics or we couldn't find any synced lyrics for it. If this song has lyrics and you'd like to add them, <b>use the Add Lyrics button</b> located in the menu."),
                LocalizationManager.L("alert.dismiss", "Dismiss")
            );
        }

        // Close the loading screen
        if (LevelResourcesCompiler.Instance != null)
        {
            LevelResourcesCompiler.Instance.LoadingDone();
            if (LevelResourcesCompiler.Instance.loadingFX != null)
            {
                LevelResourcesCompiler.Instance.loadingFX.SetActive(false);
            }
        }
    }

    [ObserversRpc]
    public void ShowDownloadError_ObserversRpc()
    {
        Debug.Log("Received download error notification from server.");

        // Show the error alert
        if (AlertManager.Instance != null)
        {
            AlertManager.Instance.ShowError(
                LocalizationManager.L("alert.download_failed.title", "Download failed"),
                LocalizationManager.L("alert.download_failed.info", "The master processor encountered an error downloading the song from YouTube. This could be due to connectivity issues or the video being unavailable. Please try again with a different song."),
                LocalizationManager.L("alert.dismiss", "Dismiss")
            );
        }

        // Close the loading screen
        if (LevelResourcesCompiler.Instance != null)
        {
            LevelResourcesCompiler.Instance.LoadingDone();
            if (LevelResourcesCompiler.Instance.loadingFX != null)
            {
                LevelResourcesCompiler.Instance.loadingFX.SetActive(false);
            }
        }
    }
    #endregion

}