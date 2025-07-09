using UnityEngine;
using UnityEngine.UI;
using System.Collections;

/// <summary>
/// Controls a ScrollRect to smoothly center its content on a specific child element.
/// This version uses Time.unscaledTime to ensure smooth animation even if Time.timeScale is 0.
/// </summary>
[RequireComponent(typeof(ScrollRect))]
public class LyricsScroller : MonoBehaviour
{
    [Header("Scrolling Settings")]
    [Tooltip("The duration of the smooth scroll animation in seconds.")]
    [SerializeField] private float scrollDuration = 0.5f;

    [Tooltip("If true, adds padding to allow the first and last items to be centered.")]
    [SerializeField] private bool addCenteringPadding = true;

    [Tooltip("Manual pixel offset for centering. Positive values move the centered lyric UP, negative values move it DOWN.")]
    [SerializeField] private float centeringOffset = 0f;

    private ScrollRect scrollRect;
    private RectTransform contentPanel;
    private RectTransform viewport;

    private Coroutine activeScrollCoroutine;
    private bool paddingHasBeenAdded = false;

    void Awake()
    {
        scrollRect = GetComponent<ScrollRect>();
        contentPanel = scrollRect.content;
        viewport = scrollRect.viewport ?? GetComponent<RectTransform>();
    }

    void Start()
    {
        if (addCenteringPadding && !paddingHasBeenAdded)
        {
            AddPaddingElements();
        }
    }

    private void AddPaddingElements()
    {
        if (viewport == null || contentPanel == null) return;
        float paddingHeight = (viewport.rect.height);
        CreateSpacer(paddingHeight, "TopCenteringSpacer").transform.SetAsFirstSibling();
        CreateSpacer(paddingHeight, "BottomCenteringSpacer").transform.SetAsLastSibling();
        paddingHasBeenAdded = true;
    }

    private GameObject CreateSpacer(float height, string name)
    {
        GameObject spacer = new GameObject(name, typeof(RectTransform), typeof(LayoutElement));
        spacer.transform.SetParent(contentPanel, false);
        LayoutElement le = spacer.GetComponent<LayoutElement>();
        le.minHeight = height;
        le.flexibleHeight = 0;
        return spacer;
    }

    public void CenterOnLyric(int lyricIndex)
    {
        int childIndexToTarget = lyricIndex + (paddingHasBeenAdded ? 1 : 0);

        if (contentPanel == null || childIndexToTarget < 0 || childIndexToTarget >= contentPanel.childCount)
        {
            Debug.LogError($"Invalid lyric index: {lyricIndex}. Cannot find child at adjusted index {childIndexToTarget}.", this);
            return;
        }

        if (activeScrollCoroutine != null) StopCoroutine(activeScrollCoroutine);

        float targetPosition = CalculateTargetNormalizedPosition(childIndexToTarget);

        if (Mathf.Approximately(scrollRect.verticalNormalizedPosition, targetPosition)) return;

        activeScrollCoroutine = StartCoroutine(SmoothScrollTo(targetPosition));
    }

    private float CalculateTargetNormalizedPosition(int itemIndex)
    {
        RectTransform targetLyric = contentPanel.GetChild(itemIndex) as RectTransform;
        float viewportHeight = viewport.rect.height;
        float contentHeight = contentPanel.rect.height;
        float scrollableHeight = contentHeight - viewportHeight;

        if (scrollableHeight <= 0) return 1f;

        float lyricPivotPosition = -targetLyric.anchoredPosition.y;
        float desiredContentTopPosition = lyricPivotPosition - (viewportHeight / 2f) + centeringOffset;
        float normalizedPosition = desiredContentTopPosition / scrollableHeight;

        return 1f - Mathf.Clamp01(normalizedPosition);
    }

    /// <summary>
    /// The robust scrolling coroutine that is not affected by Time.timeScale.
    /// </summary>
    private IEnumerator SmoothScrollTo(float target)
    {
        float startPosition = scrollRect.verticalNormalizedPosition;
        float startTime = Time.unscaledTime; // Use the unscaled time as our start point
        float elapsedTime = 0f;

        while (elapsedTime < scrollDuration)
        {
            // Calculate elapsed time based on the unscaled clock
            elapsedTime = Time.unscaledTime - startTime;

            // Get our progress (0 to 1)
            float t = Mathf.Clamp01(elapsedTime / scrollDuration);

            // Apply easing
            float easedT = 1 - Mathf.Pow(1 - t, 3);

            // Apply the new position
            scrollRect.verticalNormalizedPosition = Mathf.Lerp(startPosition, target, easedT);

            yield return null; // Wait for the next frame
        }

        // Ensure the final position is exact
        scrollRect.verticalNormalizedPosition = target;
        activeScrollCoroutine = null;
    }

    #region Inspector Test
    [Header("Inspector Test")]
    [Tooltip("The 0-based index of the lyric to center on.")]
    [SerializeField] private int testLyricIndex = 1;

    [ContextMenu("Test Center on Lyric")]
    private void TestCentering()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("Context menu tests must be run in Play Mode.");
            return;
        }
        CenterOnLyric(testLyricIndex);
    }
    #endregion
}