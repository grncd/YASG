// ConnectionUI.cs
using FishNet.Managing;
using FishNet.Connection;
using TMPro;
using UnityEngine;
using System.Collections;
using System.Net.NetworkInformation;

public class ConnectionUI : MonoBehaviour
{
    [Tooltip("The input field where the user types the desired room name.")]
    public TMP_InputField roomNameInput;

    [Tooltip("The input field for a client to type a server address to join.")]
    public TMP_InputField addressInput;

    [Tooltip("The GameObject for the Lobby Panel, which will be activated after connecting.")]
    public GameObject lobbyPanel;

    private NetworkManager _networkManager;

    private void Awake()
    {
        _networkManager = FindObjectOfType<NetworkManager>();
    }

    private void Start()
    {
        if (lobbyPanel != null)
        {
            lobbyPanel.SetActive(false);
        }
    }

    /// <summary>
    /// Called by the "Create Room" button's OnClick event.
    /// This acts as the HOST.
    /// </summary>
    public void CreateRoom(string ip)
    {
        if (string.IsNullOrWhiteSpace(roomNameInput.text))
        {
            Debug.LogWarning("Please enter a room name before creating a room.");
            return;
        }

        PlayerPrefs.SetString("masterIp", ip);

        _networkManager.SceneManager.OnClientLoadedStartScenes += OnClientLoadedStartScenes_TriggerNameSet;
        _networkManager.ServerManager.StartConnection();
        _networkManager.ClientManager.StartConnection();

        if (lobbyPanel != null)
        {
            lobbyPanel.SetActive(true);
            this.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Called by the "Join Room" button's OnClick event.
    /// This acts as a CLIENT.
    /// </summary>
    public void JoinRoom()
    {
        Debug.Log("ConnectionUI: JoinRoom button clicked.");

        PingHost(PlayerPrefs.GetString("masterIp"));

        // Start the client connection. The client does not start a server.
        _networkManager.ClientManager.StartConnection();

        // Switch to the lobby panel to wait for the connection to complete.
        if (lobbyPanel != null)
        {
            lobbyPanel.SetActive(true);
            this.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// This callback is only for the HOST, to set the initial room name.
    /// </summary>
    private void OnClientLoadedStartScenes_TriggerNameSet(NetworkConnection conn, bool asServer)
    {
        _networkManager.SceneManager.OnClientLoadedStartScenes -= OnClientLoadedStartScenes_TriggerNameSet;
        StartCoroutine(SetInitialRoomNameWhenReady());
    }

    private IEnumerator SetInitialRoomNameWhenReady()
    {
        Debug.Log("ConnectionUI: Coroutine started. Waiting for PlayerData.LocalPlayerInstance to be set...");

        float timeout = Time.time + 5f;
        while (PlayerData.LocalPlayerInstance == null)
        {
            if (Time.time > timeout)
            {
                Debug.LogError("ConnectionUI: Timed out waiting for PlayerData.LocalPlayerInstance. Something is wrong with player spawning.");
                yield break;
            }
            yield return null;
        }

        Debug.Log("ConnectionUI: PlayerData.LocalPlayerInstance is now available! Calling RPC to set room name.");
        PlayerData.LocalPlayerInstance.RequestSetRoomName_ServerRpc(roomNameInput.text);
    }

    public bool PingHost(string ipAddress, int timeout = 1000)
    {
        try
        {
            using (System.Net.NetworkInformation.Ping ping = new System.Net.NetworkInformation.Ping())
            {
                PingReply reply = ping.Send(ipAddress, timeout);
                return reply.Status == IPStatus.Success;
            }
        }
        catch
        {
            return false;
        }
    }
}