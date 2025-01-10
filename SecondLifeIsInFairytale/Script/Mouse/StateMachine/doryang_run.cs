using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class doryang_run : StateMachineBehaviour
{
    Bossmouse mouse;
    float time = 0f;

    override public void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        mouse = GameObject.FindGameObjectWithTag("Enemy").GetComponent<Bossmouse>();
        time = 0f;
    }

    override public void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        mouse.rb.velocity = new Vector2(mouse.monsterSpeed, mouse.rb.velocity.y);

        time += Time.deltaTime;
        if (time >= 1f)
        {
            mouse.ani.SetBool("run", false);
        }
    }

    override public void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        mouse.rb.velocity = Vector2.zero;
        mouse.move_attack = false;
    }
}
