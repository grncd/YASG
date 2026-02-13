using UnityEngine;
using TMPro;

[RequireComponent(typeof(TMP_Text))]
public class LocalizedText : MonoBehaviour
{
    [Tooltip("The localization key for this text (e.g., 'menu.play')")]
    public string localizationKey;

    private TMP_Text _text;

    private void Awake()
    {
        _text = GetComponent<TMP_Text>();
    }

    private void OnEnable()
    {
        LocalizationManager.OnLanguageChanged += UpdateText;
        UpdateText();
    }

    private void OnDisable()
    {
        LocalizationManager.OnLanguageChanged -= UpdateText;
    }

    private void Start()
    {
        // Backup: if OnEnable's UpdateText failed (e.g. Instance wasn't ready), retry in Start
        UpdateText();
    }

    public void UpdateText()
    {
        if (_text == null) _text = GetComponent<TMP_Text>();
        if (string.IsNullOrEmpty(localizationKey)) return;
        _text.text = LocalizationManager.L(localizationKey, _text.text);
    }
}
