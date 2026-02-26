using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class AlertManager : MonoBehaviour
{

    public static AlertManager Instance { get; private set; }
    public GameObject menuGO;
    public GameObject mpGO;
    public GameObject localGO;
    public EditorManager editorManager;

    private void Awake()
    {
        Instance = this;
        if (PlayerPrefs.GetInt("fromMP") == 0)
        {
            PlayerPrefs.SetInt("multiplayer", 0);
        }
        else
        {
            menuGO.SetActive(false);
            PlayerPrefs.SetInt("fromMP", 0);
            if (PlayerPrefs.GetInt("multiplayer") == 0)
            {
                localGO.SetActive(true);
            }
            else
            {
                mpGO.SetActive(true);
            }
        }
    }


    public void ShowSuccess(string title, string info, string button)
    {
        this.transform.GetChild(3).gameObject.SetActive(true);
        SetAlertText(this.transform.GetChild(3), title, info, button);
        this.transform.GetChild(3).GetChild(5).GetComponent<Button>().onClick.AddListener(editorManager.ReloadScene);
        this.transform.GetChild(3).GetComponent<AudioSource>().Play();
        this.GetComponent<Animator>().Play("ShowSuccess");
    }
    public void ShowSuccess(string title, string info, string button, bool none)
    {
        this.transform.GetChild(3).gameObject.SetActive(true);
        SetAlertText(this.transform.GetChild(3), title, info, button);
        this.transform.GetChild(3).GetComponent<AudioSource>().Play();
        this.GetComponent<Animator>().Play("ShowSuccess");
    }

    public void ShowInfo(string title, string info, string button)
    {
        this.transform.GetChild(2).gameObject.SetActive(true);
        SetAlertText(this.transform.GetChild(2), title, info, button);
        this.transform.GetChild(2).GetComponent<AudioSource>().Play();
        this.GetComponent<Animator>().Play("ShowInfo");
    }

    public void ShowWarning(string title, string info, string button)
    {
        this.transform.GetChild(1).gameObject.SetActive(true);
        SetAlertText(this.transform.GetChild(1), title, info, button);
        this.transform.GetChild(1).GetComponent<AudioSource>().Play();
        this.GetComponent<Animator>().Play("ShowWarning");
    }

    public void ShowWarning(string title, string info, string button, Action onDismiss)
    {
        ShowWarning(title, info, button);

        var btn = this.transform.GetChild(1).GetChild(5).GetComponent<Button>();
        void Handler()
        {
            btn.onClick.RemoveListener(Handler);
            onDismiss?.Invoke();
        }
        btn.onClick.AddListener(Handler);
    }

    public void ShowError(string title, string info, string button)
    {
        this.transform.GetChild(0).gameObject.SetActive(true);
        SetAlertText(this.transform.GetChild(0), title, info, button);
        this.transform.GetChild(0).GetComponent<AudioSource>().Play();
        this.GetComponent<Animator>().Play("ShowError");
    }

    public void Dismiss()
    {
        //SelectorOutline.Instance.defaultObject = localGO.transform.GetChild(2).gameObject;
        //SelectorOutline.Instance.UnrestrictAllButtons();
    }

    /// <summary>
    /// Sets the title, info, and button text on an alert panel, clearing any LocalizedText
    /// keys so they don't overwrite the dynamically-set text on Start/OnEnable.
    /// </summary>
    private void SetAlertText(Transform alertPanel, string title, string info, string button)
    {
        var titleTmp = alertPanel.GetChild(3);
        var infoTmp = alertPanel.GetChild(4);
        var buttonTmp = alertPanel.GetChild(5).GetChild(0);

        titleTmp.GetComponent<TextMeshProUGUI>().text = title;
        infoTmp.GetComponent<TextMeshProUGUI>().text = info;
        buttonTmp.GetComponent<TextMeshProUGUI>().text = button;

        // Clear localization keys so LocalizedText.Start()/OnEnable() don't overwrite
        var lt = titleTmp.GetComponent<LocalizedText>();
        if (lt != null) lt.localizationKey = "";
        lt = infoTmp.GetComponent<LocalizedText>();
        if (lt != null) lt.localizationKey = "";
        lt = buttonTmp.GetComponent<LocalizedText>();
        if (lt != null) lt.localizationKey = "";
    }

    private void Start()
    {
        // Delay by one frame so that all LocalizedText.Start() calls have finished
        // before we set alert text, preventing them from overwriting our values.
        StartCoroutine(CheckStartAlerts());
    }

    private IEnumerator CheckStartAlerts()
    {
        yield return null;

        if (PlayerPrefs.GetInt("ERR") == 1)
        {
            ShowError(LocalizationManager.L("alert.error_occurred.title", "An error occurred."), LocalizationManager.L("alert.error_occurred.info", "Please try playing the song again or performing a full reset (Settings > Misc > Full Reset). If the issue persists, open an issue in our GitHub page."), LocalizationManager.L("alert.close", "Close"));
            PlayerPrefs.SetInt("ERR", 0);
        }
        else if (PlayerPrefs.GetInt("ERR") == 2)
        {
            ShowError(LocalizationManager.L("alert.mic_init_failed.title", "Your microphone couldn't be initialized."), LocalizationManager.L("alert.mic_init_failed.info", "Please ensure your microphone is properly connected and that it works as expected. Check that no other application has exclusive control over it."), LocalizationManager.L("alert.close", "Close"));
            PlayerPrefs.SetInt("ERR", 0);
        }
        else if (PlayerPrefs.GetInt("HostDisconnected") == 1)
        {
            ShowInfo(LocalizationManager.L("alert.host_disconnected.title", "You have been disconnected."), LocalizationManager.L("alert.host_disconnected.info", "The host has disconnected, so you have been returned to the menu."), LocalizationManager.L("alert.close", "Close"));
            PlayerPrefs.SetInt("HostDisconnected", 0);
            PlayerPrefs.SetInt("fromMP", 0);
            PlayerPrefs.SetInt("multiplayer", 0);

            // Ensure proper UI state
            if (mpGO != null) mpGO.SetActive(false);
            if (localGO != null) localGO.SetActive(false);
            if (menuGO != null) menuGO.SetActive(true);
        }
    }


    public void MPDisclaimer()
    {
        ShowInfo(LocalizationManager.L("alert.mp_unavailable.title", "Multiplayer is unavailable."), LocalizationManager.L("alert.mp_unavailable.info", "Multiplayer mode is currently under development! Once development is complete, it will be accessible from this button."), LocalizationManager.L("alert.close", "Close"));
    }
}
