using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

public class Exit : MonoBehaviour
{
    [SerializeField] private float holdDuration = 0.8f; // X seconds to hold ESC

    private float holdProgress = 0f; // 0 to 1
    private CanvasGroup canvasGroup;
    private Image fillImage;
    private Coroutine releaseCoroutine;
    public List<GameObject> dismissButtons;

    void Start()
    {
        StartCoroutine(SetFrameRate());

        IEnumerator SetFrameRate()
        {
            yield return new WaitForSeconds(1f);

            // Apply FPS limit setting on all platforms
            if (SettingsManager.Instance != null)
            {
                bool limitFPS = SettingsManager.Instance.GetSetting<bool>("LimitFPS", false);

                if (limitFPS)
                {
                    Application.targetFrameRate = 60;
                    UnityEngine.Debug.Log("[Exit] FPS limited to 60");
                }
                else
                {
                    // On Android, use native refresh rate
                    if (Application.platform == RuntimePlatform.Android)
                    {
                        Application.targetFrameRate = (int)Screen.currentResolution.refreshRateRatio.value;
                        UnityEngine.Debug.Log($"[Exit] FPS set to native refresh rate: {Application.targetFrameRate}");
                    }
                    else
                    {
                        // On other platforms, use -1 (unlimited)
                        Application.targetFrameRate = -1;
                        UnityEngine.Debug.Log("[Exit] FPS set to unlimited (-1)");
                    }
                }
            }
            else if (Application.platform == RuntimePlatform.Android)
            {
                Application.targetFrameRate = 60;
            }
        }
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
        bool hasActiveButtons = false;
        foreach (GameObject btn in dismissButtons)
        {
            if (btn != null && btn.activeInHierarchy)
            {
                hasActiveButtons = true;
                break;
            }
        }

        GameObject highest = null;
        if (Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            foreach (GameObject btn in dismissButtons)
            {
                if (btn != null && btn.activeInHierarchy)
                {
                    if (highest == null || IsHigherInHierarchy(btn.transform, highest.transform))
                    {
                        highest = btn;
                    }
                }
            }
        }

        if (highest != null)
        {
            highest.GetComponent<UnityEngine.UI.Button>()?.onClick.Invoke();
        }

        if (Keyboard.current.escapeKey.isPressed && highest == null && !hasActiveButtons)
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
    private bool IsHigherInHierarchy(Transform t1, Transform t2)
    {
        if (t1 == t2) return false;
        if (t1.IsChildOf(t2)) return true; // Child is always above parent
        if (t2.IsChildOf(t1)) return false; // Parent is always below child

        // Find common root
        Transform root1 = t1.root;
        Transform root2 = t2.root;
        if (root1 != root2) return false; // Different roots, can't determine (assume t1 is not higher)

        // Find path from root to t1 and t2
        List<Transform> path1 = new List<Transform>();
        Transform curr = t1;
        while (curr != null) { path1.Add(curr); curr = curr.parent; }
        path1.Reverse();

        List<Transform> path2 = new List<Transform>();
        curr = t2;
        while (curr != null) { path2.Add(curr); curr = curr.parent; }
        path2.Reverse();

        // Find first divergence
        int count = Mathf.Min(path1.Count, path2.Count);
        for (int i = 0; i < count; i++)
        {
            if (path1[i] != path2[i])
            {
                // Compare sibling indices
                return path1[i].GetSiblingIndex() > path2[i].GetSiblingIndex();
            }
        }

        return false;
    }
}
