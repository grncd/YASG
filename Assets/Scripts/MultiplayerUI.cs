using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class MultiplayerUI : MonoBehaviour
{
    // Start is called before the first frame update
    public TextMeshProUGUI roomName;
    void Update()
    {
        roomName.text = RoomManager.Instance.GetRoomName();
    }
}
