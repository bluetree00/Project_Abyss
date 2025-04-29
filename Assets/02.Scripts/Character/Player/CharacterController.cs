using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using Game.CharacterStates;
using Game.CharacterStates.CharacterControllerStates;

public class CharacterController : MonoBehaviour
{
    //============================================================
    // 🔶 필드 및 프로퍼티
    //============================================================

    [Header("Character & Weapon")]
    [SerializeField] protected CharacterData characterData;
    public CharacterData CharacterData => characterData;

    protected Animator anim;
    public Animator Anim => anim;

    protected Vector3 moveDirection;
    public Vector3 MoveDirection => moveDirection;

    [SerializeField] protected Define.State _state = Define.State.Idle;
    [SerializeField] protected Vector3 _destPos;
    [SerializeField] protected GameObject _lockTarget;

    [SerializeField] private Rigidbody rb;
    public Rigidbody Rigid => rb;

    public Transform playerTransform;

    protected PlayerInputActions inputActions;
    protected bool inputReady = false;

    public IMoveAbility<CharacterController> MoveAbility { get; protected set; }
    public IDodgeAbility<CharacterController> DodgeAbility { get; protected set; }
    public ILightAttackAbility<CharacterController> LightAttackAbility { get; protected set; }
    public IHeavyAttackAbility<CharacterController> HeavyAttackAbility { get; protected set; }

    protected StateMachine<CharacterController> stateMachine = new StateMachine<CharacterController>();
    public StateMachine<CharacterController> StateMachine => stateMachine;

    public Transform handTransform;  
    public WeaponManagerSO weaponManagerSO;

    //============================================================
    // 🔷 점프 및 공중 상태 관리
    //============================================================

    private bool isGrounded;
    private bool isJumping;
    private float airStartTime = 0f;

    public enum AirState { None, JumpStart, InAir, Landing }
    public AirState CurrentAirState { get; private set; } = AirState.None;

    public bool IsInAir => CurrentAirState == AirState.InAir;
    public bool IsHardLanding => (Time.time - airStartTime) >= characterData.hardLandingTimeThreshold;

    public bool IsGrounded() => isGrounded;
    public bool IsJumping() => isJumping;
    public void FinishJump() => isJumping = false;

     // 동적으로 추출된 공격 스테이트 키
    private string[] attackStateKeys;

    //============================================================
    // 🔹 초기화
    //============================================================

    private async void Awake()
    {
        await InitAsync();
    }

    /// <summary>
    /// 캐릭터의 모든 핵심 시스템을 비동기 초기화.
    /// </summary>
    protected virtual async Task InitAsync()
    {
        InitCoreComponents();
        await InitCharacterDataAsync();
        InitInputActions();
        InitAbilities();
        InitWeaponManager();
    }

    /// <summary>
    /// 리지드바디, 애니메이터 등 핵심 컴포넌트를 초기화.
    /// </summary>
    private void InitCoreComponents()
    {
        rb = GetComponent<Rigidbody>();
        anim = GetComponent<Animator>();
        playerTransform = transform;
    }

    /// <summary>
    /// 캐릭터 데이터(속성 데이터)를 Addressables로 로드.
    /// </summary>
    private async Task InitCharacterDataAsync()
    {
        string characterName = gameObject.name.Replace("(Clone)", "");
        string characterClass = Define.GetCharacterClassString(characterName);

        await LoadCharacterDataAsync(characterName);

        rb.useGravity = false;
        rb.drag = characterData.groundDrag;
    }

    /// <summary>
    /// 캐릭터의 입력 시스템(InputActions)을 초기화하고 활성화.
    /// </summary>
    private void InitInputActions()
    {
        inputActions = new PlayerInputActions();
        inputActions.Enable();
        inputReady = true;
    }

    /// <summary>
    /// 캐릭터의 이동, 회피, 공격 등 기본 어빌리티를 초기화.
    /// </summary>
    private void InitAbilities()
    {
        MoveAbility = new DefaultMoveAbility();
        DodgeAbility = new DefaultDodgeAbility();
        LightAttackAbility = new DefaultLightAttackAbility();
        HeavyAttackAbility = new DefaultHeavyAttackAbility();
    }

    /// <summary>
    /// 무기 매니저를 생성하고 슬롯을 초기화.
    /// </summary>
    private void InitWeaponManager()
    {
        weaponManagerSO = ScriptableObject.CreateInstance<WeaponManagerSO>();
        weaponManagerSO.Initialize(2, anim); // 슬롯 수를 2로 초기화
        weaponManagerSO.weaponHandTransform = handTransform;
    }

    /// <summary>
    /// 캐릭터 데이터(Stat 등)를 비동기로 Addressables에서 로드.
    /// </summary>
    protected async Task LoadCharacterDataAsync(string characterName)
    {
        var tcs = new TaskCompletionSource<bool>();

        AddressablesManager.Instance.LoadAsset<CharacterData>(characterName, data =>
        {
            if (data == null)
            {
                Debug.LogError("캐릭터 데이터가 null입니다.");
                tcs.SetResult(false);
                return;
            }

            characterData = data;
            Managers.CharacterData.SetCharacterData(characterData);
            characterData.canDodge = true;

            tcs.SetResult(true);
        });

        await tcs.Task;
    }

    //============================================================
    // 🟢 이동 처리
    //============================================================

    /// <summary>
    /// 공중 상태가 아니라면 수평 속도를 정지시킴킴.
    /// </summary>
    public void StopHorizontalMovement()
    {
        if (IsInAir) return;
        rb.velocity = new Vector3(0f, rb.velocity.y, 0f);
    }

    //============================================================
    // 🔺 점프 처리
    //============================================================

    /// <summary>
    /// 캐릭터가 점프하도록 함함.
    /// </summary>
    public void Jump()
    {
        if (!isGrounded) return;

        isJumping = true;
        rb.velocity = new Vector3(rb.velocity.x, 0, rb.velocity.z);
        rb.AddForce(Vector3.up * characterData.jumpForce, ForceMode.Impulse);
    }

    /// <summary>
    /// 현재 공중 상태를 설정.
    /// </summary>
    public void SetAirState(AirState state)
    {
        CurrentAirState = state;
        if (state == AirState.InAir)
            airStartTime = Time.time;
    }

    //============================================================
    // 🔄 유니티 생명주기
    //============================================================

    protected virtual void Update()
    {
        if (!inputReady || characterData == null) return;

        MoveAbility?.Move(this, moveDirection);
    }

    private void FixedUpdate()
    {
        if (characterData == null) return;

        UpdateGroundedCheck();
        UpdateAirStateAuto();
        ApplyMassBasedGravity();

        rb.drag = IsInAir ? characterData.airDrag : characterData.groundDrag;
    }

    //============================================================
    // ⚙️ 물리 및 상태 관련
    //============================================================

    /// <summary>
    /// 레이캐스트로 현재 땅에 닿아 있는지 검사.
    /// </summary>
    private void UpdateGroundedCheck()
    {
        Vector3 rayOrigin = transform.position + Vector3.up * 0.1f;
        float rayLength = characterData.groundCheckDistance + 0.1f;

        if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, rayLength, characterData.groundLayer))
        {
            isGrounded = hit.distance <= characterData.groundCheckDistance + 0.05f;
        }
        else
        {
            isGrounded = false;
        }

        Debug.DrawRay(rayOrigin, Vector3.down * rayLength, isGrounded ? Color.green : Color.red);
    }

    /// <summary>
    /// 공중 상태를 자동으로 업데이트.
    /// </summary>
    private void UpdateAirStateAuto()
    {
        if (!isGrounded && CurrentAirState == AirState.None)
        {
            SetAirState(AirState.InAir);
        }
        else if (isGrounded && CurrentAirState != AirState.None)
        {
            SetAirState(AirState.None);
            FinishJump();
        }
    }

    /// <summary>
    /// 캐릭터 중력 처리를 직접 적용.
    /// </summary>
    private void ApplyMassBasedGravity()
    {
        if (!isGrounded || isJumping)
        {
            float finalGravity = characterData.gravity;
            if (rb.velocity.y < 0)
                finalGravity *= characterData.fallMultiplier;

            rb.AddForce(Vector3.up * finalGravity, ForceMode.Force);
        }
    }

    //============================================================
    // 🟧 무기 습득
    //============================================================

    /// <summary>
    /// 무기를 획득하고 빈 슬롯에 장착.
    /// </summary>
    /// 
    /// 
    public bool PickupWeapon(WeaponData newWeapon)
{
    for (int i = 0; i < weaponManagerSO.SlotCount; i++)
    {
        if (weaponManagerSO.GetWeaponAtSlot(i) == null)
        {
            weaponManagerSO.EquipWeapon(newWeapon, i, anim); // Equip the weapon with animations
          //  weaponManagerSO.SwitchWeapon(i, anim); // Switch to the new weapon slot

            Debug.Log($"[무기 습득] {newWeapon.weaponName} 을 {i}번 슬롯에 장착함");

            Managers.Instance.StartCoroutine(Managers.Instance.InitializeObjectPool("BaseTest"));

            return true;
        }
    }

    Debug.Log("⚠ 모든 슬롯이 꽉 찼습니다!");
    return false;
}


    //============================================================
    // 🟦 상태 전환
    //============================================================

    /// <summary>
    /// 캐릭터를 Idle 상태로 전환. (자식 클래스에서 오버라이드 가능)
    /// </summary>
    public virtual void GoToIdleState()
    {
        // 자식 클래스에서 구현
    }

    
}
