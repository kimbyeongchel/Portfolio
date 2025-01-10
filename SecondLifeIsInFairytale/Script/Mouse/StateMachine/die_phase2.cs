using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class die_phase2 : StateMachineBehaviour
{
    Bossmouse mouse;
    float step; // 프레임당 이동 거리

    override public void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        mouse = GameObject.FindGameObjectWithTag("Enemy").GetComponent<Bossmouse>();
        mouse.render.color = Color.white;
        mouse.pcoll.enabled = false;
        mouse.ccoll.enabled = false;
        mouse.bColl.enabled = false;
        mouse.transform.position += new Vector3(0f, -1.53f, 0f);
        mouse.rb.constraints = RigidbodyConstraints2D.FreezePositionY;
        mouse.render.flipX = false;
        mouse.monsterSpeed = 1f;
        step = mouse.monsterSpeed * Time.deltaTime; // 프레임당 이동 거리
    }

    override public void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        mouse.transform.position = Vector3.MoveTowards(mouse.transform.position, new Vector3(-12.15f, mouse.transform.position.y, 0f), step);

        if (mouse.transform.position.x == new Vector3(-12.15f, mouse.transform.position.y, 0f).x)
        { 
            mouse.monsterDestroy();
        }
    }
}
