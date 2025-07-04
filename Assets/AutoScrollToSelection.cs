using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// This script automatically scrolls a ScrollRect to keep the currently selected UI element in view.
/// It works by calculating the normalized position and is compatible with inertia and other ScrollRect effects.
/// Attach this to the GameObject that has the ScrollRect component.
/// </summary>
[RequireComponent(typeof(ScrollRect))]
public class AutoScrollToSelection : MonoBehaviour
{
    [Header("Scrolling Setup")]
    [Tooltip("How fast the content scrolls to the selected item. A value of 10 is a good starting point.")]
    public float scrollSpeed = 10f;

    private ScrollRect scrollRect;
    private RectTransform contentPanel;
    private RectTransform viewport;
    private GameObject lastSelected;
    private Coroutine scrollingCoroutine;

    void Start()
    {
        scrollRect = GetComponent<ScrollRect>();
        contentPanel = scrollRect.content;
        viewport = scrollRect.viewport;
        lastSelected = EventSystem.current.currentSelectedGameObject;
    }

    void Update()
    {
        GameObject currentSelected = EventSystem.current.currentSelectedGameObject;

        // Check if the selection has changed and is a child of our scrollable content.
        if (currentSelected != null && currentSelected != lastSelected && currentSelected.transform.IsChildOf(contentPanel))
        {
            if (scrollingCoroutine != null)
            {
                StopCoroutine(scrollingCoroutine);
            }
            scrollingCoroutine = StartCoroutine(ScrollToView(currentSelected.GetComponent<RectTransform>()));
        }

        lastSelected = currentSelected;
    }



    private IEnumerator ScrollToView(RectTransform target)
    {
        // Wait for the end of frame to ensure all layout calculations are complete.
        yield return new WaitForEndOfFrame();

        // Calculate the desired normalized position.
        float targetNormalizedPos = CalculateTargetNormalizedPosition(target);

        // Lerp the scrollrect's position to the target position.
        while (Mathf.Abs(scrollRect.verticalNormalizedPosition - targetNormalizedPos) > 0.001f)
        {
            scrollRect.verticalNormalizedPosition = Mathf.Lerp(scrollRect.verticalNormalizedPosition, targetNormalizedPos, scrollSpeed * Time.unscaledDeltaTime);
            yield return null; // Wait for the next frame
        }
        // Snap to the final position to ensure accuracy.
        scrollRect.verticalNormalizedPosition = targetNormalizedPos;
        scrollingCoroutine = null;
    }

    private float CalculateTargetNormalizedPosition(RectTransform target)
    {
        // --- This logic is for a vertically scrolling list where the content pivot is at the top (y=1) ---

        // The height of the content and the viewport.
        float contentHeight = contentPanel.rect.height;
        float viewportHeight = viewport.rect.height;

        // If content is smaller than viewport, no scrolling is needed.
        if (contentHeight <= viewportHeight)
        {
            return 1f; // Return 1 (top) as it's the default.
        }

        // The position of the target item relative to the content's top.
        // anchoredPosition.y is negative, so we make it positive.
        float itemPosInContent = -target.anchoredPosition.y;

        // The total scrollable distance.
        float scrollableDistance = contentHeight - viewportHeight;

        // The current normalized position. 1 is top, 0 is bottom.
        float currentNormalizedPos = scrollRect.verticalNormalizedPosition;

        // Convert normalized position to an absolute scroll position (from the top).
        float currentScrollPos = (1 - currentNormalizedPos) * scrollableDistance;

        // --- Check if the item is out of view ---

        // If the top of the item is above the top of the viewport.
        if (itemPosInContent < currentScrollPos)
        {
            // We need to scroll UP. The new scroll position should be the item's position.
            return 1 - (itemPosInContent / scrollableDistance);
        }

        // If the bottom of the item is below the bottom of the viewport.
        float itemBottomPosInContent = itemPosInContent + target.rect.height;
        if (itemBottomPosInContent > (currentScrollPos + viewportHeight))
        {
            // We need to scroll DOWN. The new scroll position should be adjusted to show the item's bottom.
            float newScrollPos = itemBottomPosInContent - viewportHeight;
            return 1 - (newScrollPos / scrollableDistance);
        }

        // If the item is already fully in view, don't scroll.
        return currentNormalizedPos;
    }
}