using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Threading.Tasks;
using UnityEngine.UI;
using TMPro;
using MPUIKIT;
using System.IO;
using System.Text;
using System.Linq;
using FishNet.Managing.Scened;

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
    public GameObject menuSettingsButton;
    public AudioClip settingsIn;
    public AudioClip settingsOut;
    public GameObject menuGO;
    public GameObject mainGO;
    public GameObject backButtonMenu;
    public ParticleSystem mainMenuParticles;
    public GameObject multiplayerSet;
    public AudioSource clickFX;
    private bool fromSettings = false;
    public Transform settingsTabs;
    public List<Material> backgrounds;
    public List<Color> backgroundsDarken;
    public Image darken;
    public RawImage BG;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
        
    }

    public void SelectTabSettings(int value)
    {
        foreach (Transform child in settingsTabs)
        {
            child.GetComponent<MPImage>().color = new Color(0.2169811f, 0.2169811f, 0.2169811f);
            child.GetChild(0).GetComponent<TextMeshProUGUI>().color = Color.white;
            settingsTabs.parent.GetChild(child.GetSiblingIndex() + 2).gameObject.SetActive(false);
        }

        settingsTabs.GetChild(value).GetComponent<MPImage>().color = Color.white;
        settingsTabs.GetChild(value).GetChild(0).GetComponent<TextMeshProUGUI>().color = Color.black;
        settingsTabs.parent.GetChild(value + 2).gameObject.SetActive(true);
    }


    private void Start()
    {
        for (int i = 1; i < 5; i++)
        {
            if (PlayerPrefs.GetInt("Player" + i) == 1)
            {
                AddProfile(PlayerPrefs.GetString("Player" + i + "Name"));
            }
        }
        settingsContainer.SetActive(false);
    }
    public async void ToggleSettings()
    {
        if (canClick)
        {
            if (!onSettings)
            {
                canClick = false;
                ProfileDisplay.Instance.hasSettingsBeenOpened = true;
                onSettings = !onSettings;
                SelectorOutline.Instance.defaultObject = settingsContainer.transform.GetChild(2).GetChild(0).gameObject;
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
                        AlertManager.Instance.ShowWarning("There are micless profiles.", "One of your profiles has no microphone selected. If you aren't going to use said profile, please disable it.", "Dismiss");
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

    public async void ToggleSettingsMenu()
    {
        if (canClick)
        {
            if (!onSettings)
            {
                canClick = false;
                StartCoroutine(FadeParticleSystem(mainMenuParticles, 0.06666667f, 0f, 0.2f));
                mainGO.SetActive(true);
                settingsButton.SetActive(false);
                backButtonMenu.SetActive(false);
                ProfileDisplay.Instance.hasSettingsBeenOpened = true;
                onSettings = !onSettings;
                SelectorOutline.Instance.defaultObject = settingsContainer.transform.GetChild(2).GetChild(0).gameObject;
                settingsContainer.SetActive(true);
                settingsContainer.GetComponent<Animator>().Play("FadeIn");
                menuSettingsButton.GetComponent<Animator>().Play("SettingsIn");
                menuSettingsButton.GetComponent<AudioSource>().clip = settingsIn;
                menuSettingsButton.GetComponent<AudioSource>().Play();
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
                        AlertManager.Instance.ShowWarning("There are micless profiles.", "One of your profiles has no microphone selected. If you aren't going to use said profile, please disable it.", "Dismiss");
                        return;
                    }
                }
                SelectorOutline.Instance.defaultObject = settingsButton.gameObject;
                StartCoroutine(FadeParticleSystem(mainMenuParticles, 0f, 0.06666667f, 0.2f));
                canClick = false;
                mainGO.SetActive(false);
                menuGO.SetActive(true);
                onSettings = !onSettings;
                menuSettingsButton.GetComponent<AudioSource>().clip = settingsOut;
                menuSettingsButton.GetComponent<AudioSource>().Play();
                settingsContainer.GetComponent<Animator>().Play("FadeOut");
                menuSettingsButton.GetComponent<Animator>().Play("SettingsOut");
                await Task.Delay(TimeSpan.FromSeconds(0.3f));
                settingsContainer.SetActive(false);
                settingsButton.SetActive(true);
                backButtonMenu.SetActive(true);
                canClick = true;
            }
        }
    }

    private IEnumerator FadeParticleSystem(ParticleSystem ps, float startAlpha, float endAlpha, float duration)
    {
        var main = ps.main;
        ParticleSystem.Particle[] particles = new ParticleSystem.Particle[main.maxParticles];

        Color originalColor = main.startColor.color;

        float timeStartedLerping = Time.time;

        while (Time.time < timeStartedLerping + duration)
        {
            float timeSinceStarted = Time.time - timeStartedLerping;
            float percentageComplete = timeSinceStarted / duration;
            float currentAlpha = Mathf.Lerp(startAlpha, endAlpha, percentageComplete);

            main.startColor = new Color(originalColor.r, originalColor.g, originalColor.b, currentAlpha);

            int particleCount = ps.GetParticles(particles);
            for (int i = 0; i < particleCount; i++)
            {
                Color32 particleColor = particles[i].startColor;
                particleColor.a = (byte)(currentAlpha * 255);
                particles[i].startColor = particleColor;
            }
            ps.SetParticles(particles, particleCount);

            yield return new WaitForEndOfFrame();
        }

        main.startColor = new Color(originalColor.r, originalColor.g, originalColor.b, endAlpha);

        int finalParticleCount = ps.GetParticles(particles);
        for (int i = 0; i < finalParticleCount; i++)
        {
            Color32 particleColor = particles[i].startColor;
            particleColor.a = (byte)(endAlpha * 255);
            particles[i].startColor = particleColor;
        }
        ps.SetParticles(particles, finalParticleCount);
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F11))
        {
            Screen.fullScreen = !Screen.fullScreen;
        }
        if (!LevelResourcesCompiler.Instance.compiling)
        {
            BG.material = backgrounds[SettingsManager.Instance.GetSetting<int>("MenuBG")];
            darken.color = backgroundsDarken[SettingsManager.Instance.GetSetting<int>("MenuBG")];
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

    public void CheckMultiplayer()
    {
        if (ProfileManager.Instance.GetActiveProfiles().Count == 0)
        {
            AlertManager.Instance.ShowError("You don't have any active profiles!", "Please go to the Settings (cogwheel on the bottom right) and either create a new profile or activate an existing one.", "Dismiss");
            return;
        }

        if (ProfileManager.Instance.GetActiveProfiles().Count > 1)
        {
            AlertManager.Instance.ShowError("You have more than one active profile!", "Multiplayer supports only one profile per client. Please deactivate profiles that are not going to be used.", "Dismiss");
            return;
        }

        foreach (var profile in ProfileManager.Instance.GetActiveProfiles())
        {
            if (profile.microphone == "Default")
            {
                AlertManager.Instance.ShowWarning("There are micless profiles.", "One of your profiles has no microphone selected. If you aren't going to use said profile, please disable it.", "Dismiss");
                return;
            }
        }

        multiplayerSet.SetActive(true);
        clickFX.Play();
    }

    public void SaveSettingToJson(string key, string value)
    {
        string dataPath = PlayerPrefs.GetString("dataPath");
        string filePath = Path.Combine(dataPath, "settings.json");
        Dictionary<string, string> settings = new Dictionary<string, string>();

        // Read existing settings if file exists
        if (File.Exists(filePath))
        {
            string json = File.ReadAllText(filePath, Encoding.UTF8);
            settings = JsonUtility.FromJson<SerializableDictionary>(json)?.ToDictionary() ?? new Dictionary<string, string>();
        }

        // Update or add the key-value pair
        settings[key] = value;

        // Serialize and write back to file
        string newJson = JsonUtility.ToJson(new SerializableDictionary(settings), true);
        File.WriteAllText(filePath, newJson, Encoding.UTF8);
    }

    public string GetSettingFromJson(string key)
    {
        string dataPath = PlayerPrefs.GetString("dataPath");
        string filePath = Path.Combine(dataPath, "settings.json");
        if (!File.Exists(filePath))
            return null;

        string json = File.ReadAllText(filePath, Encoding.UTF8);
        var settings = JsonUtility.FromJson<SerializableDictionary>(json)?.ToDictionary();
        if (settings != null && settings.ContainsKey(key))
            return settings[key];
        return null;
    }

    [System.Serializable]
    private class SerializableDictionary
    {
        public List<string> keys = new List<string>();
        public List<string> values = new List<string>();

        public SerializableDictionary() { }

        public SerializableDictionary(Dictionary<string, string> dict)
        {
            keys = dict.Keys.ToList();
            values = dict.Values.ToList();
        }

        public Dictionary<string, string> ToDictionary()
        {
            var dict = new Dictionary<string, string>();
            for (int i = 0; i < Mathf.Min(keys.Count, values.Count); i++)
            {
                dict[keys[i]] = values[i];
            }
            return dict;
        }
    }

}
