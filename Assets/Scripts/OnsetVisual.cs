using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class OnsetVisual : MonoBehaviour
{
    private float lifeTime = 0f;
    private void Update()
    {
        lifeTime += Time.deltaTime;
        if(lifeTime > 1f)
        {
            Destroy(gameObject);
        }
    }
}
