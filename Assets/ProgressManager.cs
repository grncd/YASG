using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class ProgressManager : MonoBehaviour
{

    public GameObject mainGO;
    public List<Slider> progress;
    private int lastActivePageIndex = -1;

    // Update is called once per frame
    void Update()
    {
        // Find the active child under mainGO and print its index if changed
        if (mainGO != null && mainGO.transform.childCount > 0)
        {
            int activeIndex = -1;
            for (int i = 0; i < mainGO.transform.childCount; i++)
            {
                var child = mainGO.transform.GetChild(i).gameObject;
                if (child.activeSelf)
                {
                    activeIndex = i;
                    break;
                }
            }

            // Cache the last index to only print when changed
            if (activeIndex != lastActivePageIndex)
            {
                lastActivePageIndex = activeIndex;
                if (activeIndex != -1)
                {
                    switch (activeIndex+1)
                    {
                        case 1:
                            progress[0].value = 0f;
                            break;
                        case 2:
                            progress[0].value = 0.5f;
                            break;
                        case 3:
                            progress[0].value = 1f;
                            break;
                        case 4:
                            progress[1].value = 0.5f;
                            break;
                        case 5:
                            progress[1].value = 1f;
                            break;
                        case 7:
                            progress[2].value = 0.5f;
                            break;
                        case 8:
                            progress[2].value = 1f;
                            break;
                        case 9:
                            progress[3].value = 1f;
                            break;
                    }
                }
            }
        }
    }
}
