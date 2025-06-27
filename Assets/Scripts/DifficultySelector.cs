using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UIElements;
using System.Threading.Tasks;

public class DifficultySelector : MonoBehaviour
{


    void OnEnable()
    {
       

        if(PlayerPrefs.GetInt("multiplayer") == 0 && transform.parent.parent.gameObject.GetComponent<CanvasGroup>() != null)
        {
            transform.parent.parent.gameObject.GetComponent<CanvasGroup>().alpha = 0f;
            if (PlayerPrefs.GetInt(gameObject.name) == 0)
            {
                gameObject.GetComponent<CanvasGroup>().alpha = 0.2f;
                gameObject.GetComponent<CanvasGroup>().interactable = false;
            }
            else
            {
                gameObject.GetComponent<CanvasGroup>().alpha = 1f;
                gameObject.GetComponent<CanvasGroup>().interactable = true;
            }
            transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = PlayerPrefs.GetString(gameObject.name + "Name");
            animIn();
        }
        else
        {
            PlayerPrefs.SetInt(gameObject.name + "Difficulty", 1);
        }

        UpdateDisplay();
        
    }

    public void Close()
    {
        transform.parent.parent.gameObject.GetComponent<CanvasGroup>().alpha = 0f;
    }

    private async void animIn()
    {
        await Task.Delay(TimeSpan.FromSeconds(0.5f));
        transform.parent.parent.gameObject.GetComponent<CanvasGroup>().alpha = 1f;
    }

    public void CycleDiffs()
    {
        if(PlayerPrefs.GetInt("multiplayer") == 0)
        {
            if (ProfileManager.Instance.GetProfileByName(PlayerPrefs.GetString(gameObject.name + "Name")).difficulty == 2)
            {
                ProfileManager.Instance.SetProfileDifficulty(PlayerPrefs.GetString(gameObject.name + "Name"), 0);
            }
            else
            {
                ProfileManager.Instance.SetProfileDifficulty(PlayerPrefs.GetString(gameObject.name + "Name"), ProfileManager.Instance.GetProfileByName(PlayerPrefs.GetString(gameObject.name + "Name")).difficulty + 1);
            }
        }
        else
        {
            if (PlayerPrefs.GetInt("Player1Difficulty") == 2)
            {
                PlayerPrefs.SetInt("Player1Difficulty", 0);
            }
            else
            {
                PlayerPrefs.SetInt("Player1Difficulty", PlayerPrefs.GetInt("Player1Difficulty") + 1);
            }
            if (PlayerData.LocalPlayerInstance != null)
            {
                PlayerData.LocalPlayerInstance.RequestChangeDiff_ServerRpc(PlayerPrefs.GetInt("Player1Difficulty"));
            }
        }
        UpdateDisplay();
    }

    private void UpdateDisplay()
    {
        if(PlayerPrefs.GetInt("multiplayer") == 0)
        {
            if (ProfileManager.Instance.GetProfileByName(PlayerPrefs.GetString(gameObject.name + "Name")).difficulty == 0) // easy
            {
                transform.GetChild(1).GetChild(0).GetComponent<TextMeshProUGUI>().text = "Easy";
                transform.GetChild(1).GetChild(0).GetComponent<TextMeshProUGUI>().color = new Color(0.3042734f, 1f, 0.2588235f);
            }
            else if (ProfileManager.Instance.GetProfileByName(PlayerPrefs.GetString(gameObject.name + "Name")).difficulty == 1) // medium
            {
                transform.GetChild(1).GetChild(0).GetComponent<TextMeshProUGUI>().text = "Medium";
                transform.GetChild(1).GetChild(0).GetComponent<TextMeshProUGUI>().color = new Color(0.9826014f, 1f, 0.259434f);
            }
            else // hard
            {
                transform.GetChild(1).GetChild(0).GetComponent<TextMeshProUGUI>().text = "Hard";
                transform.GetChild(1).GetChild(0).GetComponent<TextMeshProUGUI>().color = new Color(1f, 0.2588235f, 0.2657269f);
            }
        }
        else
        {
            if (PlayerPrefs.GetInt("Player1Difficulty") == 0) // easy
            {
                transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = "Easy";
                transform.GetChild(0).GetComponent<TextMeshProUGUI>().color = new Color(0.3042734f, 1f, 0.2588235f);
            }
            else if (PlayerPrefs.GetInt("Player1Difficulty") == 1) // medium
            {
                transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = "Medium";
                transform.GetChild(0).GetComponent<TextMeshProUGUI>().color = new Color(0.9826014f, 1f, 0.259434f);
            }
            else // hard
            {
                transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = "Hard";
                transform.GetChild(0).GetComponent<TextMeshProUGUI>().color = new Color(1f, 0.2588235f, 0.2657269f);
            }
        }
    }
}
