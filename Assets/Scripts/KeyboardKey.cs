using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class KeyboardKey : MonoBehaviour
{
    void Start()
    {
        transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = gameObject.name;
    }
}
