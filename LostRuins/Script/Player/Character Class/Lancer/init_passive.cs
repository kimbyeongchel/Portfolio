using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class init_passive : StateMachineBehaviour
{
    override public void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        animator.gameObject.GetComponent<PlayerState>().coolTime[0] = 0f;
    }
}
