using System.Collections;
using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

public class DebuffCircle : MonoBehaviour
{
    public ParticleSystem debuffShape; 
    public float damageCoolTime;
    bool debuff = false;

    void Start()
    {
        debuff = false;
        damageCoolTime = 0.5f;
    }
    private void Update()
    {
        setDebuffMonster();
    }
    void setDebuffMonster()
    {
        damageCoolTime += Time.deltaTime;
        if (damageCoolTime >= 0.5f)
        {
            Collider[] monster = Physics.OverlapSphere(transform.position, 1f, LayerMask.GetMask("monster"));

            if (monster != null)
            {
                for (int i = 0; i < monster.Length; i++)
                {
                    if (!debuff)
                    {
                        Instantiate(debuffShape, monster[i].transform);
                    }
                    monster[i].GetComponent<MonsterState>().TakeDamage(15);
                }
            }
            debuff = true;
            damageCoolTime = 0f;
        }
    }
}
