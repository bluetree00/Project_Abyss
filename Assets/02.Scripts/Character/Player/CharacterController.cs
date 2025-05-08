//============================================================
// 📦 네임스페이스 및 의존성
//============================================================
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
    // 🔶 필드 및 프로퍼티: 캐릭터 정보 및 핵심 시스템
    //============================================================

    [Header("Character & Weapon")]
    [SerializeField] protected CharacterData characterData;
    public CharacterData CharacterData => characterData;

    protected StateMachine<CharacterController> stateMachine = new StateMachine<CharacterController>();
    public StateMachine<CharacterController> StateMachine => stateMachine;

    public WeaponManagerSO weaponManagerSO;

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

    //============================================================
    // 🎮 캐릭터 능력
    //============================================================
    public IMoveAbility<CharacterController> MoveAbility { get; protected set; }
    public IDodgeAbility<CharacterController> DodgeAbility { get; protected set; }
    public ILightAttackAbility<CharacterController> LightAttackAbility { get; protected set; }
    public IHeavyAttackAbility<CharacterController> HeavyAttackAbility { get; protected set; }
    public IJumpAbility<CharacterController> JumpAbility { get; protected set; }

    public Transform handTransform;  // 무기 장착 위치

    //============================================================
    // 🔷 점프 및 공중 상태 관리
    //============================================================
    public bool isGrounded;
    public bool isJumping;
    private float airStartTime = 0f;

    public enum AirState { None, JumpStart, InAir, Landing }
    public AirState CurrentAirState { get; private set; } = AirState.None;

    public bool IsInAir => CurrentAirState == AirState.InAir;
    public bool IsHardLanding => (Time.time - airStartTime) >= characterData.hardLandingTimeThreshold;

    public bool IsGrounded() => isGrounded;
    public bool IsJumping() => isJumping;
    public void FinishJump() => isJumping = false;

    //============================================================
    // 🛠 초기화
    //============================================================
    private async void Awake()
    {
        await InitAsync();
    }

    protected virtual async Task InitAsync()
    {
        InitCoreComponents();
        await InitCharacterDataAsync();
        InitInputActions();
        InitAbilities();
        InitWeaponManager();
    }

    private void InitCoreComponents()
    {
        rb = GetComponent<Rigidbody>();
        anim = GetComponent<Animator>();
        playerTransform = transform;

        handTransform = Util.FindDeepChild(transform, "WeaponSocket");
        if (handTransform == null)
            Debug.LogWarning("⚠ WeaponSocket 트랜스폼을 찾지 못했습니다.");
    }

    private async Task InitCharacterDataAsync()
    {
        string characterName = gameObject.name.Replace("(Clone)", "");
        string characterClass = Define.GetCharacterClassString(characterName);

        await LoadCharacterDataAsync(characterName);

        rb.useGravity = false;
        rb.drag = characterData.groundDrag;
    }

    private void InitInputActions()
    {
        inputActions = new PlayerInputActions();
        inputActions.Enable();
        inputReady = true;
    }

    protected virtual void InitAbilities()
    {
        MoveAbility = new DefaultMoveAbility();
        DodgeAbility = new DefaultDodgeAbility();
        JumpAbility = new DefaultJumpAbility();
    }

    public void ClearWeaponAbilities()
    {
        LightAttackAbility = null;
        HeavyAttackAbility = null;
    }

    public void OnWeaponEquipped()
    {
        var weapon = weaponManagerSO.CurrentWeapon;
        if (weapon != null)
        {
            LightAttackAbility = weapon.LightAttack;
            // HeavyAttackAbility = weapon.HeavyAttack;
        }
    }

    private void InitWeaponManager()
    {
        weaponManagerSO = ScriptableObject.CreateInstance<WeaponManagerSO>();
        weaponManagerSO.Initialize(2, anim); // 무기 슬롯 수 2개로 초기화
        weaponManagerSO.weaponHandTransform = handTransform;
        weaponManagerSO.OnWeaponEquippedEvent += OnWeaponEquipped;
    }

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
    public void StopHorizontalMovement()
    {
        if (IsInAir) return;
        rb.velocity = new Vector3(0f, rb.velocity.y, 0f);
    }

    //============================================================
    // 🔺 점프 처리
    //============================================================
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
    // ⚙️ 물리 및 상태 관련 처리
    //============================================================
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
    // 🟧 무기 습득 및 장착
    //============================================================
    public bool PickupWeapon(WeaponData newWeapon)
    {
        for (int i = 0; i < weaponManagerSO.SlotCount; i++)
        {
            if (weaponManagerSO.GetWeaponAtSlot(i) == null)
            {
                weaponManagerSO.EquipWeapon(newWeapon, i, anim);
                Debug.Log($"[무기 습득] {newWeapon.weaponName} 을 {i}번 슬롯에 장착함");

                Managers.Instance.StartCoroutine(Managers.Instance.InitializeObjectPool("BaseTest"));

                weaponManagerSO.SwitchWeapon(i, anim);
                return true;
            }
        }

        Debug.Log("⚠ 모든 슬롯이 꽉 찼습니다!");
        return false;
    }

    //============================================================
    // 🟦 상태 전환
    //============================================================
    public virtual void GoToIdleState()
    {
        // 자식 클래스에서 구현 예정
    }

    public virtual void GoToComboAttackState()
    {
        Debug.Log("GoToComboAttackState is not implemented in the base class.");
    }
}