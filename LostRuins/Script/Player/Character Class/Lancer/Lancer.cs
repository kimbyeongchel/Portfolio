using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using static TMPro.SpriteAssetUtilities.TexturePacker_JsonArray;

public class Lancer : PlayerState
{
    #region Variables
    [Header("Q")]
    [SerializeField] protected float Q_range;
    [SerializeField] protected LayerMask playerMask;

    [Header("attackSetting")]
    public LancerEffect effectControl;
    public LancerSound sound;
    protected AttackState animState;
    public List<AnimationState> animStates;
    public int skillNum;
    public bool isLeftCombo;
    public bool isRightCombo;
    public bool transitionToMove;

    [Header("랜스 설정")]
    protected GameObject trail;
    protected BoxCollider lanceCollider;
    protected Lance lance;
    protected LanceShield lancerShield;
    protected BoxCollider shieldCollider;
    #endregion Variables

    protected override void Start()
    {
        base.Start();
        lance = GetComponentInChildren<Lance>();
        lancerShield = GetComponentInChildren<LanceShield>();
        trail = lance.transform.GetChild(0).gameObject;
        lanceCollider = lance.GetComponent<BoxCollider>();
        shieldCollider = lancerShield.GetComponent<BoxCollider>();

        trail.SetActive(false);
        lanceCollider.enabled = false;
        shieldCollider.enabled = false;
    }
    protected override void Update()
    {
        calculrateCoolTime();

        GroundCheck();
        skill_tree();
        Die();

        if (isDied || isStop || skillTree.activeSelf || hp <= 0)
        {
            animator.SetFloat("moveAmount", 0, 0.25f, Time.deltaTime);
            return;
        }

        set_speed();
        Move();
        attack_playerRotation();
        Dodge();
        Jump();
        CameraRotation();
        sync_state();
        attack();
        skill_e();
        skill_f();
        skill_q();
    }

    // 스킬
    #region ATTACK
    public override void attack()
    {
        if (Input.GetMouseButtonDown(0))
        {
            if (!isAttack && !isDodge && !isJump)
            {
                skillNum = 0;
                passive();
                StartCoroutine(tryAttack());
            }
            else if ((animState == AttackState.Impact || animState == AttackState.Cooldown) && (skillNum >= 0 && skillNum <= 2))
            {
                isLeftCombo = true;
            }
        }
        else if (Input.GetMouseButtonDown(1))
        {
            if (!isAttack && !isDodge && !isJump)
            {
                skillNum = 2;
                lancerShield.damage = set_damage(3);
                StartCoroutine(tryAttack());
                sound.PlaySkillSfx(LancerSound.skillSfx.rightAttack);
            }
            else if ((animState == AttackState.Impact || animState == AttackState.Cooldown) && (skillNum >= 0 && skillNum <= 2))
            {
                isRightCombo = true;
            }
        }
    }

    // 작업 중.... E, F를 제외한 공격 실행 중 다른 입력(점프 ,구르기, 움직임)이 들어오면 CoolDown 파트 스킵하고 애니메이션 전환
    protected virtual IEnumerator tryAttack()
    {
        isAttack = true;
        animState = AttackState.WindUp;

        animator.SetTrigger(animStates[skillNum].triggerName);
        yield return null;
        var anim = animator.GetNextAnimatorStateInfo(0);
        
        float timer = 0f;
        while (timer < anim.length)
        {
            timer += Time.deltaTime;
            float normalizedTime = timer / anim.length;


            if (animState == AttackState.WindUp)
            {
                if (normalizedTime >= animStates[skillNum].impactStartTime)
                {
                    animState = AttackState.Impact;
                    if(skillNum == 2)
                    {
                        shieldCollider.enabled = true;
                    }
                    else if (skillNum == 3)
                    {
                        takeShield();
                    }
                    else if (skillNum >= 8)
                    {
                        if (skillNum == 11)
                            skill_f_dash();
                        shieldCollider.enabled = true;
                    }
                    else
                    {
                        sound.PlaySkillSfx(LancerSound.skillSfx.attack);
                        lanceCollider.enabled = true;
                        trail.SetActive(true);
                    }
                }
            }
            else if (animState == AttackState.Impact)
            {
                if (normalizedTime >= animStates[skillNum].impactEndTime)
                {
                    animState = AttackState.Cooldown;
                    lanceCollider.enabled = false;
                    shieldCollider.enabled = false;
                    trail.SetActive(false);
                }
            }
            else if (animState == AttackState.Cooldown)
            {
                if (skillNum == 1)
                {
                    coolTime[0] = 0f;
                }

                if (isLeftCombo)
                {
                    isLeftCombo = false;
                    passive();
                    StartCoroutine(tryAttack());
                    yield break;
                }else if(isRightCombo)
                {
                    isRightCombo = false;
                    skillNum = 2;
                    lancerShield.damage = set_damage(3);
                    StartCoroutine(tryAttack());
                    sound.PlaySkillSfx(LancerSound.skillSfx.rightAttack);
                    yield break;
                }

                if (skillNum < 7 && skillNum >= 4 || skillNum >= 8 && skillNum < 11)
                {
                    skillNum++;
                    animState = AttackState.WindUp;
                }
                else if (normalizedTime >= animStates[skillNum].breakAnimTime)
                {
                    animState = AttackState.breakAnim;
                }
            }
            else if (animState == AttackState.breakAnim)
            {
                if (moveDir != Vector3.zero)
                {
                    animator.SetTrigger("attackToMove");
                    transitionToMove = true;
                    yield return new WaitUntil(() => animator.GetCurrentAnimatorStateInfo(0).IsName("Movement"));
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

        animState = AttackState.Idle;
        isAttack = false;
    }
    #endregion
    #region PASSIVE
    public override void passive()
    {
        if (coolTime[0] >= stateObejct.skillCoolTime[0])
        {
            skillNum = 1;
            lance.damage = set_damage(1);
        }
        else
        {
            skillNum = 0;
            lance.damage = set_damage(0);
        }
    }
    #endregion
    #region SKILL Q
    public override void skill_q() // 공격 중, 구르는 중, 점프중 x 아니면 실행가능
    {
        if (Input.GetKeyDown(KeyCode.Q) && !isAttack && !isDodge && !isJump && coolTime[1] >= stateObejct.skillCoolTime[1])
        {
            coolTime[1] = 0f;
            skillNum = 3;
            StartCoroutine(tryAttack());
            sound.PlaySkillSfx(LancerSound.skillSfx.q);
            StartCoroutine(ui_coolTime(0, stateObejct.skillCoolTime[1]));
        }
    }
    public virtual void takeShield()
    {
        Collider[] players = Physics.OverlapSphere(transform.position, Q_range, playerMask);

        for (int i = 0; i < players.Length; i++)
        {
            if (!players[i].GetComponent<PlayerState>().isDied)
            {
                ParticleSystem shield = effectControl.qFX();
                shield.GetComponent<Shield>().targetPlayer = players[i].gameObject;
                shield.GetComponent<Shield>().lancer = gameObject.GetComponent<Lancer>();
            }
        }
    }

    public virtual IEnumerator giveShieldEffect(GameObject players)
    {
        PlayerState player = players.GetComponent<PlayerState>();
        player.shield = 100;
        if (player.hp + player.shield > 100)
        {
            player.Shield.maxValue = player.HP.maxValue = player.hp + player.shield;
        }
        player.Shield.value = player.hp + player.shield;

        yield return new WaitForSeconds(5f);

        player.shield = 0;
        player.Shield.value = 0;
        player.HP.maxValue = 100;
        player.Shield.maxValue = 100;
    }

    public void callShieldCoroutine(GameObject players)
    {
        StartCoroutine(giveShieldEffect(players));
    }

    #endregion
    #region SKILL E
    public override void skill_e()
    {
        if (Input.GetKeyDown(KeyCode.E) && !isAttack && !isDodge && isGrounded && coolTime[2] >= stateObejct.skillCoolTime[2])
        {
            coolTime[2] = 0f;
            skillNum = 4;
            lance.damage = set_damage(2);
            StartCoroutine(tryAttack());
            StartCoroutine(ui_coolTime(1, stateObejct.skillCoolTime[2]));
        }
    }
    #endregion
    #region SKILL F 
    public override void skill_f()
    {
        if (Input.GetKeyDown(KeyCode.F) && !isAttack && !isDodge && isGrounded && coolTime[3] >= stateObejct.skillCoolTime[3])
        {
            skillNum = 8;
            coolTime[3] = 0f;
            lancerShield.damage = set_damage(3);
            effectControl.fFX();
            StartCoroutine(tryAttack());
            sound.PlaySkillSfx(LancerSound.skillSfx.f);
            StartCoroutine(ui_coolTime(2, stateObejct.skillCoolTime[3]));
        }
    }
    public void skill_f_dash()
    {
        effectControl.fDashFX();
    }
    #endregion

    // 기타 관련 기능
    public override void attack_playerRotation()
    {
        if ((!isAttack) || (animState == AttackState.WindUp && (skillNum >= 8 && skillNum < 11)) || transitionToMove)
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
