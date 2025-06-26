using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class ResultsScreenGO : MonoBehaviour
{
    void Start()
    {
        gameObject.GetComponent<TextMeshProUGUI>().text = PlayerPrefs.GetString("currentSongDisplay");
    }
}
