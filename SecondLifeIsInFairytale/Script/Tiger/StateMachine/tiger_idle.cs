using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class tiger_idle : StateMachineBehaviour
{
    Tiger tiger;
    public float currentTime = 0f;

    override public void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        tiger = GameObject.FindGameObjectWithTag("Enemy").GetComponent<Tiger>();
        currentTime = 0f;
    }

    override public void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    { 
        currentTime += Time.deltaTime;
        if (currentTime >= 0.5f)
        {
            tiger.IdleState();
            currentTime = 0f;
        }
    }

    override public void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        tiger.DirectionEnemy();
    }
}
