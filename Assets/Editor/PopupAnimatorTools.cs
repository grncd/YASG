using UnityEditor;
using UnityEngine;

public static class PopupAnimatorTools
{
    [MenuItem("Tools/Popup Animators/Disable All Dim Overlays")]
    static void DisableAllDimOverlays()
    {
        PopupAnimator[] popups = Object.FindObjectsByType<PopupAnimator>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int count = 0;
        foreach (PopupAnimator popup in popups)
        {
            if (popup.enableDimOverlay)
            {
                Undo.RecordObject(popup, "Disable Dim Overlay");
                popup.enableDimOverlay = false;
                EditorUtility.SetDirty(popup);
                count++;
            }
        }
        Debug.Log($"[PopupAnimatorTools] Disabled dim overlay on {count} PopupAnimator(s) ({popups.Length} total in scene).");
    }

    [MenuItem("Tools/Popup Animators/Enable All Dim Overlays")]
    static void EnableAllDimOverlays()
    {
        PopupAnimator[] popups = Object.FindObjectsByType<PopupAnimator>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int count = 0;
        foreach (PopupAnimator popup in popups)
        {
            if (!popup.enableDimOverlay)
            {
                Undo.RecordObject(popup, "Enable Dim Overlay");
                popup.enableDimOverlay = true;
                EditorUtility.SetDirty(popup);
                count++;
            }
        }
        Debug.Log($"[PopupAnimatorTools] Enabled dim overlay on {count} PopupAnimator(s) ({popups.Length} total in scene).");
    }
}
