using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ready_circle : StateMachineBehaviour
{
    Bossmouse mouse;
    float currentTime = 0f;
    float step; // 프레임당 이동 거리
    Vector3 targetPosition;

    override public void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        mouse = GameObject.FindGameObjectWithTag("Enemy").GetComponent<Bossmouse>();
        currentTime = 0f;
        mouse.monsterSpeed = 6f;
        step = mouse.monsterSpeed * Time.deltaTime; // 프레임당 이동 거리

        targetPosition = new Vector3(mouse.target.position.x, mouse.transform.position.y, 0f);
    }

    override public void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        mouse.transform.position = Vector3.MoveTowards(mouse.transform.position, targetPosition, step);

        currentTime += Time.deltaTime;
        if (currentTime >= 2f || (mouse.transform.position == targetPosition))
        {
            mouse.ani.SetTrigger("noisePattern");
            currentTime = 0f;
        }
    }
}
