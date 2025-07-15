using FishNet;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MultiplayerUI : MonoBehaviour
{
    public TextMeshProUGUI roomName;

    void Update()
    {
        Debug.Log(gameObject.name);
        if (RoomManager.Instance != null)
        {
            roomName.text = RoomManager.Instance.GetRoomName();
        }
    }

    /// <summary>
    /// Disconnects the client or stops the server and returns to the main menu.
    /// This should be called from a UI button.
    /// </summary>
    public void Disconnect()
    {
        // If running as host, stop the server.
        if (InstanceFinder.ServerManager.Started)
        {
            InstanceFinder.ServerManager.StopConnection(true);
        }
        // If running as client, stop the client.
        else if (InstanceFinder.ClientManager.Started)
        {
            InstanceFinder.ClientManager.StopConnection();
        }

        // After disconnecting, load the main menu scene.
        //SceneManager.LoadScene("Menu");
    }
}
