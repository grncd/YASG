using UnityEngine;
using TMPro;
public class PlaceholderLoading : MonoBehaviour
{
    public GameObject placeholder;
    void Update()
    {
        placeholder.SetActive(string.IsNullOrEmpty(GetComponent<TMP_Text>().text));
    }
}
