using UnityEngine;
using TMPro;

// This script goes on the root of your Player UI Prefab.
public class PlayerUIPanel : MonoBehaviour
{
    // --- UI References ---
    [Header("UI Elements")]
    public TextMeshProUGUI playerNameText;
    public TextMeshProUGUI scoreText;
    public TextMeshProUGUI placementText;

    // --- Component References ---
    [Header("Component References")]
    public RealTimePitchDetector pitchDetector;

    // --- Data Source ---
    private PlayerData _assignedPlayerData;
    public PlayerData AssignedPlayerData => _assignedPlayerData; // Public getter for the GameUIManager

    // --- NEW: Gradient Definitions ---
    // We define the colors here so they are easy to change.
    private readonly VertexGradient goldGradient = new VertexGradient(new Color(1, 0.847f, 0), new Color(1, 0.847f, 0), new Color(1, 0.569f, 0), new Color(1, 0.569f, 0));
    private readonly VertexGradient silverGradient = new VertexGradient(Color.white, Color.white, new Color(0.688f, 0.688f, 0.688f), new Color(0.688f, 0.688f, 0.688f));
    private readonly VertexGradient bronzeGradient = new VertexGradient(new Color(1, 0.706f, 0.184f), new Color(1, 0.706f, 0.184f), new Color(0.482f, 0.325f, 0.055f), new Color(0.482f, 0.325f, 0.055f));
    private readonly VertexGradient defaultGradient = new VertexGradient(new Color(0.482f, 0.482f, 0.482f), new Color(0.482f, 0.482f, 0.482f), new Color(0.18f, 0.18f, 0.18f), new Color(0.18f, 0.18f, 0.18f));


    private void Awake()
    {
        if (pitchDetector == null)
        {
            pitchDetector = GetComponentInChildren<RealTimePitchDetector>(true); // Search inactive children too
        }

        if (pitchDetector != null)
        {
            pitchDetector.Setup();
            pitchDetector.enabled = false;
        }
    }

    public void AssignPlayer(PlayerData playerData)
    {
        if (_assignedPlayerData != null)
        {
            _assignedPlayerData.PlayerName.OnChange -= OnPlayerNameChanged;
            _assignedPlayerData.CurrentGameScore.OnChange -= OnCurrentGameScoreChanged;
        }

        _assignedPlayerData = playerData;
        gameObject.SetActive(true);

        _assignedPlayerData.PlayerName.OnChange += OnPlayerNameChanged;
        _assignedPlayerData.CurrentGameScore.OnChange += OnCurrentGameScoreChanged;

        OnPlayerNameChanged("", _assignedPlayerData.PlayerName.Value, false);
        OnCurrentGameScoreChanged(0, _assignedPlayerData.CurrentGameScore.Value, false);

        if (pitchDetector != null)
        {
            if (_assignedPlayerData.IsOwner)
            {
                pitchDetector.enabled = true;
                pitchDetector.ActivateAndStartMicrophone();
            }
            else
            {
                pitchDetector.enabled = false;
            }
        }
    }

    /// <summary>
    /// This method now also handles updating the color based on the placement.
    /// </summary>
    public void UpdatePlacement(int placement)
    {
        if (placementText == null) return;

        placementText.text = GetPlacementString(placement);

        // --- THIS IS THE NEW LOGIC ---
        // Set the color gradient based on the placement value.
        switch (placement)
        {
            case 1:
                placementText.colorGradient = goldGradient;
                break;
            case 2:
                placementText.colorGradient = silverGradient;
                break;
            case 3:
                placementText.colorGradient = bronzeGradient;
                break;
            default:
                placementText.colorGradient = defaultGradient;
                break;
        }
    }

    // A helper method for GameUIManager to clear the slot.
    public void ClearSlot()
    {
        gameObject.SetActive(false);
        if (_assignedPlayerData != null)
        {
            _assignedPlayerData.PlayerName.OnChange -= OnPlayerNameChanged;
            _assignedPlayerData.CurrentGameScore.OnChange -= OnCurrentGameScoreChanged;
        }
        _assignedPlayerData = null;
    }

    public bool IsAssignedTo(PlayerData pd)
    {
        return _assignedPlayerData == pd;
    }

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

    private void OnDestroy()
    {
        if (_assignedPlayerData != null)
        {
            _assignedPlayerData.PlayerName.OnChange -= OnPlayerNameChanged;
            _assignedPlayerData.CurrentGameScore.OnChange -= OnCurrentGameScoreChanged;
        }
    }
}