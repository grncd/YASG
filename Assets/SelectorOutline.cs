using MPUIKIT;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.EventSystems;

public class SelectorOutline : MonoBehaviour
{
    public EventSystem eventSystem;
    public GameObject selectorOutline;
    public GameObject defaultObject;

    [Header("Smoothing")]
    public float positionSmoothTime = 0.15f;
    public float sizeSmoothTime = 0.15f;

    private Vector3 velocity = Vector3.zero;
    private Vector2 sizeVelocity = Vector2.zero;

    private RectTransform selectorRect;

    void Start()
    {
        selectorRect = selectorOutline.GetComponent<RectTransform>();
    }

    public void SetDefault(GameObject set)
    {
        defaultObject = set;
    }

    void Update()
    {
        GameObject selected = eventSystem.currentSelectedGameObject;
        if (selected != null)
        {
            if (!selected.activeInHierarchy)
            {
                eventSystem.SetSelectedGameObject(defaultObject);
                selected = eventSystem.currentSelectedGameObject;
            }
            selectorOutline.SetActive(true);

            RectTransform targetRect = selected.GetComponent<RectTransform>();

            // Smooth position
            Vector3 currentPos = selectorRect.position;
            Vector3 targetPos = targetRect.position;
            Vector3 smoothedPos = EaseOutCubic(currentPos, targetPos, positionSmoothTime);
            selectorRect.position = smoothedPos;

            // Smooth size
            Vector2 currentSize = selectorRect.sizeDelta;
            Vector2 targetSize = targetRect.sizeDelta;
            Vector2 smoothedSize = EaseOutCubic(currentSize, targetSize, sizeSmoothTime);
            selectorRect.sizeDelta = smoothedSize;
            if(selected.GetComponent<MPImage>() != null) 
            {
                if (selected.GetComponent<MPImage>().DrawShape == DrawShape.Rectangle)
                {
                    selectorRect.GetComponent<MPImage>().DrawShape = DrawShape.Rectangle;
                    selectorRect.GetComponent<MPImage>().Rectangle = selected.GetComponent<MPImage>().Rectangle;
                }
                else
                {
                    selectorRect.GetComponent<MPImage>().DrawShape = DrawShape.Circle;
                    selectorRect.GetComponent<MPImage>().Circle = selected.GetComponent<MPImage>().Circle;
                }
            }
        }
        else
        {
            selectorOutline.SetActive(false);
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
