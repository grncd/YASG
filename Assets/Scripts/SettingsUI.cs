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
                ProfileDisplay.Instance.hasSettingsBeenOpened = true;
                onSettings = !onSettings;
                settingsContainer.SetActive(true);
                settingsContainer.GetComponent<Animator>().Play("FadeIn");
                settingsButton.GetComponent<Animator>().Play("SettingsIn");
                settingsButton.GetComponent<AudioSource>().clip = settingsIn;
                settingsButton.GetComponent<AudioSource>().Play();
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

                canClick = false;
                onSettings = !onSettings;
                settingsButton.GetComponent<AudioSource>().clip = settingsOut;
                settingsButton.GetComponent<AudioSource>().Play();
                settingsContainer.GetComponent<Animator>().Play("FadeOut");
                settingsButton.GetComponent<Animator>().Play("SettingsOut");
                await Task.Delay(TimeSpan.FromSeconds(0.5f));
                settingsContainer.SetActive(false);
                canClick = true;
            }
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
