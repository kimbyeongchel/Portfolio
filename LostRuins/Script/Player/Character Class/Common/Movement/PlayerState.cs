using System.Collections;
using Photon.Pun;
using Photon.Voice.PUN;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public abstract class PlayerState : MonoBehaviour, IUnitStats, IMove, ISkill, IDamageable
{
    public int hp { get; set; } // 체력
    public int mp { get; set; } // 마나
    public float def { get; set; } // 방어력 0~1 소수로 구성
    public float atk { get; set; }// 공격력 1을 기준으로 분할
    public float ctr { get; set; }// 크리티컬 확률 0~1 소수로 구성
    public int shield; // 쉴드
    public State stateObejct;

    [Header("Skill_damage")] // passive, attack, q, e, f 순서
    public float[] s_damage; // 데미지

    [Header("BoolChecking")]
    public bool isAttack; // 공격 상태 체크
    public bool isDodge; // 구르기 상태 체크
    public bool isGrounded; // 땅인지 체크
    public bool isJump; // 점프 상태 체크 ( isGrounded로만 공중상태 체크시 두번되는 오류 해결 )
    public bool isDied; // 사망 상태 체크
    public bool isStop; // 멀티 네트워킹 시 움직임과 회전 제어용  변수

    [Header("GroundCheck")]
    [SerializeField] protected float groundCheckRadius = 0.2f; // 체크 범위
    [SerializeField] protected Vector3 groundCheckOffset; // 체크 범위 중심
    [SerializeField] protected LayerMask groundLayer; // 땅인지 확인하는 Layer

    [Header("UI")]
    public Slider HP;
    public Slider Shield;
    public Slider MP;
    public TextMeshProUGUI hpText;
    public TextMeshProUGUI mpText;
    [SerializeField] protected TextMeshProUGUI[] coolTime_Text;
    [SerializeField] protected Image[] skill_icons;
    public float[] coolTime; // 쿨타임 모음(passive, q, e, f, dodge) 
    [SerializeField] protected GameObject skillTree;

    [Header("Movement")]
    protected float rotationSpeed = 250f; // 마우스 감도
    [SerializeField] protected float playerRotationSpeed = 500f; // 플레이어 회전 속도
    protected Quaternion targetRotation; // 다음으로 바라볼 타겟방향
    protected Vector3 velocity; // 움직임의 방향 및 힘 결정
    protected Vector3 setDir; // 구르기 시에 방향 고정
    protected Vector3 moveDir; // 이동 방향
    public float moveAmount { get; set; }

    [Header("Speed")]
    [SerializeField] protected float jumpSpeed = 5f; // 점프할 때 가해지는 힘
    protected float sprintSpeed = 10f; // shift 달리기 속도 결정
    protected float moveSpeed = 5f; // 기본 속도
    protected float setSpeed; // 최종적으로 결정된 속도

    [Header("Component")]
    protected Rigidbody rig;
    public Animator animator;
    protected checkWall wallCheck; // 벽 체크, 후에 경사면도 체크할 예정
    protected SoundManager soundControl;
    protected CapsuleCollider capsuleCollider;

    [Header("Slope")]
    public GameObject raycastNextFrame;
    public bool isSlope;
    public float maxSlopeAngle;
    public float slopeRayDistance;
    protected RaycastHit hit;
    protected ConstantForce weightGravity;

    [Header("Camera")]
    public float rotationX;
    public float rotationY;
    protected Vector2 mousePointer;
    protected float minVerticalAngle = -45;
    protected float maxVerticalAngle = 45;
    public GameObject cameraTarget;

    #region Photon Var

    public PhotonView photonView;
    public PhotonVoiceView photonVoiceView;
    [SerializeField] public GameObject camPos;
    protected Vector3 receivePos;
    protected Quaternion receiveRot;
    protected Vector3 receiveCameraPos;
    protected Quaternion receiveCameraRootPos;
    public float damping = 10.0f;

    [Header("Player UI")]
    [HideInInspector] public PlayerUIManager playerUIManager;
    [SerializeField] public TextMeshProUGUI playerName;
    [SerializeField] public GameObject playerUI;

    #endregion

    // 움직임
    public virtual void Move() // 달리는 속도와 moveAmount는 별개의 변수
    {
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");

        moveDir = new Vector3(h, 0, v).normalized;
        moveDir = Quaternion.Euler(0, rotationY, 0) * moveDir;

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
    public void walkSound()
    {
        if (moveAmount > 2)
        {
            soundControl.PlaySfx(SoundManager.Sfx.walk);
        }
    }
    public void jumpEndSfx()
    {
        soundControl.PlaySfx(SoundManager.Sfx.jump);
    }
    public void dodgeSfx()
    {
        soundControl.PlaySfx(SoundManager.Sfx.roll);
    }
    public virtual void CameraRotation() // 카메라 회전 제어
    {
        mousePointer = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));

        rotationX += mousePointer.y * -rotationSpeed * Time.deltaTime;
        rotationY += mousePointer.x * rotationSpeed * Time.deltaTime;

        rotationX = Mathf.Clamp(rotationX, minVerticalAngle, maxVerticalAngle);

        cameraTarget.transform.rotation = Quaternion.Euler(rotationX, rotationY, 0.0f);
    }
    public virtual void Dodge()
    {
        if (Input.GetKeyDown(KeyCode.C) && velocity != Vector3.zero && !isDodge && !isAttack && !isJump && (coolTime[4] >= 1f))
        {
            coolTime[4] = 0f;
            StartCoroutine(controlDodge());
            setDir = moveDir;
            dodgeSfx();
        }
    }
    public virtual IEnumerator controlDodge()
    {
        isDodge = true;
        animator.SetTrigger("roll");
        yield return null;

        AnimatorStateInfo anim = animator.GetNextAnimatorStateInfo(0);

        float timer = 0f;
        while (timer < anim.length * 0.8f)
        {
            timer += Time.deltaTime;
            yield return null;
        }

        isDodge = false;
    }

    public virtual void Jump() // 점프 트리거 작동( 공격 중, 구르기 중, 점프 중 금지 )
    {
        if (Input.GetButtonDown("Jump") && !isDodge && !isAttack && !isJump && isGrounded)
        {
            animator.SetTrigger("jump");
            isJump = true;
        }
    }
    public virtual void force_jump() // 점프 -> 애니메이션 함수로 적용
    {
        rig.AddForce(Vector3.up * jumpSpeed, ForceMode.Impulse);
    }
    public void GroundCheck() // 공중 상태 체크 및 공중에서의 속도 설정
    {
        isGrounded = Physics.CheckSphere(transform.TransformPoint(groundCheckOffset), groundCheckRadius, groundLayer);
        animator.SetBool("isGrounded", isGrounded);
    }
    #region Slope
    public Vector3 adjustMoveDirToSlope()
    {
        return Vector3.ProjectOnPlane(moveDir, hit.normal);
    }

    public bool slopeCheck()
    {
        Ray ray = new Ray(transform.position, Vector3.down);
        if (Physics.Raycast(ray, out hit, slopeRayDistance, groundLayer))
        {
            var angle = Vector3.Angle(Vector3.up, hit.normal);
            return angle != 0f && angle < maxSlopeAngle;
        }
        return false;
    }

    public void slopeWithComponent()
    {
        isSlope = slopeCheck();
        if (isSlope && isGrounded)
        {
            rig.useGravity = false;
            rig.velocity = Vector3.zero;
            weightGravity.enabled = false;
            velocity = adjustMoveDirToSlope() * setSpeed;
        }
        else
        {
            rig.useGravity = true;
            weightGravity.enabled = true;
        }
    }

    public float calculateNextFrameAngle()
    {
        if (Physics.Raycast(raycastNextFrame.transform.position, Vector3.down, out RaycastHit hit, 1.2f, groundLayer))
        {
            return Vector3.Angle(Vector3.up, hit.normal);
        }
        return 0f;
    }
    #endregion

    // 스킬
    public abstract void passive();
    public abstract void attack();
    public abstract void skill_q();
    public abstract void skill_e();
    public abstract void skill_f();

    // 데미지 관련
    public virtual int set_damage(int num) // 공격 데미지 산출
    {
        int r_damage = 0;
        r_damage = Mathf.FloorToInt(s_damage[num] * atk);
        r_damage = Random.Range(r_damage - 5, r_damage + 5); // 사이 간격 10



        if (Random.Range(0f, 1f) < ctr) // 크리티컬 확률
        {
            //크리티컬
            r_damage = Mathf.FloorToInt(r_damage * 1.7f);
        }
        return r_damage;
    }
    public virtual void Die()
    {
        if (!isDied && hp <= 0)
        {
            if (isGrounded)
            {
                isDied = true;
                rig.isKinematic = true;
                soundControl.Die();
                animator.SetTrigger("die");
            }
        }
    }
    public virtual void RestoreHealth(int heal)
    {
        if (isDied)
            return;

        if (hp + 30 > 100)
            hp = 100;
        else
            hp += 30;
    }
    /// <summary>
    /// 피해 입기. ex) def = 0.3이면 데미지 30퍼 감소
    /// </summary>
    /// <param name="damage"></param>
    public virtual void TakeDamage(int damage)
    {
        int r_damage = Mathf.FloorToInt(damage * (1-def));

        if (!isDied && !isDodge)
        {
            if (Shield.value > 0)
            {
                if ((Shield.value - r_damage) <= hp)
                {
                    hp = (int)Shield.value - r_damage;
                    Shield.value = 0;
                    HP.maxValue = 100;
                    Shield.maxValue = 100;
                }
                else
                    Shield.value -= r_damage;
            }
            else
            {
                if (hp - r_damage < 0)
                    hp = 0;
                else
                    hp -= r_damage;
            }
        }
    }

    // 기타 기능
    protected virtual void init_setting() // 초기 세팅. 클래스마다 스탯설정 필요
    {
        isAttack = false;
        isDodge = false;
        isDied = false;
        isStop = false;
        isJump = false;

        coolTime = new float[5];
        coolTime[4] = 1f;

        HP.maxValue = 100;
        Shield.maxValue = 100;
        MP.maxValue = 100;

        hp = 100; mp = 100;
        Shield.value = 0;

        atk = stateObejct.atk;
        def = stateObejct.def;
        ctr = stateObejct.ctr;

        for (int i = 0; i < coolTime_Text.Length; i++)
        {
            coolTime_Text[i].enabled = false;
        }

        for (int i = 0; i < stateObejct.skillCoolTime.Length; i++)
        {
            coolTime[i] = stateObejct.skillCoolTime[i];
        }

        for (int i = 0; i < stateObejct.s_damage.Length; i++)
        {
            s_damage[i] = stateObejct.s_damage[i];
        }

        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
    }
    public virtual void attack_playerRotation() // 전사 및 근접공격의 경우 해당 ( 공격 시 다른 방향으로 회전 금지 )
    {
        if (!isAttack)
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, playerRotationSpeed * Time.deltaTime);
    }
    protected void FreezeRotation() // 자동회전 방지
    {
        rig.angularVelocity = Vector3.zero;
    }
    protected virtual void sync_state() // HP, MP 상시 동기화
    {
        HP.value = hp;
        if (Shield.value > 0)
            hpText.text = hp + "(+" + (Shield.value - hp) + ")" + "/100";
        else
            hpText.text = hp + "/100";

        MP.value = mp;
        mpText.text = mp + "/100";
    }
    protected virtual void set_speed() // 속도 설정
    {
        setSpeed = Input.GetKey(KeyCode.LeftShift) ? (isJump ? moveSpeed : sprintSpeed) : moveSpeed; // 달리기
    }
    protected virtual void skill_tree() // 스킬트리
    {
        if (Input.GetKeyDown(KeyCode.K))
        {
            if (skillTree.activeSelf)
            {
                skillTree.SetActive(false);
                Cursor.visible = false;
                Cursor.lockState = CursorLockMode.Locked;
            }
            else
            {
                skillTree.SetActive(true);
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.Confined;
            }
        }

    }
    public virtual IEnumerator ui_coolTime(int index, float coolTime) // 쿨타임 UI에 계산
    {
        float cooldownDuration = coolTime; // 지속시간
        float cooldownTimer = 0f; // 타이머

        coolTime_Text[index].enabled = true; // 텍스트 키기
        skill_icons[index].color = new Color(0.35f, 0.35f, 0.35f); // 스킬 아이콘 색 변경

        while (cooldownTimer < cooldownDuration)
        {
            // 쿨타임 시간 갱신
            cooldownTimer += Time.deltaTime;

            // UI에 쿨타임 표시
            if ((cooldownDuration - cooldownTimer) >= 0)
                coolTime_Text[index].text = (cooldownDuration - cooldownTimer).ToString("F1");
            else
                break;

            yield return null; // 한 프레임 대기
        }
        skill_icons[index].color = Color.white;
        coolTime_Text[index].enabled = false;
    }
    public virtual void calculrateCoolTime()
    {
        for (int i = 0; i < coolTime.Length; i++)
        {
            coolTime[i] += Time.deltaTime;
        }
    }

    protected virtual void OnEnable() // 기본 스탯 설정
    {
        rig = GetComponent<Rigidbody>();
        animator = GetComponent<Animator>();
        wallCheck = GetComponent<checkWall>();
        soundControl = GetComponent<SoundManager>();
        weightGravity = GetComponent<ConstantForce>();
        capsuleCollider = GetComponent<CapsuleCollider>();
    }
    protected virtual void Start()
    {
        init_setting();
    }
    protected virtual void Update()
    {
        calculrateCoolTime();

        GroundCheck();
        Die();
        sync_state();

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
        passive();
        attack();
        skill_e();
        skill_f();
        skill_q();
    }

    protected virtual void FixedUpdate()
    {
        FreezeRotation();
        if (isDied || isStop || isAttack || skillTree.activeSelf) // 움직임 제어
            return;
        rig.MovePosition(transform.position + velocity * Time.fixedDeltaTime);
    }

    private void OnCollisionStay(Collision collision)
    {
        if (collision.gameObject.CompareTag("monster"))
        {
            Vector3 contactNormal = collision.contacts[0].normal;
            float angle = Vector3.Angle(contactNormal, Vector3.up);

            if ((transform.position.y > collision.transform.position.y) && angle < 90 && animator.GetCurrentAnimatorStateInfo(0).IsName("InAir") && rig.velocity.y < 0 )
            {
                Vector3 position = transform.position;
                position.y = Mathf.Lerp(position.y, collision.transform.position.y - 0.5f, Time.deltaTime * 3f); // 서서히 내려오게 설정
                transform.position = position;
            }
        }
    }

    #region RPC Method

    [PunRPC]
    protected void RPCTrigger(string name)
    {
        animator.SetTrigger(name);
    }

    [PunRPC]
    protected void IsDie(bool _isDie)
    {
        isDied = _isDie;
        if (isDied)
        {
            GameManager.Instance.playerCnt--;
        }
    }

    [PunRPC]
    protected void RemoveCamera(int viewID)
    {
        PhotonView pv = PhotonView.Find(viewID);
        PlayerState player = pv.GetComponent<PlayerState>();

        GameManager.Instance.playerCameraList.Remove(player.camPos);
    }

    #endregion
}