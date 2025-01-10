using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Nolbu_idle : StateMachineBehaviour
{
    NewNolbu nolbu;
    float currentTime = 0f;

    override public void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        nolbu = GameObject.FindGameObjectWithTag("Nolbu").GetComponent<NewNolbu>();
        currentTime = 0f;
    }

    override public void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        currentTime += Time.deltaTime;
        if (currentTime >=1.5f)
        {
            nolbu.IdleState();
            currentTime = 0f;
        }
    }
}
