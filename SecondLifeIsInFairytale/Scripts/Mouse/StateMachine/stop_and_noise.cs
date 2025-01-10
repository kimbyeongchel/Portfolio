using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class stop_and_noise : StateMachineBehaviour
{
    Bossmouse mouse;

    override public void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        mouse = GameObject.FindGameObjectWithTag("Enemy").GetComponent<Bossmouse>();
        mouse.StartCoroutine(mouse.warningCircle());
    }
}
