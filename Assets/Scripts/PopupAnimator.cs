using UnityEngine;
using System.Collections;

[RequireComponent(typeof(CanvasGroup))]
[RequireComponent(typeof(RectTransform))]
public class PopupAnimator : MonoBehaviour
{
    public float appearDuration = 0.3f;
    public float disappearDuration = 0.12f;

    private CanvasGroup canvasGroup;
    private RectTransform rectTransform;
    private Vector2 originalPosition;
    private Coroutine currentCoroutine;

    void Awake()
    {
        canvasGroup = GetComponent<CanvasGroup>();
        rectTransform = GetComponent<RectTransform>();
    }

    void OnEnable()
    {
        if (currentCoroutine != null) StopCoroutine(currentCoroutine);

        originalPosition = rectTransform.anchoredPosition;

        // Initial State
        canvasGroup.alpha = 0f;
        rectTransform.anchoredPosition = originalPosition + new Vector2(0, -75f);

        currentCoroutine = StartCoroutine(AnimateIn());
    }

    void OnDisable()
    {
        // Reset to original state to prevent "creeping" if disabled mid-animation
        rectTransform.anchoredPosition = originalPosition;
        canvasGroup.alpha = 1f; // Optional, but good for safety
        canvasGroup.blocksRaycasts = true; // Ensure reset
    }

    IEnumerator AnimateIn()
    {
        float elapsed = 0f;
        Vector2 startPos = rectTransform.anchoredPosition;

        while (elapsed < appearDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / appearDuration);

            // Alpha: Linear 0 -> 1
            canvasGroup.alpha = t * 3;

            // Position: CubicOut Easing
            // f(t) = 1 - (1 - t)^3
            float ease = 1f - Mathf.Pow(1f - t, 3f);
            rectTransform.anchoredPosition = Vector2.Lerp(startPos, originalPosition, ease);

            yield return null;
        }

        // Finalize
        canvasGroup.alpha = 1f;
        rectTransform.anchoredPosition = originalPosition;
        currentCoroutine = null;
    }

    public void Close()
    {
        if (currentCoroutine != null) StopCoroutine(currentCoroutine);
        currentCoroutine = StartCoroutine(AnimateOut());
    }

    IEnumerator AnimateOut()
    {
        canvasGroup.blocksRaycasts = false;

        float elapsed = 0f;
        float startAlpha = canvasGroup.alpha;

        while (elapsed < disappearDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / disappearDuration);

            // Alpha: Linear -> 0
            canvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, t);

            yield return null;
        }

        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = true;
        gameObject.SetActive(false);
        currentCoroutine = null;
    }
}
