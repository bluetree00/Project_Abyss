using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// Boss summon pattern. Triggered once at 80/50/10% HP (forceExecute=true in config).
/// Boss flies up → 3 colored eggs drop in triangle → mini dragons spawn from eggs →
/// when all dead → boss lands → optional follow-up pattern.
/// </summary>
[CreateAssetMenu(fileName = "DragonSummonPattern",
    menuName = "RelicFairy/Boss/Dragon/SummonPattern")]
public class DragonSummonPatternSO : BossPatternSO
{
    [Header("소환 조건")]
    [SerializeField] private DragonSummonPhase _summonPhase = DragonSummonPhase.At70;

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

    [Header("공중 대기 패턴 (Summon 중 일정 주기로 번갈아 사용)")]
    [SerializeField] private DragonBreathSweepPatternSO  _breathSweepPattern;
    [SerializeField] private DragonFireballRainPatternSO _fireballRainPattern;
    [SerializeField] private float _airPatternInterval = 8f;

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
    public DragonBreathSweepPatternSO  BreathSweepPattern  => _breathSweepPattern;
    public DragonFireballRainPatternSO FireballRainPattern => _fireballRainPattern;
    public float                       AirPatternInterval  => _airPatternInterval;
    public PlayerStatusEffectSO  IceStatusEffect           => _iceStatusEffect;
    public PlayerStatusEffectSO  ThunderStatusEffect       => _thunderStatusEffect;
    public PlayerStatusEffectSO  FireStatusEffect          => _fireStatusEffect;
    public GameObject            MiniBreathWarningPrefab   => _miniBreathWarningPrefab;
    public float                 MiniBreathWarningScale    => _miniBreathWarningScale;

    public (Color eggColor, Color miniColor, GameObject breathPrefab) GetElementAssets() =>
        _summonPhase switch
        {
            DragonSummonPhase.At70 => (_iceColor,     _iceColor,     _iceBreathPrefab),
            DragonSummonPhase.At40 => (_thunderColor, _thunderColor, _thunderBreathPrefab),
            DragonSummonPhase.At10 => (_fireColor,    _fireColor,    _fireBreathPrefab),
            _                      => (_iceColor,     _iceColor,     _iceBreathPrefab),
        };

    public PlayerStatusEffectSO GetStatusEffect() => _summonPhase switch
    {
        DragonSummonPhase.At70 => _iceStatusEffect,
        DragonSummonPhase.At40 => _thunderStatusEffect,
        DragonSummonPhase.At10 => _fireStatusEffect,
        _                      => null,
    };

    public void SetSummonFlag(DragonBossBlackboard bb, bool value)
    {
        switch (_summonPhase)
        {
            case DragonSummonPhase.At70: bb.HasSummonedAt70 = value; break;
            case DragonSummonPhase.At40: bb.HasSummonedAt40 = value; break;
            case DragonSummonPhase.At10: bb.HasSummonedAt10 = value; break;
        }
    }

    /// <summary>소환 페이즈에 따라 드래곤 바디에 적용할 원소 색상을 반환한다.</summary>
    public Color GetNextPhaseBodyTint() => _summonPhase switch
    {
        DragonSummonPhase.At70 => DragonBossVisualHelper.GetElementColor(DragonBossBlackboard.DragonElement.Thunder),
        DragonSummonPhase.At40 => DragonBossVisualHelper.GetElementColor(DragonBossBlackboard.DragonElement.Fire),
        DragonSummonPhase.At10 => DragonBossVisualHelper.GetElementColor(DragonBossBlackboard.DragonElement.Fire),
        _                      => DragonBossVisualHelper.GetElementColor(DragonBossBlackboard.DragonElement.Ice),
    };

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
            DragonSummonPhase.At70 => hp <= 0.7f && !bb.HasSummonedAt70,
            DragonSummonPhase.At40 => hp <= 0.4f && !bb.HasSummonedAt40,
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
    private bool    _resuming;
    private bool    _handingOffToSubPattern;

    internal DragonSummonState(DragonSummonPatternSO data) : base(data) { }

    internal void Reset()
    {
        _phase                    = Phase.Done;
        _minionsSpawned           = 0;
        _minionsAlive             = 0;
        _resuming                 = false;
        _handingOffToSubPattern   = false;
    }

    // ── FSM ──────────────────────────────────────────────────────────────────

    public override void Enter(MonsterContext ctx)
    {
        // BreathSweep/FireballRain 패턴으로 핸드오프했다가 복귀한 경우 — WaitMinions를 그대로 재개
        if (_resuming)
        {
            _resuming = false;
            _phase    = Phase.WaitMinions;
            _timer    = 0f;
            if (ctx.Agent != null) ctx.Agent.enabled = false;
            ctx.Transform.position = _hoverPos;
            return;
        }

        var bb = GetDragonBB(ctx);
        bool alreadyAirborne = bb != null && bb.BodyState == BodyState.Airborne;
        if (bb != null)
        {
            Data.SetSummonFlag(bb, true);
            bb.IsAirborne = true;
        }

        if (ctx.Agent != null) ctx.Agent.enabled = false;

        // 소환 시작 시 카메라를 플레이어 시점으로 복귀 (탑다운 등 다른 시점이었을 경우)
        GameCameraController.Instance?.DeactivateDragonTopDownView();

        _phase                    = alreadyAirborne ? Phase.Hover : Phase.Takeoff;
        _timer                    = 0f;
        _minionsSpawned           = 0;
        _minionsAlive             = 0;
        _takeoffHash              = Animator.StringToHash(Data.TakeoffStateName);
        _hoverPos                 = ctx.Transform.position;
        _hoverPos.y               = Mathf.Max(ctx.Transform.position.y, ctx.Runtime.SpawnPosition.y + Data.HoverHeight);

        if (alreadyAirborne)
        {
            ctx.Transform.position = _hoverPos;
            DragonBossVisualHelper.ApplyBodyTint(ctx.Transform, Data.GetNextPhaseBodyTint());
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
        // BreathSweep/FireballRain으로의 핸드오프 — 공중 상태를 유지한 채로 넘어간다
        if (_handingOffToSubPattern)
        {
            _handingOffToSubPattern = false;
            return;
        }

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
        // 이륙 완료 = 카메라 밖 공중 위치 → 속성 색상으로 바디 틴트 전환
        DragonBossVisualHelper.ApplyBodyTint(ctx.Transform, Data.GetNextPhaseBodyTint());
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
        if (allDead)
        {
            StartLanding(ctx);
            return;
        }

        // 가만히 있지 않고 일정 주기마다 BreathSweep/FireballRain을 번갈아 사용
        if (_timer < Data.AirPatternInterval) return;
        _timer = 0f;
        TriggerAirPattern(ctx);
    }

    /// <summary>BreathSweep(메테오 통합)으로 핸드오프하고, 종료 후 WaitMinions로 복귀하도록 예약한다.</summary>
    private void TriggerAirPattern(MonsterContext ctx)
    {
        // BreathSweep에 메테오가 통합되었으므로 항상 BreathSweep만 사용
        BossPatternSO pattern = Data.BreathSweepPattern;
        if (pattern == null) return;

        var runtimeState = pattern.GetRuntimeState();
        if (runtimeState == null) return;

        var bb = GetDragonBB(ctx);
        if (bb == null) return;

        bb.AirLoopReturnState   = this;
        _resuming               = true;
        _handingOffToSubPattern = true;
        ctx.Monster.ChangeState(runtimeState);
    }

    // ── Phase: Landing ────────────────────────────────────────────────────────

    private void UpdateLanding(MonsterContext ctx)
    {
        // 착지 지점의 Y는 Spawn Y가 아닌 실제 바닥 높이를 사용 —
        // 그렇지 않으면 NavMeshAgent.Warp이 바닥과 어긋난 위치에서 실패해 착지 후 이동이 안 됨
        float targetY = DragonPatternFloorUtils.GetFloorY(ctx.Transform.position, ctx.Runtime.SpawnPosition.y);
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
        => DragonPatternFloorUtils.SnapToFloorAndRestoreAgent(ctx);

    private static DragonBossBlackboard GetDragonBB(MonsterContext ctx)
        => (ctx.Monster as DragonBossMonster)?.DragonBlackboard;
}
}
