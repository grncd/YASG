using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AddProfileButton : MonoBehaviour
{
    void Update()
    {
        if(transform.childCount == 5)
        {
            transform.GetChild(0).gameObject.SetActive(false);
        }
        else
        {
            transform.GetChild(0).gameObject.SetActive(true);
        }
    }
}
