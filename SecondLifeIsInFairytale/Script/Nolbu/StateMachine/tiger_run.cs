using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class tiger_run : StateMachineBehaviour
{
    Tiger tiger;

    override public void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        tiger = GameObject.FindGameObjectWithTag("Enemy").GetComponent<Tiger>();
    }

    override public void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        tiger.check_run();
    }

}
