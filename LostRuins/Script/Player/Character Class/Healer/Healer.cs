using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Healer : PlayerState
{
    [Header("기타사항")]
    public Transform[] magicPosition;
    protected RaycastHit[] hitinfo;
    public float q_range;
    public List<AnimationState> animStates;
    public int skillNum;

    [Header("컴포넌트")]
    protected PlayerState basePlayer;
    public healerEffect effectControl;
    public healerSound sound;

    protected override void FixedUpdate()
    {
        FreezeRotation();
        if (isDied || isStop || skillTree.activeSelf || (isAttack  && (skillNum == 1 || skillNum == 3)))// 움직임 제어
            return;
        rig.MovePosition(transform.position + velocity * Time.fixedDeltaTime);
    }

    public override void attack_playerRotation() // 전사 및 근접공격의 경우 해당 ( 공격 시 다른 방향으로 회전 금지 )
    {
        if (isAttack  && (skillNum == 1 || skillNum == 3))
            return;
        
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, playerRotationSpeed * Time.deltaTime);
    }
    public override void TakeDamage(int damage)
    {
        base.TakeDamage(damage);
        effectControl.particle[0].gameObject.SetActive(false);
        coolTime[0] = 0f;
        def = stateObejct.def;
    }
    public override IEnumerator controlDodge()
    {
        isDodge = true;
        animator.SetTrigger("roll");
        yield return null;

        AnimatorStateInfo anim = animator.GetNextAnimatorStateInfo(0);

        float timer = 0f;
        while (timer < anim.length)
        {
            timer += Time.deltaTime;
            yield return null;
        }

        isDodge = false;
    }

    //스킬
    #region MAKE_MAGIC
    public IEnumerator makeMagic()
    {
        isAttack = true;
        animator.SetTrigger(animStates[skillNum].name);

        yield return null;

        AnimatorStateInfo anim;

        if (skillNum == 0 || skillNum == 2)
        {
            anim = animator.GetNextAnimatorStateInfo(1);
        }
        else
        {
            anim = animator.GetNextAnimatorStateInfo(0);
        }
        bool shoot = false;
        float timer = 0f;

        while (timer <= anim.length)
        {
            timer += Time.deltaTime;
            float normalizedTime = timer / anim.length;

            if (normalizedTime >= animStates[skillNum].impactStartTime && !shoot)
            {
                switch (skillNum)
                {
                    case 0:
                        makeMagicFire();
                        break;
                    case 2:
                        makeMagicBall(10f);
                        break;
                    case 1:
                        healPlayer();
                        break;
                    case 3:
                        giveBuffToPlayer();
                        break;
                }
                shoot = true;
            }
            yield return null;
        }

        if(skillNum == 0 || skillNum == 2)
            isAttack = false;
    }

    public virtual void makeMagicBall(float typeOfSpeed) // e 공격
    {
        ParticleSystem magicBall = effectControl.eFX();
        Rigidbody magicBallRigidbody = magicBall.GetComponent<Rigidbody>();
        magicBallRigidbody.AddForce(Camera.main.transform.forward * typeOfSpeed, ForceMode.Impulse);
    }
    public virtual void makeMagicFire() // 기본 공격
    {
        ParticleSystem fireball = effectControl.attackFX();
        fireball.transform.rotation = Quaternion.LookRotation(Camera.main.transform.forward);
        fireball.GetComponent<magicDamage>().damage = set_damage(0);
        sound.PlaySkillSfx(healerSound.skillSfx.attack);
    }
    #endregion
    #region PASSIVE
    public override void passive() // 방어막을 쉴드보다는 def 를 다막고 데미지 입으면 풀리는 한번 데미지 막아주는 형태로 진행
    {
        if (coolTime[0] >= stateObejct.skillCoolTime[0])
        {
            def = 1f;
            effectControl.particle[0].gameObject.SetActive(true);
        }
    }
    #endregion
    #region ATTACK
    public override void attack()
    {
        if (Input.GetMouseButtonDown(0) && !isAttack && !isDodge)
        {
            skillNum = 0;
            StartCoroutine(makeMagic());
        }
    }
    #endregion
    #region skill Q
    public override void skill_q()
    {
        if (coolTime[1] >= stateObejct.skillCoolTime[1] && !isAttack && !isDodge && !isJump && Input.GetKeyDown(KeyCode.Q))
        {
            skillNum = 1;
            q_range = 7f;
            StartCoroutine(makeMagic());
        }
    }
    public virtual void healPlayer()//범위 내 팀 힐
    {
        coolTime[1] = 0f;
        hitinfo = Physics.SphereCastAll(transform.position + capsuleCollider.center, q_range, transform.forward, 0f, LayerMask.GetMask("Player"));
        if (hitinfo != null)
        {
            foreach (RaycastHit player in hitinfo)
            {
                if (!player.collider.GetComponent<PlayerState>().isDied)
                {
                    basePlayer = player.transform.gameObject.GetComponent<PlayerState>();
                    basePlayer.RestoreHealth(30);
                    ParticleSystem qParticle = effectControl.qFX();
                    qParticle.transform.SetParent(player.transform);
                    qParticle.transform.position = player.transform.position;
                    qParticle.Play();
                }
            }
        }
        StartCoroutine(ui_coolTime(0, stateObejct.skillCoolTime[1]));
    }
    #endregion
    #region skill E
    public override void skill_e()
    {
        if (coolTime[2] >= stateObejct.skillCoolTime[2] && !isAttack && !isDodge && Input.GetKeyDown(KeyCode.E))
        {
            skillNum = 2;
            StartCoroutine(makeMagic());
            coolTime[2] = 0f;
            StartCoroutine(ui_coolTime(1, stateObejct.skillCoolTime[2]));
        }
    }
    #endregion
    #region skill F
    public override void skill_f()
    {
        if (coolTime[3] >= stateObejct.skillCoolTime[3] && !isAttack && !isDodge && !isJump && Input.GetKeyDown(KeyCode.F))
        {
            skillNum = 3;
            q_range = 5f;
            coolTime[3] = 0f;
            StartCoroutine(makeMagic());
        }
    }
    public virtual void giveBuffToPlayer() // 궁버프 지속 시간은 8초
    {
        hitinfo = Physics.SphereCastAll(transform.position + capsuleCollider.center, q_range, transform.forward, 0f, LayerMask.GetMask("Player"));
        if (hitinfo != null)
        {
            foreach (RaycastHit player in hitinfo)
            {
                if (!player.collider.GetComponent<PlayerState>().isDied)
                {
                    // 10, 20, 10퍼씩 증가
                    basePlayer = player.transform.gameObject.GetComponent<PlayerState>();
                    basePlayer.atk += 0.1f;
                    basePlayer.ctr += 0.2f;
                    basePlayer.def += 0.1f;

                    ParticleSystem fParticle = effectControl.fFX();
                    ParticleSystem fBuff = effectControl.fBuffFX();
                    fParticle.Play();
                    fBuff.transform.SetParent(player.transform);
                    fBuff.transform.position = player.transform.position;
                    fBuff.Play();
                    fParticle.transform.position = transform.position;
                    StartCoroutine(downState(basePlayer));
                }
            }
        }
        StartCoroutine(ui_coolTime(2, stateObejct.skillCoolTime[3]));
    }
    protected IEnumerator downState(PlayerState player)//능력치 감소
    {
        yield return new WaitForSeconds(8f);
        player.atk -= 0.1f;
        player.ctr -= 0.2f;
        player.def -= 0.1f;
    }
    #endregion
}
