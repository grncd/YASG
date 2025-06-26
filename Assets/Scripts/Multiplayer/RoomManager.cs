using FishNet.Object;
using FishNet.Object.Synchronizing;
using System.Linq; // Make sure you have this
using FishNet.Managing.Scened; // Add this using directive for SceneLoadData
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using FishNet;
using System;

public struct SongData
{
    public string Title;
    public string Artist;
    public string Length;
    public string CoverUrl;
    public string Link;
    public string LrcFileName;

    // A constructor to make creating new SongData easier
    public SongData(string title, string artist, string length, string coverUrl, string link, string lrcFileName = "")
    {
        Title = title;
        Artist = artist;
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
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        CurrentRoomName.OnChange -= OnRoomNameChanged;
        CreatorName.OnChange -= OnCreatorNameChanged;
        SelectedSong.OnChange -= OnSelectedSongChanged;
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
    public void BroadcastDownloadInfo_Server(string masterIp, string fullFileName, string vocalFileName, string lrcFileName) // Add lrcFileName
    {
        // Send the download info (including lrcFileName) to all clients
        foreach (var conn in ServerManager.Clients.Values)
        {
            conn.FirstObject?.GetComponent<PlayerData>()?.DownloadFiles_ObserversRpc(masterIp, fullFileName, vocalFileName, lrcFileName);
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

}