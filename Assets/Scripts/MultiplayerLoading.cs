using UnityEngine;

public class MultiplayerLoading : MonoBehaviour
{
    public GameObject placeholder;
    void Update()
    {
        placeholder.SetActive(transform.childCount == 0);
    }
}
