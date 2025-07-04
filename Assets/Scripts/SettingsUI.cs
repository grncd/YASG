using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Threading.Tasks;
using UnityEngine.UI;
using TMPro;
using MPUIKIT;

public class SettingsUI : MonoBehaviour
{
    public static SettingsUI Instance { get; private set; }
    public MPImage searchGradient;
    private bool onSettings = false;
    public GameObject settingsContainer;
    public GameObject playerPrefab;
    public Transform playerContainer;
    private bool canClick = true;
    public GameObject settingsButton;
    public AudioClip settingsIn;
    public AudioClip settingsOut;
    public GameObject menuGO;
    private bool fromSettings = false;
    private void Start()
    {
        for(int i = 1; i < 5; i++)
        {
            if (PlayerPrefs.GetInt("Player"+i) == 1)
            {
                AddProfile(PlayerPrefs.GetString("Player"+i+"Name"));
            }
        }
        settingsContainer.SetActive(false);
    }
    public async void ToggleSettings()
    {
        if(canClick)
        {
            if (!onSettings)
            {
                canClick = false;
                ProfileDisplay.Instance.hasSettingsBeenOpened = true;
                onSettings = !onSettings;
                SelectorOutline.Instance.defaultObject = settingsContainer.transform.GetChild(0).GetChild(0).gameObject;
                settingsContainer.SetActive(true);
                settingsContainer.GetComponent<Animator>().Play("FadeIn");
                settingsButton.GetComponent<Animator>().Play("SettingsIn");
                settingsButton.GetComponent<AudioSource>().clip = settingsIn;
                settingsButton.GetComponent<AudioSource>().Play();
                await Task.Delay(TimeSpan.FromSeconds(0.3f));
                menuGO.SetActive(false);
                canClick = true;
            }
            else
            {

                List<Profile> profiles = ProfileManager.Instance.GetActiveProfiles();
                foreach (Profile profile in profiles)
                {
                    if (profile.microphone == "Default")
                    {
                        AlertManager.Instance.ShowWarning("There are micless profiles.","One of your profiles has no microphone selected. If you aren't going to use said profile, please disable it.","Dismiss");
                        return;
                    }
                }
                SelectorOutline.Instance.defaultObject = settingsButton.gameObject;
                canClick = false;
                menuGO.SetActive(true);
                onSettings = !onSettings;
                settingsButton.GetComponent<AudioSource>().clip = settingsOut;
                settingsButton.GetComponent<AudioSource>().Play();
                settingsContainer.GetComponent<Animator>().Play("FadeOut");
                settingsButton.GetComponent<Animator>().Play("SettingsOut");
                await Task.Delay(TimeSpan.FromSeconds(0.3f));
                settingsContainer.SetActive(false);
                canClick = true;
            }
        }
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F11))
        {
            Screen.fullScreen = !Screen.fullScreen;

        }
    }

    public void FromSettings()
    {
        PlayerPrefs.SetInt("fromSettings", 1);
    }

    public void AddProfile(string name)
    {
        Debug.Log(ProfileManager.Instance.GetProfileByName(name));
        if (ProfileManager.Instance.GetProfileByName(name) == null)
        {
            ProfileManager.Instance.CreateProfile(name);
        }
        //ProfileManager.Instance.AddProfileTotalScore(name, 750000);
        ProfileDisplay.Instance.InstantiateProfile(ProfileManager.Instance.GetProfileByName(name));
        GameObject temp = Instantiate(playerPrefab, playerContainer);
        temp.name = name;
    }

}
