using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public class SetupPage : MonoBehaviour
{
    public async void NextPage()
    {
        int currentPageIndex = transform.GetSiblingIndex();
        GameObject nextPage = transform.parent.GetChild(currentPageIndex + 1).gameObject;
        nextPage.GetComponent<Animator>().Play("PageIn");
        gameObject.GetComponent<Animator>().Play("PageOut");
        nextPage.SetActive(true);
        await Task.Delay(TimeSpan.FromSeconds(0.417f));
        gameObject.SetActive(false);
    }

    /// <summary>
    /// Navigate directly to a specific page by reference.
    /// </summary>
    public async void GoToPage(SetupPage targetPage)
    {
        if (targetPage == null)
        {
            Debug.LogError("GoToPage: targetPage is null!");
            return;
        }

        targetPage.GetComponent<Animator>().Play("PageIn");
        gameObject.GetComponent<Animator>().Play("PageOut");
        targetPage.gameObject.SetActive(true);
        await Task.Delay(TimeSpan.FromSeconds(0.417f));
        gameObject.SetActive(false);
    }
}
