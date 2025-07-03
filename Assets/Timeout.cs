
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
        targetTime = seconds;
        elapsedTime = 0f;
        waiting = true;
        GetComponent<CanvasGroup>().alpha = 1f;
    }

    private void Update()
    {
        if (waiting)
        {
            elapsedTime += Time.deltaTime;
            progressImg.fillAmount = elapsedTime / targetTime;

            if(elapsedTime > targetTime - 0.5f)
            {
                StartCoroutine(FadeCanvasGroup(GetComponent<CanvasGroup>(),1f,0f,0.7f));
            }

            if(elapsedTime / targetTime > 1f)
            {
                waiting = false;
                GetComponent<CanvasGroup>().alpha = 0f;
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
}
