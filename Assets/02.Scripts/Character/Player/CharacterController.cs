using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Game.CharacterStates;
using Game.CharacterStates.CharacterControllerStates;

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
    [SerializeField] protected Rigidbody rb;

    public Transform playerTransform;

    protected StateMachine<CharacterController> stateMachine;

    [Header("Gravity & Jump Settings")]
    [SerializeField] private float gravity = -30f;
    [SerializeField] private float fallMultiplier = 2f;
    [SerializeField] private float groundCheckDistance = 0.3f;
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private float jumpForce = 7f;

    [Header("Hard Landing Settings")]
    [SerializeField] private float hardLandingTimeThreshold = 0.8f;

    [Header("Movement Settings")]
    [SerializeField] private float airControlMultiplier = 0.5f;
    [SerializeField] private float groundDrag = 4f;
    [SerializeField] private float airDrag = 0.5f;

    private bool isGrounded;
    private bool isJumping;
    private float airTime = 0f;
    private float airStartTime = 0f;

    public enum AirState
    {
        None,
        JumpStart,
        InAir,
        Landing
    }

    public AirState CurrentAirState { get; private set; } = AirState.None;

    public void SetAirState(AirState state)
    {
        CurrentAirState = state;

        if (state == AirState.InAir)
        {
            airStartTime = Time.time;
        }
    }

    public bool IsInAir => CurrentAirState == AirState.InAir;
    public bool IsHardLanding => (Time.time - airStartTime) >= hardLandingTimeThreshold;
    public bool IsGrounded() => isGrounded;
    public bool IsJumping() => isJumping;

    public void FinishJump() => isJumping = false;

    private async void Awake()
    {
        await InitAsync();
    }

    protected virtual async Task InitAsync()
    {
        rb = GetComponent<Rigidbody>();
        rb.useGravity = false;
        rb.drag = groundDrag;

        anim = GetComponent<Animator>();
        playerTransform = transform;

        string characterName = gameObject.name.Replace("(Clone)", "");
        string characterClass = Define.GetCharacterClassString(characterName);

        await LoadCharacterDataAsync(characterName);
        await LoadWeaponContainerAsync(characterClass);
        await SetupWeaponAttachmentAsync("Weapon_parentR");
        await LoadWeaponDataAsync("basic_Knight_01");

        await Task.CompletedTask;
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

            Debug.Log($"캐릭터 데이터({characterData.characterName}) 로드됨");
            tcs.SetResult(true);
        });

        await tcs.Task;
    }

    protected async Task LoadWeaponContainerAsync(string classKey)
    {
        var tcs = new TaskCompletionSource<bool>();
        AddressablesManager.Instance.LoadAsset<WeaponContainer>(classKey, container =>
        {
            if (container == null)
            {
                Debug.LogError("무기 컨테이너가 null입니다.");
                tcs.SetResult(false);
                return;
            }

            weaponContainer = container;
            Debug.Log($"무기 컨테이너({weaponContainer.name}) 로드됨");
            tcs.SetResult(true);
        });

        await tcs.Task;
    }

    protected async Task SetupWeaponAttachmentAsync(string handName)
    {
        Transform handTransform = FindDeepChildBFS(playerTransform, handName);
        if (handTransform != null)
        {
            Managers.Weapon.ContainerDataInit(weaponContainer, handTransform);
        }
        else
        {
            Debug.LogError($"{handName} 트랜스폼을 찾을 수 없습니다.");
        }

        await Task.CompletedTask;
    }

    protected async Task LoadWeaponDataAsync(string weaponName)
    {
        var tcs = new TaskCompletionSource<bool>();
        AddressablesManager.Instance.LoadAsset<WeaponData>(weaponName, data =>
        {
            if (data == null)
            {
                Debug.LogError("무기 데이터가 null입니다.");
                tcs.SetResult(false);
                return;
            }

            currentWeapon = data;
            Debug.Log($"기본 무기 데이터({currentWeapon.weaponName}) 로드됨");
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
            {
                queue.Enqueue(child);
            }
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
        float appliedSpeed = IsInAir ? speed * airControlMultiplier : speed;
        rb.velocity = new Vector3(normalizedDirection.x * appliedSpeed, rb.velocity.y, normalizedDirection.z * appliedSpeed);

        Quaternion targetRotation = Quaternion.LookRotation(normalizedDirection);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * 10f);
    }

    public void StopHorizontalMovement()
    {
        if (IsInAir)
            return; // 공중에서는 자연스러운 감속을 위해 멈추지 않음

        rb.velocity = new Vector3(0f, rb.velocity.y, 0f);
    }

    protected virtual void Update() { }

    private void FixedUpdate()
    {
        UpdateGroundedCheck();
        UpdateAirStateAuto();
        ApplyMassBasedGravity();

        // 공중/지상 상태에 따라 drag 변경
        rb.drag = IsInAir ? airDrag : groundDrag;
    }

   private void UpdateGroundedCheck()
    {
        Vector3 rayOrigin = transform.position + Vector3.up * 0.1f;
        float rayLength = groundCheckDistance + 0.1f;

        if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, rayLength, groundLayer))
        {
            isGrounded = hit.distance <= groundCheckDistance + 0.05f; // 약간 여유
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
            float finalGravity = gravity;
            if (rb.velocity.y < 0)
                finalGravity *= fallMultiplier;

            rb.AddForce(Vector3.up * finalGravity, ForceMode.Force);
        }
    }

    public void Jump()
    {
        if (!isGrounded) return;

        isJumping = true;
        rb.velocity = new Vector3(rb.velocity.x, 0, rb.velocity.z);
        rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
    }
}
