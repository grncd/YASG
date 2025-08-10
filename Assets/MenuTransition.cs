using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MenuTransition : MonoBehaviour
{

    void Awake()
    {
        if (PlayerPrefs.GetInt("fromMP") == 1)
        {
            PlayTransition();
        }
    }

    public async void PlayTransition()
    {
        GetComponent<Animator>().Play("MenuIn");
        await Task.Delay(550);
        GetComponent<AudioSource>().Play();
    }
}
