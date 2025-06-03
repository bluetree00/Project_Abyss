using UnityEngine;
using Game.CharacterStates;
using Game.CharacterStates.MonsterControllerStates;
using UnityEngine.AI;
using System.Threading.Tasks;

public class MonsterController : CharacterBase
{
    [SerializeField] public MonsterData monsterData;
    public MonsterData MonsterData => monsterData;

    [SerializeField] protected float MaxHp;
    [SerializeField] protected float hp;

    public float MaximumHp => MaxHp;
    public float CurrentHp => hp;

    [SerializeField] protected Define.MonsterState _state = Define.MonsterState.Idle;
    [SerializeField] public Vector3 _destPos;
    [SerializeField] public GameObject _lockTarget;

    public NavMeshAgent nma;

    [SerializeField] protected Rigidbody rb;
    protected Vector3 moveDirection;

    protected Animator anim;
    public Animator Anim => anim;

    protected StateMachine<MonsterController> stateMachine;

    protected bool isInitialized = false;

    protected virtual Define.MonsterType MonsterTypeIdentifier => Define.MonsterType.Slime;

    private async void Start()
    {
        await InitAsync();
    }

    protected virtual async Task InitAsync()
    {
        rb = GetComponent<Rigidbody>();
        anim = GetComponent<Animator>();

        string key = MonsterTypeIdentifier.ToString();
        await LoadMonsterDataAsync(key);

        MaxHp = monsterData.maxHealth;
        hp = MaxHp;
        Managers.UI.MakeWorldSpaceUI<UI_HPBar>(transform);

        OnMonsterReady();
        isInitialized = true;
    }

    protected async Task LoadMonsterDataAsync(string key)
    {
        var tcs = new TaskCompletionSource<bool>();
        AddressablesManager.Instance.LoadAsset<MonsterData>(key, data =>
        {
            if (data == null)
            {
                Debug.LogError("몬스터 데이터가 null입니다.");
                tcs.SetResult(false);
                return;
            }

            monsterData = data;
            Managers.CharacterData.SetMonsterData(monsterData);
            Debug.Log($"몬스터 데이터({monsterData.monsterName})를 로드했습니다.");
            tcs.SetResult(true);
        });

        await tcs.Task;
    }

    protected virtual void OnMonsterReady()
    {
        // 자식 클래스에서 상태머신 세팅
    }

    void FreezeRotation()
    {
        rb.angularVelocity = Vector3.zero;
    }

    protected virtual void UpdateMovement() { }

    protected virtual void Update()
    {
        if (!isInitialized) return;
        FreezeRotation();
    }
}
