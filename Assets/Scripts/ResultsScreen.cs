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
    }

    [Header("UI Setup")]
    public List<PlayerResultPanel> resultPanels;
    public GameObject starPrefab;

    [Header("Audio")]
    public AudioSource levelFX;
    public AudioSource levelUpFX;
    public AudioSource starFX;

    // We define the color gradients here for easy access.
    private readonly VertexGradient goldGradient = new VertexGradient(new Color(1, 0.847f, 0), new Color(1, 0.847f, 0), new Color(1, 0.569f, 0), new Color(1, 0.569f, 0));
    private readonly VertexGradient silverGradient = new VertexGradient(Color.white, Color.white, new Color(0.688f, 0.688f, 0.688f), new Color(0.688f, 0.688f, 0.688f));
    private readonly VertexGradient bronzeGradient = new VertexGradient(new Color(1, 0.706f, 0.184f), new Color(1, 0.706f, 0.184f), new Color(0.482f, 0.325f, 0.055f), new Color(0.482f, 0.325f, 0.055f));
    private readonly VertexGradient defaultGradient = new VertexGradient(new Color(0.482f, 0.482f, 0.482f), new Color(0.482f, 0.482f, 0.18f, 0.18f), new Color(0.18f, 0.18f, 0.18f), new Color(0.18f, 0.18f, 0.18f));


    void Start()
    {
        Application.targetFrameRate = -1;

        // --- DUAL-MODE LOGIC ---
        // Check PlayerPrefs to see if we are in multiplayer mode.
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
                    StartCoroutine(AnimateStars(panel.starsLocation, PlayerPrefs.GetInt("Player" + playerId + "Stars")));
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
        // Animate the progress bar and level text for a given panel.
        // For simplicity, we can show the final level immediately.
        panel.levelText.text = finalLevel.ToString();
        panel.progressBar.fillAmount = fromProgress;

        yield return new WaitForSeconds(3.5f);

        if (leveledUp) levelUpFX.Play();
        else levelFX.Play();

        float duration = 1.5f;
        float elapsed = 0f;

        if (leveledUp)
        {
            // Animate to 100%
            float firstPhaseDuration = duration * (1f - fromProgress); // Duration based on how much is left
            while (elapsed < firstPhaseDuration)
            {
                panel.progressBar.fillAmount = Mathf.Lerp(fromProgress, 1f, elapsed / firstPhaseDuration);
                elapsed += Time.deltaTime;
                yield return null;
            }
            panel.progressBar.fillAmount = 0f; // Reset for the new level
            elapsed = 0f; // Reset timer for the next phase

            // Animate from 0% to the new progress
            float secondPhaseDuration = duration * toProgress;
            while (elapsed < secondPhaseDuration)
            {
                panel.progressBar.fillAmount = Mathf.Lerp(0f, toProgress, elapsed / secondPhaseDuration);
                elapsed += Time.deltaTime;
                yield return null;
            }
        }
        else
        {
            while (elapsed < duration)
            {
                panel.progressBar.fillAmount = Mathf.Lerp(fromProgress, toProgress, elapsed / duration);
                elapsed += Time.deltaTime;
                yield return null;
            }
        }
        panel.progressBar.fillAmount = toProgress;
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

    public void BackToMenu()
    {
        SceneManager.LoadScene("Menu");
    }
}