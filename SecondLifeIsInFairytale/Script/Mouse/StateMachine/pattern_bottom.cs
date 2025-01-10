using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class pattern_bottom : StateMachineBehaviour
{
    Bossmouse mouse;
    override public void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        mouse = GameObject.FindGameObjectWithTag("Enemy").GetComponent<Bossmouse>();
        mouse.StartCoroutine(mouse.bottomAll());
        mouse.pcoll.enabled = false;
        mouse.ccoll.enabled = true;
    }

    override public void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        mouse.ccoll.enabled = false;
        mouse.pcoll.enabled = true;
    }
}
