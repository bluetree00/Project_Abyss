using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Game.CharacterStates;
using Game.CharacterStates.States;

public class CharacterController : MonoBehaviour
{
    #region  기본 초기화
    [SerializeField]
    protected  CharacterData characterData; // CharacterData ScriptableObject 참조
    public CharacterData CharacterData { get { return characterData; } }
    [SerializeField]
    public WeaponContainer weaponContainer; // 무기 컨테이너 변수 추후 상속 구조 변경
    [SerializeField]
    public WeaponData currentWeapon;
    protected Animator anim;
    public Animator Anim => anim; 
    protected Vector3 moveDirection;  // 이동 방향
    public Vector3 MoveDirection => moveDirection;


    [SerializeField]
    protected Define.State _state = Define.State.Idle;

    [SerializeField]
    protected Vector3 _destPos;

    [SerializeField]
    protected GameObject _lockTarget;

    [SerializeField]
    protected Rigidbody rb;  // Rigidbody 참조
    public Transform playerTransform; // 플레이어의 Transform을 할당

    protected StateMachine<CharacterController> stateMachine;
    public StateMachine<CharacterController> StateMachine => stateMachine; // 혹은 아래처럼 캐스팅해서 오버라이드



    // 매니저에서 가져온 현재 무기 이름 받아줄 스트링 변수
    // 무기SO.무기이름 

    //NOTE: 초기화 작업을 Awake에서 처리하도록 변경
    private void Awake()
    {
        Init();
    }

    protected virtual void Init()
    {
        rb = GetComponent<Rigidbody>();
        playerTransform = transform;
        // string characterName = gameObject.name;
        string characterName = gameObject.name.Replace("(Clone)", ""); 
        anim = GetComponent<Animator>();

        LoadCharacterData(characterName, () =>
        {
            LoadWeaponContainer(Define.GetCharacterClassString(characterName), () =>
            {
                // 하위 오브젝트 중 "Weapon_parentR" 이름을 가진 트랜스폼을 BFS로 찾음
                Transform weaponHandTransform = FindDeepChildBFS(playerTransform, "Weapon_parentR");
                if (weaponHandTransform != null)
                {
                    // WeaponManager의 ContainerDataInit 메서드에 손의 트랜스폼을 전달
                    Managers.Weapon.ContainerDataInit(weaponContainer, weaponHandTransform);
                }
                else
                {
                    Debug.LogError("Weapon_parentR 트랜스폼을 찾을 수 없습니다.");
                }

                LoadWeaponData("basic_Knight_01");
            });
        });

        
        
    }

    protected void LoadCharacterData(string characterName, Action OnSuccess = null)
    {
        AddressablesManager.Instance.LoadAsset<CharacterData>(characterName, characterData_instance =>
        {
            if (characterData_instance == null)
            {
                Debug.LogError("캐릭터 데이터가 null입니다.");
                return;
            }
            characterData = characterData_instance;
            Debug.Log($"캐릭터 데이터({characterData.characterName})를 로드했습니다.");
            OnSuccess.Invoke();
            Managers.CharacterData.SetCharacterData(characterData);

            characterData.canDodge = true;  // 대시 가능 여부 초기화
        });
    }

    protected void LoadWeaponContainer(string characterClassString, Action OnSuccess = null)
    {
        AddressablesManager.Instance.LoadAsset<WeaponContainer>(characterClassString, weaponContainer_instance =>
        {
            if (weaponContainer_instance == null)
            {
                Debug.LogError("무기 컨테이너가 null입니다.");
                return;
            }
            weaponContainer = weaponContainer_instance;
            Debug.Log($"무기 컨테이너({weaponContainer.name})를 로드했습니다.");
            OnSuccess.Invoke();
        });
    }

    protected void LoadWeaponData(string weaponName, Action OnSuccess = null)
    {
        AddressablesManager.Instance.LoadAsset<WeaponData>(weaponName, WData_instance =>
        {
            if (WData_instance == null)
            {
                Debug.LogError("무기 데이터가 null입니다.");
                return;
            }
            currentWeapon = WData_instance;
            Debug.Log($"기본 무기 데이터({currentWeapon.weaponName})를 로드했습니다.");
            // OnSuccess.Invoke();
        });
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
    #endregion

    //캐릭터들의 기본 상속 움직임 움직임 함수는 추후에 솔리드 방식으로 전부 분해 필요요
    public void Move(Vector3 direction, float speed)
    {
        //State = Define.State.Moving;

        if (direction.magnitude <= 0) return;
        
        // 방향 벡터 정규화
        Vector3 normalizedDirection = direction.normalized;

        // 이동 처리
        rb.velocity = new Vector3(normalizedDirection.x * speed, rb.velocity.y, normalizedDirection.z * speed);

        // 회전 처리 - 부드럽게 회전하도록 변경
        Quaternion targetRotation = Quaternion.LookRotation(normalizedDirection);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * 10f); // 5f는 회전 속도 계수
    }



    public string NormalAttack(int attackIndex)  // Return the animation name
{
    if (currentWeapon != null && attackIndex >= 1 && attackIndex - 1 < currentWeapon.weapon_Attack_AnimationName.Length)
    {
        string animationName = currentWeapon.weapon_Attack_AnimationName[attackIndex - 1];
        if (!string.IsNullOrEmpty(animationName))
        {
            return animationName; // Return the animation name
        }
        else
        {   
            _state = Define.State.Idle;
            Debug.Log("해당 무기의 공격 애니메이션이 없습니다.");
            return null;
        }
    }
    else
    {
        Debug.Log("무기가 장착되어 있지 않거나 공격 인덱스가 잘못되었습니다.");
        _state = Define.State.Idle;
        return null;
    }
}

     //상태별 업데이트 패턴
    protected virtual void Update()
    {

    }

    // protected virtual void UpdateMovement(){}
    // protected virtual void UpdateIdle(){}  // Idle 상태에서의 로직
    // protected virtual void UpdateMoving(){}  // Moving 상태에서의 로직
    // protected virtual void UpdateRuning(){}  // Runing 상태에서의 로직
    // protected virtual void UpdateDodge(){}  // Dodge 상태에서의 로직
    // protected virtual void UpdateNormalAttack_01(){}  // NormalAttack_01 상태에서의 로직
    // protected virtual void UpdateNormalAttack_02(){}  // NormalAttack_02 상태에서의 로직
    // protected virtual void UpdateNormalAttack_03(){}  // NormalAttack_03 상태에서의 로직
    // protected virtual void UpdateNormalSkile_01(){}  // NormalSkile_01 상태에서의 로직
    // protected virtual void UpdateUltimateSkile_01(){}  // UltimateSkile_01 상태에서의 로직

}