using UnityEngine;
using UnityEngine.Events;

public class HostActionGuard : MonoBehaviour
{
    [Header("Actions")]
    [Tooltip("Actions to execute ONLY if the local player is the Host.")]
    public UnityEvent OnHostAction;

    /// <summary>
    /// Call this method from the Button's OnClick event.
    /// </summary>
    public void TryExecute()
    {
        int multiplayerState = PlayerPrefs.GetInt("multiplayer");
        if (multiplayerState == 1)
        {
            if (PlayerData.LocalPlayerInstance != null && PlayerData.LocalPlayerInstance.IsHost.Value)
            {
                OnHostAction?.Invoke();
            }
            else
            {
                AlertManager.Instance.ShowError(
                        LocalizationManager.L("alert.not_host.title", "You are not the host."),
                        LocalizationManager.L("alert.not_host.info", "Only the room host can perform this action. (the player with a crown icon)"),
                        LocalizationManager.L("alert.ok", "OK")
                    );
            }
        }
    }
}
