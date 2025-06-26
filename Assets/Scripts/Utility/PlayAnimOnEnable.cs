using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayAnimOnEnable : MonoBehaviour
{
    public string animationName;
    public Animator animator;

    // Start is called before the first frame update
    void Start()
    {
        
    }

    private void OnEnable()
    {
        animator.Play(animationName);
    }
}
