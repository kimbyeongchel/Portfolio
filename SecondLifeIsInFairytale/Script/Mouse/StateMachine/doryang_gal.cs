using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class doryang_gal : StateMachineBehaviour
{
    Bossmouse mouse;

    override public void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        mouse = GameObject.FindGameObjectWithTag("Enemy").GetComponent<Bossmouse>();
    }

    override public void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        mouse.check_gal = 0;
    }
}
