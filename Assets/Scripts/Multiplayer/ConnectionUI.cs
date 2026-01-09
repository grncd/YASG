using FishNet.Managing;
using FishNet.Connection;
using TMPro;
using UnityEngine;
using System.Collections;
using System.Net.NetworkInformation;
using System.Threading.Tasks;

public class ConnectionUI : MonoBehaviour
{
    [Tooltip("The input field where the user types the desired room name.")]
    public TMP_InputField roomNameInput;

    [Tooltip("The input field for a client to type a server address to join.")]
    public TMP_InputField addressInput;

    [Tooltip("The GameObject for the Lobby Panel, which will be activated after connecting.")]
    public GameObject lobbyPanel;

    [Header("UI State References (for reverting on join failure)")]
    [Tooltip("The Multiplayer panel GameObject that gets shown when joining.")]
    public GameObject multiplayerPanel;

    [Tooltip("The Menu GameObject that gets hidden when joining.")]
    public GameObject menuPanel;

    private NetworkManager _networkManager;
    private bool _isJoining = false;

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

    public void CreateRoom(string ip)
    {
        if (string.IsNullOrWhiteSpace(roomNameInput.text))
        {
            Debug.LogWarning("Please enter a room name before creating a room.");
            return;
        }

        // Ensure any previous connections are fully stopped before creating a new room
        if (_networkManager.ServerManager.Started)
        {
            _networkManager.ServerManager.StopConnection(true);
        }
        if (_networkManager.ClientManager.Started)
        {
            _networkManager.ClientManager.StopConnection();
        }

        PlayerPrefs.SetString("masterIp", ip);
        // --- FIX: Explicitly set multiplayer flag ---
        PlayerPrefs.SetInt("multiplayer", 1);

        // Ensure we don't double-subscribe to the event
        _networkManager.SceneManager.OnClientLoadedStartScenes -= OnClientLoadedStartScenes_TriggerNameSet;
        _networkManager.SceneManager.OnClientLoadedStartScenes += OnClientLoadedStartScenes_TriggerNameSet;

        _networkManager.ServerManager.StartConnection();
        _networkManager.ClientManager.StartConnection();

        if (lobbyPanel != null)
        {
            lobbyPanel.SetActive(true);
            this.gameObject.SetActive(false);
        }
    }

    public void JoinRoom()
    {
        if (_isJoining) return; // Prevent double-clicking

        Debug.Log("ConnectionUI: JoinRoom button clicked.");
        StartCoroutine(JoinRoomCoroutine());
    }

    private IEnumerator JoinRoomCoroutine()
    {
        _isJoining = true;
        string targetIp = PlayerPrefs.GetString("masterIp");

        // Show loading screen using LevelResourcesCompiler
        if (LevelResourcesCompiler.Instance != null)
        {
            LevelResourcesCompiler.Instance.loadingCanvas.SetActive(true);
            LevelResourcesCompiler.Instance.loadingSecond.SetActive(true);
            LevelResourcesCompiler.Instance.loadingFirst.SetActive(false);
            LevelResourcesCompiler.Instance.BeginLoading();
            LevelResourcesCompiler.Instance.status.text = "Checking connection...";
        }

        // Run ping check on background thread to avoid freezing
        bool pingSuccess = false;
        bool pingComplete = false;

        Task.Run(() =>
        {
            pingSuccess = PingHost(targetIp);
            pingComplete = true;
        });

        // Wait for ping to complete
        while (!pingComplete)
        {
            yield return null;
        }

        // Hide loading screen
        if (LevelResourcesCompiler.Instance != null)
        {
            LevelResourcesCompiler.Instance.LoadingDone();
        }

        if (!pingSuccess)
        {
            Debug.LogWarning($"ConnectionUI: Cannot reach host at {targetIp}");

            // Show error alert
            if (AlertManager.Instance != null)
            {
                AlertManager.Instance.ShowError(
                    "Cannot reach host",
                    $"The IP address '{targetIp}' is not reachable. Please check the address and make sure the host is running.",
                    "Dismiss"
                );
            }

            // Revert UI state to original menu state
            RevertToMenuState();
            _isJoining = false;
            yield break;
        }

        // Ensure any previous connections are fully stopped before joining
        if (_networkManager.ServerManager.Started)
        {
            _networkManager.ServerManager.StopConnection(true);
        }
        if (_networkManager.ClientManager.Started)
        {
            _networkManager.ClientManager.StopConnection();
        }

        // --- FIX: Explicitly set multiplayer flag ---
        PlayerPrefs.SetInt("multiplayer", 1);

        _networkManager.ClientManager.StartConnection();

        if (lobbyPanel != null)
        {
            lobbyPanel.SetActive(true);
            this.gameObject.SetActive(false);
        }

        _isJoining = false;
    }

    private void RevertToMenuState()
    {
        Debug.Log($"ConnectionUI: RevertToMenuState called. multiplayerPanel={multiplayerPanel}, menuPanel={menuPanel}");

        // Hide multiplayer panel and show menu
        if (multiplayerPanel != null)
        {
            Debug.Log("ConnectionUI: Hiding multiplayerPanel");
            multiplayerPanel.SetActive(false);
        }
        else
        {
            Debug.LogWarning("ConnectionUI: multiplayerPanel is null!");
        }

        if (menuPanel != null)
        {
            Debug.Log("ConnectionUI: Showing menuPanel");
            menuPanel.SetActive(true);
        }
        else
        {
            Debug.LogWarning("ConnectionUI: menuPanel is null!");
        }

        // Reset multiplayer flag
        PlayerPrefs.SetInt("multiplayer", 0);
    }

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