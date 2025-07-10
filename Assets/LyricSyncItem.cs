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
    public Image backgroundImage; // Add this reference for highlighting

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

        // Find the background image if not assigned
        if (backgroundImage == null) backgroundImage = GetComponent<Image>();

        // Add a listener to the delete button programmatically
        deleteButton.onClick.AddListener(OnDeleteClicked);

        // Set initial state
        ClearTimestamp();

        // If not interactive, hide the UI elements
        if (!isInteractive)
        {
            lyricText.text = "";
            timestampText.text = "";
            timestampText.transform.parent.gameObject.SetActive(false);
            deleteButton.gameObject.SetActive(false);
            // Make the background fully transparent but still take up space
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
        timestampText.text = MusicPlayer.Instance.FormatTime(timestamp); // Using the static function from MusicPlayer
    }

    /// <summary>
    /// Resets the timestamp for this lyric and updates the UI.
    /// </summary>
    public void ClearTimestamp()
    {
        timestamp = -1f;
        timestampText.text = "0:00.00";
    }

    /// <summary>
    /// Highlights this item to show it's the next one to be synced.
    /// </summary>
    public void SetAsCurrent()
    {
        if (!isInteractive) return;
        GetComponent<TextMeshProUGUI>().color = new Color(1f, 1f, 1f, 1f); // Highlight color
    }

    /// <summary>
    /// Resets the highlight.
    /// </summary>
    public void ClearAsCurrent()
    {
        if (!isInteractive) return;
        GetComponent<TextMeshProUGUI>().color = new Color(1f, 1f, 1f, 0.5f); // Highlight color
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