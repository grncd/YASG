// GameUIManager.cs
using UnityEngine;
using System.Collections.Generic;
using System.Collections;
using System.Linq;

public class GameUIManager : MonoBehaviour
{
    [Tooltip("The parent transform where player UI panels will be instantiated.")]
    public Transform playerUIParent;

    [Tooltip("The prefab that represents the entire UI for one player. Must have a PlayerUIPanel component.")]
    public GameObject playerUIPrefab;

    // A dictionary to track the UI panel for each player.
    private readonly Dictionary<PlayerData, PlayerUIPanel> _playerUIPanels = new Dictionary<PlayerData, PlayerUIPanel>();
    private Coroutine _updateCoroutine;

    void OnEnable()
    {
        if(PlayerPrefs.GetInt("multiplayer") == 0)
        {
            enabled = false;
        }
        _updateCoroutine = StartCoroutine(UpdateUIAndPlacements());
    }

    void OnDisable()
    {
        if (_updateCoroutine != null) StopCoroutine(_updateCoroutine);
    }

    private IEnumerator UpdateUIAndPlacements()
    {
        while (true)
        {
            // --- 1. Get all players ---
            List<PlayerData> allPlayers = new List<PlayerData>(FindObjectsOfType<PlayerData>());

            // --- 2. Add UI for new players ---
            foreach (PlayerData player in allPlayers)
            {
                if (!_playerUIPanels.ContainsKey(player))
                {
                    Debug.Log($"New player detected: {player.PlayerName.Value}. Instantiating UI panel.");
                    GameObject panelGO = Instantiate(playerUIPrefab, playerUIParent);
                    PlayerUIPanel panelController = panelGO.GetComponent<PlayerUIPanel>();
                    panelController.AssignPlayer(player);
                    _playerUIPanels[player] = panelController;
                }
            }

            // --- 3. Remove UI for disconnected players ---
            List<PlayerData> disconnectedPlayers = _playerUIPanels.Keys.Where(p => p == null).ToList();
            foreach (var player in disconnectedPlayers)
            {
                Destroy(_playerUIPanels[player].gameObject);
                _playerUIPanels.Remove(player);
            }

            // --- 4. Calculate and set placements ---
            List<PlayerData> sortedPlayers = _playerUIPanels.Keys
                .OrderByDescending(p => p.CurrentGameScore.Value)
                .ToList();

            for (int i = 0; i < sortedPlayers.Count; i++)
            {
                int placement = i + 1;
                // Find the UI panel for this ranked player and update it.
                if (_playerUIPanels.TryGetValue(sortedPlayers[i], out PlayerUIPanel panel))
                {
                    panel.UpdatePlacement(placement);
                }
            }

            yield return new WaitForSeconds(0.5f);
        }
    }
}