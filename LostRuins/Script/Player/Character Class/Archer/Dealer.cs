using Cinemachine;
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Animations.Rigging;

public class Dealer : PlayerState
{

    #region Variables
    [Header("camera")]
    public CinemachineVirtualCamera aimCamera; // 에임 활성화 시 카메라
    public LayerMask target; // 타겟
    [SerializeField] protected float aimDistance;
    protected float originFOV = 0f; // 현재 카메라의 FOV 값
    protected Vector3 aimPoint; // 목표 위치

    [Header("Aim")]
    [SerializeField] protected GameObject aimObject; // rigging을 위한 목표 타겟설정
    [SerializeField] protected GameObject aimImage; // 조준선
    [SerializeField] protected bool isAim = false; // 현재 왼쪽 마우스 버튼 누르는 중
    [SerializeField] protected bool aim_set = false; // 오른쪽 마우스 버튼 누르는 중

    [Header("Arrow")]
    public bool isDrawing = false;
    public int skill_index = 3; // 스킬 작동 트리거 (q, e, f, attack )
    protected float arrowSpeed = 30f; // 화살 속도
    [SerializeField] protected Transform shootTransform; // 프리팹 동작 위치
    [SerializeField] protected GameObject arrow; // 기본 공격 게임오브젝트 할당
    [SerializeField] protected GameObject autoArrow; // q 스킬 사용 시 게임 오브젝트 할당
    [SerializeField] protected GameObject f_Arrow; // f 스킬 사용 시 게임 오브젝트 할당
    protected Vector3 arrow_dir; // 화살 방향
    protected Rigidbody arrow_rid; // 화살 리지드바디
    protected int shoot_count = 0;

    [Header("ReloadArrow")]
    public GameObject[] reloadArrows;
    [SerializeField] protected GameObject reloadArrow; // 장전 중 화살

    [Header("SkillArrow")]
    protected GameObject[] autoArrows = new GameObject[4];
    protected GameObject[] e_arrows = new GameObject[3];
    protected autoArrow skillArrow;
    protected arrow baseArrow;

    [Header("updateAimPosition")]
    protected RaycastHit aimhit;
    protected float turnSpeed = 5f;

    [Header("sound")]
    public archerSound sound;

    [Header("IK")]
    public Rig aimIk;
    #endregion Variables

    //기본 기능
    protected override void Start()
    {
        originFOV = 40f;
        aimCamera.m_Lens.FieldOfView = originFOV;
        isAim = false;
        aim_set = false;
        reloadArrow.SetActive(false);
        base.Start();
    }
    protected override void Update()
    {
        calculrateCoolTime();
        GroundCheck();
        skill_tree();
        Die();

        animator.SetBool("isAim", isAim);
        animator.SetBool("aimSet", aim_set);
        animator.SetInteger("skill_index", skill_index);

        if (isDied || isStop || skillTree.activeSelf || hp <= 0)
        {
            animator.SetFloat("moveAmount", 0, 0.25f, Time.deltaTime);
            return;
        }

        UpdateAimPosition();
        AimReady();
        set_speed();
        Move();
        aim_playerRotation();
        attack_playerRotation();
        Dodge();
        Jump();
        CameraRotation();
        sync_state();
        skill_e();
        skill_f();
        skill_q();
        setReloadArrow();
        attack();
    }

    public override void Move() // 달리는 속도와 moveAmount는 별개의 변수
    {
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");

        moveDir = new Vector3(h, 0, v).normalized;
        moveDir = Quaternion.Euler(0, rotationY, 0) * moveDir;

        animator.SetFloat("moveX", h, 0.2f, Time.deltaTime);
        animator.SetFloat("moveY", v, 0.2f, Time.deltaTime);

        moveAmount = Mathf.Abs(moveDir.magnitude) * setSpeed;

        velocity = moveDir * setSpeed;
        velocity = calculateNextFrameAngle() < maxSlopeAngle ? velocity : Vector3.zero;

        if (isDodge)
            velocity = setDir * sprintSpeed;

        if (wallCheck.CheckHitWall(transform.forward))
            velocity = Vector3.zero;

        slopeWithComponent();

        if (moveAmount> 0)
        {
            if (isDodge)
            {
                targetRotation = Quaternion.LookRotation(setDir);
            }
            else
                targetRotation = Quaternion.LookRotation(moveDir);
        }

        animator.SetFloat("moveAmount", moveAmount, 0.2f, Time.deltaTime);
    }
    public override void Jump()
    {
        if (Input.GetButtonDown("Jump") && !isDodge && !isAttack && !isJump && isGrounded && skill_index != 2)
        {
            animator.SetTrigger("jump");
            isJump = true;
        }
    }
    protected override void set_speed() // 점프하거나 isAim이 활성화될때 낮은 속도 유지
    {
        base.set_speed();

        if (isAim)
            setSpeed = 2f;
        else if (skill_index == 2)
            setSpeed = 1f;

        playerRotationSpeed = isAim ? 1000f : 500f;
        rotationSpeed = isAim ? 150f : (skill_index == 2) ? 75f : 250f;
    }
    #region Dodge
    public override void Dodge()
    {
        if (Input.GetKeyDown(KeyCode.C) && isGrounded && velocity != Vector3.zero && !isDodge && (coolTime[4] >= 1f) && skill_index != 2)
        {
            coolTime[4] = 0f;
            setDir = moveDir;
            usualState();
            StartCoroutine(controlDodge());
            dodgeSfx();
        }
    }
    public override IEnumerator controlDodge()
    {
        isDodge = true;
        animator.SetTrigger("roll");
        yield return new WaitUntil(() => animator.GetCurrentAnimatorStateInfo(0).IsName("Roll"));

        AnimatorStateInfo anim = animator.GetCurrentAnimatorStateInfo(0);

        float timer = 0f;
        while (timer < anim.length * 0.6f)
        {
            timer += Time.deltaTime;
            yield return null;
        }

        isDodge = false;
    }
    #endregion
    #region 스킬
    #region 스킬에 따른 화살 선택
    public virtual void setReloadArrow()
    {
        switch (skill_index)
        {
            case 0:
            case 1:
            case 2:
            default:
                for (int i = 0; i < reloadArrows.Length; i++)
                {
                    if (i != skill_index)
                        reloadArrows[i].SetActive(false);
                    else
                        reloadArrows[skill_index].SetActive(true);
                }
                break;
        }
    }
    #endregion
    #region 화살 속도 상승 및 카메라 확대
    protected void chargeArrow(ref float chargetTarget, float maxValue, float increaseValue)
    {
        if (chargetTarget < maxValue)
        {
            chargetTarget += increaseValue * Time.deltaTime;
            aimCamera.m_Lens.FieldOfView -= 10 * Time.deltaTime;
        }
    }
    #endregion
    #region 조준에 따른 상태 변환
    public virtual void usualState()
    {
        isAim = false;
        isDrawing = false;
        arrowSpeed = 30f;
        s_damage[4] = stateObejct.s_damage[4];
        skill_index = 3;
        aimCamera.m_Lens.FieldOfView = originFOV;
        SetRigWeight(0);
        reloadArrow.SetActive(false);
        aimImage.SetActive(false);
        turnSpeed = 5f;
    }
    public virtual void aimState(ref float arrowSpeed, float from, float to)
    {
        arrow_dir = (aimPoint - shootTransform.position).normalized;

        if (!isDrawing)
        {
            if (skill_index != 2)
                isAim = true;
            isDrawing = true;
            aimImage.SetActive(true);
            reloadArrow.SetActive(true);
            animator.SetTrigger("aimMove");
            sound.PlaySkillSfx(archerSound.skillSfx.loading);
        }
        chargeArrow(ref arrowSpeed, from, to);
    }
    #endregion
    #region isAim == true
    public virtual void loadingArrow() // f를 제외한 전시 상태 전환
    {
        if (Input.GetMouseButton(0))
        {
            aimState(ref arrowSpeed, 70f, 50f);
        }
    }
    #endregion
    public virtual void Shoot()// 발사
    {
        if (Input.GetMouseButtonUp(0))
        {
            if (arrowSpeed >= 50f)
            {
                shoot_count++;
                coolTime[0] = 0;
                sound.PlaySkillSfx(archerSound.skillSfx.shoot);
                animator.SetTrigger("shoot");

                switch (skill_index)
                {
                    case 0: // q
                        skill_q_play();
                        break;
                    case 1: // e
                        skill_e_play();
                        break;
                    default: // 기본 공격
                        ShootBasicArrow();
                        break;
                }
                passive();
                usualState();
            }
            else
                usualState();
        }
    }

    public override void passive() // 3번씩 쏠 때마다 100% 치명타 화살 적용
    {
        ctr = stateObejct.ctr;
        if (shoot_count >= 3)
        {
            shoot_count = 0;
            ctr = 1f;
        }
    }
    public override void attack() // 기본 공격 ( 활 쏘기 )
    {
        if (aim_set && !isDodge)
        {
            if (coolTime[0] >= 1f && skill_index != 2)
            {
                loadingArrow();
                Shoot();
            }
        }
        else
        {
            usualState();
        }
    }

    #region 기본 화살
    public virtual void ShootBasicArrow()
    {
        GameObject normalArrow = Instantiate(arrow, shootTransform.position, Quaternion.LookRotation(arrow_dir));
        baseArrow = normalArrow.GetComponent<arrow>();
        if (shoot_count == 3)
            baseArrow.damage = set_damage(0);
        else
            baseArrow.damage = set_damage(1);
        arrow_rid = baseArrow.rid;
        arrow_rid.AddForce(arrow_dir * arrowSpeed, ForceMode.Impulse);
    }
    #endregion
    #region 스킬 Q
    public override void skill_q() // 유도화살 4발 한번에 발사
    {
        if (Input.GetKeyDown(KeyCode.Q) && coolTime[1] > stateObejct.skillCoolTime[1])
        {
            skill_index = 0;
        }
    }
    public virtual void skill_q_play()
    {

        for (int i = 0; i < autoArrows.Length; i++)
        {
            autoArrows[i] = Instantiate(autoArrow, shootTransform.position, Quaternion.LookRotation(arrow_dir));
            skillArrow = autoArrows[i].GetComponent<autoArrow>();
            skillArrow.damage = set_damage(2);
            skillArrow.rid.AddForce(arrow_dir * 10f, ForceMode.Impulse);

            switch (i)
            {
                case 0:
                    skillArrow.rotateVector = Vector3.zero;
                    break;
                case 1:
                    skillArrow.rotateVector = transform.up;
                    break;
                case 2:
                    skillArrow.rotateVector = transform.right;
                    break;
                case 3:
                    skillArrow.rotateVector = transform.right * -1;
                    break;
            }
        }
        coolTime[1] = 0f;
        StartCoroutine(ui_coolTime(0, stateObejct.skillCoolTime[1]));
    }
    #endregion
    #region 스킬 E
    public override void skill_e() // 3방향 화살 발사
    {
        if (Input.GetKeyDown(KeyCode.E) && coolTime[2] > stateObejct.skillCoolTime[2])
        {
            skill_index = 1;
        }
    }
    public virtual void skill_e_play()
    {
        for (int i = 0; i < e_arrows.Length; i++)
        {
            e_arrows[i] = Instantiate(arrow, shootTransform.position, Quaternion.LookRotation(arrow_dir));
            baseArrow = e_arrows[i].GetComponent<arrow>();
            baseArrow.damage = set_damage(3);
            arrow_rid = baseArrow.rid;
            arrow_rid.useGravity = false;
            arrowSpeed = 30f;

            switch (i)
            {
                case 0:
                    arrow_rid.AddForce(((arrow_dir - Camera.main.transform.right) + arrow_dir) * arrowSpeed, ForceMode.Impulse);
                    break;
                case 1:
                    arrow_rid.AddForce((arrow_dir) * arrowSpeed, ForceMode.Impulse);
                    break;
                case 2:
                    arrow_rid.AddForce(((arrow_dir + Camera.main.transform.right) + arrow_dir) * arrowSpeed, ForceMode.Impulse);
                    break;
            }
        }
        coolTime[2] = 0f;
        StartCoroutine(ui_coolTime(1, stateObejct.skillCoolTime[2]));
    }
    #endregion
    #region 스킬 F
    public override void skill_f()
    {
        if (!aim_set || coolTime[3] < stateObejct.skillCoolTime[3] || coolTime[0] < 0.5f || !isGrounded || isAim)
            return;

        if (Input.GetKey(KeyCode.F))
        {
            skill_index = 2;
            aimState(ref s_damage[4], 100f, 50f);
        }
        else if (Input.GetKeyUp(KeyCode.F))
        {
            if (s_damage[4] >= 55f)
            {
                animator.SetTrigger("shoot");

                GameObject chargeArrow = Instantiate(f_Arrow, shootTransform.position, Quaternion.LookRotation(arrow_dir));
                chargeArrow.GetComponent<charge_arrow>().damage = set_damage(4);
                chargeArrow.gameObject.GetComponent<Rigidbody>().AddForce(arrow_dir * 40f, ForceMode.Impulse);

                coolTime[0] = 0f;
                coolTime[3] = 0f;
                usualState();
                passive();
                StartCoroutine(ui_coolTime(2, stateObejct.skillCoolTime[3]));
            }
            else
            {
                usualState();
            }
        }
    }
    #endregion
    #endregion
    // 기타 기능
    protected void AimCameraControl(bool isCheck) // aimCamera 활성화 여부 결정
    {
        aimCamera.gameObject.SetActive(isCheck);
    }
    protected void AimReady() // 우클릭이면서 구르기 중이 아닐 경우, aimCamera ON, aim_set = true 설정
    {
        if (Input.GetMouseButton(1) && !isDodge)
        {
            AimCameraControl(true);
            aim_set = true;
        }
        else
        {
            AimCameraControl(false);
            aim_set = false;
        }
    }
    protected void UpdateAimPosition() // 카메라보다 4f보다 작으면 물체로 인식 x
    {
        var ray = Camera.main.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

        if (Physics.Raycast(ray, out aimhit, aimDistance, target) && (Vector3.Distance(Camera.main.transform.position, aimhit.point) > 4f))
            aimPoint = aimhit.point;
        else
            aimPoint = Camera.main.transform.position + Camera.main.transform.forward * aimDistance;
        aimObject.transform.position = aimPoint;

    }
    public void aim_playerRotation() // 좌클릭 시, 오브젝트의 회전 금지
    {
        if (isAim)
        {
            targetRotation = Quaternion.Euler(0, rotationY, 0);
        }
    }

    public void walkAimSound()
    {
        if (moveAmount <= 2 && aim_set)
        {
            soundControl.PlaySfx(SoundManager.Sfx.walk);
        }
    }

    protected virtual void SetRigWeight(float weight) // rigging weight 설정
    {
        aimIk.weight = weight;
    }
    public void calculatorAngle()//좌우 회전에 따른 각도 계산
    {
        Vector3 objectForward = transform.forward;
        Vector3 targetMe = (aimPoint - transform.position).normalized;

        float angle = Vector3.SignedAngle(new Vector3(objectForward.x, 0, objectForward.z), new Vector3(targetMe.x, 0, targetMe.z), Vector3.up);

        if (Mathf.Abs(angle) < 20) // 일정 각도 내 도달할 시 rig 일부분 활성화 ( 부자연스러움 해결 )
            SetRigWeight(0.8f);

        if (Mathf.Abs(angle) < 5)
            turnSpeed = 500f;

        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.AngleAxis(angle, Vector3.up) * transform.rotation, Time.deltaTime * turnSpeed); // AngleAxis 해당 축을 기준으로 angle 만큼 회전. 양수 음수에 따라의 차이는 SignedAngle과 같음
    }
    public override void attack_playerRotation()
    {
        if (isAim || skill_index == 2)
        {
            calculatorAngle();
        }
        else if (!isAttack)
        {
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, playerRotationSpeed * Time.deltaTime);
        }
    }
    public override void Die()
    {
        if (!isDied && hp <= 0)
        {
            if (isGrounded)
            {
                isDied = true;
                rig.isKinematic = true;
                animator.SetLayerWeight(1, 0);
                animator.SetLayerWeight(2, 0);
                soundControl.Die();
                usualState();
                AimCameraControl(false);
                animator.SetTrigger("die");
            }
        }
    }
}