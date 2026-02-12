using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LanguageSetup : MonoBehaviour
{
    public GameObject languagePrefab;
    public Transform languageParent;

    void Start()
    {
        PopulateLanguages();
    }

    private void PopulateLanguages()
    {
        if (LocalizationManager.Instance == null) return;

        var codes = LocalizationManager.Instance.AvailableLanguageCodes;
        var names = LocalizationManager.Instance.GetLanguageDisplayNames();

        for (int i = 0; i < codes.Count; i++)
        {
            string langCode = codes[i];
            int langIndex = i;

            GameObject btn = Instantiate(languagePrefab, languageParent);
            btn.SetActive(true);

            TMP_Text label = btn.transform.GetChild(0).GetComponent<TMP_Text>();
            if (label != null)
                label.text = names[i];

            Button button = btn.GetComponent<Button>();
            if (button != null)
            {
                button.onClick.AddListener(() =>
                {
                    LocalizationManager.Instance.LoadLanguage(langCode);
                    if (SettingsManager.Instance != null)
                        SettingsManager.Instance.SetSetting("Language", langIndex);
                });
            }
        }
    }
}
