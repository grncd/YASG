using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class Exit : MonoBehaviour
{
    [SerializeField] private float holdDuration = 0.8f; // X seconds to hold ESC

    private float holdProgress = 0f; // 0 to 1
    private CanvasGroup canvasGroup;
    private Image fillImage;
    private Coroutine releaseCoroutine;

    void Start()
    {
        canvasGroup = GetComponent<CanvasGroup>();
        fillImage = transform.GetChild(1).GetComponent<Image>();

        // Initialize with alpha 0
        if (canvasGroup != null)
            canvasGroup.alpha = 0f;

        if (fillImage != null)
            fillImage.fillAmount = 0f;
    }

    void Update()
    {
        if (Input.GetKey(KeyCode.Escape))
        {
            // Stop any release animation if ESC is pressed again
            if (releaseCoroutine != null)
            {
                StopCoroutine(releaseCoroutine);
                releaseCoroutine = null;
            }

            // Increase hold progress
            holdProgress += Time.deltaTime / holdDuration;
            holdProgress = Mathf.Clamp01(holdProgress);

            // Update canvas alpha (0 to 0.1 range maps to 0 to 1 alpha)
            if (canvasGroup != null)
            {
                float alphaProgress = Mathf.Clamp01(holdProgress / 0.1f);
                canvasGroup.alpha = alphaProgress;
            }

            // Update fill amount (0 to 1 range)
            if (fillImage != null)
            {
                fillImage.fillAmount = holdProgress;
            }

            // Exit when fully held
            if (holdProgress >= 1f)
            {
                ExitApplication();
            }
        }
        else if (holdProgress > 0f)
        {
            // ESC released before completion - animate back
            releaseCoroutine = StartCoroutine(ReleaseAnimation());
        }
    }

    private IEnumerator ReleaseAnimation()
    {
        float startFill = fillImage != null ? fillImage.fillAmount : 0f;
        float startAlpha = canvasGroup != null ? canvasGroup.alpha : 0f;

        float elapsed = 0f;
        float fillDuration = 0.1f;
        float alphaDuration = 0.2f;

        while (elapsed < Mathf.Max(fillDuration, alphaDuration))
        {
            elapsed += Time.deltaTime;

            // Cubic out easing for fill (0.1 seconds)
            if (fillImage != null && elapsed < fillDuration)
            {
                float t = elapsed / fillDuration;
                float cubicOut = 1f - Mathf.Pow(1f - t, 3f);
                fillImage.fillAmount = Mathf.Lerp(startFill, 0f, cubicOut);
            }
            else if (fillImage != null)
            {
                fillImage.fillAmount = 0f;
            }

            // Linear easing for alpha (0.2 seconds)
            if (canvasGroup != null && elapsed < alphaDuration)
            {
                float t = elapsed / alphaDuration;
                canvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, t);
            }
            else if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
            }

            yield return null;
        }

        // Reset progress
        holdProgress = 0f;
        releaseCoroutine = null;
    }

    private void ExitApplication()
    {
        #if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
        #else
            Application.Quit();
        #endif
    }
}
