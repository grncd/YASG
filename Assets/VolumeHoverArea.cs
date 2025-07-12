using UnityEngine;
using UnityEngine.EventSystems;

// This script requires an Image component on the same GameObject to be detected by the Graphic Raycaster.
[RequireComponent(typeof(UnityEngine.UI.Image))]
public class VolumeHoverArea : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    // Define which volume type this UI area represents.
    public enum VolumeType { Music, FX }
    [SerializeField] private VolumeType areaType;

    // A reference to the main controller.
    private VolumeController volumeController;

    private void Start()
    {
        // Find the VolumeController in the scene.
        // This is a simple way to connect them without manual drag-and-drop.
        volumeController = FindObjectOfType<VolumeController>();
        if (volumeController == null)
        {
            Debug.LogError("VolumeHoverArea could not find a VolumeController in the scene!");
        }
    }

    // Called by the EventSystem when the mouse pointer enters the area of this UI element.
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (volumeController != null)
        {
            volumeController.SetCurrentHover(areaType);
        }
    }

    // Called by the EventSystem when the mouse pointer exits the area of this UI element.
    public void OnPointerExit(PointerEventData eventData)
    {
        if (volumeController != null)
        {
            volumeController.ClearCurrentHover();
        }
    }
}