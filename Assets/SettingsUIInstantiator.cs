using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SettingsUIInstantiator : MonoBehaviour
{
    public GameObject textInputPrefab;
    public GameObject dropdownPrefab;
    public GameObject togglePrefab;
    public Transform prefabTarget;
    void OnEnable()
    {
        if (gameObject.name == "GameplayTab")
        {
            Dictionary<string,Setting> settings = SettingsManager.Instance.GetSettingsByCategory(SettingCategory.Gameplay);
            foreach (var setting in settings)
            {
                if (setting.Value.UIType == UIType.TextInput)
                {
                    GameObject textInput = Instantiate(textInputPrefab, prefabTarget);
                }
            }
        }
        
    }
}
