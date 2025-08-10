using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MenuTransition : MonoBehaviour
{
    void Awake()
    {
        if (PlayerPrefs.GetInt("fromMP") == 1)
        {
            GetComponent<Animator>().Play("MenuIn");
            GetComponent<AudioSource>().Play();
        }
    }
    
}
