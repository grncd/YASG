using MPUIKIT;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class DeleteProfileButton : MonoBehaviour
{
    private float confirmationTime = 2f;
    private float elapsedTime = 0f;
    private bool holding = false;
    private MPImage confirmationVisual;
    public GameObject profileGO;
    public TextMeshProUGUI profileName;


    void Awake()
    {
        confirmationVisual = GetComponent<MPImage>();
    }

    private void Update()
    {
        if (holding)
        {
            elapsedTime += Time.deltaTime;
            confirmationVisual.fillAmount = elapsedTime / 2f;
        }
        else
        {
            elapsedTime = 0f;
            confirmationVisual.fillAmount = 0f;
        }
        if(elapsedTime > confirmationTime)
        {
            holding = false;
            elapsedTime = 0f;
            ProfileManager.Instance.RemoveProfile(profileName.text);
            Destroy(profileGO);
        }
    }
    
    public void ActivateProfile()
    {
        PlayerPrefs.SetInt("fromSettings", 1);
        //ProfileManager.Instance.ActivateProfile(profileName.text);
        GameObject.Find("Handlers").GetComponent<SettingsUI>().AddProfile(profileName.text);
        profileGO.transform.parent.parent.parent.parent.parent.GetComponent<AddProfilesPopup>().Dismiss();
    }

    public void Hold()
    {
        //Debug.Log("Hi");
        holding = true;
    }

    public void Unhold()
    {
        holding = false;
    }
}
