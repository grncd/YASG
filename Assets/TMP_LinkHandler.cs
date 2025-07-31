using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;

[RequireComponent(typeof(TextMeshProUGUI))]
public class TMP_LinkHandler : MonoBehaviour, IPointerClickHandler
{
    private TextMeshProUGUI p_textMeshPro;
    private Canvas p_canvas;

    void Awake()
    {
        p_textMeshPro = GetComponent<TextMeshProUGUI>();
        p_canvas = GetComponentInParent<Canvas>();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        // Determine the camera to use based on the canvas render mode
        Camera camera = null;
        if (p_canvas.renderMode == RenderMode.ScreenSpaceOverlay)
        {
            camera = null;
        }
        else
        {
            camera = p_canvas.worldCamera;
        }

        // Check if the click is on a link
        int linkIndex = TMP_TextUtilities.FindIntersectingLink(p_textMeshPro, eventData.position, camera);

        // If a link was clicked
        if (linkIndex != -1)
        {
            // Get the link info
            TMP_LinkInfo linkInfo = p_textMeshPro.textInfo.linkInfo[linkIndex];

            // Get the link ID (which is the URL in your case)
            string linkID = linkInfo.GetLinkID();

            // Open the URL in the default browser
            Debug.Log("Link clicked: " + linkID);
            Application.OpenURL(linkID);
        }
    }
}