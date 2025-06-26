using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ControllerCompat : MonoBehaviour
{
    private string[] previous;
    public List<GameObject> myObjects;


    private void Start()
    {
        previous = Input.GetJoystickNames();
        string[] joysticks = Input.GetJoystickNames();
        foreach (string joystick in joysticks)
        {
            if (!string.IsNullOrEmpty(joystick))
            {
                Debug.Log("Controller connected on start: " + joystick);
                EnableAll();
                return;
            }
        }

        DisableAll();
        StartCoroutine(PollJoysticks());
    }

    private IEnumerator PollJoysticks()
    {
        var wait = new WaitForSeconds(1f);  // check every 1 second
        while (true)
        {
            var current = Input.GetJoystickNames();
            for (int i = 0; i < current.Length; i++)
            {
                bool wasConnected = i < previous.Length && !string.IsNullOrEmpty(previous[i]);
                bool isNowConnected = !string.IsNullOrEmpty(current[i]);

                if (!wasConnected && isNowConnected)
                    EnableAll();
                else if (wasConnected && !isNowConnected)
                    DisableAll();
            }
            previous = current;
            yield return wait;
        }
    }
    public void EnableAll()
    {
        foreach (GameObject obj in myObjects)
        {
            if (obj != null)
                obj.SetActive(true);
        }
    }

    public void DisableAll()
    {
        foreach (GameObject obj in myObjects)
        {
            if (obj != null)
                obj.SetActive(false);
        }
    }
}
