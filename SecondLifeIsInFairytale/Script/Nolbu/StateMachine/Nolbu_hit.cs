using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Nolbu_hit : StateMachineBehaviour
{
    NewNolbu nolbu;

    override public void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        nolbu = GameObject.FindGameObjectWithTag("Nolbu").GetComponent<NewNolbu>();
        nolbu.pattern_check_stop();
    }

    override public void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        nolbu.StartCoroutine(nolbu.hitAndGold());
    }

}
