using System;
using FishNet.Managing;
using FishNet.Connection;
using FishNet.Transporting.Tugboat;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
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

    [Header("Room Discovery")]
    [Tooltip("Parent transform for instantiated room list items.")]
    public Transform roomListParent;

    [Tooltip("Prefab for displaying a discovered room.")]
    public GameObject roomPrefab;

    [Tooltip("Port used for UDP room discovery broadcasts.")]
    public int discoveryPort = 7771;

    [Tooltip("How often to refresh the room list (in seconds).")]
    public float scanInterval = 2f;

    [Tooltip("Enable direct subnet scanning (more reliable for VPNs like ZeroTier).")]
    public bool enableSubnetScan = true;
    public ParticleSystem mainMenuParticles;

    private NetworkManager _networkManager;
    private bool _isJoining = false;

    // Room discovery
    private UdpClient _broadcastClient;
    private UdpClient _listenerClient;
    private CancellationTokenSource _discoveryCts;
    private Dictionary<string, GameObject> _discoveredRooms = new Dictionary<string, GameObject>();
    private bool _isBroadcasting = false;
    private string _currentRoomName = "";
    private List<IPAddress> _cachedSubnetIPs = null;
    private List<IPAddress> _cachedBroadcastAddresses = null;

    private void Awake()
    {
        _networkManager = FindObjectOfType<NetworkManager>();

        // Initialize the main thread dispatcher on the main thread
        UnityMainThreadDispatcher.Initialize();
    }

    private void Start()
    {
        if (lobbyPanel != null)
        {
            lobbyPanel.SetActive(false);
        }
    }

    private void OnEnable()
    {
        StartRoomDiscovery();
    }

    private void OnDisable()
    {
        StopRoomDiscovery();
        StopBroadcasting();
    }

    private void OnDestroy()
    {
        StopRoomDiscovery();
        StopBroadcasting();
    }

    public void ShowParticles()
    {
        StartCoroutine(FadeParticleSystem(mainMenuParticles, 0f, 0.06666667f, 0.2f));
    }

    public void HideParticles()
    {
        StartCoroutine(FadeParticleSystem(mainMenuParticles, 0.06666667f, 0f, 0.2f));
    }

    private IEnumerator FadeParticleSystem(ParticleSystem ps, float startAlpha, float endAlpha, float duration)
    {
        var main = ps.main;
        ParticleSystem.Particle[] particles = new ParticleSystem.Particle[main.maxParticles];

        Color originalColor = main.startColor.color;

        float timeStartedLerping = Time.time;

        while (Time.time < timeStartedLerping + duration)
        {
            float timeSinceStarted = Time.time - timeStartedLerping;
            float percentageComplete = timeSinceStarted / duration;
            float currentAlpha = Mathf.Lerp(startAlpha, endAlpha, percentageComplete);

            main.startColor = new Color(originalColor.r, originalColor.g, originalColor.b, currentAlpha);

            int particleCount = ps.GetParticles(particles);
            for (int i = 0; i < particleCount; i++)
            {
                Color32 particleColor = particles[i].startColor;
                particleColor.a = (byte)(currentAlpha * 255);
                particles[i].startColor = particleColor;
            }
            ps.SetParticles(particles, particleCount);

            yield return new WaitForEndOfFrame();
        }

        main.startColor = new Color(originalColor.r, originalColor.g, originalColor.b, endAlpha);

        int finalParticleCount = ps.GetParticles(particles);
        for (int i = 0; i < finalParticleCount; i++)
        {
            Color32 particleColor = particles[i].startColor;
            particleColor.a = (byte)(endAlpha * 255);
            particles[i].startColor = particleColor;
        }
        ps.SetParticles(particles, finalParticleCount);
    }

    public void CreateRoom(string ip)
    {
        if (string.IsNullOrWhiteSpace(roomNameInput.text))
        {
            StartCoroutine(ShowRoomNameErrorAndRevert());
            return;
        }

        StartCoroutine(CreateRoomCoroutine(ip));
    }

    private IEnumerator ShowRoomNameErrorAndRevert()
    {
        AlertManager.Instance?.ShowError(
            "Room name required.",
            "Please enter a room name before creating a room.",
            "Dismiss"
        );
        yield return null;
        RevertToMenuState();
    }

    private IEnumerator CreateRoomCoroutine(string ip)
    {
        // Ensure any previous connections are fully stopped before creating a new room
        if (_networkManager.ServerManager.Started)
        {
            _networkManager.ServerManager.StopConnection(true);
        }
        if (_networkManager.ClientManager.Started)
        {
            _networkManager.ClientManager.StopConnection();
        }

        // Wait a frame to ensure FishNet fully processes the stop
        yield return null;

        PlayerPrefs.SetString("masterIp", ip);
        PlayerPrefs.Save(); // Ensure IP is saved before LobbyDisplayUI reads it
        // --- FIX: Explicitly set multiplayer flag ---
        PlayerPrefs.SetInt("multiplayer", 1);

        // Ensure we don't double-subscribe to the event
        _networkManager.SceneManager.OnClientLoadedStartScenes -= OnClientLoadedStartScenes_TriggerNameSet;
        _networkManager.SceneManager.OnClientLoadedStartScenes += OnClientLoadedStartScenes_TriggerNameSet;

        // Start server first
        _networkManager.ServerManager.StartConnection();

        // Wait for server to be fully ready before starting client
        float timeout = Time.time + 5f;
        while (!_networkManager.ServerManager.Started)
        {
            if (Time.time > timeout)
            {
                Debug.LogError("ConnectionUI: Timed out waiting for server to start.");
                AlertManager.Instance?.ShowError(
                    "Failed to start server",
                    "Could not start the server. Please try again.",
                    "Dismiss"
                );
                RevertToMenuState();
                yield break;
            }
            yield return null;
        }

        // Additional wait to ensure server is fully initialized
        yield return new WaitForSeconds(0.5f);

        // Start broadcasting this room for discovery
        _currentRoomName = roomNameInput.text;
        StartBroadcasting(ip, _currentRoomName);

        // Set IP to localhost for client connection (since server is on same machine)
        PlayerPrefs.SetString("masterIp", "127.0.0.1");
        PlayerPrefs.Save();

        // Now start the client
        _networkManager.ClientManager.StartConnection();

        // Restore the original IP so other clients can use it to join
        PlayerPrefs.SetString("masterIp", ip);
        PlayerPrefs.Save();

        if (lobbyPanel != null)
        {
            // Ensure the multiplayer panel is active first (in case it was hidden by RevertToMenuState)
            if (multiplayerPanel != null)
            {
                multiplayerPanel.SetActive(true);
            }
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

        // Get IP from input field, fallback to PlayerPrefs if input is empty
        string targetIp = addressInput != null ? addressInput.text.Trim() : "";

        // If addressInput is empty, try reading from PlayerPrefs (might have been set elsewhere)
        if (string.IsNullOrEmpty(targetIp))
        {
            targetIp = PlayerPrefs.GetString("masterIp", "").Trim();
        }

        Debug.Log($"ConnectionUI: JoinRoomCoroutine started. Target IP: '{targetIp}', addressInput null: {addressInput == null}, addressInput.text: '{addressInput?.text}'");

        // Validate IP is not empty
        if (string.IsNullOrEmpty(targetIp))
        {
            Debug.LogWarning("ConnectionUI: IP address is empty!");
            if (AlertManager.Instance != null)
            {
                AlertManager.Instance.ShowError(
                    "Invalid IP",
                    "Please enter a valid IP address.",
                    "Dismiss"
                );
            }
            yield return null;
            RevertToMenuState();
            _isJoining = false;
            yield break;
        }

        PlayerPrefs.SetString("masterIp", targetIp);
        PlayerPrefs.Save(); // Ensure IP is saved before LobbyDisplayUI reads it

        // Show loading screen using LevelResourcesCompiler
        if (LevelResourcesCompiler.Instance != null)
        {
            LevelResourcesCompiler.Instance.loadingCanvas.SetActive(true);
            LevelResourcesCompiler.Instance.loadingSecond.SetActive(true);
            LevelResourcesCompiler.Instance.loadingFirst.SetActive(false);
            LevelResourcesCompiler.Instance.BeginLoading();
            LevelResourcesCompiler.Instance.status.text = "Checking connection...";
            Debug.Log("ConnectionUI: Loading screen shown");
        }

        // Run ping check on background thread to avoid freezing
        bool pingSuccess = false;
        bool pingComplete = false;

        Task.Run(() =>
        {
            Debug.Log($"ConnectionUI: Starting ping to {targetIp}");
            pingSuccess = PingHost(targetIp);
            Debug.Log($"ConnectionUI: Ping complete. Success: {pingSuccess}");
            pingComplete = true;
        });

        // Wait for ping to complete
        while (!pingComplete)
        {
            yield return null;
        }

        Debug.Log($"ConnectionUI: Ping finished. pingSuccess={pingSuccess}");

        // Hide loading screen
        if (LevelResourcesCompiler.Instance != null)
        {
            LevelResourcesCompiler.Instance.LoadingDone();
            Debug.Log("ConnectionUI: Loading screen hidden");
        }

        if (!pingSuccess)
        {
            Debug.LogWarning($"ConnectionUI: Cannot reach host at {targetIp}");

            // Show error alert
            if (AlertManager.Instance != null)
            {
                Debug.Log("ConnectionUI: Showing error alert");
                AlertManager.Instance.ShowError(
                    "Cannot reach host",
                    $"The IP address '{targetIp}' is not reachable. Please check the address and make sure the host is running.",
                    "Dismiss"
                );
            }
            else
            {
                Debug.LogError("ConnectionUI: AlertManager.Instance is NULL!");
            }

            // Revert UI state to original menu state
            Debug.Log("ConnectionUI: Calling RevertToMenuState");
            RevertToMenuState();
            _isJoining = false;
            yield break;
        }

        Debug.Log("ConnectionUI: Ping succeeded, proceeding to connect...");

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
            // Ensure the multiplayer panel is active first
            if (multiplayerPanel != null)
            {
                multiplayerPanel.SetActive(true);
            }
            lobbyPanel.SetActive(true);
            this.gameObject.SetActive(false);
        }

        _isJoining = false;
    }

    private void RevertToMenuState()
    {
        Debug.Log($"ConnectionUI: RevertToMenuState called. multiplayerPanel={multiplayerPanel}, menuPanel={menuPanel}");

        // Ensure any FishNet connections are fully stopped to clean up state
        if (_networkManager != null)
        {
            if (_networkManager.ServerManager.Started)
            {
                _networkManager.ServerManager.StopConnection(true);
            }
            if (_networkManager.ClientManager.Started)
            {
                _networkManager.ClientManager.StopConnection();
            }
        }

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

    #region Room Discovery

    private void StartRoomDiscovery()
    {
        if (_discoveryCts != null) return;

        _discoveryCts = new CancellationTokenSource();
        _discoveredRooms.Clear();

        // Clear existing room list UI
        if (roomListParent != null)
        {
            foreach (Transform child in roomListParent)
            {
                Destroy(child.gameObject);
            }
        }

        Task.Run(() => ListenForRooms(_discoveryCts.Token));
        StartCoroutine(CleanupStaleRooms());
    }

    private void StopRoomDiscovery()
    {
        _discoveryCts?.Cancel();
        _discoveryCts?.Dispose();
        _discoveryCts = null;

        try
        {
            _listenerClient?.Close();
        }
        catch { }
        _listenerClient = null;
    }

    private async void ListenForRooms(CancellationToken token)
    {
        try
        {
            _listenerClient = new UdpClient();
            _listenerClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _listenerClient.Client.Bind(new IPEndPoint(IPAddress.Any, discoveryPort));

            while (!token.IsCancellationRequested)
            {
                try
                {
                    var result = await _listenerClient.ReceiveAsync();
                    string message = Encoding.UTF8.GetString(result.Buffer);

                    // Expected format: "YASG_ROOM|RoomName|IP|Port"
                    if (message.StartsWith("YASG_ROOM|"))
                    {
                        string[] parts = message.Split('|');
                        if (parts.Length >= 4)
                        {
                            string roomName = parts[1];
                            string ip = parts[2];
                            string port = parts[3];

                            // Use the sender's actual IP
                            string actualIp = result.RemoteEndPoint.Address.ToString();

                            // Queue UI update on main thread
                            UnityMainThreadDispatcher.Enqueue(() => OnRoomDiscovered(roomName, actualIp, port));
                        }
                    }
                }
                catch (SocketException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested)
            {
                Debug.LogError($"ConnectionUI: Room discovery error: {ex.Message}");
            }
        }
    }

    private void OnRoomDiscovered(string roomName, string ip, string port)
    {
        string key = $"{ip}:{port}";

        if (_discoveredRooms.ContainsKey(key))
        {
            // Update existing room entry (refresh timestamp)
            var roomObj = _discoveredRooms[key];
            if (roomObj != null)
            {
                var roomData = roomObj.GetComponent<DiscoveredRoomData>();
                if (roomData != null)
                {
                    roomData.lastSeen = Time.time;
                    // Update room name in case it changed
                    roomObj.transform.GetChild(0).GetComponent<TMP_Text>().text = roomName;
                }
            }
            return;
        }

        // Create new room entry
        if (roomPrefab != null && roomListParent != null)
        {
            GameObject roomItem = Instantiate(roomPrefab, roomListParent);

            // Set room name text
            roomItem.transform.GetChild(0).GetComponent<TMP_Text>().text = roomName;
            // Set IP text
            roomItem.transform.GetChild(1).GetComponent<TMP_Text>().text = ip;

            // Add tracking component
            var roomData = roomItem.AddComponent<DiscoveredRoomData>();
            roomData.ip = ip;
            roomData.port = port;
            roomData.roomName = roomName;
            roomData.lastSeen = Time.time;

            // Setup button click
            Button btn = roomItem.GetComponent<Button>();
            if (btn != null)
            {
                string capturedIp = ip;
                btn.onClick.AddListener(() => OnRoomClicked(capturedIp));
            }

            _discoveredRooms[key] = roomItem;
        }
    }

    private void OnRoomClicked(string ip)
    {
        if (_isJoining) return;

        // Set the client address on Tugboat transport
        var tugboat = _networkManager.GetComponent<Tugboat>();
        if (tugboat != null)
        {
            tugboat.SetClientAddress(ip);
        }

        // Show multiplayer panel, hide menu
        if (multiplayerPanel != null)
        {
            multiplayerPanel.SetActive(true);
        }
        if (menuPanel != null)
        {
            menuPanel.SetActive(false);
        }

        // Join the room
        JoinRoom();
    }

    private IEnumerator CleanupStaleRooms()
    {
        while (_discoveryCts != null && !_discoveryCts.IsCancellationRequested)
        {
            yield return new WaitForSeconds(scanInterval * 2f);

            float staleThreshold = Time.time - (scanInterval * 3f);
            List<string> keysToRemove = new List<string>();

            foreach (var kvp in _discoveredRooms)
            {
                if (kvp.Value == null)
                {
                    keysToRemove.Add(kvp.Key);
                    continue;
                }

                var roomData = kvp.Value.GetComponent<DiscoveredRoomData>();
                if (roomData != null && roomData.lastSeen < staleThreshold)
                {
                    keysToRemove.Add(kvp.Key);
                    Destroy(kvp.Value);
                }
            }

            foreach (var key in keysToRemove)
            {
                _discoveredRooms.Remove(key);
            }
        }
    }

    #endregion

    #region Room Broadcasting

    private void StartBroadcasting(string ip, string roomName)
    {
        if (_isBroadcasting) return;

        _isBroadcasting = true;
        StartCoroutine(BroadcastRoom(ip, roomName));
    }

    private void StopBroadcasting()
    {
        _isBroadcasting = false;

        try
        {
            _broadcastClient?.Close();
        }
        catch { }
        _broadcastClient = null;
        _cachedSubnetIPs = null;
        _cachedBroadcastAddresses = null;
    }

    private IEnumerator BroadcastRoom(string ip, string roomName)
    {
        _broadcastClient = new UdpClient();
        _broadcastClient.EnableBroadcast = true;

        int gamePort = 7770; // Default FishNet port
        string message = $"YASG_ROOM|{roomName}|{ip}|{gamePort}";
        byte[] data = Encoding.UTF8.GetBytes(message);

        // Cache addresses once at start (they rarely change during a session)
        _cachedBroadcastAddresses = GetAllBroadcastAddresses();
        if (enableSubnetScan)
        {
            _cachedSubnetIPs = GetAllSubnetIPs();
        }

        while (_isBroadcasting)
        {
            // Run all network operations on background thread
            byte[] dataCopy = data;
            int port = discoveryPort;
            var broadcastAddrs = _cachedBroadcastAddresses;
            var subnetIPs = _cachedSubnetIPs;
            var client = _broadcastClient;
            bool doSubnetScan = enableSubnetScan && subnetIPs != null;

            Task.Run(() =>
            {
                // Broadcast to all broadcast addresses
                foreach (var broadcastAddr in broadcastAddrs)
                {
                    try
                    {
                        client?.Send(dataCopy, dataCopy.Length, new IPEndPoint(broadcastAddr, port));
                    }
                    catch { }
                }

                // Global broadcast
                try
                {
                    client?.Send(dataCopy, dataCopy.Length, new IPEndPoint(IPAddress.Broadcast, port));
                }
                catch { }

                // Direct subnet scan for VPNs like ZeroTier
                if (doSubnetScan)
                {
                    foreach (var targetIp in subnetIPs)
                    {
                        try
                        {
                            client?.Send(dataCopy, dataCopy.Length, new IPEndPoint(targetIp, port));
                        }
                        catch { }
                    }
                }
            });

            yield return new WaitForSeconds(scanInterval);
        }
    }

    private List<IPAddress> GetAllSubnetIPs()
    {
        List<IPAddress> subnetIPs = new List<IPAddress>();
        HashSet<string> seenSubnets = new HashSet<string>();

        try
        {
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                var ipProps = ni.GetIPProperties();
                foreach (var uniAddr in ipProps.UnicastAddresses)
                {
                    if (uniAddr.Address.AddressFamily != AddressFamily.InterNetwork)
                        continue;

                    var ip = uniAddr.Address;
                    byte[] ipBytes = ip.GetAddressBytes();

                    // Skip loopback and local-only ranges
                    if (ipBytes[0] == 127)
                        continue;

                    // Skip common LAN ranges (192.168.x.x) - broadcast usually works there
                    // Focus on VPN ranges like 10.x.x.x, 172.16-31.x.x
                    bool isVpnRange = ipBytes[0] == 10 ||
                                      (ipBytes[0] == 172 && ipBytes[1] >= 16 && ipBytes[1] <= 31);

                    if (!isVpnRange)
                        continue;

                    // Create subnet key to avoid duplicate scans
                    string subnetKey = $"{ipBytes[0]}.{ipBytes[1]}.{ipBytes[2]}";
                    if (seenSubnets.Contains(subnetKey))
                        continue;
                    seenSubnets.Add(subnetKey);

                    // Add all IPs in this /24 subnet (skip .0 and .255)
                    for (int i = 1; i < 255; i++)
                    {
                        // Skip our own IP
                        if (i == ipBytes[3])
                            continue;

                        var targetIp = new IPAddress(new byte[] { ipBytes[0], ipBytes[1], ipBytes[2], (byte)i });
                        subnetIPs.Add(targetIp);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"ConnectionUI: Error getting subnet IPs: {ex.Message}");
        }

        return subnetIPs;
    }

    private List<IPAddress> GetAllBroadcastAddresses()
    {
        List<IPAddress> broadcastAddresses = new List<IPAddress>();

        try
        {
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                // Only skip loopback - allow all other interfaces regardless of status/type
                // This ensures ZeroTier and other virtual adapters are included
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                // Skip if address is 127.x.x.x (loopback range)
                var ipProps = ni.GetIPProperties();
                foreach (var uniAddr in ipProps.UnicastAddresses)
                {
                    // Only IPv4
                    if (uniAddr.Address.AddressFamily != AddressFamily.InterNetwork)
                        continue;

                    var ip = uniAddr.Address;

                    // Skip loopback addresses
                    if (ip.GetAddressBytes()[0] == 127)
                        continue;

                    // Calculate broadcast address: IP | ~SubnetMask
                    var mask = uniAddr.IPv4Mask;

                    // If mask is null or 0.0.0.0, assume /24 (255.255.255.0)
                    if (mask == null || mask.Equals(IPAddress.Any))
                    {
                        mask = IPAddress.Parse("255.255.255.0");
                    }

                    byte[] ipBytes = ip.GetAddressBytes();
                    byte[] maskBytes = mask.GetAddressBytes();
                    byte[] broadcastBytes = new byte[4];

                    for (int i = 0; i < 4; i++)
                    {
                        broadcastBytes[i] = (byte)(ipBytes[i] | ~maskBytes[i]);
                    }

                    var broadcastAddr = new IPAddress(broadcastBytes);

                    // Avoid duplicates and 255.255.255.255 (will add separately)
                    if (!broadcastAddresses.Contains(broadcastAddr) && !broadcastAddr.Equals(IPAddress.Broadcast))
                    {
                        broadcastAddresses.Add(broadcastAddr);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"ConnectionUI: Error getting broadcast addresses: {ex.Message}");
        }

        return broadcastAddresses;
    }

    public void RefreshRoomList()
    {
        StopRoomDiscovery();

        // Clear existing rooms
        foreach (var kvp in _discoveredRooms)
        {
            if (kvp.Value != null)
            {
                Destroy(kvp.Value);
            }
        }
        _discoveredRooms.Clear();

        StartRoomDiscovery();
    }

    #endregion
}

// Helper component to store discovered room data
public class DiscoveredRoomData : MonoBehaviour
{
    public string ip;
    public string port;
    public string roomName;
    public float lastSeen;
}

// Simple main thread dispatcher for Unity
public class UnityMainThreadDispatcher : MonoBehaviour
{
    private static UnityMainThreadDispatcher _instance;
    private static readonly Queue<Action> _executionQueue = new Queue<Action>();
    private static bool _initialized = false;

    public static void Initialize()
    {
        if (_initialized) return;

        var go = new GameObject("UnityMainThreadDispatcher");
        _instance = go.AddComponent<UnityMainThreadDispatcher>();
        DontDestroyOnLoad(go);
        _initialized = true;
    }

    private void Awake()
    {
        if (_instance == null)
        {
            _instance = this;
            DontDestroyOnLoad(gameObject);
            _initialized = true;
        }
        else if (_instance != this)
        {
            Destroy(gameObject);
        }
    }

    private void Update()
    {
        lock (_executionQueue)
        {
            while (_executionQueue.Count > 0)
            {
                _executionQueue.Dequeue()?.Invoke();
            }
        }
    }

    public static void Enqueue(Action action)
    {
        if (action == null) return;

        lock (_executionQueue)
        {
            _executionQueue.Enqueue(action);
        }
    }
}