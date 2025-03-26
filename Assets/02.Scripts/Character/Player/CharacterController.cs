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
        if (direction.magnitude <= 0) return;

        Vector3 normalizedDirection = direction.normalized;
        rb.velocity = new Vector3(normalizedDirection.x * speed, rb.velocity.y, normalizedDirection.z * speed);

        Quaternion targetRotation = Quaternion.LookRotation(normalizedDirection);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * 10f);
    }

    protected virtual void Update() { }
}
