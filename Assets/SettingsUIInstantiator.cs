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
                string localizedName = LocalizationManager.L("setting." + setting.Key + ".name", setting.Value.FormalName);
                string localizedDesc = LocalizationManager.L("setting." + setting.Key + ".desc", setting.Value.Description);

                if (setting.Value.UIType == UIType.TextInput)
                {
                    GameObject textInput = Instantiate(textInputPrefab, prefabTarget);
                    textInput.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = localizedName;
                    textInput.transform.GetChild(1).GetComponent<TextMeshProUGUI>().text = localizedDesc;
                    TMP_InputField inputfield = textInput.transform.GetChild(2).GetComponent<TMP_InputField>();
                    inputfield.onEndEdit.AddListener(delegate {
                        // Check if this is a string-based setting (like ApiKey or ClientId)
                        if (setting.Key == "ApiKey" || setting.Key == "ClientId")
                        {
                            SettingsManager.Instance.SetSetting(setting.Key, inputfield.text);
                        }
                        else
                        {
                            // For numeric settings, validate as float
                            float result;
                            if (float.TryParse(inputfield.text, out result))
                            {
                                SettingsManager.Instance.SetSetting(setting.Key, inputfield.text);
                            }
                            else
                            {
                                inputfield.text = setting.Value.Value.ToString();
                            }
                        }
                    });
                    inputfield.text = setting.Value.Value.ToString();
                }
                if (setting.Value.UIType == UIType.Dropdown)
                {
                    GameObject textInput = Instantiate(dropdownPrefab, prefabTarget);
                    textInput.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = localizedName;
                    textInput.transform.GetChild(1).GetComponent<TextMeshProUGUI>().text = localizedDesc;
                    TMP_Dropdown dropdown = textInput.transform.GetChild(2).GetComponent<TMP_Dropdown>();

                    // Localize dropdown options
                    var options = new List<string>(setting.Value.DropdownOptions);
                    for (int i = 0; i < options.Count; i++)
                    {
                        options[i] = LocalizationManager.L("setting." + setting.Key + ".option." + i, options[i]);
                    }
                    dropdown.AddOptions(options);

                    dropdown.onValueChanged.AddListener(delegate { SettingsManager.Instance.SetSetting(setting.Key, dropdown.value); });
                    dropdown.value = Convert.ToInt32(setting.Value.Value);
                }
                if (setting.Value.UIType == UIType.Toggle)
                {
                    GameObject textInput = Instantiate(togglePrefab, prefabTarget);
                    textInput.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = localizedName;
                    textInput.transform.GetChild(1).GetComponent<TextMeshProUGUI>().text = localizedDesc;
                    UnityEngine.UI.Toggle toggle = textInput.transform.GetChild(2).GetComponent<UnityEngine.UI.Toggle>();
                    toggle.onValueChanged.AddListener(delegate { SettingsManager.Instance.SetSetting(setting.Key, toggle.isOn); });
                    toggle.isOn = (bool)setting.Value.Value;
                }
            }
        }

    }
}
