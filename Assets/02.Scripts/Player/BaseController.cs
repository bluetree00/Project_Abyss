using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BaseController : MonoBehaviour
{
    #region  기본 초기화
    [SerializeField]
    protected  CharacterData characterData; // CharacterData ScriptableObject 참조
    public CharacterData CharacterData { get { return characterData; } }
    [SerializeField]
    protected WeaponContainer weaponContainer; // 무기 컨테이너 변수
    [SerializeField]
    protected WeaponData currentWeapon;
    protected Animator anim;
    protected Vector3 moveDirection;  // 이동 방향

    [SerializeField]
    protected Define.State _state = Define.State.Idle;

    [SerializeField]
    protected Vector3 _destPos;

    [SerializeField]
    protected GameObject _lockTarget;

    [SerializeField]
    protected Rigidbody rb;  // Rigidbody 참조
    public Transform playerTransform; // 플레이어의 Transform을 할당


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

        //NOTE: 리소스 로드를 AddressablesManager를 통해 하도록 변경
        //characterData = Managers.Resource.Load<CharacterData>($"Data/PlayerData/{characterName}");
        //weaponContainer = Managers.Resource.Load<WeaponContainer>($"Data/Container/{Define.GetCharacterClassString(characterName)}");

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

    //캐릭터들의 기본 상속 움직임
    protected virtual void Move(Vector3 direction, float speed)
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


    //상태별 애니메이션 설정
    protected virtual Define.State State
    {
        get { return _state; }
        set
        {
            _state = value;

            // Animator anim = GetComponent<Animator>();
            switch(_state)
            {   //TODO : 아무 무기도 소지하고 있지 않은 상태 구분해서 처리 필요
                // 플레이어 기본 움직임 상태
                case Define.State.Idle:
                    if (weaponContainer != null && 
                    weaponContainer.isWeaponEquipped && 
                    currentWeapon.weapon_Idle_AnimationName != "")
                    {   
                        anim.CrossFade($"{currentWeapon.weapon_Idle_AnimationName}", 0.1f);
                    }
                    else
                    {
                        anim.CrossFade("Idle", 0.2f);
                    } 
                   break;
                case Define.State.Moving:
                   anim.CrossFade("Moving", 0.1f);
                   break;
                case Define.State.Runing:
                   anim.CrossFade("Runing", 0.3f);
                   break;
                case Define.State.Dodge:
                    anim.CrossFade("Dodge", 0.1f, -1);
                   break;
                case Define.State.Die:
                    anim.CrossFade("Die", 0.1f);
                   break;
                case Define.State.ChangeWeapon:
                    anim.CrossFade($"{currentWeapon.weapon_ChangeWeapon_AnimationName}", 0.2f);
                   break;
                case Define.State.NormalAttack_01:
                    NormalAttack(1);
                    // anim.CrossFade("NormalAttack_01", 0.1f);
                   break;
                case Define.State.NormalAttack_02:
                    NormalAttack(2);
                    // anim.CrossFade("NormalAttack_02", 0.1f);
                   break;
                case Define.State.NormalAttack_03:
                    NormalAttack(3);
                    // anim.CrossFade("NormalAttack_03", 0.1f);
                   break;
                case Define.State.NormalSkill_01:
                    anim.CrossFade("NormalSkile_01", 0.1f);
                   break;
                case Define.State.UltimateSkill_01:
                    anim.CrossFade("UltimateSkile_01", 0.1f);
                   break;
            }
        }
    }

    private void NormalAttack(int attackIndex)  //NOTE: 편의를 위해 매개변수를 1부터 시작하도록 설정
    {
        if (currentWeapon != null && attackIndex >= 1 && attackIndex - 1 < currentWeapon.weapon_Attack_AnimationName.Length)
        {
            if (currentWeapon.weapon_Attack_AnimationName[attackIndex - 1] != "")
            {
                anim.CrossFade($"{currentWeapon.weapon_Attack_AnimationName[attackIndex - 1]}", 0.1f);
            }
            else
            {   
                _state = Define.State.Idle;
                Debug.Log("해당 무기의 공격 애니메이션이 없습니다.");
            }
        }
        else
        {
            Debug.Log("무기가 장착되어 있지 않거나 공격 인덱스가 잘못되었습니다.");
            _state = Define.State.Idle;
        }
    }

     //상태별 업데이트 패턴
    protected virtual void Update()
    {
        switch (_state)
        {
            case Define.State.Idle:
                UpdateIdle();
                break;
            case Define.State.Moving:
                UpdateMoving();
                break;
             case Define.State.Runing:
                UpdateRuning();
                break;
            case Define.State.Dodge:
                UpdateDodge();
                break;
            case Define.State.NormalAttack_01:
                UpdateNormalAttack_01();
                break;
            case Define.State.NormalAttack_02:
                UpdateNormalAttack_02();
                break;
            case Define.State.NormalAttack_03:
                UpdateNormalAttack_03();
                break;
            case Define.State.NormalSkill_01:
                UpdateNormalSkile_01();
                break;
            case Define.State.UltimateSkill_01:
                UpdateUltimateSkile_01();
                break;
        }
    }

    protected virtual void UpdateMovement(){}
    protected virtual void UpdateIdle(){}  // Idle 상태에서의 로직
    protected virtual void UpdateMoving(){}  // Moving 상태에서의 로직
    protected virtual void UpdateRuning(){}  // Runing 상태에서의 로직
    protected virtual void UpdateDodge(){}  // Dodge 상태에서의 로직
    protected virtual void UpdateNormalAttack_01(){}  // NormalAttack_01 상태에서의 로직
    protected virtual void UpdateNormalAttack_02(){}  // NormalAttack_02 상태에서의 로직
    protected virtual void UpdateNormalAttack_03(){}  // NormalAttack_03 상태에서의 로직
    protected virtual void UpdateNormalSkile_01(){}  // NormalSkile_01 상태에서의 로직
    protected virtual void UpdateUltimateSkile_01(){}  // UltimateSkile_01 상태에서의 로직

}
