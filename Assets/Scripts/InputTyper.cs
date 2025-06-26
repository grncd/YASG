using TMPro;
using UnityEngine;

public class InputTyper : MonoBehaviour
{
    public TMP_InputField inputField;

    public void TypeText(string textToType)
    {
        if (inputField != null)
        {
            inputField.text += textToType;
        }
    }
    public void DeleteText()
    {
        if (inputField != null)
        {
            inputField.text = inputField.text[..^1];
        }
    }
}