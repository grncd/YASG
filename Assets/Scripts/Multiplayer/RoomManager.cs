using FishNet.Object;
using FishNet.Object.Synchronizing;
using System.Linq; // Make sure you have this
using System.Collections; // Required for IEnumerator
using FishNet.Managing.Scened; // Add this using directive for SceneLoadData
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using FishNet;
using FishNet.Connection;
using System;
using FishNet.Managing;
using FishNet.Transporting;

public struct SongData
{
    public string Title;
    public string Artist;
    public string Album;
    public string Length;
    public string CoverUrl;
    public string Link;
    public string LrcFileName;

    // A constructor to make creating new SongData easier
    public SongData(string title, string artist, string album, string length, string coverUrl, string link, string lrcFileName = "")
    {
        Title = title;
        Artist = artist;
        Album = album;
        Length = length;
        CoverUrl = coverUrl;
        Link = link;
        LrcFileName = lrcFileName;
    }
}

public class RoomManager : NetworkBehaviour
{
    public static RoomManager Instance;

    // --- SyncVars ---
    public readonly SyncVar<string> CurrentRoomName = new SyncVar<string>("");
    public readonly SyncVar<string> CreatorName = new SyncVar<string>("");
    public readonly SyncVar<SongData> SelectedSong = new SyncVar<SongData>();

    // --- UnityEvents (for broadcasting to UI) ---
    public UnityEvent<string> OnRoomNameUpdated = new UnityEvent<string>();
    public UnityEvent<string> OnCreatorNameUpdated = new UnityEvent<string>();
    public UnityEvent<SongData> OnSelectedSongUpdated = new UnityEvent<SongData>();

    public static event Action<RoomManager> OnInstanceAvailable;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            Debug.Log("RoomManager Instance is now set. Invoking OnInstanceAvailable.");
            OnInstanceAvailable?.Invoke(this);
        }
        else if (Instance != this)
        {
            // If another RoomManager already exists (from a previous scene load), destroy this new one.
            Destroy(gameObject);
        }
    }

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        CurrentRoomName.OnChange += OnRoomNameChanged;
        CreatorName.OnChange += OnCreatorNameChanged;
        SelectedSong.OnChange += OnSelectedSongChanged;

        // Subscribe to player disconnection events on the server
        if (IsServer)
        {
            ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;
        }
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        CurrentRoomName.OnChange -= OnRoomNameChanged;
        CreatorName.OnChange -= OnCreatorNameChanged;
        SelectedSong.OnChange -= OnSelectedSongChanged;

        // Unsubscribe from player disconnection events
        if (IsServer)
        {
            ServerManager.OnRemoteConnectionState -= OnRemoteConnectionState;
        }
    }

    /// <summary>
    /// Called when a remote client disconnects from the server.
    /// Handles host migration if the disconnecting player was the host (but not the room creator).
    /// Handles master processor migration if the disconnecting player had the role.
    /// </summary>
    private void OnRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args)
    {
        // Only handle disconnections (Stopped state)
        if (args.ConnectionState != RemoteConnectionState.Stopped) return;

        Debug.Log($"Player disconnected: ClientId {conn.ClientId}");

        // Get the disconnecting player's data
        if (conn.FirstObject == null) return;
        PlayerData disconnectingPlayer = conn.FirstObject.GetComponent<PlayerData>();
        if (disconnectingPlayer == null) return;

        // Get all remaining players for migration
        var remainingPlayers = ServerManager.Clients.Values
            .Select(c => c.FirstObject?.GetComponent<PlayerData>())
            .Where(p => p != null && p != disconnectingPlayer)
            .ToList();

        // If the disconnecting player was the host but NOT the room creator, migrate host
        if (disconnectingPlayer.IsHost.Value && disconnectingPlayer.PlayerName.Value != CreatorName.Value)
        {
            Debug.Log($"Host {disconnectingPlayer.PlayerName.Value} left the room. Migrating host to a random player...");

            if (remainingPlayers.Count > 0)
            {
                // Pick a random player
                PlayerData newHost = remainingPlayers[UnityEngine.Random.Range(0, remainingPlayers.Count)];
                newHost.IsHost.Value = true;
                Debug.Log($"Migrated host role to {newHost.PlayerName.Value}");
            }
            else
            {
                Debug.LogWarning("No remaining players to migrate host to.");
            }
        }

        // If the disconnecting player was the master processor, migrate the role
        if (disconnectingPlayer.IsMasterProcessor.Value)
        {
            Debug.Log($"Master processor {disconnectingPlayer.PlayerName.Value} left the room. Migrating master processor role to a random player...");

            if (remainingPlayers.Count > 0)
            {
                // Pick a random player
                PlayerData newMasterProcessor = remainingPlayers[UnityEngine.Random.Range(0, remainingPlayers.Count)];
                newMasterProcessor.IsMasterProcessor.Value = true;
                Debug.Log($"Migrated master processor role to {newMasterProcessor.PlayerName.Value}");
            }
            else
            {
                Debug.LogWarning("No remaining players to migrate master processor to.");
            }
        }
    }

    // --- Server-Side Methods ---
    public void SetRoomName_Server(string roomName, string creatorName)
    {
        if (!IsServer) return;
        CurrentRoomName.Value = roomName;
        CreatorName.Value = creatorName;
    }

    [Server]
    public void SetSelectedSong_Server(SongData newSong)
    {
        SelectedSong.Value = newSong;
    }

    // --- Getter Methods ---
    public string GetRoomName()
    {
        return CurrentRoomName.Value;
    }

    public string GetCreatorName()
    {
        return CreatorName.Value;
    }

    // --- OnChange Callbacks (The Corrected Part) ---

    /// <summary>
    /// When the RoomName SyncVar changes, invoke the corresponding event.
    /// </summary>
    private void OnRoomNameChanged(string oldName, string newName, bool asServer)
    {
        Debug.Log($"SyncVar OnChange! Room name changed from '{oldName}' to '{newName}'.");
        // This broadcasts the change to any listening UI scripts.
        OnRoomNameUpdated.Invoke(newName);
    }

    /// <summary>
    /// When the CreatorName SyncVar changes, invoke the corresponding event.
    /// </summary>
    private void OnCreatorNameChanged(string oldName, string newName, bool asServer)
    {
        Debug.Log($"SyncVar OnChange! Creator name changed from '{oldName}' to '{newName}'.");
        // This broadcasts the change to any listening UI scripts.
        OnCreatorNameUpdated.Invoke(newName);
    }

    /// <summary>
    /// When the SelectedSong SyncVar changes, invoke the corresponding event.
    /// </summary>
    private void OnSelectedSongChanged(SongData oldSong, SongData newSong, bool asServer)
    {
        Debug.Log($"SyncVar OnChange! Selected song changed to '{newSong.Title}'.");
        // This broadcasts the change to any listening UI scripts.
        OnSelectedSongUpdated.Invoke(newSong);
    }

    [Server]
    public void StartGame_Server()
    {
        // Optional: Check if all players are ready before starting
        if (!AreAllPlayersReady())
        {
            Debug.LogWarning("Host tried to start game, but not all players are ready.");
            return;
        }

        Debug.Log("Server starting game sequence...");

        // --- Broadcast game start sound to all clients ---
        PlayGameStartSound_ObserversRpc();

        // Reset the "IsReady" status for everyone. We will re-use it for the next step.
        foreach (var conn in ServerManager.Clients.Values)
        {
            if (conn.FirstObject != null)
            {
                conn.FirstObject.GetComponent<PlayerData>().IsReady.Value = false;
            }
        }

        foreach (var conn in ServerManager.Clients.Values)
        {
            if (conn.FirstObject != null)
            {
                conn.FirstObject.GetComponent<PlayerData>().IsGameReady.Value = false;
            }
        }

        // Tell all players (including master) to prepare for loading
        foreach (var conn in ServerManager.Clients.Values)
        {
            conn.FirstObject?.GetComponent<PlayerData>()?.PrepareToLoad_ClientRpc();
        }
    }

    [Server]
    public void BroadcastDownloadInfo_Server(string[] masterIps, string fullFileName, string vocalFileName, string lrcFileName, string instrumentalFileName) // Add lrcFileName and instrumentalFileName
    {
        // Send the download info (including lrcFileName and instrumentalFileName) to all clients
        foreach (var conn in ServerManager.Clients.Values)
        {
            conn.FirstObject?.GetComponent<PlayerData>()?.DownloadFiles_ObserversRpc(masterIps, fullFileName, vocalFileName, lrcFileName, instrumentalFileName);
        }
    }

    [Server]
    public void CheckIfAllPlayersAreReadyToLoadScene_Server()
    {
        // This logic checks if every player has called the RPC to say they are done downloading.
        bool allReady = ServerManager.Clients.Values.All(conn =>
            conn.FirstObject != null &&
            conn.FirstObject.GetComponent<PlayerData>().IsReady.Value
        );

        // If allReady is not true, this method does nothing. It will be called again when the next player finishes.
        if (allReady)
        {
            Debug.Log("All clients have finished downloading! Loading game scene for everyone...");
            SceneLoadData sld = new SceneLoadData("Main");
            sld.ReplaceScenes = ReplaceOption.All;
            InstanceFinder.NetworkManager.SceneManager.LoadGlobalScenes(sld);
        }
    }

    [Server]
    public bool AreAllPlayersReady()
    {
        if (!IsServer) return false;
        if (ServerManager.Clients.Count == 0) return false;
        return ServerManager.Clients.Values.All(conn =>
            conn.FirstObject != null &&
            conn.FirstObject.GetComponent<PlayerData>().IsReady.Value
        );
    }

    [Server]
    public void CheckIfAllPlayersAreGameReady_Server()
    {
        // Check if all connected players have their IsGameReady flag set to true.
        bool allReady = ServerManager.Clients.Values.All(conn =>
            conn.FirstObject != null &&
            conn.FirstObject.GetComponent<PlayerData>().IsGameReady.Value
        );

        if (allReady)
        {
            Debug.Log("All players are game-ready! Broadcasting StartCountdown RPC...");

            // Everyone is ready, so tell every client to start their countdown.
            foreach (var conn in ServerManager.Clients.Values)
            {
                conn.FirstObject?.GetComponent<PlayerData>()?.StartCountdown_ObserversRpc();
            }
        }
        else
        {
            Debug.Log("Not all players are game-ready yet. Waiting for more reports.");
        }
    }

    [Server]
    public void ResetAllPlayersState_Server()
    {
        if (!IsServer) return;

        Debug.Log("Server is resetting all player states for replayability.");

        foreach (var conn in ServerManager.Clients.Values)
        {
            if (conn.FirstObject != null)
            {
                PlayerData playerData = conn.FirstObject.GetComponent<PlayerData>();
                if (playerData != null)
                {
                    playerData.CurrentGameScore.Value = 0;
                    playerData.Perfects.Value = 0;
                    playerData.Greats.Value = 0;
                    playerData.Mehs.Value = 0;
                    playerData.Stars.Value = 0;
                    playerData.Placement.Value = 0;
                    playerData.IsReady.Value = false;
                    playerData.IsGameReady.Value = false;
                }
            }
        }
    }

    [Server]
    public void ReturnToLobby_Server()
    {
        if (!IsServer) return;
        StartCoroutine(ReturnToLobbyRoutine());
    }

    private IEnumerator ReturnToLobbyRoutine()
    {
        // Notify all clients that we are returning to the lobby so they can set up their UI state AND play transition
        NotifyReturningToLobby_ObserversRpc();

        // Wait for the transition animation (approx 1s)
        yield return new WaitForSeconds(1.0f);

        // First, reset everyone's state
        ResetAllPlayersState_Server();

        // Then, load the lobby scene for everyone
        Debug.Log("All players reset. Returning to lobby scene.");
        SceneLoadData sld = new SceneLoadData("Menu"); // Assuming "Menu" is the lobby scene
        sld.ReplaceScenes = ReplaceOption.All;
        InstanceFinder.NetworkManager.SceneManager.LoadGlobalScenes(sld);
    }

    [ObserversRpc]
    public void NotifyReturningToLobby_ObserversRpc()
    {
        Debug.Log("Received RPC: Returning to lobby. Setting 'fromMP' flag.");
        PlayerPrefs.SetInt("fromMP", 1);
        PlayerPrefs.Save();

        // Trigger Results Screen transition if present
        var resultsScreen = FindObjectOfType<ResultsScreen>();
        if (resultsScreen != null)
        {
            resultsScreen.PlayTransitionAndLeave();
        }
    }

    [Server]
    public void LoadResultsScene_Server()
    {
        if (!IsServer) return;

        Debug.Log("Server loading Results scene for all players.");
        SceneLoadData sld = new SceneLoadData("Results");
        sld.ReplaceScenes = ReplaceOption.All;
        InstanceFinder.NetworkManager.SceneManager.LoadGlobalScenes(sld);
    }



    [Server]
    public void HandleLyricsError_Server()
    {
        if (!IsServer) return;

        Debug.Log("Server handling lyrics error - resetting all player states and notifying clients.");

        // Reset all player states back to initial room state
        ResetAllPlayersState_Server();

        // Broadcast the error to all clients
        foreach (var conn in ServerManager.Clients.Values)
        {
            conn.FirstObject?.GetComponent<PlayerData>()?.ShowLyricsError_ObserversRpc();
        }
    }

    [Server]
    public void HandleDownloadError_Server()
    {
        if (!IsServer) return;

        Debug.Log("Server handling download error - resetting all player states and notifying clients.");

        // Reset all player states back to initial room state
        ResetAllPlayersState_Server();

        // Broadcast the error to all clients
        foreach (var conn in ServerManager.Clients.Values)
        {
            conn.FirstObject?.GetComponent<PlayerData>()?.ShowDownloadError_ObserversRpc();
        }
    }

    #region Audio FX RPCs

    /// <summary>
    /// Broadcasts game start sound to all clients.
    /// </summary>
    [ObserversRpc]
    public void PlayGameStartSound_ObserversRpc()
    {
        Debug.Log("Playing game start sound on this client.");
        var lobbyUI = FindObjectOfType<LobbyDisplayUI>();
        if (lobbyUI != null && lobbyUI.lobbyAudioSource != null && lobbyUI.gameStartClip != null)
        {
            lobbyUI.lobbyAudioSource.PlayOneShot(lobbyUI.gameStartClip);
        }
    }

    /// <summary>
    /// Broadcasts player ready sound to all clients.
    /// </summary>
    [ObserversRpc]
    public void PlayPlayerReadySound_ObserversRpc()
    {
        Debug.Log("Playing player ready sound on this client.");
        var lobbyUI = FindObjectOfType<LobbyDisplayUI>();
        if (lobbyUI != null && lobbyUI.lobbyAudioSource != null && lobbyUI.playerReadyClip != null)
        {
            lobbyUI.lobbyAudioSource.PlayOneShot(lobbyUI.playerReadyClip);
        }
    }

    #endregion
}