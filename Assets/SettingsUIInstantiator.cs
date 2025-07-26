using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SettingsUIInstantiator : MonoBehaviour
{
    public GameObject textInputPrefab;
    public GameObject dropdownPrefab;
    public GameObject togglePrefab;
    public Transform prefabTarget;
    
    void OnEnable()
    {
        if (prefabTarget.childCount != 0)
        {
            foreach (Transform child in prefabTarget)
            {
                Destroy(child.gameObject);
            }
        }

        Dictionary<string, Setting> settings;
        switch (gameObject.name)
        {
            case "GameplayTab":
                settings = SettingsManager.Instance.GetSettingsByCategory(SettingCategory.Gameplay);
                break;
            case "ProcessingTab":
                settings = SettingsManager.Instance.GetSettingsByCategory(SettingCategory.Processing);
                break;
            case "MiscTab":
                settings = SettingsManager.Instance.GetSettingsByCategory(SettingCategory.Misc);
                break;
            default:
                settings = SettingsManager.Instance.GetSettingsByCategory(SettingCategory.Misc);
                break;
        }

        foreach (var setting in settings)
        {
            if (!setting.Value.IsHidden)
            {
                if (setting.Value.UIType == UIType.TextInput)
                {
                    GameObject textInput = Instantiate(textInputPrefab, prefabTarget);
                    textInput.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = setting.Value.FormalName;
                    textInput.transform.GetChild(1).GetComponent<TextMeshProUGUI>().text = setting.Value.Description;
                    TMP_InputField inputfield = textInput.transform.GetChild(2).GetComponent<TMP_InputField>();
                    inputfield.onEndEdit.AddListener(delegate { SettingsManager.Instance.SetSetting(setting.Key, inputfield.text); });
                    inputfield.text = setting.Value.Value.ToString();
                }
                if (setting.Value.UIType == UIType.Dropdown)
                {
                    GameObject textInput = Instantiate(dropdownPrefab, prefabTarget);
                    textInput.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = setting.Value.FormalName;
                    textInput.transform.GetChild(1).GetComponent<TextMeshProUGUI>().text = setting.Value.Description;
                    TMP_Dropdown dropdown = textInput.transform.GetChild(2).GetComponent<TMP_Dropdown>();
                    dropdown.AddOptions(setting.Value.DropdownOptions);
                    dropdown.onValueChanged.AddListener(delegate { SettingsManager.Instance.SetSetting(setting.Key, dropdown.value); });
                    dropdown.value = Convert.ToInt32(setting.Value.Value);
                }
                if (setting.Value.UIType == UIType.Toggle)
                {
                    GameObject textInput = Instantiate(togglePrefab, prefabTarget);
                    textInput.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = setting.Value.FormalName;
                    textInput.transform.GetChild(1).GetComponent<TextMeshProUGUI>().text = setting.Value.Description;
                    UnityEngine.UI.Toggle toggle = textInput.transform.GetChild(2).GetComponent<UnityEngine.UI.Toggle>();
                    toggle.onValueChanged.AddListener(delegate { SettingsManager.Instance.SetSetting(setting.Key, toggle.isOn); });
                    toggle.isOn = (bool)setting.Value.Value;
                }
            }
        }

    }
}
