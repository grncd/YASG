using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using MPUIKIT;

public class ResultsScreen : MonoBehaviour
{
    // This nested class holds all the UI elements for one player's results panel.
    [System.Serializable]
    public class PlayerResultPanel
    {
        public GameObject panelRoot;
        public TextMeshProUGUI playerNameText;
        public TextMeshProUGUI scoreText;
        public TextMeshProUGUI placementText;
        public Transform starsLocation;
        public TextMeshProUGUI perfectsText;
        public TextMeshProUGUI greatsText;
        public TextMeshProUGUI mehsText;
        public MPImage progressBar;
        public TextMeshProUGUI levelText;
        public ParticleSystem levelUpParticles;
    }

    [Header("UI Setup")]
    public List<PlayerResultPanel> resultPanels;
    public GameObject starPrefab;
    public TextMeshProUGUI btmLabel;

    [Header("Audio")]
    public AudioSource levelFX;
    public AudioSource levelUpFX;
    public AudioSource starFX;
    private string currentSongUrl;

    // We define the color gradients here for easy access.
    private readonly VertexGradient goldGradient = new VertexGradient(new Color(1, 0.847f, 0), new Color(1, 0.847f, 0), new Color(1, 0.569f, 0), new Color(1, 0.569f, 0));
    private readonly VertexGradient silverGradient = new VertexGradient(Color.white, Color.white, new Color(0.688f, 0.688f, 0.688f), new Color(0.688f, 0.688f, 0.688f));
    private readonly VertexGradient bronzeGradient = new VertexGradient(new Color(1, 0.706f, 0.184f), new Color(1, 0.706f, 0.184f), new Color(0.482f, 0.325f, 0.055f), new Color(0.482f, 0.325f, 0.055f));
    private readonly VertexGradient defaultGradient = new VertexGradient(new Color(0.482f, 0.482f, 0.482f), new Color(0.482f, 0.482f, 0.18f, 0.18f), new Color(0.18f, 0.18f, 0.18f), new Color(0.18f, 0.18f, 0.18f));


    void Start()
    {
        Application.targetFrameRate = -1;

        currentSongUrl = PlayerPrefs.GetString("currentSongURL");
        if (string.IsNullOrEmpty(currentSongUrl))
        {
            Debug.LogError("Could not find 'currentSongURL' in PlayerPrefs. Highscores will not be saved.");
        }
        PlayerPrefs.SetInt("firstPlay", 1);
        if (PlayerPrefs.GetInt("multiplayer") == 1)
        {
            StartMultiplayerResults();
        }
        else
        {
            StartLocalResults();
        }
    }

    // ==========================================================
    // MULTIPLAYER RESULTS LOGIC
    // ==========================================================
    private void StartMultiplayerResults()
    {
        ProfileManager.Instance.SetProfileTotalScore(ProfileManager.Instance.GetActiveProfiles()[0].name, PlayerData.LocalPlayerInstance.TotalScore.Value);

        Debug.Log("Starting Results Screen in MULTIPLAYER mode.");

        if (PlayerData.LocalPlayerInstance.IsHost.Value == false)
        {
            btmLabel.text = "Waiting for host...";
        }

        // Deactivate all panels initially.
        foreach (var panel in resultPanels) panel.panelRoot.SetActive(false);

        // Find all PlayerData objects that have persisted from the game scene.
        List<PlayerData> allPlayers = new List<PlayerData>(FindObjectsOfType<PlayerData>());

        // We will populate based on the ClientId to keep the slots consistent.
        foreach (PlayerData player in allPlayers)
        {
            int slotIndex = player.Owner.ClientId;
            if (slotIndex < resultPanels.Count)
            {
                PlayerResultPanel panel = resultPanels[slotIndex];
                panel.panelRoot.SetActive(true);

                // Populate all UI fields using the synced data from PlayerData.
                panel.playerNameText.text = player.PlayerName.Value;
                panel.scoreText.text = player.CurrentGameScore.Value.ToString("#,#");
                panel.placementText.text = GetPlacementString(player.Placement.Value);

                SetPlacementColor(panel.placementText, player.Placement.Value);

                panel.perfectsText.text = "x" + player.Perfects.Value;
                panel.greatsText.text = "x" + player.Greats.Value;
                panel.mehsText.text = "x" + player.Mehs.Value;

                if (!string.IsNullOrEmpty(currentSongUrl))
                {
                    HighscoreManager.Instance.SetHighscore(
                        currentSongUrl,
                        player.PlayerName.Value,
                        player.CurrentGameScore.Value,
                        player.Stars.Value
                    );
                }

                // --- Level Up Animation ---
                // We calculate the "before" progress based on the final "after" state.
                float xpGainedRatio = (float)player.CurrentGameScore.Value / GetRequiredXPForLevel(player.Level.Value);
                float previousLevelProgress = player.PercentageToNextLevel.Value - xpGainedRatio;

                bool leveledUp = previousLevelProgress < 0;
                if (leveledUp)
                {
                    previousLevelProgress += 1.0f; // Wrap around from the previous level
                }

                StartCoroutine(AnimatePlayerXP(panel, player.Level.Value, previousLevelProgress, player.PercentageToNextLevel.Value, leveledUp));

                // Animate stars
                StartCoroutine(AnimateStars(panel.starsLocation, player.Stars.Value));
            }
        }
    }

    // ==========================================================
    // LOCAL/OFFLINE RESULTS LOGIC
    // ==========================================================
    private void StartLocalResults()
    {
        Debug.Log("Starting Results Screen in LOCAL mode.");

        for (int i = 0; i < resultPanels.Count; i++)
        {
            int playerId = i + 1; // Local player IDs are 1-4
            PlayerResultPanel panel = resultPanels[i];

            if (PlayerPrefs.GetInt("Player" + playerId) == 1)
            {
                panel.panelRoot.SetActive(true);

                string profileName = PlayerPrefs.GetString("Player" + playerId + "Name");
                int achievedScore = PlayerPrefs.GetInt("Player" + playerId + "Score");
                int placement = PlayerPrefs.GetInt("Player" + playerId + "Placement") + 1;
                int stars = PlayerPrefs.GetInt("Player" + playerId + "Stars"); 

                if (!string.IsNullOrEmpty(currentSongUrl))
                {
                    HighscoreManager.Instance.SetHighscore(
                        currentSongUrl,
                        profileName,
                        achievedScore,
                        stars
                    );
                }

                panel.playerNameText.text = profileName;
                panel.scoreText.text = achievedScore.ToString("#,#");
                panel.placementText.text = GetPlacementString(placement);
                SetPlacementColor(panel.placementText, placement);

                panel.perfectsText.text = "x" + PlayerPrefs.GetInt("Player" + playerId + "Perfect");
                panel.greatsText.text = "x" + PlayerPrefs.GetInt("Player" + playerId + "Great");
                panel.mehsText.text = "x" + PlayerPrefs.GetInt("Player" + playerId + "Meh");

                // The local version does the profile modification directly.
                Profile profile = ProfileManager.Instance.GetProfileByName(profileName);
                if (profile != null)
                {
                    int formerScore = profile.totalScore;
                    int formerLevel = profile.level;

                    // This method handles the level up logic internally for the local profile
                    ProfileManager.Instance.AddProfileTotalScore(profileName, achievedScore);

                    profile = ProfileManager.Instance.GetProfileByName(profileName); // Re-fetch to get updated values

                    float previousPercent = ((float)(formerScore - GetXpForPreviousLevels(formerLevel)) / GetRequiredXPForLevel(formerLevel));

                    StartCoroutine(AnimatePlayerXP(panel, profile.level, previousPercent, profile.progressRemaining / 100f, profile.level > formerLevel));
                    StartCoroutine(AnimateStars(panel.starsLocation, stars));
                }
            }
            else
            {
                panel.panelRoot.SetActive(false);
            }
        }
    }

    // ==========================================================
    // SHARED ANIMATION AND HELPER LOGIC
    // ==========================================================
    private IEnumerator AnimatePlayerXP(PlayerResultPanel panel, int finalLevel, float fromProgress, float toProgress, bool leveledUp)
    {
        // Total duration for a full 0-100% bar fill.
        // The actual animation phases will be shorter based on their percentage.
        float fullBarAnimationDuration = 1.5f;

        // Set initial state
        panel.levelText.text = leveledUp ? (finalLevel - 1).ToString() : finalLevel.ToString();
        panel.progressBar.fillAmount = fromProgress;

        // Optional: A delay before the animation starts. 3.5s is quite long, you might want to reduce it.
        yield return new WaitForSeconds(3.5f); // Reduced for better feel, change as needed.

        if (leveledUp)
        {
            // --- Phase 1: Animate from current progress to 100% ---
            float elapsed = 0f;
            float firstPhaseDuration = fullBarAnimationDuration / 2;

            if (levelUpFX != null) levelUpFX.Play(); // Play one-shot sound effect

            if (firstPhaseDuration > 0)
            {
                levelFX.Play(); // Start the looping sound effect
                while (elapsed < firstPhaseDuration)
                {
                    float t = elapsed / firstPhaseDuration;
                    // CHANGED: Use EaseInSine for a "build-up" effect, accelerating towards the end.
                    panel.progressBar.fillAmount = Mathf.Lerp(fromProgress, 1f, EaseInSine(t));
                    elapsed += Time.deltaTime;
                    yield return null;
                }
            }

            // --- Level Up Moment ---
            panel.progressBar.fillAmount = 0f; // Reset bar for the new level
            panel.levelText.text = finalLevel.ToString();
            if (panel.levelUpParticles != null) panel.levelUpParticles.Play(); // Play one-shot particle effect

            // --- Phase 2: Animate from 0% to the new progress ---
            elapsed = 0f;
            // Duration is proportional to the final progress
            float secondPhaseDuration = fullBarAnimationDuration / 2;

            if (secondPhaseDuration > 0)
            {
                if (!levelFX.isPlaying) levelFX.Play(); // Ensure looping sound is playing
                while (elapsed < secondPhaseDuration)
                {
                    float t = elapsed / secondPhaseDuration;
                    // CHANGED: Use EaseOutSine for a "burst" effect, starting fast and slowing down.
                    panel.progressBar.fillAmount = Mathf.Lerp(0f, toProgress, EaseOutSine(t));
                    elapsed += Time.deltaTime;
                    yield return null;
                }
            }
        }
        else
        {
            // --- Standard Animation (No Level Up) ---
            float elapsed = 0f;
            float standardDuration = fullBarAnimationDuration * (toProgress - fromProgress); // Proportional duration
            if (!levelFX.isPlaying) levelFX.Play();

            while (elapsed < standardDuration)
            {
                float t = elapsed / standardDuration;
                // EaseInOutSine is good for a standard, smooth animation
                panel.progressBar.fillAmount = Mathf.Lerp(fromProgress, toProgress, EaseInOutSine(t));
                elapsed += Time.deltaTime;
                yield return null;
            }
        }

        // --- Finalization ---
        // Ensure the progress bar and text end at the exact final values
        panel.progressBar.fillAmount = toProgress;
        panel.levelText.text = finalLevel.ToString(); // Ensure final level is displayed
        if (levelFX.isPlaying) levelFX.Stop();
    }

    private IEnumerator AnimateStars(Transform starParent, int starCount)
    {
        // Clear any existing stars first
        foreach (Transform child in starParent) Destroy(child.gameObject);

        yield return new WaitForSeconds(2.5f);
        for (int i = 0; i < starCount; i++)
        {
            Instantiate(starPrefab, starParent);
            if (starFX != null)
            {
                starFX.pitch = 1.0f + (i * 0.1f);
                starFX.Play();
            }
            yield return new WaitForSeconds(0.3f);
        }
    }

    private void SetPlacementColor(TextMeshProUGUI placementText, int placement)
    {
        if (placementText == null) return;
        switch (placement)
        {
            case 1: placementText.colorGradient = goldGradient; break;
            case 2: placementText.colorGradient = silverGradient; break;
            case 3: placementText.colorGradient = bronzeGradient; break;
            default: placementText.colorGradient = defaultGradient; break;
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

    private int GetRequiredXPForLevel(int level)
    {
        return Mathf.RoundToInt(1350000f + 500000f * (level - 1));
    }

    private int GetXpForPreviousLevels(int level)
    {
        int totalXp = 0;
        for (int i = 1; i < level; i++)
        {
            totalXp += GetRequiredXPForLevel(i);
        }
        return totalXp;
    }

    /// <summary>
    /// Easing function that starts slow and accelerates. Perfect for building anticipation.
    /// </summary>
    private float EaseInSine(float x)
    {
        return 1 - Mathf.Cos((x * Mathf.PI) / 2);
    }

    /// <summary>
    /// Easing function that starts fast and decelerates. Perfect for a satisfying burst.
    /// </summary>
    private float EaseOutSine(float x)
    {
        return Mathf.Sin((x * Mathf.PI) / 2);
    }

    /// <summary>
    /// Easing function that starts slow, speeds up, and then ends slow. Good for general purpose animations.
    /// </summary>
    private float EaseInOutSine(float x)
    {
        return -(Mathf.Cos(Mathf.PI * x) - 1) / 2;
    }

    public void BackToMenu()
    {
        PlayerPrefs.SetInt("fromMP", 1);
        // Check if we are in a multiplayer session.
        if (PlayerPrefs.GetInt("multiplayer") == 1)
        {
            // In multiplayer, only the host can trigger the return to the lobby.
            if (PlayerData.LocalPlayerInstance != null && PlayerData.LocalPlayerInstance.IsHost.Value)
            {
                Debug.Log("Host is requesting to return to the lobby.");
                PlayerData.LocalPlayerInstance.RequestReturnToLobby_ServerRpc();
            }
            else
            {
                Debug.Log("Only the host can return to the lobby. Non-host button should be disabled.");
                // Optionally, you could have non-hosts just leave the room, but for now, we do nothing.
            }
        }
        else
        {
            // This is the original single-player logic.
            SceneManager.LoadScene("Menu", LoadSceneMode.Single);
        }
    }
}