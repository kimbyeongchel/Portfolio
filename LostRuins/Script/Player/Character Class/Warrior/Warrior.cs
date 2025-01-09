using Photon.Pun;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Warrior : PlayerState
{
    #region Variables
    public float increaseAtPassive;
    public AttackState attackState;
    public List<AnimationState> animState;
    public int comboCount;
    public bool isCombo;
    public GameObject trail;
    public GameObject sword;
    public Sword swordScript;
    public Sword alphaLengthSword;
    protected BoxCollider swordCollider;
    protected BoxCollider alphaLengthCollider;
    protected WarriorEffect warriorEffect;
    protected WarriorSound warriorSound;
    protected bool transitionToMove;
    public Vector3[] HitRange;
    protected ParticleSystem hitEffect;
    #endregion Variables

    protected override void Start()
    {
        base.Start();
        swordCollider = sword.GetComponent<BoxCollider>();
        alphaLengthCollider = sword.transform.GetChild(1).GetComponent<BoxCollider>();
        swordScript = sword.GetComponent<Sword>();
        alphaLengthSword = alphaLengthCollider.gameObject.GetComponent<Sword>();
        warriorEffect = GetComponent<WarriorEffect>();
        warriorSound = GetComponent<WarriorSound>();
        trail.SetActive(false);
        swordCollider.enabled = false;
        alphaLengthCollider.enabled = false;
    }

    #region jump
    public override void Jump()
    {
        if (Input.GetButtonDown("Jump") && !isDodge && !isAttack && !isJump && isGrounded)
        {
            animator.SetTrigger("jump");
            StartCoroutine(forceJump());
            isJump = true;
        }
    }
    public IEnumerator forceJump()
    {
        yield return null;
        var anim = animator.GetNextAnimatorStateInfo(0);
        float timer = 0f;
        while (timer / anim.length <= 0.1f)
        {
            yield return null;
            timer += Time.deltaTime;
        }

        rig.AddForce(Vector3.up * jumpSpeed, ForceMode.Impulse);
    }
    #endregion
    public override void attack()
    {
        if (isDodge || isJump)
            return;

        if (Input.GetMouseButton(0))
        {
            if (!isAttack)
            {
                StartCoroutine(tryAttack());
            }
            else if ((attackState == AttackState.Impact || attackState == AttackState.Cooldown) && comboCount <= 2)
            {
                isCombo = true;
            }
        }
    }

    /// <summary>
    /// 검의 콜라이더 On/OFF를 조정하는 함수.
    /// Attack, F를 제외하고 검 타격 X
    /// </summary>
    public void turnSwordCollider(int comboCount)
    {
        switch (comboCount)
        {
            case 3:
            case 4:
                swordCollider.enabled = false;
                break;
            case 0:
            case 1:
            case 2:
                swordCollider.enabled = true;
                break;
            case 5:
                swordCollider.enabled = true;
                alphaLengthCollider.enabled = true;
                break;
            default:
                break;
        }
    }

    public void setDamage(int comboCount)
    {
        switch (comboCount)
        {
            case 0:
            case 1:
            case 2:
                swordScript.damage = set_damage(0);
                break;
            case 5:
                swordScript.damage = set_damage(3);
                break;
            default:
                break;
        }
    }

    protected virtual IEnumerator tryAttack()
    {
        isAttack = true;
        attackState = AttackState.WindUp;

        setDamage(comboCount);

        animator.SetTrigger(animState[comboCount].triggerName);
        yield return null;

        var anim = animator.GetNextAnimatorStateInfo(1);

        if (Mathf.Abs(comboCount) < 3)
        {
            warriorSound.skillNum = comboCount;
            warriorSound.PlaySkillSfx(WarriorSound.skillSfx.attack);
        }

        float timer = 0f;
        while (timer <= anim.length)
        {
            timer += Time.deltaTime;
            float normalizedTime = timer / anim.length;

            if (attackState == AttackState.WindUp)
            {
                if (normalizedTime >= animState[comboCount].impactStartTime)
                {
                    attackState = AttackState.Impact;
                    trail.SetActive(true);
                    turnSwordCollider(comboCount);

                    if (comboCount == 5)
                    {
                        ParticleSystem fFx = warriorEffect.fFX();
                        fFx.transform.parent = transform;
                    }
                }
            }
            else if (attackState == AttackState.Impact)
            {
                if (normalizedTime >= animState[comboCount].impactEndTime)
                {
                    attackState = AttackState.Cooldown;
                    swordCollider.enabled = false;
                    alphaLengthCollider.enabled = false;
                    trail.SetActive(false);

                    if (comboCount == 4)
                    {
                        warriorEffect.makePlayerFX(warriorEffect.eFX(), warriorEffect.swordEdgePoint);
                        warriorSound.PlaySkillSfx(WarriorSound.skillSfx.e + 1);
                        VFXDamage(HitRange[0], transform.position, 2);
                    }
                    else if (comboCount == 5)
                    {
                        ParticleSystem fFx = warriorEffect.fFX();
                        fFx.transform.parent = transform;
                        warriorEffect.AfterUseF();
                        StartCoroutine(slashDamage());
                    }
                }
            }
            else if (AttackState.Cooldown == attackState)
            {
                if (isCombo)
                {
                    isCombo = false;
                    comboCount = (comboCount + 1) % (animState.Count - 3);
                    StartCoroutine(tryAttack());
                    yield break;
                }

                if (normalizedTime >= animState[comboCount].breakAnimTime)
                {
                    attackState = AttackState.breakAnim;
                }
            }
            else if (AttackState.breakAnim == attackState)
            {
                if (moveDir != Vector3.zero)
                {
                    animator.SetTrigger("attackToMove");
                    transitionToMove = true;
                    yield return new WaitUntil(() => animator.GetCurrentAnimatorStateInfo(1).IsName("Empty"));
                    transitionToMove = false;
                    break;
                }
                else if (Input.GetButtonDown("Jump") || Input.GetKeyDown(KeyCode.C))
                {
                    break;
                }
            }
            yield return null;
        }

        comboCount = 0;
        attackState = AttackState.Idle;
        isAttack = false;
    }

    #region Passive
    public void upStateAtPassive(int scalar)
    {
        def = stateObejct.def + scalar * increaseAtPassive;
        atk = stateObejct.atk + scalar * increaseAtPassive;
    }
    public override void passive()
    {
        if (hp >= 70)
        {
            upStateAtPassive(0);
        }
        else if (hp < 70 && hp >= 40)
        {
            upStateAtPassive(1);
        }
        else if (hp < 40 && hp >= 10)
        {
            upStateAtPassive(2);
        }
        else
        {
            upStateAtPassive(3);
        }
    }
    #endregion

    /// <summary>
    /// VFX로 데미지. E, F 스킬이 사용함
    /// </summary>
    public virtual void VFXDamage(Vector3 hitRange, Vector3 getPosition, int skillNum)
    {
        Collider[] enemyColliders = Physics.OverlapBox(getPosition, hitRange, Quaternion.identity, LayerMask.GetMask("monster"));

        foreach (Collider collider in enemyColliders)
        {
            collider.TryGetComponent(out PhotonView pv);
            if (pv != null && PhotonNetwork.IsConnected)
            {
                pv.RPC("TakeDamage", RpcTarget.MasterClient, set_damage(skillNum));
            }

            if (skillNum == 2)
            {
                hitEffect = warriorEffect.ELightHitVFX();
            }
            else if (skillNum == 3)
            {
                hitEffect = warriorEffect.fSlashHitVFX();
            }

            hitEffect.transform.position = collider.transform.position + new Vector3(0, Random.Range(1f, 1.5f), 0);
        }
    }

    public override void skill_e()
    {
        if (Input.GetKeyDown(KeyCode.E) && !isAttack && !isDodge && isGrounded && coolTime[1] >= stateObejct.skillCoolTime[1])
        {
            coolTime[1] = 0f;
            comboCount = 4;
            gameObject.GetComponent<Rigidbody>().AddForce(Vector3.up * 10f, ForceMode.Impulse);
            warriorSound.PlaySkillSfx(WarriorSound.skillSfx.e);
            StartCoroutine(tryAttack());
            StartCoroutine(ui_coolTime(1, stateObejct.skillCoolTime[1]));
        }
    }

    public override void skill_f()
    {
        if (Input.GetKeyDown(KeyCode.F) && !isAttack && !isDodge && isGrounded && coolTime[2] >= stateObejct.skillCoolTime[2])
        {
            coolTime[2] = 0f;
            comboCount = 5;
            ParticleSystem[] fDraw = new ParticleSystem[3];

            for (int i = 0; i < 3; i++)
            {
                fDraw[i] = warriorEffect.fChargeVFX();
                fDraw[i].transform.parent = sword.transform;
                fDraw[i].transform.localPosition = Vector3.zero + new Vector3(0, 0.5f * i, 0);
                fDraw[i].transform.localScale = Vector3.one * (1 - 0.2f * i);
            }

            StartCoroutine(tryAttack());
            StartCoroutine(ui_coolTime(2, stateObejct.skillCoolTime[2]));
        }
    }

    public IEnumerator slashDamage()
    {
        float timer = 0f;
        while (timer <= 0.8f)
        {
            timer += Time.deltaTime;
            yield return null;
        }

        VFXDamage(HitRange[1], transform.position + new Vector3(0, 2, 0), 3);
    }

    public override void skill_q()
    {
        if (Input.GetKeyDown(KeyCode.Q) && !isAttack && !isDodge && coolTime[0] >= stateObejct.skillCoolTime[0])
        {
            coolTime[0] = 0f;

            if (!isJump)
            { // comboCount를 밖으로 내보내는 것도 고려
                comboCount = 3;
                warriorSound.PlaySkillSfx(WarriorSound.skillSfx.q);
                StartCoroutine(tryAttack());
            }
            StartCoroutine(ui_coolTime(0, stateObejct.skillCoolTime[0]));

            GameObject[] lightningSwords = warriorEffect.makeLightningSword();

            foreach (var lightningSword in lightningSwords)
            {
                lightningSword.GetComponent<LightningSword>().damage = set_damage(1);
            }
        }
    }

    public override void attack_playerRotation() // 전사 및 근접공격의 경우 해당 ( 공격 시 다른 방향으로 회전 금지 )
    {
        if (!isAttack || attackState == AttackState.WindUp || transitionToMove)
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, playerRotationSpeed * Time.deltaTime);
    }

    protected override void FixedUpdate()
    {
        FreezeRotation();
        if (isDied || isStop || (isAttack && !transitionToMove) || skillTree.activeSelf) // 움직임 제어
            return;
        rig.MovePosition(transform.position + velocity * Time.fixedDeltaTime);
    }
}
