using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DifficultySelectorGroup : MonoBehaviour
{
    void OnEnable()
    {
        foreach(Transform child in transform)
        {
            if(PlayerPrefs.GetInt(child.name) == 1)
            {
                child.gameObject.SetActive(true);
            }
            else
            {
                child.gameObject.SetActive(false);
            }
        }
    }
}
