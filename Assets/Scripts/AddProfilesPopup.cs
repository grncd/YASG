using MPUIKIT;
using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using System.Threading.Tasks;

public class AddProfilesPopup : MonoBehaviour
{
    private Animator animator;
    public GameObject profilePrefab;
    public Transform prefabDestination;
    void Awake()
    {
        animator = GetComponent<Animator>();

    }

    void OnEnable()
    {
        SelectorOutline.Instance.defaultObject = transform.GetChild(1).GetChild(0).GetChild(0).GetChild(0).GetChild(0).gameObject;
        SelectorOutline.Instance.RestrictButtonSelection(gameObject);
        if (animator != null)
        {
            animator.Play("ProfileCreationIn"); 
        }
        for (int i = 1; i < prefabDestination.childCount; i++)
        {
            Destroy(prefabDestination.GetChild(i).gameObject);
        }
        // Instantiate profiles
        foreach (Profile p in ProfileManager.Instance.PrintAllProfileData())
        {
            GameObject temp = Instantiate(profilePrefab, prefabDestination);
            temp.transform.GetChild(0).GetChild(0).GetComponent<MPImage>().fillAmount = p.progressRemaining / 100f;
            temp.transform.GetChild(0).GetChild(0).GetChild(0).GetChild(0).GetComponent<TextMeshProUGUI>().text = p.level.ToString();
            temp.transform.GetChild(1).GetComponent<TextMeshProUGUI>().text = p.name;
        }
    }

    public async void Dismiss()
    {
        SelectorOutline.Instance.defaultObject = transform.parent.GetChild(0).gameObject;
        SelectorOutline.Instance.UnrestrictAllButtons();
        animator.Play("ProfileCreationOut");
        await Task.Delay(TimeSpan.FromSeconds(0.5f));
        gameObject.SetActive(false);
    }
}
