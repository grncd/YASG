// PlayerUIController.cs
using UnityEngine;
using TMPro;

public class PlayerUIController : MonoBehaviour
{
    // --- UI References for this specific slot ---
    public TextMeshProUGUI playerNameText;
    public TextMeshProUGUI scoreText;
    public TextMeshProUGUI placementText;

    // --- The Networked Data Source ---
    private PlayerData _assignedPlayerData;

    /// <summary>
    /// Called by the GameUIManager to link this UI slot to a networked player.
    /// </summary>
    public void AssignPlayer(PlayerData playerData)
    {
        // If we were already assigned to a player, unsubscribe from their old events first.
        if (_assignedPlayerData != null)
        {
            _assignedPlayerData.PlayerName.OnChange -= OnPlayerNameChanged;
            _assignedPlayerData.CurrentGameScore.OnChange -= OnCurrentGameScoreChanged;
        }

        // Store the new player data reference
        _assignedPlayerData = playerData;
        gameObject.SetActive(true);

        // Subscribe to the new player's data changes
        _assignedPlayerData.PlayerName.OnChange += OnPlayerNameChanged;
        _assignedPlayerData.CurrentGameScore.OnChange += OnCurrentGameScoreChanged;

        // Set the initial UI values
        OnPlayerNameChanged("", _assignedPlayerData.PlayerName.Value, false);
        OnCurrentGameScoreChanged(0, _assignedPlayerData.CurrentGameScore.Value, false);
    }

    /// <summary>
    /// Clears the assignment and hides this UI slot.
    /// </summary>
    public void ClearSlot()
    {
        // Unsubscribe from events to prevent memory leaks
        if (_assignedPlayerData != null)
        {
            _assignedPlayerData.PlayerName.OnChange -= OnPlayerNameChanged;
            _assignedPlayerData.CurrentGameScore.OnChange -= OnCurrentGameScoreChanged;
        }

        _assignedPlayerData = null;
        gameObject.SetActive(false);
    }

    // --- OnChange Callbacks ---
    private void OnPlayerNameChanged(string oldName, string newName, bool asServer)
    {
        if (playerNameText != null)
        {
            playerNameText.text = newName;
        }
    }

    private void OnCurrentGameScoreChanged(int oldScore, int newScore, bool asServer)
    {
        if (scoreText != null)
        {
            scoreText.text = newScore.ToString("#,#");
        }
    }

    /// <summary>
    /// This method will be called by the GameUIManager after it calculates all placements.
    /// </summary>
    public void UpdatePlacement(int placement)
    {
        if (placementText != null)
        {
            placementText.text = GetPlacementString(placement);
        }
    }

    private string GetPlacementString(int placement)
    {
        if (placement <= 0) return "";
        switch (placement)
        {
            case 1: return "1st";
            case 2: return "2nd";
            case 3: return "3rd";
            default: return placement + "th";
        }
    }

    // This is called by Unity when the object is destroyed.
    private void OnDestroy()
    {
        // Final cleanup just in case OnDisable wasn't called.
        if (_assignedPlayerData != null)
        {
            _assignedPlayerData.PlayerName.OnChange -= OnPlayerNameChanged;
            _assignedPlayerData.CurrentGameScore.OnChange -= OnCurrentGameScoreChanged;
        }
    }
}