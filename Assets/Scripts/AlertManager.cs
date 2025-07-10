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

    private void Awake()
    {
        Instance = this;
        if(PlayerPrefs.GetInt("fromMP") == 0)
        {
            PlayerPrefs.SetInt("multiplayer", 0);
        }
        else
        {
            menuGO.SetActive(false);
            PlayerPrefs.SetInt("fromMP", 0);
            if(PlayerPrefs.GetInt("multiplayer") == 0)
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
        SelectorOutline.Instance.defaultObject = this.transform.GetChild(3).GetChild(5).gameObject;
        SelectorOutline.Instance.RestrictButtonSelection(gameObject);
        this.transform.GetChild(3).GetChild(3).GetComponent<TextMeshProUGUI>().text = title;
        this.transform.GetChild(3).GetChild(4).GetComponent<TextMeshProUGUI>().text = info;
        this.transform.GetChild(3).GetChild(5).GetChild(0).GetComponent<TextMeshProUGUI>().text = button;

        this.transform.GetChild(3).gameObject.SetActive(true);
        this.transform.GetChild(3).GetComponent<AudioSource>().Play();

        this.GetComponent<Animator>().Play("ShowSuccess");
    }

    public void ShowInfo(string title, string info, string button)
    {
        SelectorOutline.Instance.defaultObject = this.transform.GetChild(2).GetChild(5).gameObject;
        SelectorOutline.Instance.RestrictButtonSelection(gameObject);
        this.transform.GetChild(2).GetChild(3).GetComponent<TextMeshProUGUI>().text = title;
        this.transform.GetChild(2).GetChild(4).GetComponent<TextMeshProUGUI>().text = info;
        this.transform.GetChild(2).GetChild(5).GetChild(0).GetComponent<TextMeshProUGUI>().text = button;

        this.transform.GetChild(2).gameObject.SetActive(true);
        this.transform.GetChild(2).GetComponent<AudioSource>().Play();

        this.GetComponent<Animator>().Play("ShowInfo");
    }

    public void ShowWarning(string title, string info, string button)
    {
        SelectorOutline.Instance.defaultObject = this.transform.GetChild(1).GetChild(5).gameObject;
        SelectorOutline.Instance.RestrictButtonSelection(gameObject);
        this.transform.GetChild(1).GetChild(3).GetComponent<TextMeshProUGUI>().text = title;
        this.transform.GetChild(1).GetChild(4).GetComponent<TextMeshProUGUI>().text = info;
        this.transform.GetChild(1).GetChild(5).GetChild(0).GetComponent<TextMeshProUGUI>().text = button;

        this.transform.GetChild(1).gameObject.SetActive(true);
        this.transform.GetChild(1).GetComponent<AudioSource>().Play();

        this.GetComponent<Animator>().Play("ShowWarning");
    }

    public void ShowError(string title, string info, string button)
    {
        SelectorOutline.Instance.defaultObject = this.transform.GetChild(0).GetChild(5).gameObject;
        SelectorOutline.Instance.RestrictButtonSelection(gameObject);
        this.transform.GetChild(0).GetChild(3).GetComponent<TextMeshProUGUI>().text = title;
        this.transform.GetChild(0).GetChild(4).GetComponent<TextMeshProUGUI>().text = info;
        this.transform.GetChild(0).GetChild(5).GetChild(0).GetComponent<TextMeshProUGUI>().text = button;

        this.transform.GetChild(0).gameObject.SetActive(true);
        this.transform.GetChild(0).GetComponent<AudioSource>().Play();

        this.GetComponent<Animator>().Play("ShowError");
    }

    public void Dismiss()
    {
        SelectorOutline.Instance.defaultObject = localGO.transform.GetChild(2).gameObject;
        SelectorOutline.Instance.UnrestrictAllButtons();
    }
}
