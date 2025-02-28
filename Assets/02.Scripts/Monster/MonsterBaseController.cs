using UnityEngine;

public class MonsterBaseController : MonoBehaviour
{
    #region 기본 초기화
    [SerializeField]
    protected MonsterData monsterData; // MonsterData ScriptableObject 참조
    public MonsterData MonsterData => monsterData; // Public getter

    [SerializeField]
    protected float MaxHp;

    [SerializeField]
    protected float hp;  // 기본 HP

    public float MaximumHp => MaxHp;
    public float CurrentHp => hp; // 읽기 전용 프로퍼티

    [SerializeField]
    protected Define.MonsterState _state = Define.MonsterState.Idle;

    [SerializeField]
    protected Vector3 _destPos;

    [SerializeField]
    protected GameObject _lockTarget;

    [SerializeField]
    protected Rigidbody rb;  // Rigidbody 참조
    protected Vector3 moveDirection;  // 이동 방향

    // 데이터 로딩 및 초기화가 완료되었는지 여부 플래그
    protected bool isInitialized = false;

    // 강제 회전 방지
    void FreezeRotation()
    {
        rb.angularVelocity = Vector3.zero;
    }

    // 각 몬스터의 고유 식별자를 Define.MonsterType으로 반환하는 가상 프로퍼티
    // 기본값은 Slime으로 설정 (파생 클래스에서 반드시 오버라이드)
    protected virtual Define.MonsterType MonsterTypeIdentifier 
    {
        get { return Define.MonsterType.Slime; }
    }

    protected virtual void Init()
    {
        rb = GetComponent<Rigidbody>();

        // Define에서 제공하는 MonsterType을 문자열 키로 변환하여 데이터 로드
        string key = MonsterTypeIdentifier.ToString(); 
        LoadMonsterData(key, OnMonsterDataLoaded);
    }

    // 데이터 로딩 완료 후 호출될 콜백
    private void OnMonsterDataLoaded()
    {
        // 데이터 로드 완료 후 HP 초기화 및 UI 생성
        MaxHp = monsterData.maxHealth; // 개인 HP 초기화
        hp = MaxHp;
        Managers.UI.MakeWorldSpaceUI<UI_HPBar>(transform);

        // 초기화 완료 설정
        isInitialized = true;
    }

    private void Start()
    {
        Init();
    }
    #endregion

    // 몬스터 데이터 로드 (비동기) 및 콜백 실행
    protected void LoadMonsterData(string key, System.Action onSuccess = null)
    {
        AddressablesManager.Instance.LoadAsset<MonsterData>(key, MonsterData_instance =>
        {
            if (MonsterData_instance == null)
            {
                Debug.LogError("몬스터 데이터가 null입니다.");
                return;
            }
            monsterData = MonsterData_instance;
            Debug.Log($"몬스터 데이터({monsterData.monsterName})를 로드했습니다.");
            Managers.CharacterData.SetMonsterData(monsterData);

            // 콜백이 할당되어 있다면 호출
            onSuccess?.Invoke();
        });
    }
    
    // 상태 업데이트는 각 파생 클래스에서 구현하도록 가상 함수로 선언
    protected virtual void UpdateMovement() { }

    // 상태별 애니메이션 설정
    protected virtual Define.MonsterState State
    {
        get { return _state; }
        set
        {
            _state = value;
            Animator anim = GetComponent<Animator>();
            switch (_state)
            {
                case Define.MonsterState.Idle:
                    anim.CrossFade("Idle", 0.2f);
                    break;
                case Define.MonsterState.Moving:
                    anim.CrossFade("Moving", 0.1f);
                    break;
                case Define.MonsterState.Runing:
                    anim.CrossFade("Runing", 0.3f);
                    break;
                case Define.MonsterState.Dodge:
                    anim.CrossFade("Dodge", 0.1f, -1);
                    break;
                case Define.MonsterState.Die:
                    anim.CrossFade("Die", 0.1f);
                    break;
                case Define.MonsterState.NormalAttack_01:
                    anim.CrossFade("NormalAttack_01", 0.1f);
                    break;
                case Define.MonsterState.NormalAttack_02:
                    anim.CrossFade("NormalAttack_02", 0.1f);
                    break;
                case Define.MonsterState.NormalAttack_03:
                    anim.CrossFade("NormalAttack_03", 0.1f);
                    break;
                case Define.MonsterState.NormalSkile_01:
                    anim.CrossFade("NormalSkile_01", 0.1f);
                    break;
                case Define.MonsterState.UltimateSkile_01:
                    anim.CrossFade("UltimateSkile_01", 0.1f);
                    break;
            }
        }
    }

    // 매 프레임 업데이트 전에 초기화 여부 체크
    protected virtual void Update()
    {
        if (!isInitialized)
            return; // 데이터가 로드되지 않았다면 업데이트 중단

        FreezeRotation(); // 강제 회전 방지

        // 상태에 따른 업데이트 실행
        switch (_state)
        {
            case Define.MonsterState.Idle:
                UpdateIdle();
                break;
            case Define.MonsterState.Moving:
                UpdateMoving();
                break;
            case Define.MonsterState.Runing:
                UpdateRuning();
                break;
            case Define.MonsterState.Dodge:
                UpdateDodge();
                break;
            case Define.MonsterState.NormalAttack_01:
                UpdateNormalAttack_01();
                break;
            case Define.MonsterState.NormalAttack_02:
                UpdateNormalAttack_02();
                break;
            case Define.MonsterState.NormalAttack_03:
                UpdateNormalAttack_03();
                break;
            case Define.MonsterState.NormalSkile_01:
                UpdateNormalSkile_01();
                break;
            case Define.MonsterState.UltimateSkile_01:
                UpdateUltimateSkile_01();
                break;
        }
    }

    protected virtual void UpdateIdle() { }           // Idle 상태 로직
    protected virtual void UpdateMoving() { }         // Moving 상태 로직
    protected virtual void UpdateRuning() { }         // Runing 상태 로직
    protected virtual void UpdateDodge() { }          // Dodge 상태 로직
    protected virtual void UpdateNormalAttack_01() { }  // NormalAttack_01 상태 로직
    protected virtual void UpdateNormalAttack_02() { }  // NormalAttack_02 상태 로직
    protected virtual void UpdateNormalAttack_03() { }  // NormalAttack_03 상태 로직
    protected virtual void UpdateNormalSkile_01() { }   // NormalSkile_01 상태 로직
    protected virtual void UpdateUltimateSkile_01() { } // UltimateSkile_01 상태 로직
}
