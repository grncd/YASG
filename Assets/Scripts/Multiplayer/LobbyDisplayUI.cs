using MPUIKIT;
using System.Collections;
using System.Collections.Generic;
using System.Linq; // Required for Linq
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class LobbyDisplayUI : MonoBehaviour
{
    // --- UI References (No changes here) ---
    public TextMeshProUGUI currentRoomNameDisplay;
    public Transform playerListContainer;
    public GameObject playerListItemPrefab;
    public TextMeshProUGUI creatorNameDisplay;
    public TextMeshProUGUI ipAddress;

    [Header("Song Display")]
    public TextMeshProUGUI songTitleText;
    public TextMeshProUGUI songArtistText;
    public TextMeshProUGUI songLengthText;
    public TextMeshProUGUI songLinkText;
    public MPImage coverImage1;
    public MPImage coverImage2;

    [Header("Lobby Buttons")]
    public Button readyButton;
    public Button unreadyButton;
    public Button startGameButton;

    // --- Private Fields (No changes here) ---
    private readonly Dictionary<PlayerData, GameObject> _playerListItems = new Dictionary<PlayerData, GameObject>();
    private Coroutine _refreshCoroutine;
    public float checkInterval = 0.5f;

    // --- NEW OnEnable ---
    private void OnEnable()
    {
        Debug.Log("LobbyDisplayUI: OnEnable called.");
        PlayerPrefs.SetInt("multiplayer", 1);

        ipAddress.text = PlayerPrefs.GetString("masterIp");

        // Subscribe to the static event to be notified when RoomManager is spawned.
        RoomManager.OnInstanceAvailable += HandleRoomManagerAvailable;

        // It's possible RoomManager was already spawned before this UI was enabled.
        // We check if it's available right away to handle that case.
        if (RoomManager.Instance != null)
        {
            HandleRoomManagerAvailable(RoomManager.Instance);
        }

        // The player list refresh logic is independent and can start immediately.
        if (_refreshCoroutine != null) StopCoroutine(_refreshCoroutine);
        _refreshCoroutine = StartCoroutine(RefreshPlayerListPeriodically());

        // Setup button listeners
        readyButton.onClick.AddListener(() => SetReadyStatus(true));
        unreadyButton.onClick.AddListener(() => SetReadyStatus(false));
        startGameButton.onClick.AddListener(OnStartGameButtonClicked);
    }

    // --- NEW OnDisable ---
    private void OnDisable()
    {
        Debug.Log("LobbyDisplayUI: OnDisable called.");

        // Unsubscribe from the static event to prevent memory leaks.
        RoomManager.OnInstanceAvailable -= HandleRoomManagerAvailable;

        // Unsubscribe from the instance-specific events if the instance still exists.
        if (RoomManager.Instance != null)
        {
            RoomManager.Instance.OnRoomNameUpdated.RemoveListener(UpdateRoomNameDisplay);
            RoomManager.Instance.OnCreatorNameUpdated.RemoveListener(UpdateCreatorNameDisplay);
            RoomManager.Instance.OnSelectedSongUpdated.RemoveListener(UpdateSongDisplay);
        }

        if (_refreshCoroutine != null)
        {
            StopCoroutine(_refreshCoroutine);
        }
        ClearPlayerList();
    }

    // --- NEW Handler Method ---
    /// <summary>
    /// This method is called ONLY when we know for sure that RoomManager.Instance is not null.
    /// It subscribes to all the necessary events and sets the initial UI values.
    /// </summary>
    private void HandleRoomManagerAvailable(RoomManager rmInstance)
    {
        Debug.Log("LobbyDisplayUI: RoomManager instance is available. Subscribing to events and refreshing displays.");

        // Unsubscribe first to prevent double-subscription if this gets called more than once.
        rmInstance.OnRoomNameUpdated.RemoveListener(UpdateRoomNameDisplay);
        rmInstance.OnCreatorNameUpdated.RemoveListener(UpdateCreatorNameDisplay);
        rmInstance.OnSelectedSongUpdated.RemoveListener(UpdateSongDisplay);

        // Subscribe to all relevant events from the RoomManager.
        rmInstance.OnRoomNameUpdated.AddListener(UpdateRoomNameDisplay);
        rmInstance.OnCreatorNameUpdated.AddListener(UpdateCreatorNameDisplay);
        rmInstance.OnSelectedSongUpdated.AddListener(UpdateSongDisplay);

        // Set initial values for all UI elements with the now-guaranteed-to-be-valid instance.
        UpdateRoomNameDisplay(rmInstance.GetRoomName());
        UpdateCreatorNameDisplay(rmInstance.GetCreatorName());
        UpdateSongDisplay(rmInstance.SelectedSong.Value);
    }



    private void OnStartGameButtonClicked()
    {
        if (PlayerData.LocalPlayerInstance != null && PlayerData.LocalPlayerInstance.IsHost.Value)
        {
            PlayerData.LocalPlayerInstance.RequestStartGame_ServerRpc();
            startGameButton.interactable = false;
        }
    }

    private void SetReadyStatus(bool isReady)
    {
        if (PlayerData.LocalPlayerInstance != null)
        {
            PlayerData.LocalPlayerInstance.RequestSetReadyStatus_ServerRpc(isReady);
        }
    }

    private IEnumerator RefreshPlayerListPeriodically()
    {
        while (true)
        {
            List<PlayerData> allPlayers = new List<PlayerData>(FindObjectsOfType<PlayerData>());

            foreach (PlayerData player in allPlayers)
            {
                if (!_playerListItems.ContainsKey(player))
                {
                    GameObject itemGO = Instantiate(playerListItemPrefab, playerListContainer);
                    itemGO.GetComponent<PlayerListItemUI>().Setup(player);
                    _playerListItems[player] = itemGO;
                }
            }

            List<PlayerData> playersToRemove = _playerListItems.Keys.Where(p => p == null).ToList();
            foreach (PlayerData player in playersToRemove)
            {
                Destroy(_playerListItems[player]);
                _playerListItems.Remove(player);
            }

            CheckForAllPlayersReady(allPlayers);

            yield return new WaitForSeconds(checkInterval);
        }
    }

    private void CheckForAllPlayersReady(List<PlayerData> players)
    {
        if (PlayerData.LocalPlayerInstance == null || !PlayerData.LocalPlayerInstance.IsHost.Value)
        {
            startGameButton.gameObject.SetActive(false);
            return;
        }

        bool allReady = players.Count > 0 && players.All(p => p.IsReady.Value);

        if (allReady)
        {
            startGameButton.gameObject.SetActive(true);
            unreadyButton.gameObject.SetActive(false);
        }
        else
        {
            startGameButton.gameObject.SetActive(false);
            if (PlayerData.LocalPlayerInstance != null)
            {
                unreadyButton.gameObject.SetActive(PlayerData.LocalPlayerInstance.IsReady.Value);
                readyButton.gameObject.SetActive(!PlayerData.LocalPlayerInstance.IsReady.Value);
            }
        }
    }

    private void ClearPlayerList()
    {
        foreach (GameObject item in _playerListItems.Values)
        {
            Destroy(item);
        }
        _playerListItems.Clear();
    }

    private void UpdateSongDisplay(SongData song)
    {
        if (songTitleText == null) return;

        if (string.IsNullOrEmpty(song.Title))
        {
            songTitleText.text = "Select Song";
            songArtistText.text = "Click here to select!";
            songLengthText.text = "----------------------------------";
            songLinkText.text = "";
            return;
        }
        PlayerPrefs.SetString("currentSong", song.Title);
        songTitleText.text = song.Title;
        songArtistText.text = song.Artist;
        songLengthText.text = "Song Length: " + song.Length;
        songLinkText.text = song.Link;

        if (!string.IsNullOrEmpty(song.CoverUrl))
        {
            StartCoroutine(DownloadImage(song.CoverUrl));
        }
    }

    private void UpdateRoomNameDisplay(string newName)
    {
        if (currentRoomNameDisplay != null)
        {
            currentRoomNameDisplay.text = newName;
        }
    }

    private void UpdateCreatorNameDisplay(string newCreatorName)
    {
        if (creatorNameDisplay != null)
        {
            creatorNameDisplay.text = "Created by: " + newCreatorName;
        }
    }

    private IEnumerator DownloadImage(string imageUrl)
    {
        UnityWebRequest request = UnityWebRequestTexture.GetTexture(imageUrl);
        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            Texture2D texture = ((DownloadHandlerTexture)request.downloadHandler).texture;
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));

            if (coverImage1 != null) coverImage1.sprite = sprite;
            if (coverImage2 != null) coverImage2.sprite = sprite;
        }
        else
        {
            Debug.LogError("Failed to download cover image: " + request.error);
        }
    }
}