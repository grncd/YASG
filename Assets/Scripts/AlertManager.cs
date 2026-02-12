using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

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
        //SelectorOutline.Instance.defaultObject = this.transform.GetChild(3).GetChild(5).gameObject;
        //SelectorOutline.Instance.RestrictButtonSelection(gameObject);
        this.transform.GetChild(3).GetChild(3).GetComponent<TextMeshProUGUI>().text = title;
        this.transform.GetChild(3).GetChild(4).GetComponent<TextMeshProUGUI>().text = info;
        this.transform.GetChild(3).GetChild(5).GetChild(0).GetComponent<TextMeshProUGUI>().text = button;
        this.transform.GetChild(3).GetChild(5).GetComponent<UnityEngine.UI.Button>().onClick.AddListener(editorManager.ReloadScene);

        this.transform.GetChild(3).gameObject.SetActive(true);
        this.transform.GetChild(3).GetComponent<AudioSource>().Play();

        this.GetComponent<Animator>().Play("ShowSuccess");
    }
    public void ShowSuccess(string title, string info, string button, bool none)
    {
        //SelectorOutline.Instance.defaultObject = this.transform.GetChild(3).GetChild(5).gameObject;
        //SelectorOutline.Instance.RestrictButtonSelection(gameObject);

        this.transform.GetChild(3).GetChild(3).GetComponent<TextMeshProUGUI>().text = title;
        this.transform.GetChild(3).GetChild(4).GetComponent<TextMeshProUGUI>().text = info;
        this.transform.GetChild(3).GetChild(5).GetChild(0).GetComponent<TextMeshProUGUI>().text = button;

        this.transform.GetChild(3).gameObject.SetActive(true);
        this.transform.GetChild(3).GetComponent<AudioSource>().Play();

        this.GetComponent<Animator>().Play("ShowSuccess");
    }

    public void ShowInfo(string title, string info, string button)
    {
        //SelectorOutline.Instance.defaultObject = this.transform.GetChild(2).GetChild(5).gameObject;
        //SelectorOutline.Instance.RestrictButtonSelection(gameObject);
        this.transform.GetChild(2).GetChild(3).GetComponent<TextMeshProUGUI>().text = title;
        this.transform.GetChild(2).GetChild(4).GetComponent<TextMeshProUGUI>().text = info;
        this.transform.GetChild(2).GetChild(5).GetChild(0).GetComponent<TextMeshProUGUI>().text = button;

        this.transform.GetChild(2).gameObject.SetActive(true);
        this.transform.GetChild(2).GetComponent<AudioSource>().Play();

        this.GetComponent<Animator>().Play("ShowInfo");
    }

    public void ShowWarning(string title, string info, string button)
    {
        //SelectorOutline.Instance.defaultObject = this.transform.GetChild(1).GetChild(5).gameObject;
        ////SelectorOutline.Instance.RestrictButtonSelection(gameObject);
        this.transform.GetChild(1).GetChild(3).GetComponent<TextMeshProUGUI>().text = title;
        this.transform.GetChild(1).GetChild(4).GetComponent<TextMeshProUGUI>().text = info;
        this.transform.GetChild(1).GetChild(5).GetChild(0).GetComponent<TextMeshProUGUI>().text = button;

        this.transform.GetChild(1).gameObject.SetActive(true);
        this.transform.GetChild(1).GetComponent<AudioSource>().Play();

        this.GetComponent<Animator>().Play("ShowWarning");
    }

    public void ShowError(string title, string info, string button)
    {
        ////SelectorOutline.Instance.defaultObject = this.transform.GetChild(0).GetChild(5).gameObject;
        //SelectorOutline.Instance.RestrictButtonSelection(gameObject);
        this.transform.GetChild(0).GetChild(3).GetComponent<TextMeshProUGUI>().text = title;
        this.transform.GetChild(0).GetChild(4).GetComponent<TextMeshProUGUI>().text = info;
        this.transform.GetChild(0).GetChild(5).GetChild(0).GetComponent<TextMeshProUGUI>().text = button;

        this.transform.GetChild(0).gameObject.SetActive(true);
        this.transform.GetChild(0).GetComponent<AudioSource>().Play();

        this.GetComponent<Animator>().Play("ShowError");
    }

    public void Dismiss()
    {
        //SelectorOutline.Instance.defaultObject = localGO.transform.GetChild(2).gameObject;
        //SelectorOutline.Instance.UnrestrictAllButtons();
    }

    private void Start()
    {
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
