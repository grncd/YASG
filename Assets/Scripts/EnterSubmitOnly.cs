using UnityEngine;
using UnityEngine.Events;
using TMPro;

public class EnterSubmitOnly : MonoBehaviour
{
    public UnityEvent OnEnterPressed;

    private TMP_InputField inputField;

    void Awake()
    {
        inputField = GetComponent<TMP_InputField>();
    }

    void OnEnable()
    {
        if (inputField != null)
        {
            inputField.onSubmit.AddListener(HandleSubmit);
        }
    }

    void OnDisable()
    {
        if (inputField != null)
        {
            inputField.onSubmit.RemoveListener(HandleSubmit);
        }
    }

    private void HandleSubmit(string text)
    {
        OnEnterPressed?.Invoke();
    }
}