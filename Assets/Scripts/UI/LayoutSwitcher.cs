using System;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class LayoutSwitcher : MonoBehaviour
{
    [Serializable]
    public class LayoutData
    {
        public Vector2 anchorMin;
        public Vector2 anchorMax;
        public Vector2 pivot;
        public Vector2 anchoredPosition;
        public Vector2 sizeDelta;
        public Vector3 localScale;
        public Vector3 localEulerAngles;

        public bool hasTMPText;
        public float fontSize;

        public void CaptureFrom(RectTransform rt)
        {
            anchorMin = rt.anchorMin;
            anchorMax = rt.anchorMax;
            pivot = rt.pivot;
            anchoredPosition = rt.anchoredPosition;
            sizeDelta = rt.sizeDelta;
            localScale = rt.localScale;
            localEulerAngles = rt.localEulerAngles;

            var tmp = rt.GetComponent<TMP_Text>();
            if (tmp != null)
            {
                hasTMPText = true;
                fontSize = tmp.fontSize;
            }
        }

        public void ApplyTo(RectTransform rt)
        {
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = anchoredPosition;
            rt.sizeDelta = sizeDelta;
            rt.localScale = localScale;
            rt.localEulerAngles = localEulerAngles;

            if (hasTMPText)
            {
                var tmp = rt.GetComponent<TMP_Text>();
                if (tmp != null)
                    tmp.fontSize = fontSize;
            }
        }
    }

    [Serializable]
    public class LayoutEntry
    {
        public RectTransform target;
        public LayoutData landscape = new LayoutData();
        public LayoutData portrait = new LayoutData();
        [HideInInspector] public bool captured;
    }

    public List<LayoutEntry> entries = new List<LayoutEntry>();

    bool _isPortrait;

    void Start()
    {
        _isPortrait = Screen.height > Screen.width;
        ApplyLayout(_isPortrait);
    }

    void Update()
    {
        bool portrait = Screen.height > Screen.width;
        if (portrait != _isPortrait)
        {
            _isPortrait = portrait;
            ApplyLayout(_isPortrait);
        }
    }

    public void ApplyLayout(bool portrait)
    {
        foreach (var entry in entries)
        {
            if (entry.target == null) continue;
            if (portrait)
                entry.portrait.ApplyTo(entry.target);
            else
                entry.landscape.ApplyTo(entry.target);
        }
    }

    public void CaptureCurrentAs(bool portrait)
    {
        foreach (var entry in entries)
        {
            if (entry.target == null) continue;
            var data = portrait ? entry.portrait : entry.landscape;
            data.CaptureFrom(entry.target);
            entry.captured = true;
        }
    }

    public void AddAllChildRectTransforms()
    {
        var existing = new HashSet<RectTransform>();
        foreach (var entry in entries)
        {
            if (entry.target != null)
                existing.Add(entry.target);
        }

        var allRTs = GetComponentsInChildren<RectTransform>(true);
        foreach (var rt in allRTs)
        {
            if (rt == (RectTransform)transform) continue;
            if (existing.Contains(rt)) continue;
            entries.Add(new LayoutEntry { target = rt });
        }
    }
}
