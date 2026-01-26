using UnityEngine;
using UnityEngine.AI;
using Cysharp.Threading.Tasks;
using System.Collections.Generic;

public abstract class MonsterController : CharacterBase
{
    public abstract Define.MonsterType Type { get; }

    public enum MonsterState
    {
        None,
        Idle,
        Patrol,
        Chase,
        AttackReady,
        Attack,
        Die
    }
    [HideInInspector] public Define.AttackPurpose currentAttackPurpose;

    [Header("Abilities")]
    [SerializeField] private MonsterAbilitySetSO abilitySetSO;
    public MonsterAbilitySet AbilitySet { get; private set; }

    [Header("Effects")]
    [SerializeField] private MonsterEffectProfileSO effectProfile;
    public MonsterEffectProfileSO EffectProfile => effectProfile;


    public bool HasDetectedTarget { get; private set; }
    public bool IsInAttackRange { get; private set; }
    public bool IsAttacking { get; private set; }
    public float AttackReadyTime = 0f;
    public int comboCount = 0;

    [Header("References")]
    public Transform playerTarget;
    public NavMeshAgent agent;
    public Animator animator;

    // 현재 선택된 공격 (공격 대기 상태에서 결정됨)
    public IAttackAbility CurrentAttackAbility { get; set; }

    public void SetDetected(bool detected) => HasDetectedTarget = detected;
    public void SetInAttackRange(bool inRange) => IsInAttackRange = inRange;
    public void SetAttack(bool isAttacking) => IsAttacking = isAttacking;
    public void SetAttackReadyTime(float time) => AttackReadyTime = time;
    [SerializeField] private MonsterStat _myStat;
    public MonsterStat MyStat => _myStat;

    protected abstract int MonsterId { get; }

    protected override async UniTask InitAsync()
    {
        await base.InitAsync();
        // 로그인 씬 안 거치고 테스트하도록 몬스터컨트롤러 에서 매니저 접근
        Managers.MonsterData.LoadFromJson();
        _myStat = Managers.MonsterData.GetStatById(MonsterId);

        agent = GetComponent<NavMeshAgent>();
        animator = GetComponent<Animator>();


        // AbilitySet 생성
        AbilitySet = abilitySetSO.CreateRuntimeSet(this);

        

    }

    private void OnEnable()
    {
        if (Managers.Player.PlayerTransform != null)
        {
            SetTarget(Managers.Player.PlayerTransform);
        }
        else
        {
            Managers.Player.OnPlayerSpawned += SetTarget;
        }
    }

    private void OnDisable()
    {
        if (Managers.Player != null)
        {
            Managers.Player.OnPlayerSpawned -= SetTarget;
        }
    }

    private void SetTarget(Transform player)
    {
        playerTarget = player;
    }

    public void SetCurrentAttackAbility(IAttackAbility ability)
    {
        CurrentAttackAbility = ability;
    }


    public void MoveTo(Vector3 destination)
    {
        if (agent != null && agent.isActiveAndEnabled)
        {
            agent.SetDestination(destination);
        }
    }

    public void StopMoving()
    {
        if (agent != null && agent.isActiveAndEnabled)
        {
            agent.ResetPath();
        }
    }

    protected override void Update()
    {
        base.Update();
        HandleAI();

        if (AttackReadyTime > 0f)
        {
            AttackReadyTime -= Time.deltaTime;
        }
    }

    public abstract void HandleAI();
    

    /// <summary>
    /// 몬스터의 Animator에서 특정 기본 애니메이션 클립을 새 애니메이션 클립으로 오버라이드
    /// </summary>
    /// <param name="clipName">기본 클립 이름 (예: AnimationClipNames.Attack)</param>
    /// <param name="newClip">대체할 애니메이션 클립</param>
    public void OverrideAnimationClip(string clipName, AnimationClip newClip)
    {
        if (animator == null || newClip == null)
            return;

        AnimatorOverrideController overrideController = null;

        if (animator.runtimeAnimatorController is AnimatorOverrideController currentOverride)
        {
            overrideController = currentOverride;
        }
        else
        {
            overrideController = new AnimatorOverrideController(animator.runtimeAnimatorController);
            animator.runtimeAnimatorController = overrideController;
        }

        var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
        overrideController.GetOverrides(overrides);

        bool replaced = false;
        for (int i = 0; i < overrides.Count; i++)
        {
            if (overrides[i].Key.name == clipName)
            {
                overrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(overrides[i].Key, newClip);
                replaced = true;
                break;
            }
        }

        if (!replaced)
        {
            Debug.LogWarning($"Animator 기본 클립 '{clipName}'을(를) 찾지 못했습니다.");
        }

        overrideController.ApplyOverrides(overrides);
    }


}