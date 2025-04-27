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

    protected StateMachine<CharacterController> stateMachine = new StateMachine<CharacterController>();
    public StateMachine<CharacterController> StateMachine => stateMachine;


    public Transform handTransform;  // 플레이어 손 트랜스폼
    public WeaponManagerSO weaponManagerSO;           // ✔️ 인게임에서 사용하는 런타임용 인스턴스


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

    //============================================================
    // 🔹 초기화
    //============================================================

    private async void Awake()
    {
        await InitAsync();
    }

    protected virtual async Task InitAsync()
    {
        rb = GetComponent<Rigidbody>();
        anim = GetComponent<Animator>();
        playerTransform = transform;

        string characterName = gameObject.name.Replace("(Clone)", "");
        string characterClass = Define.GetCharacterClassString(characterName);

        await LoadCharacterDataAsync(characterName);
        rb.useGravity = false;
        rb.drag = characterData.groundDrag;

        LoadInputActions();

        MoveAbility = new DefaultMoveAbility();
        DodgeAbility = new DefaultDodgeAbility();

        weaponManagerSO = ScriptableObject.CreateInstance<WeaponManagerSO>(); // 자신의 장비 런타임 인스턴스 생성
        weaponManagerSO.Initialize(2); // 슬롯 수 설정

        weaponManagerSO.weaponHandTransform = handTransform;

    }

    protected void LoadInputActions()
    {
        inputActions = new PlayerInputActions();
        inputActions.Enable();
        inputReady = true;
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


    private Transform FindDeepChildBFS(Transform parent, string name)
    {
        Queue<Transform> queue = new Queue<Transform>();
        queue.Enqueue(parent);

        while (queue.Count > 0)
        {
            Transform current = queue.Dequeue();
            if (current.name == name)
                return current;

            foreach (Transform child in current)
                queue.Enqueue(child);
        }

        return null;
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

    public void Jump()
    {
        if (!isGrounded) return;

        isJumping = true;
        rb.velocity = new Vector3(rb.velocity.x, 0, rb.velocity.z);
        rb.AddForce(Vector3.up * characterData.jumpForce, ForceMode.Impulse);
    }

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

    public bool PickupWeapon(WeaponData newWeapon)
    {
        for (int i = 0; i < weaponManagerSO.SlotCount; i++)
        {
            if (weaponManagerSO.GetWeaponAtSlot(i) == null)
            {
                weaponManagerSO.EquipWeapon(newWeapon, i);
                weaponManagerSO.SwitchWeapon(i);

                Debug.Log($"[무기 습득] {newWeapon.weaponName} 을 {i}번 슬롯에 장착함");


                //추후 무기에 맞는 SO로 수정하면 무기별 이펙트 초기화 가능. 스킬도 동일 처리 가능
                Managers.Instance.StartCoroutine(Managers.Instance.InitializeObjectPool("BaseTest"));

                return true; // 습득 성공

            }
        }

        Debug.Log("⚠ 모든 슬롯이 꽉 찼습니다!");
        return false; // 습득 실패
    }


    //============================================================
    // 🟦 상태 전환 관련
    //============================================================

    public virtual void GoToIdleState()
    {
        // 자식 클래스에서 오버라이드
    }
}
