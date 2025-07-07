using FishNet.Managing;
using FishNet.Transporting;
using UnityEngine;

// This is a regular MonoBehaviour. It does NOT need to be a NetworkBehaviour.
// Attach this to a persistent object in your first scene, like your NetworkManager GameObject.
public class ServerManagerHelper : MonoBehaviour
{
    [Tooltip("The RoomManager prefab that will be spawned as a global object.")]
    public GameObject roomManagerPrefab;

    private NetworkManager _networkManager;
    private bool _roomManagerSpawned = false;

    private void Start()
    {
        _networkManager = FindObjectOfType<NetworkManager>();
        if (_networkManager != null)
        {
            _networkManager.ServerManager.OnServerConnectionState += OnServerConnectionState;
        }
    }

    private void OnDestroy()
    {
        if (_networkManager != null)
        {
            _networkManager.ServerManager.OnServerConnectionState -= OnServerConnectionState;
        }
    }

    private void OnServerConnectionState(ServerConnectionStateArgs args)
    {
        // When the server has fully started...
        if (args.ConnectionState == LocalConnectionState.Started)
        {
            // ...and if we haven't spawned the manager yet...
            if (!_roomManagerSpawned)
            {
                Debug.Log("Server has started. Spawning RoomManager...");
                _roomManagerSpawned = true;

                // Tell the NetworkManager to spawn our global prefab.
                // FishNet will see "IsGlobal" is checked and handle everything for us.
                GameObject temp = Instantiate(roomManagerPrefab);
                _networkManager.ServerManager.Spawn(temp);
            }
        }
        // If the server stops, reset the flag so we can spawn it again if the server restarts.
        else if (args.ConnectionState == LocalConnectionState.Stopped)
        {
            _roomManagerSpawned = false;
        }
    }
}