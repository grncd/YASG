using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public class ConsoleLogHandler : MonoBehaviour
{
    public TextMeshProUGUI logText;
    public ScrollRect scrollRect;

    public void AddLog(string message)
    {
        logText.text += $"\n{message}";

        // Wait for one frame so the Content Size Fitter can recalculate the height
        StartCoroutine(SnapToBottom());
    }

    IEnumerator SnapToBottom()
    {
        yield return new WaitForEndOfFrame();
        scrollRect.verticalNormalizedPosition = 0f; // 0 is the bottom
    }
}