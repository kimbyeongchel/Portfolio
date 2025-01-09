using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class moveState : StateMachineBehaviour
{
    override public void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        PlayerState player = animator.gameObject.GetComponent<PlayerState>();
        player.isDodge = false;
        player.isAttack = false;
    }
}
