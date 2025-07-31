using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class ShuffleButton : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        
        GetComponent<Button>().onClick.AddListener(delegate {
            GameObject.Find("Music").GetComponent<BGMusic>().Reshuffle();
        });
    }

}
