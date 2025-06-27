// PlayerListItemUI.cs
using UnityEngine;
using TMPro;
using MPUIKIT; // Assuming this is for your MPImage
using FishNet.Object;

public class PlayerListItemUI : MonoBehaviour
{
    // --- UI References ---
    // Assign these in the Inspector of your playerPrefab
    public TextMeshProUGUI playerNameText;
    public MPImage xpFillImage;
    public TextMeshProUGUI difficultyText;
    public TextMeshProUGUI levelText;
    public TextMeshProUGUI totalScoreText;
    public GameObject readyIndicator;
    public GameObject hostIndicator;
    public GameObject masterProcessorIndicator;

    // --- Data Reference ---
    private PlayerData _playerData;

    /// <summary>
    /// This is the main setup method, called by LobbyDisplayUI when this item is created.
    /// </summary>
    public void Setup(PlayerData playerData)
    {
        // Store the reference to the player's data
        _playerData = playerData;

        // --- Subscribe to OnChange events for THIS player ---
        // This is the core of the event-driven update
        _playerData.PlayerName.OnChange += OnPlayerNameChanged;
        _playerData.Level.OnChange += OnLevelChanged;
        _playerData.PercentageToNextLevel.OnChange += OnPercentageChanged;
        _playerData.TotalScore.OnChange += OnTotalScoreChanged;
        _playerData.Difficulty.OnChange += OnDifficultyChanged;
        _playerData.IsReady.OnChange += OnIsReadyChanged;
        _playerData.IsHost.OnChange += OnIsHostChanged;
        _playerData.IsMasterProcessor.OnChange += OnIsMasterProcessorChanged;

        // --- Set Initial Values ---
        // Call all the update methods once to populate the UI with the current data
        OnPlayerNameChanged("", _playerData.PlayerName.Value, false);
        OnLevelChanged(0, _playerData.Level.Value, false);
        OnPercentageChanged(0, _playerData.PercentageToNextLevel.Value, false);
        OnTotalScoreChanged(0, _playerData.TotalScore.Value, false);
        OnDifficultyChanged(0, 1, false);
        OnIsReadyChanged(false, _playerData.IsReady.Value, false);
        OnIsHostChanged(false, _playerData.IsHost.Value, false);
        OnIsMasterProcessorChanged(false, _playerData.IsMasterProcessor.Value, false);
    }

    /// <summary>
    /// Called when this UI object is destroyed.
    /// It's crucial to unsubscribe to prevent memory leaks.
    /// </summary>
    private void OnDestroy()
    {
        // Unsubscribe from all events if our PlayerData reference is valid
        if (_playerData != null)
        {
            _playerData.PlayerName.OnChange -= OnPlayerNameChanged;
            _playerData.Level.OnChange -= OnLevelChanged;
            _playerData.PercentageToNextLevel.OnChange -= OnPercentageChanged;
            _playerData.TotalScore.OnChange -= OnTotalScoreChanged;
            _playerData.IsReady.OnChange -= OnIsReadyChanged;
            _playerData.IsHost.OnChange -= OnIsHostChanged;
            _playerData.IsMasterProcessor.OnChange -= OnIsMasterProcessorChanged;
        }
    }

    // --- Individual Update Methods ---
    // Each of these is called automatically by the SyncVar's OnChange event

    private void OnPlayerNameChanged(string oldName, string newName, bool asServer)
    {
        playerNameText.text = newName;

    }

    private void OnLevelChanged(int oldLevel, int newLevel, bool asServer)
    {
        levelText.text = newLevel.ToString();
    }

    private void OnPercentageChanged(float oldPercent, float newPercent, bool asServer)
    {
        xpFillImage.fillAmount = newPercent;
    }

    private void OnTotalScoreChanged(int oldScore, int newScore, bool asServer)
    {
        if(newScore != 0)
        {
            totalScoreText.text = "Total Score: " + newScore.ToString("#,#");
        }
        else
        {
            totalScoreText.text = "Total Score: 0";
        }
    }

    private void OnDifficultyChanged(int oldDiff, int newDiff, bool asServer)
    {
        if(newDiff == 0)
        {
            difficultyText.text = "Easy";
            difficultyText.color = new Color(0.3042734f, 1f, 0.2588235f);
        }else if (newDiff == 1)
        {
            difficultyText.text = "Medium";
            difficultyText.color = new Color(0.9826014f, 1f, 0.259434f);
        }else if (newDiff == 2)
        {
            difficultyText.text = "Hard";
            difficultyText.color = new Color(1f, 0.2588235f, 0.2657269f);
        }
    }

    private void OnIsReadyChanged(bool oldStatus, bool newStatus, bool asServer)
    {
        readyIndicator.SetActive(newStatus);
    }

    private void OnIsHostChanged(bool oldStatus, bool newStatus, bool asServer)
    {
        hostIndicator.SetActive(newStatus);
    }

    private void OnIsMasterProcessorChanged(bool oldStatus, bool newStatus, bool asServer)
    {
        masterProcessorIndicator.SetActive(newStatus);
    }

    public void RequestSetHost()
    {
        PlayerData.LocalPlayerInstance.RequestTransferHost_ServerRpc(GameObject.Find("Player_" + _playerData.PlayerName.Value).GetComponent<NetworkObject>());
    }

    public void RequestSetMasterProcessor()
    {
        PlayerData.LocalPlayerInstance.RequestTransferMasterProcessor_ServerRpc(GameObject.Find("Player_" + _playerData.PlayerName.Value).GetComponent<NetworkObject>());
    }
}