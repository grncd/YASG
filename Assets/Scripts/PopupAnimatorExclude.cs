using UnityEngine;

[RequireComponent(typeof(CanvasGroup))]
public class PopupAnimatorExclude : MonoBehaviour
{
    void Awake()
    {
        GetComponent<CanvasGroup>().ignoreParentGroups = true;
    }
}
