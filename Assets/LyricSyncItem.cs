using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System;

public class LyricSyncItem : MonoBehaviour
{
    [Header("UI References")]
    public TextMeshProUGUI lyricText;
    public TextMeshProUGUI timestampText;
    public Button deleteButton;
    public Image backgroundImage; // This remains but is not used for highlighting

    // --- Private State ---
    private int myIndex;
    private EditorManager editorManager;
    private float timestamp = -1f;
    private bool isInteractive = true;

    /// <summary>
    /// Initializes this lyric item with its text and a reference to the manager.
    /// </summary>
    public void Setup(string text, int index, EditorManager manager, bool interactive = true)
    {
        lyricText.text = text;
        myIndex = index;
        editorManager = manager;
        isInteractive = interactive;

        if (backgroundImage == null) backgroundImage = GetComponent<Image>();

        deleteButton.onClick.AddListener(OnDeleteClicked);

        // Set initial state
        ClearTimestamp();
        SetHighlight(false); // Set to default non-highlighted state

        if (!isInteractive)
        {
            lyricText.text = "";
            timestampText.text = "";
            timestampText.transform.parent.gameObject.SetActive(false);
            deleteButton.gameObject.SetActive(false);
            if (backgroundImage != null)
            {
                var color = backgroundImage.color;
                color.a = 0;
                backgroundImage.color = color;
            }
        }
    }

    /// <summary>
    /// Sets the timestamp for this lyric and updates the UI.
    /// </summary>
    public void SetTimestamp(float time)
    {
        timestamp = time;
        // Format to [mm:ss.xx] to match LRC standard
        TimeSpan timeSpan = TimeSpan.FromSeconds(time);
        timestampText.text = string.Format("{0:00}:{1:00}.{2:00}", timeSpan.Minutes, timeSpan.Seconds, timeSpan.Milliseconds / 10);
    }

    /// <summary>
    /// Resets the timestamp for this lyric and updates the UI.
    /// </summary>
    public void ClearTimestamp()
    {
        timestamp = -1f;
        timestampText.text = "--:--.--"; // A clearer default
    }

    // --- LOGIC CHANGE HERE ---
    // The two methods below have been replaced by the single SetHighlight method.
    // public void SetAsCurrent() { ... }
    // public void ClearAsCurrent() { ... }

    /// <summary>
    /// Sets the visual highlight state of this item's text, preserving the original behavior.
    /// </summary>
    /// <param name="isHighlighted">True for full alpha, false for half alpha.</param>
    public void SetHighlight(bool isHighlighted)
    {
        if (!isInteractive || lyricText == null) return;

        // This logic directly replicates the behavior of the old SetAsCurrent and ClearAsCurrent methods
        // by targeting the lyricText's color property.
        if (isHighlighted)
        {
            // Replicates SetAsCurrent()
            lyricText.color = new Color(1f, 1f, 1f, 1f);
        }
        else
        {
            // Replicates ClearAsCurrent()
            lyricText.color = new Color(1f, 1f, 1f, 0.5f);
        }
    }

    /// <summary>
    /// Sets the interactability of the delete button.
    /// </summary>
    public void SetDeleteButtonInteractable(bool isInteractable)
    {
        deleteButton.interactable = isInteractable;
    }

    private void OnDeleteClicked()
    {
        if (editorManager != null)
        {
            editorManager.ClearTimestampFor(myIndex);
        }
    }
}