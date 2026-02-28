using UnityEngine;
using UnityEngine.UI;
using System.Collections;

[RequireComponent(typeof(CanvasGroup))]
[RequireComponent(typeof(RectTransform))]
public class PopupAnimator : MonoBehaviour
{
    public float appearDuration = 0.3f;
    public float disappearDuration = 0.12f;
    public bool enableDimOverlay = false;
    [Range(0f, 1f)] public float dimAlpha = 0.5f;

    private CanvasGroup canvasGroup;
    private RectTransform rectTransform;
    private Vector2 originalPosition;
    private Coroutine currentCoroutine;
    private GameObject dimOverlay;
    private Image dimImage;

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

        if (enableDimOverlay) CreateDimOverlay();

        currentCoroutine = StartCoroutine(AnimateIn());
    }

    void OnDisable()
    {
        // Reset to original state to prevent "creeping" if disabled mid-animation
        rectTransform.anchoredPosition = originalPosition;
        canvasGroup.alpha = 1f;
        canvasGroup.blocksRaycasts = true;
        DestroyDimOverlay();
    }

    void CreateDimOverlay()
    {
        DestroyDimOverlay();

        dimOverlay = new GameObject("DimOverlay");
        dimOverlay.transform.SetParent(transform.parent, false);
        dimOverlay.transform.SetSiblingIndex(transform.GetSiblingIndex());

        RectTransform dimRect = dimOverlay.AddComponent<RectTransform>();
        dimRect.anchorMin = Vector2.zero;
        dimRect.anchorMax = Vector2.one;
        dimRect.offsetMin = Vector2.zero;
        dimRect.offsetMax = Vector2.zero;

        dimImage = dimOverlay.AddComponent<Image>();
        dimImage.color = new Color(0f, 0f, 0f, 0f);
        dimImage.raycastTarget = true;
    }

    void DestroyDimOverlay()
    {
        if (dimOverlay != null)
        {
            Destroy(dimOverlay);
            dimOverlay = null;
            dimImage = null;
        }
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

            // Dim overlay fade in (fixed 0.15s)
            if (dimImage != null)
            {
                float dimT = Mathf.Clamp01(elapsed / 0.15f);
                dimImage.color = new Color(0f, 0f, 0f, Mathf.Lerp(0f, dimAlpha, dimT));
            }

            // Position: CubicOut Easing
            // f(t) = 1 - (1 - t)^3
            float ease = 1f - Mathf.Pow(1f - t, 3f);
            rectTransform.anchoredPosition = Vector2.Lerp(startPos, originalPosition, ease);

            yield return null;
        }

        // Finalize
        canvasGroup.alpha = 1f;
        rectTransform.anchoredPosition = originalPosition;
        if (dimImage != null) dimImage.color = new Color(0f, 0f, 0f, dimAlpha);
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
        float startDimAlpha = dimImage != null ? dimImage.color.a : 0f;

        while (elapsed < disappearDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / disappearDuration);

            // Alpha: Linear -> 0
            canvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, t);

            // Dim overlay fade out
            if (dimImage != null)
                dimImage.color = new Color(0f, 0f, 0f, Mathf.Lerp(startDimAlpha, 0f, t));

            yield return null;
        }

        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = true;
        DestroyDimOverlay();
        gameObject.SetActive(false);
        currentCoroutine = null;
    }
}
