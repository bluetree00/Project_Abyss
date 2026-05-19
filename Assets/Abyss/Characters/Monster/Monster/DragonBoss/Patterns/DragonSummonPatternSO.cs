using UnityEngine;

namespace Abyss.Monster
{
/// <summary>
/// Boss summon pattern. Triggered once at 80/50/10% HP (forceExecute=true in config).
/// Boss flies up → 3 colored eggs drop in triangle → mini dragons spawn from eggs →
/// when all dead → boss lands → optional follow-up pattern.
/// </summary>
[CreateAssetMenu(fileName = "DragonSummonPattern",
    menuName = "Abyss/Boss/Dragon/SummonPattern")]
public class DragonSummonPatternSO : BossPatternSO
{
    [Header("소환 조건")]
    [SerializeField] private DragonSummonPhase _summonPhase = DragonSummonPhase.At80;

    [Header("비행")]
    [SerializeField] private float  _hoverHeight       = 6f;
    [SerializeField] private string _takeoffStateName  = "Takeoff";
    [SerializeField] private string _landingStateName  = "Landing";

    [Header("알 소환")]
    [SerializeField] private GameObject _eggPrefab;
    [SerializeField] private float _triangleRadius  = 5f;
    [SerializeField] private float _eggDropHeight   = 8f;
    [SerializeField] private float _eggDropDuration = 1.5f;
    [SerializeField] private float _eggCrackDelay   = 0.8f;

    [Header("미니 드래곤")]
    [SerializeField] private GameObject _miniDragonPrefab;
    [SerializeField] private float _miniScale           = 0.35f;
    [SerializeField] private int   _miniHp              = 150;
    [SerializeField] private float _miniSpeed           = 4.5f;
    [SerializeField] private float _miniBreathRange     = 8f;
    [SerializeField] private float _miniBreathCooldown  = 3f;
    [SerializeField] private int   _miniBreathDamage    = 15;
    [SerializeField] private float _miniBreathSpeed     = 12f;

    [Header("속성 브레스 프리팹")]
    [Tooltip("얼음 속성 (Projectile 6 blue fire)")]
    [SerializeField] private GameObject _iceBreathPrefab;
    [Tooltip("번개 속성 (Projectile 2 electro)")]
    [SerializeField] private GameObject _thunderBreathPrefab;
    [Tooltip("불 속성 (Projectile 3 black fire)")]
    [SerializeField] private GameObject _fireBreathPrefab;

    [Header("속성 색상")]
    [SerializeField] private Color _iceColor     = new Color(0.5f, 0.85f, 1.0f);
    [SerializeField] private Color _thunderColor = new Color(0.65f, 0.3f, 1.0f);
    [SerializeField] private Color _fireColor    = new Color(1.0f, 0.35f, 0.1f);

    [Header("속성 상태이상 (Inspector에서 할당)")]
    [SerializeField] private PlayerStatusEffectSO _iceStatusEffect;
    [SerializeField] private PlayerStatusEffectSO _thunderStatusEffect;
    [SerializeField] private PlayerStatusEffectSO _fireStatusEffect;

    [Header("미니 드래곤 브레스 경고 마커")]
    [SerializeField] private GameObject _miniBreathWarningPrefab;
    [SerializeField] private float      _miniBreathWarningScale = 1.5f;

    [Header("착지 후 추가 패턴 (선택)")]
    [Tooltip("착지 직후 CanExecute가 참이면 이 패턴을 이어서 실행. null이면 ChaseState 복귀.")]
    [SerializeField] private BossPatternSO _landingFollowUpPattern;

    // ── Properties ──────────────────────────────────────────────────────────
    public DragonSummonPhase SummonPhase        => _summonPhase;
    public float  HoverHeight                   => _hoverHeight;
    public string TakeoffStateName              => _takeoffStateName;
    public string LandingStateName              => _landingStateName;
    public GameObject EggPrefab                 => _eggPrefab;
    public float  TriangleRadius                => _triangleRadius;
    public float  EggDropHeight                 => _eggDropHeight;
    public float  EggDropDuration               => _eggDropDuration;
    public float  EggCrackDelay                 => _eggCrackDelay;
    public GameObject MiniDragonPrefab          => _miniDragonPrefab;
    public float  MiniScale                     => _miniScale;
    public int    MiniHp                        => _miniHp;
    public float  MiniSpeed                     => _miniSpeed;
    public float  MiniBreathRange               => _miniBreathRange;
    public float  MiniBreathCooldown            => _miniBreathCooldown;
    public int    MiniBreathDamage              => _miniBreathDamage;
    public float  MiniBreathSpeed               => _miniBreathSpeed;
    public BossPatternSO         LandingFollowUpPattern    => _landingFollowUpPattern;
    public PlayerStatusEffectSO  IceStatusEffect           => _iceStatusEffect;
    public PlayerStatusEffectSO  ThunderStatusEffect       => _thunderStatusEffect;
    public PlayerStatusEffectSO  FireStatusEffect          => _fireStatusEffect;
    public GameObject            MiniBreathWarningPrefab   => _miniBreathWarningPrefab;
    public float                 MiniBreathWarningScale    => _miniBreathWarningScale;

    public (Color eggColor, Color miniColor, GameObject breathPrefab) GetElementAssets() =>
        _summonPhase switch
        {
            DragonSummonPhase.At80 => (_iceColor,     _iceColor,     _iceBreathPrefab),
            DragonSummonPhase.At50 => (_thunderColor, _thunderColor, _thunderBreathPrefab),
            DragonSummonPhase.At10 => (_fireColor,    _fireColor,    _fireBreathPrefab),
            _                      => (_iceColor,     _iceColor,     _iceBreathPrefab),
        };

    public PlayerStatusEffectSO GetStatusEffect() => _summonPhase switch
    {
        DragonSummonPhase.At80 => _iceStatusEffect,
        DragonSummonPhase.At50 => _thunderStatusEffect,
        DragonSummonPhase.At10 => _fireStatusEffect,
        _                      => null,
    };

    public void SetSummonFlag(DragonBossBlackboard bb, bool value)
    {
        switch (_summonPhase)
        {
            case DragonSummonPhase.At80: bb.HasSummonedAt80 = value; break;
            case DragonSummonPhase.At50: bb.HasSummonedAt50 = value; break;
            case DragonSummonPhase.At10: bb.HasSummonedAt10 = value; break;
        }
    }

    // ── BossPatternSO ────────────────────────────────────────────────────────

    private DragonSummonState _runtimeState;

    public override void Initialize(BossPatternContext ctx)
        => _runtimeState = new DragonSummonState(this);

    public override void OnRecycled() => _runtimeState?.Reset();

    public override bool CanExecute(BossPatternContext ctx)
    {
        if (ctx.Blackboard is not DragonBossBlackboard bb) return false;
        float hp = ctx.Ctx.Config?.stat.maxHp > 0
            ? (float)ctx.Ctx.Runtime.CurrentHp / ctx.Ctx.Config.stat.maxHp
            : 1f;
        return _summonPhase switch
        {
            DragonSummonPhase.At80 => hp <= 0.8f && !bb.HasSummonedAt80,
            DragonSummonPhase.At50 => hp <= 0.5f && !bb.HasSummonedAt50,
            DragonSummonPhase.At10 => hp <= 0.1f && !bb.HasSummonedAt10,
            _                      => false,
        };
    }

    public override SpecialStateBase GetRuntimeState() => _runtimeState;
}

// ────────────────────────────────────────────────────────────────────────────
// Runtime state
// ────────────────────────────────────────────────────────────────────────────

internal sealed class DragonSummonState : FullLockState<DragonSummonPatternSO>
{
    private enum Phase { Takeoff, Hover, WaitMinions, Landing, Done }

    private const int TotalMinions = 3;

    private Phase   _phase;
    private float   _timer;
    private int     _takeoffHash;
    private Vector3 _hoverPos;
    private int     _minionsSpawned;
    private int     _minionsAlive;

    internal DragonSummonState(DragonSummonPatternSO data) : base(data) { }

    internal void Reset()
    {
        _phase          = Phase.Done;
        _minionsSpawned = 0;
        _minionsAlive   = 0;
    }

    // ── FSM ──────────────────────────────────────────────────────────────────

    public override void Enter(MonsterContext ctx)
    {
        var bb = GetDragonBB(ctx);
        bool alreadyAirborne = bb != null && bb.BodyState == BodyState.Airborne;
        if (bb != null)
        {
            Data.SetSummonFlag(bb, true);
            bb.IsAirborne = true;
        }

        if (ctx.Agent != null) ctx.Agent.enabled = false;

        _phase          = alreadyAirborne ? Phase.Hover : Phase.Takeoff;
        _timer          = 0f;
        _minionsSpawned = 0;
        _minionsAlive   = 0;
        _takeoffHash    = Animator.StringToHash(Data.TakeoffStateName);
        _hoverPos       = ctx.Transform.position;
        _hoverPos.y     = Mathf.Max(ctx.Transform.position.y, ctx.Runtime.SpawnPosition.y + Data.HoverHeight);

        if (alreadyAirborne)
        {
            ctx.Transform.position = _hoverPos;
            SpawnEggs(ctx);
        }
        else
        {
            PlayAnim(ctx, Data.TakeoffStateName);
        }
    }

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime;
        switch (_phase)
        {
            case Phase.Takeoff:     UpdateTakeoff(ctx);     break;
            case Phase.Hover:       UpdateHover(ctx);       break;
            case Phase.WaitMinions: UpdateWaitMinions(ctx); break;
            case Phase.Landing:     UpdateLanding(ctx);     break;
        }
    }

    public override void Exit(MonsterContext ctx)
    {
        RestoreAgent(ctx);
        var bb = GetDragonBB(ctx);
        if (bb != null) bb.IsAirborne = false;
    }

    // ── Phase: Takeoff ───────────────────────────────────────────────────────

    private void UpdateTakeoff(MonsterContext ctx)
    {
        float targetY = ctx.Runtime.SpawnPosition.y + Data.HoverHeight;
        MoveY(ctx, targetY);

        if (!IsAnimNearEnd(ctx, _takeoffHash)) return;

        _hoverPos = ctx.Transform.position;
        _phase    = Phase.Hover;
        _timer    = 0f;
        SpawnEggs(ctx);
    }

    // ── Phase: Hover ─────────────────────────────────────────────────────────

    private void UpdateHover(MonsterContext ctx)
    {
        ctx.Transform.position = _hoverPos;
        FacePlayer(ctx);

        if (_minionsSpawned >= TotalMinions)
        {
            _phase = Phase.WaitMinions;
            _timer = 0f;
        }
    }

    // ── Phase: WaitMinions ────────────────────────────────────────────────────

    private void UpdateWaitMinions(MonsterContext ctx)
    {
        ctx.Transform.position = _hoverPos;
        FacePlayer(ctx);

        // 미니 드래곤이 모두 죽기 전까지는 절대로 착지하지 않음
        bool allDead = _minionsSpawned >= TotalMinions && _minionsAlive <= 0;
        if (!allDead) return;

        StartLanding(ctx);
    }

    // ── Phase: Landing ────────────────────────────────────────────────────────

    private void UpdateLanding(MonsterContext ctx)
    {
        float targetY = ctx.Runtime.SpawnPosition.y;
        MoveY(ctx, targetY);

        int landHash = Animator.StringToHash(Data.LandingStateName);
        if (!IsAnimNearEnd(ctx, landHash)) return;

        RestoreAgent(ctx);
        FinishPattern(ctx);
    }

    // ── 알 소환 ───────────────────────────────────────────────────────────────

    private void SpawnEggs(MonsterContext ctx)
    {
        if (Data.EggPrefab == null)
        {
            _minionsSpawned = TotalMinions;
            return;
        }

        var (eggColor, miniColor, breathPrefab) = Data.GetElementAssets();
        Vector3 center = ctx.Runtime.PlayerTarget != null
            ? ctx.Runtime.PlayerTarget.position
            : ctx.Transform.position;

        for (int i = 0; i < TotalMinions; i++)
        {
            float   angle    = i * 120f * Mathf.Deg2Rad;
            Vector3 offset   = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * Data.TriangleRadius;
            Vector3 ground   = center + offset;
            Vector3 spawnPos = ground + Vector3.up * Data.EggDropHeight;

            var go  = Object.Instantiate(Data.EggPrefab, spawnPos, Quaternion.identity);
            var egg = go.GetComponent<DragonSummonEgg>() ?? go.AddComponent<DragonSummonEgg>();

            egg.Init(
                targetPos:          ground,
                dropDuration:       Data.EggDropDuration,
                crackDelay:         Data.EggCrackDelay,
                eggColor:           eggColor,
                miniDragonPrefab:   Data.MiniDragonPrefab,
                miniScale:          Data.MiniScale,
                miniHp:             Data.MiniHp,
                miniSpeed:          Data.MiniSpeed,
                miniBreathRange:    Data.MiniBreathRange,
                miniBreathCooldown: Data.MiniBreathCooldown,
                miniBreathDamage:   Data.MiniBreathDamage,
                miniBreathSpeed:    Data.MiniBreathSpeed,
                miniBreathPrefab:   breathPrefab,
                miniColor:          miniColor,
                playerTarget:       ctx.Runtime.PlayerTarget,
                orbitIndex:         i,
                statusEffect:       Data.GetStatusEffect());

            egg.OnMiniDragonSpawned += OnMiniDragonSpawned;
        }
    }

    private void OnMiniDragonSpawned(DragonMiniDragon mini)
    {
        _minionsSpawned++;
        _minionsAlive++;
        mini.OnDied += _ => _minionsAlive = Mathf.Max(0, _minionsAlive - 1);
    }

    // ── Landing & Finish ─────────────────────────────────────────────────────

    private void StartLanding(MonsterContext ctx)
    {
        _phase = Phase.Landing;
        _timer = 0f;
        PlayAnim(ctx, Data.LandingStateName);
    }

    private void FinishPattern(MonsterContext ctx)
    {
        _phase = Phase.Done;

        if (Data.LandingFollowUpPattern != null)
        {
            var patCtx = new BossPatternContext
            {
                Boss       = ctx.Monster as IBoss,
                Ctx        = ctx,
                Blackboard = (ctx.Monster as IBoss)?.Blackboard,
            };
            if (Data.LandingFollowUpPattern.CanExecute(patCtx))
            {
                ctx.Monster.ChangeState(Data.LandingFollowUpPattern.GetRuntimeState());
                return;
            }
        }

        ctx.Monster.ChangeState<ChaseState>();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void MoveY(MonsterContext ctx, float targetY)
    {
        Vector3 pos = ctx.Transform.position;
        pos.y = Mathf.MoveTowards(pos.y, targetY, ctx.Stat.moveSpeed * 2f * Time.deltaTime);
        ctx.Transform.position = pos;
    }

    private static void PlayAnim(MonsterContext ctx, string stateName)
    {
        if (ctx.Animator == null) return;
        int hash = Animator.StringToHash(stateName);
        if (ctx.Animator.HasState(0, hash))
            ctx.Animator.CrossFade(stateName, 0.1f, 0, 0f);
    }

    private static bool IsAnimNearEnd(MonsterContext ctx, int hash)
    {
        if (ctx.Animator == null) return true;
        if (!ctx.Animator.HasState(0, hash)) return true;
        if (ctx.Animator.IsInTransition(0)) return false;
        var info = ctx.Animator.GetCurrentAnimatorStateInfo(0);
        if (info.shortNameHash != hash) return true;
        return info.normalizedTime >= 0.9f;
    }

    private static void FacePlayer(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return;
        Vector3 dir = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.01f)
            ctx.Transform.rotation = Quaternion.Slerp(
                ctx.Transform.rotation, Quaternion.LookRotation(dir), 8f * Time.deltaTime);
    }

    private static void RestoreAgent(MonsterContext ctx)
    {
        if (ctx.Agent == null || ctx.Agent.enabled) return;
        ctx.Agent.enabled = true;
        ctx.Agent.Warp(ctx.Transform.position);
    }

    private static DragonBossBlackboard GetDragonBB(MonsterContext ctx)
        => (ctx.Monster as DragonBossMonster)?.DragonBlackboard;
}
}
