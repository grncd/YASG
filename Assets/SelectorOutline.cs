using MPUIKIT;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using System.Collections.Generic;

public class SelectorOutline : MonoBehaviour
{
    public EventSystem eventSystem;
    public GameObject selectorOutline;
    public GameObject defaultObject;

    [Header("Smoothing")]
    public float positionSmoothTime = 0.15f;
    public float sizeSmoothTime = 0.15f;

    [Header("Buttons to Toggle Interactability")]
    public List<Button> buttonsToToggle;

    private RectTransform selectorRect;
    private Vector3 velocity = Vector3.zero;
    private Vector2 sizeVelocity = Vector2.zero;

    private Vector3 lastMousePos;
    private string lastInput = "controller"; // "mouse" or "controller"

    public static SelectorOutline Instance { get; private set; }

    void Start()
    {
        Instance = this;
        selectorRect = selectorOutline.GetComponent<RectTransform>();
        lastMousePos = Input.mousePosition;
    }

    public GameObject allowedParentProfileAdd;
    public GameObject allowedParentAlertBox;

    private List<Button> cachedButtons = new List<Button>();
    private List<Button> allowedButtons = new List<Button>();

    public void RestrictButtonSelection(GameObject select)
    {

        // Get all buttons in the scene (active + inactive)
        Button[] allButtons = GameObject.FindObjectsOfType<Button>();
        cachedButtons.Clear();
        allowedButtons.Clear();

        // Get all buttons that are children of the allowed parent
        allowedButtons.AddRange(select.GetComponentsInChildren<Button>());

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

    public void SetDefault(GameObject set)
    {
        defaultObject = set;
    }

    void Update()
    {
        DetectInputDevice();

        GameObject selected = eventSystem.currentSelectedGameObject;
        if (selected != null)
        {
            if (!selected.activeInHierarchy)
            {
                eventSystem.SetSelectedGameObject(defaultObject);
                selected = eventSystem.currentSelectedGameObject;
            }
            if(selected.GetComponent<Button>() != null)
            {
                if (!selected.GetComponent<Button>().enabled)
                {
                    eventSystem.SetSelectedGameObject(defaultObject);
                    selected = eventSystem.currentSelectedGameObject;
                }
            }

            selectorOutline.SetActive(true);
            RectTransform targetRect = selected.GetComponent<RectTransform>();

            // Smooth position
            Vector3 smoothedPos = EaseOutCubic(selectorRect.position, targetRect.position, positionSmoothTime);
            selectorRect.position = smoothedPos;

            // Smooth size
            Vector2 smoothedSize = EaseOutCubic(selectorRect.sizeDelta, targetRect.sizeDelta, sizeSmoothTime);
            selectorRect.sizeDelta = smoothedSize;

            // Match shape (MPImage)
            MPImage selectedImg = selected.GetComponent<MPImage>();
            MPImage outlineImg = selectorRect.GetComponent<MPImage>();

            if (selectedImg != null && outlineImg != null)
            {
                if (selectedImg.DrawShape == DrawShape.Rectangle)
                {
                    outlineImg.DrawShape = DrawShape.Rectangle;
                    outlineImg.Rectangle = selectedImg.Rectangle;
                }
                else
                {
                    outlineImg.DrawShape = DrawShape.Circle;
                    outlineImg.Circle = selectedImg.Circle;
                }
            }
        }
        else
        {
            selectorOutline.SetActive(false);
        }
    }

    void DetectInputDevice()
    {
        // Detect mouse movement
        if ((Input.mousePosition - lastMousePos).sqrMagnitude > 1f)
        {
            if (lastInput != "mouse")
            {
                SetButtonInteractivity(false); // disable buttons for mouse
                eventSystem.SetSelectedGameObject(null); // unselect on mouse input
            }
            lastInput = "mouse";
        }

        // Detect keyboard/controller input
        if (Input.GetAxis("Horizontal") != 0 || Input.GetAxis("Vertical") != 0 || Input.GetButtonDown("Submit"))
        {
            if (lastInput != "controller")
            {
                SetButtonInteractivity(true); // enable buttons for controller
                eventSystem.SetSelectedGameObject(defaultObject); // select default on controller input
            }
            lastInput = "controller";
        }

        lastMousePos = Input.mousePosition;
    }

    void SetButtonInteractivity(bool state)
    {
        foreach (Button btn in buttonsToToggle)
        {
            if (btn != null)
                btn.interactable = state;
        }
    }

    // EaseOutCubic interpolation for Vector3
    Vector3 EaseOutCubic(Vector3 current, Vector3 target, float smoothTime)
    {
        float t = Time.deltaTime / smoothTime;
        t = Mathf.Clamp01(t);
        t = 1f - Mathf.Pow(1f - t, 3f);
        return Vector3.Lerp(current, target, t);
    }

    // EaseOutCubic interpolation for Vector2
    Vector2 EaseOutCubic(Vector2 current, Vector2 target, float smoothTime)
    {
        float t = Time.deltaTime / smoothTime;
        t = Mathf.Clamp01(t);
        t = 1f - Mathf.Pow(1f - t, 3f);
        return Vector2.Lerp(current, target, t);
    }
}
