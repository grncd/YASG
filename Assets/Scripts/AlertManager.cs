using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class AlertManager : MonoBehaviour
{

    public static AlertManager Instance { get; private set; }

    private void Awake()
    {
        Instance = this;
        PlayerPrefs.SetInt("multiplayer", 0);
    }

    public void ShowInfo(string title, string info, string button)
    {
        this.transform.GetChild(2).GetChild(3).GetComponent<TextMeshProUGUI>().text = title;
        this.transform.GetChild(2).GetChild(4).GetComponent<TextMeshProUGUI>().text = info;
        this.transform.GetChild(2).GetChild(5).GetChild(0).GetComponent<TextMeshProUGUI>().text = button;

        this.transform.GetChild(2).gameObject.SetActive(true);
        this.transform.GetChild(2).GetComponent<AudioSource>().Play();

        this.GetComponent<Animator>().Play("ShowInfo");
    }

    public void ShowWarning(string title, string info, string button)
    {
        this.transform.GetChild(1).GetChild(3).GetComponent<TextMeshProUGUI>().text = title;
        this.transform.GetChild(1).GetChild(4).GetComponent<TextMeshProUGUI>().text = info;
        this.transform.GetChild(1).GetChild(5).GetChild(0).GetComponent<TextMeshProUGUI>().text = button;

        this.transform.GetChild(1).gameObject.SetActive(true);
        this.transform.GetChild(1).GetComponent<AudioSource>().Play();

        this.GetComponent<Animator>().Play("ShowWarning");
    }

    public void ShowError(string title, string info, string button)
    {
        this.transform.GetChild(0).GetChild(3).GetComponent<TextMeshProUGUI>().text = title;
        this.transform.GetChild(0).GetChild(4).GetComponent<TextMeshProUGUI>().text = info;
        this.transform.GetChild(0).GetChild(5).GetChild(0).GetComponent<TextMeshProUGUI>().text = button;

        this.transform.GetChild(0).gameObject.SetActive(true);
        this.transform.GetChild(0).GetComponent<AudioSource>().Play();

        this.GetComponent<Animator>().Play("ShowError");
    }
}
