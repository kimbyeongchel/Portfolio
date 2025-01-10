using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class doryang_idle : StateMachineBehaviour
{
    Bossmouse mouse;
    float currentTime = 0f;

    override public void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        mouse = GameObject.FindGameObjectWithTag("Enemy").GetComponent<Bossmouse>();
        currentTime = 0f;
    }

    override public void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        currentTime += Time.deltaTime;
        if (currentTime >= 0.5f)
        {
            mouse.IdleState();
            currentTime = 0f;
        }
    }
}
