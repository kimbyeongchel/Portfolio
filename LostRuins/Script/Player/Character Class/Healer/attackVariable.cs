using UnityEngine;

public class attackVariable : StateMachineBehaviour
{
    public override void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        animator.GetComponent<PlayerState>().isAttack = false;
    }
}
