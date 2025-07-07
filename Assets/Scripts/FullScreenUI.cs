using UnityEngine;

[RequireComponent(typeof(RectTransform))]
public class FullScreenUI : MonoBehaviour
{
    void Awake()
    {
        RectTransform rectTransform = GetComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0, 0);
        rectTransform.anchorMax = new Vector2(1, 1);
        rectTransform.offsetMin = new Vector2(0, 0);
        rectTransform.offsetMax = new Vector2(0, 0);
    }
}
