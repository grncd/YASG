using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Advisor : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        if (PlayerPrefs.GetInt("firstPlay") == 1)
        {
            PlayerPrefs.SetInt("firstPlay", 2);
        }
        else
        {
            this.gameObject.SetActive(false);
        }
    }
}
