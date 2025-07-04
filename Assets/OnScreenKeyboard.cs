using System.Collections;
using TMPro;
using UnityEngine;

public class OnScreenKeyboard : MonoBehaviour
{
    public TMP_InputField targetInputField;
    public float fadeDuration = 0.25f;

    private CanvasGroup canvasGroup;
    private Coroutine fadeRoutine;

    private void Start()
    {
        canvasGroup = GetComponent<CanvasGroup>();
        Disable();
    }

    private void Update()
    {
        if (canvasGroup != null && canvasGroup.interactable)
        {
            // B button on Xbox controller = JoystickButton1
            if (Input.GetKeyDown(KeyCode.JoystickButton1))
            {
                Backspace();
            }

            // X button on Xbox controller = JoystickButton2
            if (Input.GetKeyDown(KeyCode.JoystickButton2))
            {
                Dismiss();
            }
        }
    }

    public void SetTarget(TMP_InputField targetInputField)
    {
        this.targetInputField = targetInputField;
    }

    public void Type(GameObject key)
    {
        if (targetInputField == null) return;

        if (key.name == "Space")
        {
            targetInputField.text += " ";
        }
        else
        {
            targetInputField.text += key.name;
        }
    }

    public void Backspace()
    {
        if (targetInputField == null) return;

        if (targetInputField.text.Length > 0)
        {
            targetInputField.text = targetInputField.text.Substring(0, targetInputField.text.Length - 1);
        }
    }

    public void Dismiss()
    {
        Disable();
    }

    public void Disable()
    {
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        fadeRoutine = StartCoroutine(FadeCanvasGroup(0f, false));
    }

    public void Enable()
    {
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        fadeRoutine = StartCoroutine(FadeCanvasGroup(1f, true));
    }

    private IEnumerator FadeCanvasGroup(float targetAlpha, bool enableInteraction)
    {
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();

        float startAlpha = canvasGroup.alpha;
        float time = 0f;

        if (enableInteraction)
        {
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
        }

        while (time < fadeDuration)
        {
            time += Time.deltaTime;
            float t = Mathf.Clamp01(time / fadeDuration);
            canvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, t);
            yield return null;
        }

        canvasGroup.alpha = targetAlpha;

        if (!enableInteraction)
        {
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }
    }
}
