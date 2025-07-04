using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class OnScreenKeyboard : MonoBehaviour
{
    public TMP_InputField targetInputField;
    public float fadeDuration = 0.25f;

    private CanvasGroup canvasGroup;
    private Coroutine fadeRoutine;
    public EventSystem eventSystem;
    public GameObject defaultKey;

    public GameObject allowedParent;

    private List<Button> cachedButtons = new List<Button>();
    private List<Button> allowedButtons = new List<Button>();

    public void RestrictButtonSelection()
    {
        if (allowedParent == null)
        {
            Debug.LogWarning("Allowed parent not assigned!");
            return;
        }

        // Get all buttons in the scene (active + inactive)
        Button[] allButtons = GameObject.FindObjectsOfType<Button>();
        cachedButtons.Clear();
        allowedButtons.Clear();

        // Get all buttons that are children of the allowed parent
        allowedButtons.AddRange(allowedParent.GetComponentsInChildren<Button>());

        foreach (Button btn in allButtons)
        {
            cachedButtons.Add(btn);
            bool shouldEnable = allowedButtons.Contains(btn);
            btn.interactable = shouldEnable;
        }
    }

    public void UnrestrictAllButtons()
    {
        if (cachedButtons.Count == 0)
        {
            cachedButtons.AddRange(GameObject.FindObjectsOfType<Button>());
        }

        foreach (Button btn in cachedButtons)
        {
            if (btn != null)
                btn.interactable = true;
        }

        cachedButtons.Clear();
        allowedButtons.Clear();
    }

    private void OnEnable()
    {
        if (canvasGroup == null)
        {
            RestrictButtonSelection();
            eventSystem.SetSelectedGameObject(defaultKey);
            eventSystem.gameObject.GetComponent<SelectorOutline>().defaultObject = defaultKey;
        }
    }

    private void OnDisable()
    {
        if (canvasGroup == null)
        {
            UnrestrictAllButtons();
        }
    }

    private void Start()
    {
        canvasGroup = transform.parent.GetComponent<CanvasGroup>();
        if(canvasGroup != null) Disable();
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
        UnrestrictAllButtons();
        if (targetInputField != null) targetInputField.onEndEdit.Invoke(targetInputField.text);
        fadeRoutine = StartCoroutine(FadeCanvasGroup(0f, false));
    }

    public void Enable()
    {
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        RestrictButtonSelection();
        eventSystem.SetSelectedGameObject(defaultKey);
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
