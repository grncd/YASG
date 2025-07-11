using MPUIKIT;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TimeoutPlayer : MonoBehaviour
{
    private float elapsedTime = 0f;
    private bool waiting = false;
    private float targetTime = 0f;
    private bool hasStartedFade = false;

    void Start()
    {
        if (LyricsHandler.Instance != null)
        {
            LyricsHandler.Instance.OnNewLyricLine += OnNewLyricLine;
        }
        GetComponent<CanvasGroup>().alpha = 0f;
    }

    public void CallTimeout(float seconds)
    {
        targetTime = seconds;
        elapsedTime = 0f;
        waiting = true;
        hasStartedFade = false;

        CanvasGroup cg = GetComponent<CanvasGroup>();
        StartCoroutine(FadeCanvasGroup(cg, 0f, 1f, 0.25f));

        // If timeout is too short to trigger fade-out later, start it now
        if (targetTime <= 0.5f)
        {
            hasStartedFade = true;
            StartCoroutine(FadeCanvasGroup(cg, 1f, 0f, 0.25f));
        }
    }

    private void Update()
    {
        if (waiting)
        {
            elapsedTime += Time.deltaTime;

            if (elapsedTime >= targetTime - 0.5f && !hasStartedFade)
            {
                hasStartedFade = true;
                StartCoroutine(FadeCanvasGroup(GetComponent<CanvasGroup>(), 1f, 0f, 0.25f));
            }

            if (elapsedTime >= targetTime)
            {
                waiting = false;
                hasStartedFade = false;
                GetComponent<CanvasGroup>().alpha = 0f;
            }
        }
    }

    private void OnNewLyricLine(int lineIndex, float startTime, bool judge)
    {
        if (!judge && lineIndex != LyricsHandler.Instance.parsedLyrics.Count)
        {
            if (lineIndex == 0)
            {
                CallTimeout(startTime - 1f);
            }
            else
            {
                CallTimeout(LyricsHandler.Instance.GetLineDuration(lineIndex) - 1f);
            }
        }
    }

    private IEnumerator FadeCanvasGroup(CanvasGroup canvasGroup, float startAlpha, float endAlpha, float duration)
    {
        float time = 0f;
        canvasGroup.alpha = startAlpha;

        while (time < duration)
        {
            time += Time.deltaTime;
            canvasGroup.alpha = Mathf.Lerp(startAlpha, endAlpha, time / duration);
            yield return null;
        }

        canvasGroup.alpha = endAlpha;
    }
}
