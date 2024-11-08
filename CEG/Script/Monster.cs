using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Monster : Living
{
    public int monsterFlag;
    private Player player;

    private void Start()
    {
        player = FindObjectOfType<Player>();
        OnEnable();
    }

    private void Update()
    {
        if(dead)
        {
            return;
        }

        if(player.playerFlag == 1)
        {
            base.Attack();
            player.playerFlag = 0;
        }
    }

    public override void OnEnable()
    {
        base.OnEnable();
        UImanager.instance.monsterHealth.gameObject.SetActive(true);
        UImanager.instance.monsterHealth.maxValue = health;
        UImanager.instance.monsterHealth.value = health;
    }

    public override void TakeDamage(float damage)
    {
        monsterFlag = 1;
        base.TakeDamage(damage);
        UImanager.instance.monsterHealth.value = health;
    }

    public override void Die()
    {
        base.Die();
    }
}
