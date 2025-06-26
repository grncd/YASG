using UnityEngine;
using UnityEngine.UI;

public class SliderPixel : MonoBehaviour
{
    public Slider mySlider;

    void Start()
    {
        if (mySlider == null)
        {
            Debug.LogError("Slider not assigned!");
            return;
        }

        float pixels = GetPixelsPerIncrement(mySlider);
        if (pixels > 0)
        {
            Debug.Log($"The slider '{mySlider.name}' handle moves approximately {pixels:F2} pixels for an increment of 1 in its value.");
        }
    }

    public float GetPixelsPerIncrement(Slider slider)
    {
        if (slider == null) return 0f;

        // 1. Calculate the total range of slider values
        float valueRange = slider.maxValue - slider.minValue;
        if (valueRange <= 0)
        {
            Debug.LogWarning("Slider maxValue must be greater than minValue for a valid calculation.");
            return 0f;
        }

        // 2. Get the RectTransform of the area the handle slides within.
        // This is usually the parent of the handleRect.
        RectTransform handleContainerRect = null;
        if (slider.handleRect != null && slider.handleRect.parent != null)
        {
            handleContainerRect = slider.handleRect.parent.GetComponent<RectTransform>();
        }

        if (handleContainerRect == null)
        {
            Debug.LogError("Could not determine handle container RectTransform. Ensure slider is set up correctly (has a handle and its parent is a RectTransform).");
            // As a fallback, you could try using slider.GetComponent<RectTransform>(),
            // but this is less accurate as the handle usually doesn't span the entire slider width.
            // For now, we'll return 0 if the preferred container isn't found.
            return 0f;
        }

        // 3. Determine the total pixel width/height available for the handle to move.
        // This depends on the slider's direction.
        float trackPixelLength;
        Slider.Direction direction = slider.direction;

        // Unity's default UI coordinate system has (0,0) at bottom-left for anchors.
        // rect.width and rect.height are always positive.
        if (direction == Slider.Direction.LeftToRight || direction == Slider.Direction.RightToLeft)
        {
            trackPixelLength = handleContainerRect.rect.width;
        }
        else // TopToBottom or BottomToTop
        {
            trackPixelLength = handleContainerRect.rect.height;
        }

        if (trackPixelLength <= 0)
        {
            Debug.LogWarning("Calculated track pixel length is zero or negative. Check UI layout and ensure the handle container has a positive size.");
            return 0f;
        }

        // 4. Calculate pixels moved per single unit increment.
        float pixelsPerUnit = trackPixelLength / valueRange;

        return pixelsPerUnit;
    }
}