using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class ClearSearch : MonoBehaviour
{
    public GameObject searchContainer;
    public TMP_InputField searchField;

    // Start is called before the first frame update
    void Start()
    {
        this.GetComponent<CanvasGroup>().alpha = 0;
        this.GetComponent<CanvasGroup>().interactable = false;
    }

    // Update is called once per frame
    void FixedUpdate()
    {
        if (searchContainer.transform.childCount != 0)
        {
            this.GetComponent<CanvasGroup>().alpha = 1;
            this.GetComponent<CanvasGroup>().interactable = true;
        }
        else
        {
            this.GetComponent<CanvasGroup>().alpha = 0;
            this.GetComponent<CanvasGroup>().interactable = false;
        }
    }

    public void SearchClear()
    {
        foreach (Transform child in searchContainer.transform)
        {
            Destroy(child.gameObject);
        }
        searchField.text = string.Empty;
    }

}
