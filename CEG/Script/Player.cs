using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Player : Living
{
    public int playerFlag;
    private Monster monster;

    private void Start()
    {
        monster = FindObjectOfType<Monster>();
        OnEnable();
    }

    private void Update()
    {
        if (dead)
        {
            return;
        }

        if (monster.monsterFlag == 1)
        {
            base.Attack();
            monster.monsterFlag = 0;
        }
    }
    public override void OnEnable()
    {
        base.OnEnable();
        health = 5f;
    }

    public override void TakeDamage(float damage)
    {
        playerFlag = 1;
        base.TakeDamage(damage);
    }

    public override void Die()
    {
        base.Die();
    }
}
