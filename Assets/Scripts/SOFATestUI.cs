using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Test MonoBehaviour for SOFA alignment. Place in any scene to generate a test UI.
/// Runs alignment on a test song and visualizes results with real-time word/phoneme highlighting.
/// </summary>
public class SOFATestUI : MonoBehaviour
{
    [Header("Test Input")]
    public AudioClip testAudioClip;
    [TextArea(5, 20)]
    public string transcript = "";

    [Header("Settings")]
    [Range(0f, 1f)]
    public float volume = 0.8f;

    // UI references (auto-generated)
    private Canvas _canvas;
    private Text _statusText;
    private Text _confidenceText;
    private Text _phonemeDetailText;
    private Text _debugText;
    private ScrollRect _lyricsScrollRect;
    private ScrollRect _debugScrollRect;
    private RectTransform _lyricsContent;
    private Button _alignButton;
    private Button _playButton;
    private Button _stopButton;
    private Button _exportButton;
    private AudioSource _audioSource;

    // State
    private SOFAAligner.AlignmentResult _alignmentResult;
    private List<Text> _wordTexts = new List<Text>();
    private int _currentWordIndex = -1;
    private int _currentPhonemeIndex = -1;
    private bool _isPlaying;
    private float _lastWordChangeTime = -1f;

    // Colors
    private static readonly Color PastColor = new Color(0.5f, 0.5f, 0.5f);
    private static readonly Color CurrentColor = new Color(1f, 0.9f, 0.1f);
    private static readonly Color UpcomingColor = Color.white;
    private static readonly Color PhonemeColor = new Color(0.3f, 1f, 0.3f);
    private static readonly Color BGColor = new Color(0.12f, 0.12f, 0.15f);
    private static readonly Color PanelColor = new Color(0.18f, 0.18f, 0.22f);
    private static readonly Color ButtonColor = new Color(0.25f, 0.45f, 0.8f);
    private static readonly Color ButtonStopColor = new Color(0.8f, 0.25f, 0.25f);

    void Awake()
    {
        BuildUI();
    }

    void OnDestroy()
    {
        SOFAAligner.Dispose();
    }

    void Update()
    {
        if (!_isPlaying || _alignmentResult == null || _audioSource == null || !_audioSource.isPlaying)
        {
            if (_isPlaying && _audioSource != null && !_audioSource.isPlaying)
            {
                _isPlaying = false;
                _statusText.text = "Status: Finished";
                ResetHighlighting();
            }
            return;
        }

        float currentTime = _audioSource.time;
        UpdateWordHighlighting(currentTime);
        UpdatePhonemeDetail(currentTime);
    }

    #region UI Construction

    private void BuildUI()
    {
        // Ensure AudioSource
        _audioSource = GetComponent<AudioSource>();
        if (_audioSource == null)
            _audioSource = gameObject.AddComponent<AudioSource>();
        _audioSource.playOnAwake = false;

        // Canvas
        var canvasObj = new GameObject("SOFATestCanvas");
        canvasObj.transform.SetParent(transform);
        _canvas = canvasObj.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 100;
        canvasObj.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasObj.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1920, 1080);
        canvasObj.AddComponent<GraphicRaycaster>();

        // Background
        var bgObj = CreatePanel(canvasObj.transform, "Background", BGColor);
        var bgRect = bgObj.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.sizeDelta = Vector2.zero;

        // Main layout
        var mainLayout = bgObj.AddComponent<VerticalLayoutGroup>();
        mainLayout.padding = new RectOffset(20, 20, 20, 20);
        mainLayout.spacing = 10;
        mainLayout.childForceExpandWidth = true;
        mainLayout.childForceExpandHeight = false;
        var mainFitter = bgObj.AddComponent<ContentSizeFitter>();
        mainFitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

        // Header
        BuildHeader(bgObj.transform);

        // Controls
        BuildControls(bgObj.transform);

        // Content area (lyrics + debug side by side)
        BuildContentArea(bgObj.transform);
    }

    private void BuildHeader(Transform parent)
    {
        var headerPanel = CreatePanel(parent, "HeaderPanel", PanelColor);
        var headerRect = headerPanel.GetComponent<RectTransform>();
        headerRect.sizeDelta = new Vector2(0, 80);
        var headerLayout = headerPanel.AddComponent<HorizontalLayoutGroup>();
        headerLayout.padding = new RectOffset(15, 15, 10, 10);
        headerLayout.spacing = 20;
        headerLayout.childAlignment = TextAnchor.MiddleLeft;
        headerLayout.childForceExpandWidth = false;
        headerLayout.childForceExpandHeight = true;
        var headerLE = headerPanel.AddComponent<LayoutElement>();
        headerLE.preferredHeight = 80;
        headerLE.flexibleWidth = 1;

        // Title
        var title = CreateText(headerPanel.transform, "Title", "SOFA Alignment Test", 28, FontStyle.Bold);
        var titleLE = title.gameObject.AddComponent<LayoutElement>();
        titleLE.preferredWidth = 400;

        // Status
        _statusText = CreateText(headerPanel.transform, "Status", "Status: Ready", 20, FontStyle.Normal);
        _statusText.color = new Color(0.7f, 0.7f, 0.7f);
        var statusLE = _statusText.gameObject.AddComponent<LayoutElement>();
        statusLE.preferredWidth = 400;
        statusLE.flexibleWidth = 1;

        // Confidence
        _confidenceText = CreateText(headerPanel.transform, "Confidence", "Confidence: --", 20, FontStyle.Normal);
        _confidenceText.color = PhonemeColor;
        var confLE = _confidenceText.gameObject.AddComponent<LayoutElement>();
        confLE.preferredWidth = 250;
    }

    private void BuildControls(Transform parent)
    {
        var controlsPanel = CreatePanel(parent, "ControlsPanel", PanelColor);
        var controlsRect = controlsPanel.GetComponent<RectTransform>();
        var controlsLayout = controlsPanel.AddComponent<HorizontalLayoutGroup>();
        controlsLayout.padding = new RectOffset(15, 15, 10, 10);
        controlsLayout.spacing = 15;
        controlsLayout.childAlignment = TextAnchor.MiddleLeft;
        controlsLayout.childForceExpandWidth = false;
        controlsLayout.childForceExpandHeight = true;
        var controlsLE = controlsPanel.AddComponent<LayoutElement>();
        controlsLE.preferredHeight = 60;
        controlsLE.flexibleWidth = 1;

        _alignButton = CreateButton(controlsPanel.transform, "AlignBtn", "Align", ButtonColor, OnAlignClicked);
        _playButton = CreateButton(controlsPanel.transform, "PlayBtn", "Play", ButtonColor, OnPlayClicked);
        _stopButton = CreateButton(controlsPanel.transform, "StopBtn", "Stop", ButtonStopColor, OnStopClicked);
        _exportButton = CreateButton(controlsPanel.transform, "ExportBtn", "Export TG", new Color(0.2f, 0.65f, 0.35f), OnExportClicked);

        _playButton.interactable = false;
        _exportButton.interactable = false;

        // Phoneme detail
        _phonemeDetailText = CreateText(controlsPanel.transform, "PhonemeDetail", "Current: --", 18, FontStyle.Normal);
        _phonemeDetailText.color = PhonemeColor;
        var pdLE = _phonemeDetailText.gameObject.AddComponent<LayoutElement>();
        pdLE.preferredWidth = 600;
        pdLE.flexibleWidth = 1;
    }

    private void BuildContentArea(Transform parent)
    {
        var contentPanel = CreatePanel(parent, "ContentPanel", Color.clear);
        var contentLayout = contentPanel.AddComponent<HorizontalLayoutGroup>();
        contentLayout.spacing = 10;
        contentLayout.childForceExpandWidth = true;
        contentLayout.childForceExpandHeight = true;
        var contentLE = contentPanel.AddComponent<LayoutElement>();
        contentLE.flexibleHeight = 1;
        contentLE.flexibleWidth = 1;

        // Lyrics panel (left side, 60%)
        BuildLyricsPanel(contentPanel.transform);

        // Debug panel (right side, 40%)
        BuildDebugPanel(contentPanel.transform);
    }

    private void BuildLyricsPanel(Transform parent)
    {
        var lyricsPanel = CreatePanel(parent, "LyricsPanel", PanelColor);
        var lyricsLE = lyricsPanel.AddComponent<LayoutElement>();
        lyricsLE.flexibleWidth = 6;
        lyricsLE.flexibleHeight = 1;

        // Label
        var label = CreateText(lyricsPanel.transform, "LyricsLabel", "Words", 16, FontStyle.Bold);
        label.color = new Color(0.6f, 0.6f, 0.6f);
        var labelRect = label.GetComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0, 1);
        labelRect.anchorMax = new Vector2(1, 1);
        labelRect.pivot = new Vector2(0.5f, 1);
        labelRect.anchoredPosition = new Vector2(0, -5);
        labelRect.sizeDelta = new Vector2(-20, 25);

        // ScrollRect
        var scrollObj = new GameObject("LyricsScroll");
        scrollObj.transform.SetParent(lyricsPanel.transform, false);
        _lyricsScrollRect = scrollObj.AddComponent<ScrollRect>();
        _lyricsScrollRect.horizontal = false;
        var scrollRect = scrollObj.GetComponent<RectTransform>();
        scrollRect.anchorMin = new Vector2(0, 0);
        scrollRect.anchorMax = new Vector2(1, 1);
        scrollRect.offsetMin = new Vector2(10, 10);
        scrollRect.offsetMax = new Vector2(-10, -30);

        // Viewport
        var viewportObj = new GameObject("Viewport");
        viewportObj.transform.SetParent(scrollObj.transform, false);
        var viewportRect = viewportObj.AddComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.sizeDelta = Vector2.zero;
        viewportObj.AddComponent<Image>().color = Color.clear;
        viewportObj.AddComponent<Mask>().showMaskGraphic = false;
        _lyricsScrollRect.viewport = viewportRect;

        // Content
        var contentObj = new GameObject("Content");
        contentObj.transform.SetParent(viewportObj.transform, false);
        _lyricsContent = contentObj.AddComponent<RectTransform>();
        _lyricsContent.anchorMin = new Vector2(0, 1);
        _lyricsContent.anchorMax = new Vector2(1, 1);
        _lyricsContent.pivot = new Vector2(0.5f, 1);
        _lyricsContent.anchoredPosition = Vector2.zero;

        var contentLayout = contentObj.AddComponent<VerticalLayoutGroup>();
        contentLayout.padding = new RectOffset(5, 5, 5, 5);
        contentLayout.spacing = 2;
        contentLayout.childForceExpandWidth = false;
        contentLayout.childForceExpandHeight = false;
        contentLayout.childAlignment = TextAnchor.UpperLeft;

        var contentFitter = contentObj.AddComponent<ContentSizeFitter>();
        contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        _lyricsScrollRect.content = _lyricsContent;
    }

    private void BuildDebugPanel(Transform parent)
    {
        var debugPanel = CreatePanel(parent, "DebugPanel", PanelColor);
        var debugLE = debugPanel.AddComponent<LayoutElement>();
        debugLE.flexibleWidth = 4;
        debugLE.flexibleHeight = 1;

        // Label
        var label = CreateText(debugPanel.transform, "DebugLabel", "Phoneme Data", 16, FontStyle.Bold);
        label.color = new Color(0.6f, 0.6f, 0.6f);
        var labelRect = label.GetComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0, 1);
        labelRect.anchorMax = new Vector2(1, 1);
        labelRect.pivot = new Vector2(0.5f, 1);
        labelRect.anchoredPosition = new Vector2(0, -5);
        labelRect.sizeDelta = new Vector2(-20, 25);

        // ScrollRect
        var scrollObj = new GameObject("DebugScroll");
        scrollObj.transform.SetParent(debugPanel.transform, false);
        _debugScrollRect = scrollObj.AddComponent<ScrollRect>();
        _debugScrollRect.horizontal = false;
        var scrollRect = scrollObj.GetComponent<RectTransform>();
        scrollRect.anchorMin = new Vector2(0, 0);
        scrollRect.anchorMax = new Vector2(1, 1);
        scrollRect.offsetMin = new Vector2(10, 10);
        scrollRect.offsetMax = new Vector2(-10, -30);

        // Viewport
        var viewportObj = new GameObject("Viewport");
        viewportObj.transform.SetParent(scrollObj.transform, false);
        var viewportRect = viewportObj.AddComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.sizeDelta = Vector2.zero;
        viewportObj.AddComponent<Image>().color = Color.clear;
        viewportObj.AddComponent<Mask>().showMaskGraphic = false;
        _debugScrollRect.viewport = viewportRect;

        // Content
        var contentObj = new GameObject("Content");
        contentObj.transform.SetParent(viewportObj.transform, false);
        var contentRect = contentObj.AddComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0, 1);
        contentRect.anchorMax = new Vector2(1, 1);
        contentRect.pivot = new Vector2(0.5f, 1);
        contentRect.anchoredPosition = Vector2.zero;

        var contentFitter = contentObj.AddComponent<ContentSizeFitter>();
        contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        _debugText = CreateText(contentObj.transform, "DebugContent", "Run alignment to see data...", 14, FontStyle.Normal);
        _debugText.font = Font.CreateDynamicFontFromOSFont("Courier New", 14);
        _debugText.color = new Color(0.8f, 0.8f, 0.8f);
        var debugTextRect = _debugText.GetComponent<RectTransform>();
        debugTextRect.anchorMin = new Vector2(0, 1);
        debugTextRect.anchorMax = new Vector2(1, 1);
        debugTextRect.pivot = new Vector2(0, 1);
        debugTextRect.anchoredPosition = Vector2.zero;
        debugTextRect.sizeDelta = new Vector2(0, 0);

        _debugScrollRect.content = contentRect;
    }

    #endregion

    #region UI Helpers

    private GameObject CreatePanel(Transform parent, string name, Color color)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        var rect = obj.AddComponent<RectTransform>();
        var img = obj.AddComponent<Image>();
        img.color = color;
        return obj;
    }

    private Text CreateText(Transform parent, string name, string content, int fontSize, FontStyle style)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        obj.AddComponent<RectTransform>();
        var text = obj.AddComponent<Text>();
        text.text = content;
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.color = Color.white;
        text.supportRichText = true;
        return text;
    }

    private Button CreateButton(Transform parent, string name, string label, Color color, UnityEngine.Events.UnityAction onClick)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        var rect = obj.AddComponent<RectTransform>();
        var img = obj.AddComponent<Image>();
        img.color = color;
        var btn = obj.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);

        var le = obj.AddComponent<LayoutElement>();
        le.preferredWidth = 120;
        le.preferredHeight = 40;

        var textObj = new GameObject("Text");
        textObj.transform.SetParent(obj.transform, false);
        var textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.sizeDelta = Vector2.zero;
        var text = textObj.AddComponent<Text>();
        text.text = label;
        text.fontSize = 18;
        text.fontStyle = FontStyle.Bold;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.color = Color.white;
        text.alignment = TextAnchor.MiddleCenter;

        return btn;
    }

    #endregion

    #region Button Handlers

    private void OnAlignClicked()
    {
        if (testAudioClip == null)
        {
            _statusText.text = "Status: No AudioClip assigned!";
            return;
        }
        if (string.IsNullOrWhiteSpace(transcript))
        {
            _statusText.text = "Status: No transcript provided!";
            return;
        }

        _statusText.text = "Status: Initializing SOFA...";
        _alignButton.interactable = false;

        // Run alignment (may take a while for long songs)
        try
        {
            SOFAAligner.Initialize();
            _statusText.text = "Status: Running alignment...";

            // Force UI update
            Canvas.ForceUpdateCanvases();

            _alignmentResult = SOFAAligner.Align(testAudioClip, transcript);

            _statusText.text = $"Status: Done! {_alignmentResult.words.Length} words, {_alignmentResult.phonemes.Length} phonemes";
            _confidenceText.text = $"Confidence: {_alignmentResult.totalConfidence:P1}";

            PopulateLyricsPanel();
            PopulateDebugPanel();

            _playButton.interactable = true;
            _exportButton.interactable = true;
        }
        catch (System.Exception e)
        {
            _statusText.text = $"Status: Error - {e.Message}";
            Debug.LogException(e);
        }
        finally
        {
            _alignButton.interactable = true;
        }
    }

    private void OnPlayClicked()
    {
        if (_alignmentResult == null || testAudioClip == null) return;

        _audioSource.clip = testAudioClip;
        _audioSource.volume = volume;
        _audioSource.Play();
        _isPlaying = true;
        _currentWordIndex = -1;
        _currentPhonemeIndex = -1;
        _lastWordChangeTime = -1f;
        _statusText.text = "Status: Playing...";
    }

    private void OnStopClicked()
    {
        if (_audioSource != null && _audioSource.isPlaying)
            _audioSource.Stop();

        _isPlaying = false;
        _statusText.text = "Status: Stopped";
        ResetHighlighting();
    }

    private void OnExportClicked()
    {
        if (_alignmentResult == null || testAudioClip == null)
        {
            _statusText.text = "Status: Run alignment first!";
            return;
        }

        // Export to desktop or project root
        string fileName = testAudioClip.name.Replace(" ", "_") + "_alignment.TextGrid";
        string desktopPath = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop);
        string filePath = System.IO.Path.Combine(desktopPath, fileName);

        float totalDuration = testAudioClip.length;
        SOFAAligner.ExportTextGrid(_alignmentResult, totalDuration, filePath);

        _statusText.text = $"Status: Exported to {filePath}";
    }

    #endregion

    #region Lyrics Display

    private void PopulateLyricsPanel()
    {
        // Clear existing word texts
        foreach (var wt in _wordTexts)
            if (wt != null) Destroy(wt.gameObject);
        _wordTexts.Clear();

        if (_alignmentResult == null) return;

        // Use a flow layout approach - create word texts inline
        foreach (var word in _alignmentResult.words)
        {
            var wordText = CreateText(_lyricsContent, "Word", word.word, 24, FontStyle.Normal);
            wordText.color = UpcomingColor;

            var le = wordText.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 35;

            // Add timing info as tooltip-like suffix
            string timeInfo = $"  [{word.startTime:F2}s - {word.endTime:F2}s]";
            wordText.text = word.word + $"<color=#666666><size=14>{timeInfo}</size></color>";

            _wordTexts.Add(wordText);
        }
    }

    private void PopulateDebugPanel()
    {
        if (_alignmentResult == null) return;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Phoneme | Start  | End    | Conf");
        sb.AppendLine("--------|--------|--------|-----");

        foreach (var ph in _alignmentResult.phonemes)
        {
            sb.AppendLine($"{ph.phoneme,-7} | {ph.startTime,6:F3}s | {ph.endTime,6:F3}s | {ph.confidence:F2}");
        }

        sb.AppendLine();
        sb.AppendLine("--- Words ---");
        foreach (var w in _alignmentResult.words)
        {
            sb.AppendLine($"\n[{w.startTime:F3}s - {w.endTime:F3}s] \"{w.word}\"");
            foreach (var ph in w.phonemes)
                sb.AppendLine($"  {ph.phoneme}: {ph.startTime:F3}s - {ph.endTime:F3}s");
        }

        _debugText.text = sb.ToString();
    }

    #endregion

    #region Real-time Highlighting

    private void UpdateWordHighlighting(float currentTime)
    {
        if (_alignmentResult == null || _alignmentResult.words.Length == 0) return;

        // Anticipation offset: highlight words slightly early (better for karaoke feel)
        const float ANTICIPATION = 0.08f; // 80ms early
        float lookupTime = currentTime + ANTICIPATION;

        // Binary search for current word
        int newIndex = -1;
        int lo = 0, hi = _alignmentResult.words.Length - 1;

        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (lookupTime >= _alignmentResult.words[mid].startTime)
            {
                if (lookupTime < _alignmentResult.words[mid].endTime)
                {
                    newIndex = mid;
                    break;
                }
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        // Fallback: if in a gap between words, show the NEXT upcoming word
        // (for karaoke, seeing the next word early is better than lingering on the past word)
        if (newIndex < 0)
        {
            for (int i = 0; i < _alignmentResult.words.Length; i++)
            {
                if (lookupTime < _alignmentResult.words[i].startTime)
                {
                    // Check if we're close enough to the next word (within 200ms)
                    float gap = _alignmentResult.words[i].startTime - lookupTime;
                    if (gap < 0.2f)
                        newIndex = i;
                    else if (i > 0)
                        newIndex = i - 1; // Too far from next word, stay on previous
                    break;
                }
            }
            // If we're past the last word
            if (newIndex < 0 && _alignmentResult.words.Length > 0)
                newIndex = _alignmentResult.words.Length - 1;
        }

        if (newIndex == _currentWordIndex) return;

        // Enforce minimum highlight duration: don't advance too quickly
        // This prevents rapid-fire word changes when multiple words have short durations
        const float MIN_HIGHLIGHT = 0.1f; // 100ms minimum per word
        if (_currentWordIndex >= 0 && newIndex > _currentWordIndex && _lastWordChangeTime > 0f)
        {
            float elapsed = currentTime - _lastWordChangeTime;
            if (elapsed < MIN_HIGHLIGHT)
                return; // Keep showing current word a bit longer
        }

        _lastWordChangeTime = currentTime;
        _currentWordIndex = newIndex;

        // Update colors
        for (int i = 0; i < _wordTexts.Count && i < _alignmentResult.words.Length; i++)
        {
            if (_wordTexts[i] == null) continue;

            var word = _alignmentResult.words[i];
            string timeInfo = $"  [{word.startTime:F2}s - {word.endTime:F2}s]";

            if (i < _currentWordIndex)
            {
                _wordTexts[i].color = PastColor;
                _wordTexts[i].fontStyle = FontStyle.Normal;
                _wordTexts[i].text = word.word + $"<color=#444444><size=14>{timeInfo}</size></color>";
            }
            else if (i == _currentWordIndex)
            {
                _wordTexts[i].color = CurrentColor;
                _wordTexts[i].fontStyle = FontStyle.Bold;
                _wordTexts[i].fontSize = 28;
                _wordTexts[i].text = word.word + $"<color=#888844><size=14>{timeInfo}</size></color>";
            }
            else
            {
                _wordTexts[i].color = UpcomingColor;
                _wordTexts[i].fontStyle = FontStyle.Normal;
                _wordTexts[i].fontSize = 24;
                _wordTexts[i].text = word.word + $"<color=#666666><size=14>{timeInfo}</size></color>";
            }
        }

        // Auto-scroll to current word
        if (_currentWordIndex >= 0 && _lyricsScrollRect != null && _lyricsContent != null)
        {
            float normalizedPos = 1f - (float)_currentWordIndex / Mathf.Max(1, _alignmentResult.words.Length - 1);
            _lyricsScrollRect.verticalNormalizedPosition = Mathf.Clamp01(normalizedPos + 0.1f);
        }
    }

    private void UpdatePhonemeDetail(float currentTime)
    {
        if (_alignmentResult == null || _alignmentResult.phonemes.Length == 0)
        {
            _phonemeDetailText.text = "Current: --";
            return;
        }

        // Find current phoneme
        int newPhIdx = -1;
        for (int i = 0; i < _alignmentResult.phonemes.Length; i++)
        {
            if (currentTime >= _alignmentResult.phonemes[i].startTime &&
                currentTime < _alignmentResult.phonemes[i].endTime)
            {
                newPhIdx = i;
                break;
            }
        }

        if (newPhIdx >= 0)
        {
            var ph = _alignmentResult.phonemes[newPhIdx];
            string wordCtx = "";
            if (_currentWordIndex >= 0 && _currentWordIndex < _alignmentResult.words.Length)
                wordCtx = $" in \"{_alignmentResult.words[_currentWordIndex].word}\"";

            _phonemeDetailText.text = $"Phoneme: [{ph.phoneme}] ({ph.startTime:F3}s - {ph.endTime:F3}s){wordCtx}  |  Conf: {ph.confidence:F2}";
        }
        else
        {
            _phonemeDetailText.text = "Current: (silence)";
        }
    }

    private void ResetHighlighting()
    {
        _currentWordIndex = -1;
        _currentPhonemeIndex = -1;

        if (_alignmentResult == null) return;

        for (int i = 0; i < _wordTexts.Count && i < _alignmentResult.words.Length; i++)
        {
            if (_wordTexts[i] == null) continue;
            _wordTexts[i].color = UpcomingColor;
            _wordTexts[i].fontStyle = FontStyle.Normal;
            _wordTexts[i].fontSize = 24;

            var word = _alignmentResult.words[i];
            string timeInfo = $"  [{word.startTime:F2}s - {word.endTime:F2}s]";
            _wordTexts[i].text = word.word + $"<color=#666666><size=14>{timeInfo}</size></color>";
        }
    }

    #endregion
}
