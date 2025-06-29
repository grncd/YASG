using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;

public class ResultScreenTS : MonoBehaviour
{
    private TextMeshProUGUI _teamScoreText;

    void Start()
    {
        // Get the TextMeshProUGUI component on this same GameObject.
        _teamScoreText = GetComponent<TextMeshProUGUI>();

        // --- DUAL-MODE LOGIC ---
        // Check PlayerPrefs to see which mode we're in.
        if (PlayerPrefs.GetInt("multiplayer") == 1)
        {
            CalculateMultiplayerTeamScore();
        }
        else
        {
            CalculateLocalTeamScore();
        }
    }

    /// <summary>
    /// Calculates and displays the team score using data from networked PlayerData objects.
    /// </summary>
    private void CalculateMultiplayerTeamScore()
    {
        Debug.Log("Calculating team score in MULTIPLAYER mode.");

        // Find all active PlayerData objects that have persisted from the game scene.
        PlayerData[] allPlayers = FindObjectsOfType<PlayerData>();

        if (allPlayers.Length == 0)
        {
            _teamScoreText.text = "0";
            Debug.LogWarning("No players found to calculate multiplayer team score.");
            return;
        }

        // Use LINQ to easily sum up the CurrentGameScore from all players.
        int totalScore = allPlayers.Sum(player => player.CurrentGameScore.Value);

        int playerCount = allPlayers.Length;

        // Calculate the average score.
        float teamScoreF = (float)totalScore / playerCount;

        // Display the formatted average score.
        _teamScoreText.text = teamScoreF.ToString("#,#");
    }

    /// <summary>
    /// Calculates and displays the team score using data from local PlayerPrefs.
    /// </summary>
    private void CalculateLocalTeamScore()
    {
        Debug.Log("Calculating team score in LOCAL mode.");

        int playerCount = 0;
        int totalScore = 0;

        // Loop through the 4 possible local player slots.
        for (int i = 1; i <= 4; i++)
        {
            // Check if this player slot was active.
            if (PlayerPrefs.GetInt("Player" + i) == 1)
            {
                playerCount++;
                totalScore += PlayerPrefs.GetInt("Player" + i + "Score");
            }
        }

        if (playerCount == 0)
        {
            _teamScoreText.text = "0";
            Debug.LogWarning("No active local players found to calculate team score.");
            return;
        }

        // Calculate the average score.
        float teamScoreF = (float)totalScore / playerCount;

        // Display the formatted average score.
        _teamScoreText.text = teamScoreF.ToString("#,#");
    }
}
