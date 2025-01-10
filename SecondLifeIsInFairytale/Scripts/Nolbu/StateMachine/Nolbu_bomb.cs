using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Nolbu_bomb : StateMachineBehaviour
{
    NewNolbu nolbu;

    override public void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        nolbu = GameObject.FindGameObjectWithTag("Nolbu").GetComponent<NewNolbu>();
        nolbu.addBomb();
    }

    override public void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        nolbu.SetCollider(1);
    }
}
