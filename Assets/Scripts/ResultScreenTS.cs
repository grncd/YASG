using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;

public class ResultScreenTS : MonoBehaviour
{
    void Start()
    {
        int playerCount = 0;
        int totalScore = 0;
        for (int i = 0; i < 4; i++)
        {
            if (PlayerPrefs.GetInt("Player" + (i+1)) == 1)
            {
                playerCount++;
                totalScore += PlayerPrefs.GetInt("Player" + (i + 1) + "Score");
            }
        }
        float teamScoreF = totalScore / playerCount;
        int teamScore = Mathf.RoundToInt(teamScoreF);
        gameObject.GetComponent<TextMeshProUGUI>().text = teamScoreF.ToString("#,#");
    }
}
