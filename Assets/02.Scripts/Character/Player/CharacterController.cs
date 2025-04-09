using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Game.CharacterStates;
using Game.CharacterStates.CharacterControllerStates;
using UnityEngine.InputSystem;

public class CharacterController : MonoBehaviour
{
    [SerializeField] protected CharacterData characterData;
    public CharacterData CharacterData => characterData;

    [SerializeField] public WeaponContainer weaponContainer;
    [SerializeField] public WeaponData currentWeapon;

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

    protected StateMachine<CharacterController> stateMachine;
    public StateMachine<CharacterController> StateMachine => stateMachine;

    protected PlayerInputActions inputActions;
    protected bool inputReady = false;

    private bool isGrounded;
    private bool isJumping;
    private float airStartTime = 0f;

    public enum AirState { None, JumpStart, InAir, Landing }
    public AirState CurrentAirState { get; private set; } = AirState.None;

    public void SetAirState(AirState state)
    {
        CurrentAirState = state;
        if (state == AirState.InAir)
            airStartTime = Time.time;
    }

    public bool IsInAir => CurrentAirState == AirState.InAir;
    public bool IsHardLanding => (Time.time - airStartTime) >= characterData.hardLandingTimeThreshold;
    public bool IsGrounded() => isGrounded;
    public bool IsJumping() => isJumping;
    public void FinishJump() => isJumping = false;

    public virtual string GetIdleAnimationName() => "Idle";

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

        await LoadCharacterDataAsync(characterName); // ❗ 반드시 먼저 로드

        // ✅ characterData가 로드된 후에 접근
        rb.useGravity = false;
        rb.drag = characterData.groundDrag;

        await LoadWeaponContainerAsync(characterClass);
        await SetupWeaponAttachmentAsync("Weapon_parentR");
        await LoadWeaponDataAsync("basic_Knight_01");

        LoadInputActions();
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

    protected async Task LoadWeaponContainerAsync(string classKey)
    {
        var tcs = new TaskCompletionSource<bool>();
        AddressablesManager.Instance.LoadAsset<WeaponContainer>(classKey, container =>
        {
            weaponContainer = container;
            tcs.SetResult(container != null);
        });

        await tcs.Task;
    }

    protected async Task SetupWeaponAttachmentAsync(string handName)
    {
        Transform handTransform = FindDeepChildBFS(playerTransform, handName);
        if (handTransform != null)
            Managers.Weapon.ContainerDataInit(weaponContainer, handTransform);
        await Task.CompletedTask;
    }

    protected async Task LoadWeaponDataAsync(string weaponName)
    {
        var tcs = new TaskCompletionSource<bool>();
        AddressablesManager.Instance.LoadAsset<WeaponData>(weaponName, data =>
        {
            currentWeapon = data;
            tcs.SetResult(data != null);
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

    public void Move(Vector3 direction, float speed)
    {
        if (direction.magnitude <= 0)
        {
            StopHorizontalMovement();
            return;
        }

        Vector3 normalizedDirection = direction.normalized;
        float appliedSpeed = IsInAir ? speed * characterData.airControlMultiplier : speed;
        rb.velocity = new Vector3(normalizedDirection.x * appliedSpeed, rb.velocity.y, normalizedDirection.z * appliedSpeed);

        Quaternion targetRotation = Quaternion.LookRotation(normalizedDirection);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * 10f);
    }

    public void StopHorizontalMovement()
    {
        if (IsInAir) return;
        rb.velocity = new Vector3(0f, rb.velocity.y, 0f);
    }

    protected virtual void Update() { }

    private void FixedUpdate()
    {
        if (characterData == null)
            return; // 아직 초기화 안 되었음

        UpdateGroundedCheck();
        UpdateAirStateAuto();
        ApplyMassBasedGravity();

        rb.drag = IsInAir ? characterData.airDrag : characterData.groundDrag;
    }


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

    public void Jump()
    {
        if (!isGrounded) return;

        isJumping = true;
        rb.velocity = new Vector3(rb.velocity.x, 0, rb.velocity.z);
        rb.AddForce(Vector3.up * characterData.jumpForce, ForceMode.Impulse);
    }
}
