using MPUIKIT;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Timeout : MonoBehaviour
{
    private float elapsedTime = 0f;
    private bool waiting = false;
    private float targetTime = 0f;
    private MPImage progressImg;
    private bool hasStartedFade = false;
    public CanvasGroup pitchTrackCanvasGroup;
    void Start()
    {
        if (LyricsHandler.Instance != null)
        {
            LyricsHandler.Instance.OnNewLyricLine += OnNewLyricLine;
        }
        GetComponent<CanvasGroup>().alpha = 0f;
        progressImg = GetComponent<MPImage>();
    }

    public void CallTimeout(float seconds)
    {
        progressImg.fillAmount = 0f;
        targetTime = seconds;
        elapsedTime = 0f;
        waiting = true;
        GetComponent<CanvasGroup>().alpha = 1f;
        // Fade pitchTrackCanvasGroup from 1f to 0.5f
        if (pitchTrackCanvasGroup != null)
        {
            StopCoroutine("FadePitchTrackCanvasGroupUp"); // Stop any ongoing fade up
            StartCoroutine(FadePitchTrackCanvasGroupDown());
        }
    }

    private void Update()
    {
        if (waiting)
        {
            elapsedTime += Time.deltaTime;
            progressImg.fillAmount = elapsedTime / targetTime;

            if (elapsedTime > targetTime - 0.5f && !hasStartedFade)
            {
                hasStartedFade = true;
                StartCoroutine(FadeCanvasGroup(GetComponent<CanvasGroup>(), 1f, 0f, 0.7f));
            }

            if (elapsedTime / targetTime > 1f)
            {
                waiting = false;
                hasStartedFade = false;
                GetComponent<CanvasGroup>().alpha = 0f;
                // Fade pitchTrackCanvasGroup from 0.5f to 1f
                if (pitchTrackCanvasGroup != null)
                {
                    StopCoroutine("FadePitchTrackCanvasGroupDown"); // Stop any ongoing fade down
                    StartCoroutine(FadePitchTrackCanvasGroupUp());
                }
            }
        }
    }

    private void OnNewLyricLine(int lineIndex, float startTime, bool judge)
    {
        if (!judge && lineIndex != LyricsHandler.Instance.parsedLyrics.Count)
        {
            if(lineIndex == 0)
            {
                CallTimeout(startTime-1f);
            }
            else
            {
                CallTimeout(LyricsHandler.Instance.GetLineDuration(lineIndex)-1f);
            }
        }
    }

    private System.Collections.IEnumerator FadeCanvasGroup(CanvasGroup canvasGroup, float startAlpha, float endAlpha, float duration)
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

    private IEnumerator FadePitchTrackCanvasGroupDown()
    {
        float duration = 0.7f;
        float startAlpha = 1f;
        float endAlpha = 0.5f;
        float time = 0f;
        pitchTrackCanvasGroup.alpha = startAlpha;

        while (time < duration)
        {
            time += Time.deltaTime;
            pitchTrackCanvasGroup.alpha = Mathf.Lerp(startAlpha, endAlpha, time / duration);
            yield return null;
        }

        pitchTrackCanvasGroup.alpha = endAlpha;
    }

    private IEnumerator FadePitchTrackCanvasGroupUp()
    {
        float duration = 0.7f;
        float startAlpha = 0.5f;
        float endAlpha = 1f;
        float time = 0f;
        pitchTrackCanvasGroup.alpha = startAlpha;

        while (time < duration)
        {
            time += Time.deltaTime;
            pitchTrackCanvasGroup.alpha = Mathf.Lerp(startAlpha, endAlpha, time / duration);
            yield return null;
        }

        pitchTrackCanvasGroup.alpha = endAlpha;
    }
}
