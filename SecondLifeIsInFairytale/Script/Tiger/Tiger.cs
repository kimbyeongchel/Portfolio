using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;

public class Tiger : Enemy
{
    private bool isAttacking = false; // ���� �� ����
    private bool isIdleAfterAttack = false; // ���� �� idle

    public Transform[] pos;
    public Vector2[] boxsizes;
    public float idleTime = 1f; // ���� �� idle �ð�

    private int posIndex = 0; // ���� ������ ó�� �Ǻ� ����
    public double value;

    public Boss boss;
    public AudioSource axeSound;
    public AudioSource toothsound;
    public AudioSource nailsound;
    void Start()
    {
        OnEnable();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        HP = 300f;
        Health.value = HP;
        monsterSpeed = 10f;
    }

    void Update()
    {
        if (dead)
            return;

        yDis = target.position.y - transform.position.y;

        if (Mathf.Abs(yDis) > 2f) // y��ǥ�� ���̷� ���� ���� ����
        {
            ani.SetBool("isFollow", false); // �޸��� �ִ� �� ����
            return;
        }
    }

    public void check_run()
    {
        float distance = Vector2.Distance(transform.position, target.position);

        if (distance > 5f)
        {
            DirectionEnemy();
            ani.SetBool("isFollow", true);
            Vector3 targetPosition = new Vector3(target.position.x, transform.position.y, transform.position.z);
            transform.position = Vector3.MoveTowards(transform.position, targetPosition, monsterSpeed * Time.deltaTime);
        }
        else
        {
            ani.SetBool("isFollow", false);
        }
    }

    public void IdleState()
    {
        float distance = Vector2.Distance(transform.position, target.position);

        if (distance > 5f)
        {
            ani.SetBool("isFollow", true);
        }
        else if (distance <= 5f && distance > 4f)
        {
            axeAttackRoutine();
        }
        else
        {
            AttackRoutine();
        }
    }

    void AttackRoutine()
    {
        value = rand.NextDouble();

        if (value > 0.5)
        { 
            ani.SetTrigger("attack1");
            posIndex = 0;
        }
        else
        {
            ani.SetTrigger("bite");
            posIndex = 1;
        }
    }

    void axeAttackRoutine()
    {
        ani.SetTrigger("attack2");
        posIndex = 2;
    }

    protected override void OnDrawGizmos()
    {
        for (int i = 0; i < pos.Length; i++)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireCube(pos[i].transform.position, boxsizes[i]);
        }
    }

    public override void FindAnd()
    {
        Collider2D[] collider2Ds = Physics2D.OverlapBoxAll(pos[posIndex].transform.position, boxsizes[posIndex], 0);
        foreach (Collider2D collider in collider2Ds)
        {
            if (collider.tag == "Player")
            {
                PlayerHit player = GameObject.FindGameObjectWithTag("Player").GetComponent<PlayerHit>();
                if (posIndex == 0)
                {
                    player.Hit(10, this.gameObject);
                }
                else if (posIndex == 1)
                {
                    player.Hit(20, this.gameObject);
                }
                else if (posIndex == 2)
                {
                    player.Hit(30, this.gameObject);
                }
            }
        }
    }
    public override void monsterDestroy()
    {
        boss.IsDie();

        Destroy(gameObject);
    }

    public void AxePlay()
    {
        axeSound.Play();
    }
    public void NailPlay()
    {
        nailsound.Play();
    }
    public void ToothPlay()
    {
        toothsound.Play();
    }
}