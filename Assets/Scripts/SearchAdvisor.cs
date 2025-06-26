using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class SearchAdvisor : MonoBehaviour
{
    public GameObject content;
    public GameObject searchAdvisor;
    public Animator searchAnimator;
    private int prev = 10;
    // Update is called once per frame
    void Update()
    {
        if(content.transform.childCount == 0)
        {
            searchAdvisor.SetActive(true);
        }
        else
        {
            searchAdvisor.SetActive(false);
        }
        if(prev != content.transform.childCount) // changed
        {
            prev = content.transform.childCount;
            if (content.transform.childCount == 0)
            {
                Debug.Log("triggered");
                searchAnimator.Play("SearchAdvisor");
            }
        }
    }
}
