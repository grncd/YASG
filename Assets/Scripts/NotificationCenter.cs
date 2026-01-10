using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class NotificationCenter : MonoBehaviour
{
    public static NotificationCenter Instance { get; private set; }

    [Header("Prefabs")]
    public GameObject errorPrefab;
    public GameObject warningPrefab;
    public GameObject infoPrefab;
    public GameObject successPrefab;

    [Header("Notification Center")]
    public Transform contentParent;

    [Header("Popup Settings")]
    public Transform popupParent;
    public float popupDuration = 5f;
    public float slideAnimationDuration = 0.2f;
    public float slideDistance = 500f;

    [Header("Audio")]
    public AudioSource notificationSound;

    [Header("Effects")]
    public ParticleSystem backgroundParticles;

    private Animator animator;
    private Coroutine particleFadeCoroutine;
    private List<GameObject> activeNotifications = new List<GameObject>();

    public enum NotificationType
    {
        Error,
        Warning,
        Info,
        Success
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        animator = GetComponent<Animator>();
    }

    /// <summary>
    /// Plays the NotificationsIn animation to show the notification center.
    /// </summary>
    public void ShowNotificationCenter()
    {
        animator.Play("NotificationsIn");

        // Fade out particle system
        if (backgroundParticles != null)
        {
            if (particleFadeCoroutine != null)
                StopCoroutine(particleFadeCoroutine);
            particleFadeCoroutine = StartCoroutine(FadeParticleSystem(backgroundParticles, 0.06666667f, 0f, 0.2f));
        }
    }

    /// <summary>
    /// Plays the NotificationsOut animation to hide the notification center.
    /// </summary>
    public void HideNotificationCenter()
    {
        animator.Play("NotificationsOut");

        // Fade in particle system
        if (backgroundParticles != null)
        {
            if (particleFadeCoroutine != null)
                StopCoroutine(particleFadeCoroutine);
            particleFadeCoroutine = StartCoroutine(FadeParticleSystem(backgroundParticles, 0f, 0.06666667f, 0.2f));
        }
    }

    private IEnumerator FadeParticleSystem(ParticleSystem ps, float startAlpha, float endAlpha, float duration)
    {
        var main = ps.main;
        ParticleSystem.Particle[] particles = new ParticleSystem.Particle[main.maxParticles];

        Color originalColor = main.startColor.color;

        float timeStartedLerping = Time.time;

        while (Time.time < timeStartedLerping + duration)
        {
            float timeSinceStarted = Time.time - timeStartedLerping;
            float percentageComplete = timeSinceStarted / duration;
            float currentAlpha = Mathf.Lerp(startAlpha, endAlpha, percentageComplete);

            main.startColor = new Color(originalColor.r, originalColor.g, originalColor.b, currentAlpha);

            int particleCount = ps.GetParticles(particles);
            for (int i = 0; i < particleCount; i++)
            {
                Color32 particleColor = particles[i].startColor;
                particleColor.a = (byte)(currentAlpha * 255);
                particles[i].startColor = particleColor;
            }
            ps.SetParticles(particles, particleCount);

            yield return new WaitForEndOfFrame();
        }

        main.startColor = new Color(originalColor.r, originalColor.g, originalColor.b, endAlpha);

        int finalParticleCount = ps.GetParticles(particles);
        for (int i = 0; i < finalParticleCount; i++)
        {
            Color32 particleColor = particles[i].startColor;
            particleColor.a = (byte)(endAlpha * 255);
            particles[i].startColor = particleColor;
        }
        ps.SetParticles(particles, finalParticleCount);
    }

    private GameObject GetPrefab(NotificationType type)
    {
        return type switch
        {
            NotificationType.Error => errorPrefab,
            NotificationType.Warning => warningPrefab,
            NotificationType.Info => infoPrefab,
            NotificationType.Success => successPrefab,
            _ => infoPrefab
        };
    }

    private void SetupNotificationText(GameObject notification, string title, string info)
    {
        Transform titleTransform = notification.transform.Find("Title");
        Transform infoTransform = notification.transform.Find("Info");

        if (titleTransform != null)
        {
            TextMeshProUGUI titleText = titleTransform.GetComponent<TextMeshProUGUI>();
            if (titleText != null)
                titleText.text = title;
        }

        if (infoTransform != null)
        {
            TextMeshProUGUI infoText = infoTransform.GetComponent<TextMeshProUGUI>();
            if (infoText != null)
                infoText.text = info;
        }
    }

    /// <summary>
    /// Sends a notification to both the notification center and popup.
    /// </summary>
    public void SendNotification(NotificationType type, string title, string info)
    {
        GameObject prefab = GetPrefab(type);

        // Create notification center entry
        GameObject centerNotification = Instantiate(prefab, contentParent);
        centerNotification.SetActive(true);
        SetupNotificationText(centerNotification, title, info);

        // Disable timeout slider for notification center
        Transform timeoutTransform = centerNotification.transform.Find("Timeout");
        if (timeoutTransform != null)
            timeoutTransform.gameObject.SetActive(false);

        // Add CanvasGroup for fade animation and fade in
        CanvasGroup centerCanvasGroup = centerNotification.GetComponent<CanvasGroup>();
        if (centerCanvasGroup == null)
            centerCanvasGroup = centerNotification.AddComponent<CanvasGroup>();
        StartCoroutine(FadeIn(centerCanvasGroup, 0.2f));

        activeNotifications.Add(centerNotification);

        // Play notification sound
        if (notificationSound != null)
            notificationSound.Play();

        // Create popup notification
        if (popupParent != null)
        {
            // --- FIX: Wrap popup in a container to maintain layout stability while animating ---
            GameObject container = new GameObject("PopupContainer", typeof(RectTransform));
            container.transform.SetParent(popupParent, false);
            container.AddComponent<CanvasGroup>(); // Optional, for safety

            GameObject popupNotification = Instantiate(prefab, container.transform);
            popupNotification.SetActive(true);
            
            // Ensure container matches popup size for layout purposes
            RectTransform popupRect = popupNotification.GetComponent<RectTransform>();
            RectTransform containerRect = container.GetComponent<RectTransform>();
            containerRect.sizeDelta = popupRect.sizeDelta;

            // If the popup has a LayoutElement, ideally we should copy it or add one to container
            // For now, we assume standard usage (Grid or VLG uses RectTransform size)
            // But let's add a LayoutElement to container just in case the VLG needs it
            LayoutElement containerLE = container.AddComponent<LayoutElement>();
            LayoutElement popupLE = popupNotification.GetComponent<LayoutElement>();
            if (popupLE != null)
            {
                containerLE.preferredWidth = popupLE.preferredWidth;
                containerLE.preferredHeight = popupLE.preferredHeight;
                containerLE.minWidth = popupLE.minWidth;
                containerLE.minHeight = popupLE.minHeight;
                containerLE.flexibleWidth = popupLE.flexibleWidth;
                containerLE.flexibleHeight = popupLE.flexibleHeight;
            }
            else
            {
                // Fallback to rect size
                containerLE.preferredWidth = popupRect.sizeDelta.x;
                containerLE.preferredHeight = popupRect.sizeDelta.y;
            }

            SetupNotificationText(popupNotification, title, info);
            StartCoroutine(HandlePopupLifecycle(popupNotification));
        }
    }

    private IEnumerator HandlePopupLifecycle(GameObject popup)
    {
        RectTransform rectTransform = popup.GetComponent<RectTransform>();
        
        // Ensure popup anchors are set to fill the container so we can animate offset
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.sizeDelta = Vector2.zero; // Fill container

        CanvasGroup canvasGroup = popup.GetComponent<CanvasGroup>();
        if (canvasGroup == null)
            canvasGroup = popup.AddComponent<CanvasGroup>();

        // Get the timeout slider
        Transform timeoutTransform = popup.transform.Find("Timeout");
        Slider timeoutSlider = null;
        if (timeoutTransform != null)
        {
            timeoutSlider = timeoutTransform.GetComponent<Slider>();
            if (timeoutSlider != null)
                timeoutSlider.value = 0f;
        }

        // --- NEW LOGIC: Animate relative to container ---
        // Target position is (0,0) (centered in container)
        // Start position is (slideDistance, 0)
        Vector2 originalPosition = Vector2.zero;
        Vector2 rightHiddenPosition = new Vector2(slideDistance, 0);

        // Start from right hidden position
        rectTransform.anchoredPosition = rightHiddenPosition;

        // Slide in from right with cubic out easing
        yield return StartCoroutine(SlideAnimation(rectTransform, rightHiddenPosition, originalPosition, slideAnimationDuration, EaseOutCubic));

        // Stay on screen and update timeout slider
        float elapsed = 0f;
        while (elapsed < popupDuration)
        {
            elapsed += Time.deltaTime;
            if (timeoutSlider != null)
                timeoutSlider.value = elapsed / popupDuration;
            yield return null;
        }

        // Slide out to right with cubic in easing
        yield return StartCoroutine(SlideAnimation(rectTransform, originalPosition, rightHiddenPosition, slideAnimationDuration, EaseInCubic));

        // --- FIX: Destroy the container, not just the popup ---
        if (popup.transform.parent != null && popup.transform.parent != popupParent)
        {
            Destroy(popup.transform.parent.gameObject);
        }
        else
        {
            Destroy(popup);
        }
    }

    private IEnumerator SlideAnimation(RectTransform rectTransform, Vector2 from, Vector2 to, float duration, System.Func<float, float> easingFunction)
    {
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float easedT = easingFunction(t);
            rectTransform.anchoredPosition = Vector2.Lerp(from, to, easedT);
            yield return null;
        }

        rectTransform.anchoredPosition = to;
    }

    private float EaseOutCubic(float t)
    {
        return 1f - Mathf.Pow(1f - t, 3f);
    }

    private float EaseInCubic(float t)
    {
        return t * t * t;
    }

    /// <summary>
    /// Sends an error notification.
    /// </summary>
    public static void Error(string title, string info)
    {
        if (Instance != null)
            Instance.SendNotification(NotificationType.Error, title, info);
    }

    /// <summary>
    /// Sends a warning notification.
    /// </summary>
    public static void Warning(string title, string info)
    {
        if (Instance != null)
            Instance.SendNotification(NotificationType.Warning, title, info);
    }

    /// <summary>
    /// Sends an info notification.
    /// </summary>
    public static void Info(string title, string info)
    {
        if (Instance != null)
            Instance.SendNotification(NotificationType.Info, title, info);
    }

    /// <summary>
    /// Sends a success notification.
    /// </summary>
    public static void Success(string title, string info)
    {
        if (Instance != null)
            Instance.SendNotification(NotificationType.Success, title, info);
    }

    /// <summary>
    /// Clears all notifications from the notification center.
    /// </summary>
    public void ClearAllNotifications()
    {
        StartCoroutine(ClearAllNotificationsWithFade());
    }

    private IEnumerator ClearAllNotificationsWithFade()
    {
        List<Coroutine> fadeCoroutines = new List<Coroutine>();

        foreach (GameObject notification in activeNotifications)
        {
            if (notification != null)
            {
                CanvasGroup canvasGroup = notification.GetComponent<CanvasGroup>();
                if (canvasGroup == null)
                    canvasGroup = notification.AddComponent<CanvasGroup>();
                fadeCoroutines.Add(StartCoroutine(FadeOut(canvasGroup, 0.2f)));
            }
        }

        // Wait for all fade outs to complete
        foreach (Coroutine coroutine in fadeCoroutines)
        {
            yield return coroutine;
        }

        // Destroy all notifications
        foreach (GameObject notification in activeNotifications)
        {
            if (notification != null)
                Destroy(notification);
        }
        activeNotifications.Clear();
    }

    private IEnumerator FadeIn(CanvasGroup canvasGroup, float duration)
    {
        canvasGroup.alpha = 0f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            canvasGroup.alpha = Mathf.Clamp01(elapsed / duration);
            yield return null;
        }

        canvasGroup.alpha = 1f;
    }

    private IEnumerator FadeOut(CanvasGroup canvasGroup, float duration)
    {
        canvasGroup.alpha = 1f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            canvasGroup.alpha = 1f - Mathf.Clamp01(elapsed / duration);
            yield return null;
        }

        canvasGroup.alpha = 0f;
    }
}
