    using UnityEngine;
    using Game.CharacterStates;
    using Game.CharacterStates.MonsterControllerStates;
    using UnityEngine.AI;

    public class MonsterController : MonoBehaviour
    {
        #region 기본 초기화
        [SerializeField]
        public MonsterData monsterData; // MonsterData ScriptableObject 참조
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
        public Vector3 _destPos;

        [SerializeField]
        public GameObject _lockTarget;

        public  NavMeshAgent nma;
    
        [SerializeField]
        protected Rigidbody rb;  // Rigidbody 참조
        protected Vector3 moveDirection;  // 이동 방향

        // 데이터 로딩 및 초기화가 완료되었는지 여부 플래그
        protected bool isInitialized = false;

        protected Animator anim;
        public Animator Anim => anim; 

        protected StateMachine<MonsterController> stateMachine;

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

         private void Start()
        {
            Init();
        }

        protected virtual void Init()
        {
            rb = GetComponent<Rigidbody>();
            anim = GetComponent<Animator>(); 

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
             OnMonsterReady();

            // 초기화 완료 설정
            isInitialized = true;
        }

        protected virtual void OnMonsterReady()
        {
            // 자식이 상태머신 세팅을 여기에 덮어씌움
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
    

        // 매 프레임 업데이트 전에 초기화 여부 체크
        protected virtual void Update()
        {
            
            FreezeRotation(); // 강제 회전 방지


        
        }

        
    }
