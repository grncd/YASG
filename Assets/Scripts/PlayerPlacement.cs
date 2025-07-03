using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

public class PlayerPlacement : MonoBehaviour
{
    public Dictionary<RealTimePitchDetector, float> scores = new Dictionary<RealTimePitchDetector, float>();
    // Start is called before the first frame update
    void Start()
    {
        if(PlayerPrefs.GetInt("multiplayer") == 1)
        {
            GetComponent<PlayerPlacement>().enabled = false;
        }
        foreach (Transform child in transform)
        {
            scores.Add(child.GetComponent<RealTimePitchDetector>(),0f);
        }
    }

    // Update is called once per frame
    void Update()
    {
        foreach (KeyValuePair<RealTimePitchDetector, float> pair in scores.ToList())
        {
            scores[pair.Key] = pair.Key.score;
        }

        var sortedScores = scores.OrderByDescending(pair => pair.Value).ToList();

        int i = 0;
        foreach (KeyValuePair<RealTimePitchDetector, float> pair in sortedScores)
        {
            if (i == 0)
            {
                pair.Key.transform.GetChild(15).GetChild(0).GetComponent<TextMeshProUGUI>().colorGradient = new VertexGradient(new Color(1, 0.847f, 0), new Color(1, 0.847f, 0), new Color(1, 0.569f, 0), new Color(1, 0.569f, 0));
                pair.Key.transform.GetChild(15).GetChild(0).GetComponent<TextMeshProUGUI>().text = "1st";
            }
            else if (i == 1)
            {
                pair.Key.transform.GetChild(15).GetChild(0).GetComponent<TextMeshProUGUI>().colorGradient = new VertexGradient(new Color(1, 1, 1), new Color(1, 1, 1), new Color(0.6886792f, 0.6886792f, 0.6886792f), new Color(0.6886792f, 0.6886792f, 0.6886792f));
                pair.Key.transform.GetChild(15).GetChild(0).GetComponent<TextMeshProUGUI>().text = "2nd";
            }
            else if (i == 2)
            {
                pair.Key.transform.GetChild(15).GetChild(0).GetComponent<TextMeshProUGUI>().colorGradient = new VertexGradient(new Color(1, 0.706f, 0.184f), new Color(1, 0.706f, 0.184f), new Color(0.482f, 0.325f, 0.055f), new Color(0.482f, 0.325f, 0.055f));
                pair.Key.transform.GetChild(15).GetChild(0).GetComponent<TextMeshProUGUI>().text = "3rd";
            }
            else
            {
                pair.Key.transform.GetChild(15).GetChild(0).GetComponent<TextMeshProUGUI>().colorGradient = new VertexGradient(new Color(0.482f, 0.482f, 0.482f), new Color(0.482f, 0.482f, 0.482f), new Color(0.18f, 0.18f, 0.18f), new Color(0.18f, 0.18f, 0.18f));
                pair.Key.transform.GetChild(15).GetChild(0).GetComponent<TextMeshProUGUI>().text = ""+(i+1)+"th";
            }
            pair.Key.placement = i;
            i++;
        }
    }
}
