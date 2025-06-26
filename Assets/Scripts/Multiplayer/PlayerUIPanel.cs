using UnityEngine;
using TMPro;
using System.Collections; // Required for using Coroutines

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

    // --- Gradient Definitions ---
    private readonly VertexGradient goldGradient = new VertexGradient(new Color(1, 0.847f, 0), new Color(1, 0.847f, 0), new Color(1, 0.569f, 0), new Color(1, 0.569f, 0));
    private readonly VertexGradient silverGradient = new VertexGradient(Color.white, Color.white, new Color(0.688f, 0.688f, 0.688f), new Color(0.688f, 0.688f, 0.688f));
    private readonly VertexGradient bronzeGradient = new VertexGradient(new Color(1, 0.706f, 0.184f), new Color(1, 0.706f, 0.184f), new Color(0.482f, 0.325f, 0.055f), new Color(0.482f, 0.325f, 0.055f));
    private readonly VertexGradient defaultGradient = new VertexGradient(new Color(0.482f, 0.482f, 0.482f), new Color(0.482f, 0.482f, 0.18f, 0.18f), new Color(0.18f, 0.18f, 0.18f), new Color(0.18f, 0.18f, 0.18f));


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
                // --- START OF MODIFICATIONS ---
                // We use a coroutine to handle the startup sequence safely.
                // This ensures the microphone is fully started before the pitch detector tries to use it.
                StartCoroutine(InitializeLocalPlayerSystems());
                // --- END OF MODIFICATIONS ---
            }
            else
            {
                pitchDetector.enabled = false;
            }
        }
    }

    /// <summary>
    /// This coroutine safely starts the microphone and then activates the pitch detector.
    /// </summary>
    private IEnumerator InitializeLocalPlayerSystems()
    {
        // 1. Enable the pitch detector component so it can run its logic.
        pitchDetector.enabled = true;

        // 2. Tell the SharedMicrophoneManager to start the microphone for this player.
        // We use the playerIndex from the pitchDetector component itself.
        SharedMicrophoneManager.Instance.StartMicrophone(pitchDetector.playerIndex);

        // 3. Wait until the SharedMicrophoneManager confirms that the microphone is actually recording.
        // This is crucial to prevent errors.
        yield return new WaitUntil(() => SharedMicrophoneManager.Instance.IsRecording);

        // 4. Now that the mic is confirmed to be running, activate the pitch detector.
        pitchDetector.Activate();
    }


    /// <summary>
    /// This method now also handles updating the color based on the placement.
    /// </summary>
    public void UpdatePlacement(int placement)
    {
        if (placementText == null) return;

        placementText.text = GetPlacementString(placement);

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