using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class phase2_updown : StateMachineBehaviour
{
    Bossmouse mouse;
    float currentTime = 0f;

    override public void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        mouse = GameObject.FindGameObjectWithTag("Enemy").GetComponent<Bossmouse>();
        currentTime = 0f;
        mouse.pcoll.enabled = false;
        mouse.ccoll.enabled = true;
        mouse.ChangeMaterial(mouse.newPhysicsMaterial);
        mouse.currentPatternCoroutine = mouse.StartCoroutine(mouse.pattern_updown());
    }

    override public void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        currentTime += Time.deltaTime;
        if (currentTime >= 5f)
        {
            mouse.UpDownPlay();
            currentTime = 0f;
            mouse.ani.SetTrigger("bottom_all");
        }
    }

    override public void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        mouse.ChangeMaterial(null);
        mouse.pcoll.enabled = true;
        mouse.ccoll.enabled = false;
    }
}
