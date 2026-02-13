using UnityEngine;

public class UIBounce : MonoBehaviour
{
    private RectTransform _rect;
    private Vector2 _startPos;

    private void Awake()
    {
        _rect = GetComponent<RectTransform>();
        _startPos = _rect.anchoredPosition;
    }

    private void Update()
    {
        float t = (Time.time % 1f) / 1f;
        // Starts at 0, peaks at +60, returns to 0
        float offset = Mathf.Sin(t * Mathf.PI) * 60f;
        _rect.anchoredPosition = _startPos + new Vector2(0f, offset);
    }
}
