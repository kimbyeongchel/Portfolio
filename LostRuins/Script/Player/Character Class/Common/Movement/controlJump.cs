using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class controlJump : StateMachineBehaviour
{
    override public void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        animator.gameObject.GetComponent<PlayerState>().isJump = false;
     }
}
