using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DebuffShape : MonoBehaviour
{
    MonsterState monster;

    void Start()
    {
        monster = transform.parent.GetComponent<MonsterState>();
        StartCoroutine( downDEF(monster));
    }
    IEnumerator downDEF(MonsterState monster)
    {
        monster.DEF -= 0.2f;
        yield return new WaitForSeconds(3f);
        monster.DEF += 0.2f;
    }

}
