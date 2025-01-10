using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class attackThrow : StateMachineBehaviour
{
    NewNolbu nolbu;

    override public void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        nolbu = GameObject.FindGameObjectWithTag("Nolbu").GetComponent<NewNolbu>();
        nolbu.currentPatternCoroutine = nolbu.StartCoroutine(nolbu.ShootCoin());
    }
    override public void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        nolbu.currentPatternCoroutine = null;
    }
}
