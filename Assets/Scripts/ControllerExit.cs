using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;


public class ControllerExit : MonoBehaviour
{

    public UnityEvent onEndEdit;
    public SearchHandler searchHandler;
    public TMP_InputField inputField;
    public Animator keyboardOut;
    public GameObject keyboardGO;
    // Update is called once per frame
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.JoystickButton1))
        {
            searchHandler.Search(inputField.text);
            onEndEdit?.Invoke();
            StartCoroutine(AnimEnd());
        }
    }
    private IEnumerator AnimEnd()
    {
        keyboardOut.Play("KeyboardOut");
        yield return new WaitForSeconds(0.4f);
        keyboardGO.SetActive(false);
    }
}
