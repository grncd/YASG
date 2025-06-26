using MPUIKIT;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class ProfileDisplay : MonoBehaviour
{
    public static ProfileDisplay Instance { get; private set; }
    public GameObject profilePrefab;
    public bool hasSettingsBeenOpened = false;

    void Awake()
    {
        PlayerPrefs.SetInt("fromSettings", 0);
        Instance = this;
    }

    public void InstantiateProfile(Profile profile)
    {
        if(ProfileManager.Instance.IsProfileActive(profile.name) == false && !hasSettingsBeenOpened)
        {
            Debug.Log("activating");
            int succ = ProfileManager.Instance.ActivateProfile(profile.name);
            if (succ == 0 && PlayerPrefs.GetInt("fromSettings") == 1)
            {
                return;
            }
        }
        GameObject temp = Instantiate(profilePrefab,transform);
        temp.name = profile.name;
        temp.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = profile.name;
        temp.transform.GetChild(1).GetChild(0).GetChild(0).GetChild(0).GetComponent<TextMeshProUGUI>().text = profile.level.ToString();
        temp.transform.GetChild(1).GetChild(0).GetComponent<MPImage>().fillAmount = profile.progressRemaining / 100f;
        if(profile.totalScore != 0)
        {
            temp.transform.GetChild(2).GetComponent<TextMeshProUGUI>().text = "Total Score: " + profile.totalScore.ToString("#,#");
        }
        else
        {
            temp.transform.GetChild(2).GetComponent<TextMeshProUGUI>().text = "Total Score: 0";
        }
    }

    public void RemoveProfile(string name)
    {
        Destroy(transform.Find(name).gameObject);
    }

    public void ChangeName(string formerName, string newName)
    {
        transform.Find(formerName).GetChild(0).gameObject.GetComponent<TextMeshProUGUI>().text = newName;
        transform.Find(formerName).gameObject.name = newName;
    }
}
