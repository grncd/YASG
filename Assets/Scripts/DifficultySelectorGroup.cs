using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DifficultySelectorGroup : MonoBehaviour
{
    public Transform hover;
    public bool hovering = false;
    private CanvasGroup canvasGroup;
    private float fadeDuration = 0.15f;
    private float targetAlpha = 0f;
    public Canvas canvas;
    void OnEnable()
    {
        canvasGroup = hover.GetComponent<CanvasGroup>();
        foreach (Transform child in transform)
        {
            if (PlayerPrefs.GetInt(child.name) == 1)
            {
                child.gameObject.SetActive(true);
            }
            else
            {
                child.gameObject.SetActive(false);
            }
        }
    }

    public void HoverIn()
    {
        hovering = true;
        targetAlpha = 1f;
    }

    public void HoverOut()
    {
        hovering = false;
        targetAlpha = 0f;
    }

    private void Update()
    {
        // Smooth fade
        canvasGroup.alpha = Mathf.MoveTowards(canvasGroup.alpha, targetAlpha, Time.deltaTime / fadeDuration);

        Vector2 localPoint;
        RectTransform canvasRect = hover.GetComponentInParent<Canvas>().GetComponent<RectTransform>();
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRect,
            Input.mousePosition,
            canvas.worldCamera, // Replace with canvas.worldCamera if using Screen Space - Camera
            out localPoint
        );
        ((RectTransform)hover).localPosition = localPoint;
    }
}
