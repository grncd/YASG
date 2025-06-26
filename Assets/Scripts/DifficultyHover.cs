using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Threading.Tasks;

public class DifficultyHover : MonoBehaviour
{
    public Transform dropdown;
    private bool mouseIn = false;
    private float animTimeElapsed = 0f;
    private bool booting = true;

    void OnEnable()
    {
        booting = true;
        Open();
    }

    private async void Open()
    {
        gameObject.GetComponent<CanvasGroup>().alpha = 0f;
        dropdown.position = new Vector3(0,0,0);
        await Task.Delay(TimeSpan.FromSeconds(0.5f));
        FadeInCanvasGroup(gameObject, 0.5f, () => {
            animTimeElapsed = 0f;
            dropdown.position = new Vector3(0, 0, 0);
            booting = false;
        });
    }

    public async void Close()
    {
        MouseOut();
        FadeOutCanvasGroup(gameObject, 0.25f, () => {
        });
    }

    private void Update()
    {
        if (mouseIn)
        {
            if(animTimeElapsed < 0.25f)
            {
                animTimeElapsed += Time.deltaTime;
            }
            else
            {
                animTimeElapsed = 0.25f;
            }
        }
        else
        {
            if (animTimeElapsed > 0f)
            {
                animTimeElapsed -= Time.deltaTime;
            }
            else
            {
                animTimeElapsed = 0f;
            }
        }
        Debug.Log(animTimeElapsed);
        if (!booting)
        {
            dropdown.position = new Vector3(0, EaseOutCubic(animTimeElapsed * 4f) * -1.645f, 0);
            gameObject.GetComponent<CanvasGroup>().alpha = Remap(EaseOutCubic(animTimeElapsed * 4f), 0, 1, 1, 0);
        }
    }

    public void MouseIn()
    {
        if (!booting)
        {
            mouseIn = true;
        }
    }

    public void MouseOut()
    {
        if (!booting)
        {
            mouseIn = false;
        }
    }

    public static float Remap(float value, float from1, float to1, float from2, float to2)
    {
        return (value - from1) / (to1 - from1) * (to2 - from2) + from2;
    }

    public static float EaseOutCubic(float progress)
    {
        progress = Mathf.Clamp01(progress);
        float p = 1.0f - progress;
        return 1.0f - (p * p * p);
    }

    public Coroutine FadeInCanvasGroup(GameObject targetGameObject, float durationInSeconds, System.Action onComplete = null)
    {
        return StartFade(targetGameObject, 0f, 1f, durationInSeconds, onComplete, "FadeInCanvasGroup");
    }
    public Coroutine FadeOutCanvasGroup(GameObject targetGameObject, float durationInSeconds, System.Action onComplete = null)
    {
        return StartFade(targetGameObject, 1f, 0f, durationInSeconds, onComplete, "FadeOutCanvasGroup");
    }

    private Coroutine StartFade(GameObject targetGameObject, float startAlpha, float endAlpha, float durationInSeconds, System.Action onComplete, string callingMethodName)
    {
        if (targetGameObject == null)
        {
            Debug.LogError($"{callingMethodName}: targetGameObject is null. Cannot fade.");
            onComplete?.Invoke();
            return null;
        }

        CanvasGroup canvasGroup = targetGameObject.GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            Debug.LogWarning($"{callingMethodName}: No CanvasGroup found on '{targetGameObject.name}'. Adding one.");
            canvasGroup = targetGameObject.AddComponent<CanvasGroup>();
        }

        return StartCoroutine(FadeAlphaCoroutine(canvasGroup, startAlpha, endAlpha, durationInSeconds, onComplete));
    }

    private IEnumerator FadeAlphaCoroutine(CanvasGroup canvasGroup, float startAlpha, float endAlpha, float duration, System.Action onComplete)
    {
        if (canvasGroup == null)
        {
            Debug.LogError("FadeAlphaCoroutine: CanvasGroup is null.");
            onComplete?.Invoke();
            yield break;
        }

        if (duration <= 0f) // Handle immediate fade
        {
            canvasGroup.alpha = endAlpha;
            onComplete?.Invoke();
            yield break;
        }

        float elapsedTime = 0f;
        // Set initial alpha based on the intended start, especially if a previous fade was interrupted
        canvasGroup.alpha = startAlpha;

        while (elapsedTime < duration)
        {
            elapsedTime += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsedTime / duration);
            float easedProgress = progress * progress * (3f - 2f * progress); // SmoothStep

            canvasGroup.alpha = Mathf.Lerp(startAlpha, endAlpha, easedProgress);
            yield return null;
        }

        canvasGroup.alpha = endAlpha;
        onComplete?.Invoke();
    }

}
