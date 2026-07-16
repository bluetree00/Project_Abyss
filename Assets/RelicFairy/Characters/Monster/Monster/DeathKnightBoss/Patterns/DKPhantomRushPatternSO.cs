using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// DeathKnight 환영 베기 (PhantomRush / Attack1) 패턴.
///
/// Phase 1 콤보 전용 (Phase 2에서는 DKP2PassiveAttackRunner만 발동).
///
/// 흐름 (3회 반복):
///  Windup → Attack1
///  1×1 경고 타일 + 분신(보스 Animator child 복제) 등장 (페이드인)
///  warningDuration 후: 타일 제거, 분신 Attack1 재시작, VFX 스폰 (1/10 스케일, ParticleHierarchy)
///  hitDelay 후 피격 판정
///  roundInterval 랜덤 대기 → 분신 페이드아웃 → 다음 라운드
///  Recovery
///
/// 분신: ctx.Animator.gameObject 가 보스 루트와 다를 때 자동 복제.
///       같으면(root Animator) 분신 없이 VFX만 재생.
/// </summary>
[CreateAssetMenu(menuName = "Abyss/Boss/DeathKnight/DK_PhantomRushPattern",
                 fileName = "DK_PhantomRushPattern")]
public class DKPhantomRushPatternSO : BossPatternSO
{
    [Header("그리드 타일")]
    public GameObject whiteTilePrefab;
    public GameObject blackTilePrefab;

    [Header("VFX")]
    [Tooltip("환영 베기 이펙트")]
    public GameObject phantomVfxPrefab;
    [Tooltip("VFX 스케일 (1셀=2m 기준)")]
    public float phantomVfxScale = 1.5f;

    [Header("사운드")]
    public AudioClip slashSfx;

    [Header("분신 설정")]
    [Tooltip("분신 Attack1 재생 속도 배율")]
    public float phantomAnimSpeed       = 2.5f;
    [Tooltip("분신 페이드인 시간 (초)")]
    public float phantomFadeInDuration  = 0.25f;
    [Tooltip("분신 페이드아웃 시간 (초)")]
    public float phantomFadeOutDuration = 0.3f;

    [Header("타이밍 — Phase 1 콤보")]
    public float windupDuration   = 0.6f;
    public float warningDuration  = 0.4f;
    [Tooltip("VFX 출현 → 피격까지 회피 창")]
    public float hitDelay         = 0.2f;
    public float roundIntervalMin = 0.5f;
    public float roundIntervalMax = 0.7f;
    public int   strikeCount      = 3;
    public float recoveryTime     = 0.4f;

    [Header("데미지")]
    public float hitRadius           = 0.9f;
    public float damageMultiplier    = 1f;
    public float knockbackMultiplier = 1f;

    [Header("패시브 (Phase 2 DKP2PassiveAttackRunner)")]
    public float passiveWarningDuration   = 0.4f;
    [Tooltip("버스트 내 공격과 공격 사이 대기 시간 최소 (s)")]
    public float passiveBurstIntervalMin  = 1.0f;
    [Tooltip("버스트 내 공격과 공격 사이 대기 시간 최대 (s)")]
    public float passiveBurstIntervalMax  = 2.0f;
    public float passiveCooldownMin       = 8f;
    public float passiveCooldownMax       = 12f;

    private DKPhantomRushState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new DKPhantomRushState(this);
    public override void OnRecycled()                       => _state = new DKPhantomRushState(this);

    public override bool CanExecute(BossPatternContext ctx)
    {
        if (ctx.Ctx.Runtime.PlayerTarget == null) return false;
        // Phase 2에서는 패시브 러너만 발동 — 콤보 풀에서 제외
        var dk = ctx.Ctx.Monster as DeathKnightBossMonster;
        return dk == null || !dk.DKBlackboard.IsPhase2;
    }

    public override SpecialStateBase GetRuntimeState() => _state;
}

public class DKPhantomRushState : FullLockState<DKPhantomRushPatternSO>
{
    private const string AnimName    = "Attack1";
    private const string PhantomAnim = "Attack1";

    private enum Phase    { Windup, Striking, Recovery }
    private enum SubPhase { Warning, PostWarning, WaitInterval }

    private Phase         _phase;
    private float         _timer;
    private int           _strikeIndex;
    private SubPhase      _subPhase;
    private float         _subTimer;
    private bool          _hitApplied;
    private Vector3       _capturedCellCenter;
    private DKSwordColor  _swordColor;
    private GameObject    _warningTileGo;
    private float         _roundInterval;
    private GameObject    _phantomGo;
    private Animator      _phantomAnim;
    private DKPhantomClone _phantomClone;

    public DKPhantomRushState(DKPhantomRushPatternSO data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        _phase         = Phase.Windup;
        _timer         = 0f;
        _strikeIndex   = 0;
        _swordColor    = GetSwordColor(ctx);
        _warningTileGo = null;
        _phantomGo     = null;
        _phantomAnim   = null;
        _phantomClone  = null;

        StopAgent(ctx);
        FacePlayer(ctx);
        PlayAnim(ctx, AnimName);
    }

    public override void Update(MonsterContext ctx)
    {
        float speed = AnimSpeed(ctx);
        _timer    += Time.deltaTime * speed;
        _subTimer += Time.deltaTime * speed;

        switch (_phase)
        {
            case Phase.Windup:
                if (_timer >= Data.windupDuration)
                {
                    _phase = Phase.Striking;
                    _timer = 0f;
                    BeginRound(ctx);
                }
                break;
            case Phase.Striking:
                UpdateStriking(ctx);
                break;
            case Phase.Recovery:
                if (_timer >= Data.recoveryTime)
                    ctx.Monster.ChangeState<AttackReadyState>();
                break;
        }
    }

    private void UpdateStriking(MonsterContext ctx)
    {
        switch (_subPhase)
        {
            case SubPhase.Warning:
                if (_subTimer >= Data.warningDuration)
                {
                    ReleaseTile();
                    // 경고 종료 후 분신 등장 + VFX + 사운드
                    SpawnPhantom(ctx);
                    if (Data.phantomVfxPrefab != null)
                    {
                        Quaternion vfxRot = _phantomGo != null
                            ? _phantomGo.transform.rotation
                            : Quaternion.identity;
                        var vfxGo = BossEffectPool.SpawnOneShot(Data.phantomVfxPrefab,
                            _capturedCellCenter, vfxRot, fallbackLifetime: 2.5f);
                        if (vfxGo != null)
                            vfxGo.transform.localScale = Vector3.one * Data.phantomVfxScale;
                    }
                    Managers.Sound?.PlayEffectAt(Data.slashSfx, _capturedCellCenter);
                    _subPhase   = SubPhase.PostWarning;
                    _subTimer   = 0f;
                    _hitApplied = false;
                }
                break;

            case SubPhase.PostWarning:
                if (!_hitApplied && _subTimer >= Data.hitDelay)
                {
                    _hitApplied = true;
                    ApplyPhantomDamage(ctx);
                    DestroyPhantom();   // 피격 후 즉시 페이드아웃 시작
                    _subPhase = SubPhase.WaitInterval;
                    _subTimer = 0f;
                }
                break;

            case SubPhase.WaitInterval:
                if (_subTimer >= _roundInterval)
                {
                    _strikeIndex++;
                    if (_strikeIndex >= Data.strikeCount)
                    {
                        _phase = Phase.Recovery;
                        _timer = 0f;
                    }
                    else
                    {
                        BeginRound(ctx);
                    }
                }
                break;
        }
    }

    public override void Exit(MonsterContext ctx)
    {
        ReleaseTile();
        DestroyPhantom();
        RestoreAgent(ctx);
    }

    private void BeginRound(MonsterContext ctx)
    {
        _subPhase      = SubPhase.Warning;
        _subTimer      = 0f;
        _hitApplied    = false;
        _roundInterval = UnityEngine.Random.Range(Data.roundIntervalMin, Data.roundIntervalMax);
        _swordColor    = GetSwordColor(ctx);

        if (ctx.Runtime.PlayerTarget == null) return;

        Vector2Int pc = DKBossRoomContext.WorldToCell(ctx.Runtime.PlayerTarget.position);
        _capturedCellCenter = DKBossRoomContext.CellToWorld(pc.x, pc.y, 0.1f);

        // 1단계: 경고 타일만 생성 (분신은 warningDuration 후 SpawnPhantom에서 생성)
        ReleaseTile();
        GameObject tilePrefab = _swordColor == DKSwordColor.White
            ? Data.whiteTilePrefab : Data.blackTilePrefab;
        if (tilePrefab != null && DKBossRoomContext.IsInterior(pc.x, pc.y))
        {
            _warningTileGo = BossEffectPool.Spawn(tilePrefab,
                DKBossRoomContext.CellToWorld(pc.x, pc.y, 0.05f),
                Quaternion.Euler(-90f, 0f, 0f));
            if (_warningTileGo != null)
                _warningTileGo.transform.localScale = Vector3.one * DKBossRoomContext.CellSize;
        }
    }

    // 2단계: 경고 종료 후 분신 등장 (충돌체 비활성화로 플레이어와 물리 간섭 없음)
    private void SpawnPhantom(MonsterContext ctx)
    {
        DestroyPhantom();
        if (ctx.Animator == null || ctx.Animator.gameObject == ctx.Monster.gameObject) return;

        float      randomAngle = UnityEngine.Random.Range(0f, 360f);
        Vector3    randomDir   = Quaternion.AngleAxis(randomAngle, Vector3.up) * Vector3.forward;
        Vector3    spawnPos    = _capturedCellCenter + randomDir * (DKBossRoomContext.CellSize * 0.5f);
        spawnPos.y = ctx.Transform.position.y - 1f;
        Quaternion spawnRot = Quaternion.LookRotation(-randomDir);

        _phantomGo    = UnityEngine.Object.Instantiate(ctx.Animator.gameObject, spawnPos, spawnRot);
        _phantomAnim  = _phantomGo.GetComponent<Animator>();
        _phantomClone = _phantomGo.AddComponent<DKPhantomClone>();
        _phantomClone.Initialize();
        _phantomClone.StartFadeIn(Data.phantomFadeInDuration);

        foreach (var col in _phantomGo.GetComponentsInChildren<Collider>())
            col.enabled = false;

        if (_phantomAnim != null)
        {
            _phantomAnim.speed = Data.phantomAnimSpeed;
            _phantomAnim.CrossFade(PhantomAnim, 0.05f, 0, 0f);
        }
    }

    private void ReleaseTile()
    {
        if (_warningTileGo == null) return;
        BossEffectPool.Release(_warningTileGo);
        _warningTileGo = null;
    }

    private void DestroyPhantom()
    {
        if (_phantomGo == null) return;

        var go    = _phantomGo;
        _phantomGo    = null;
        _phantomAnim  = null;

        if (_phantomClone != null)
        {
            _phantomClone.StartFadeOut(Data.phantomFadeOutDuration,
                () => { if (go != null) UnityEngine.Object.Destroy(go); });
            _phantomClone = null;
        }
        else
        {
            UnityEngine.Object.Destroy(go);
        }
    }

    private void ApplyPhantomDamage(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return;

        Vector2Int playerCell = DKBossRoomContext.WorldToCell(ctx.Runtime.PlayerTarget.position);
        Vector2Int tileCell   = DKBossRoomContext.WorldToCell(_capturedCellCenter);
        if (playerCell != tileCell) return;

        var player = ctx.Runtime.PlayerTarget.GetComponent<PlayerController>();
        if (player == null) return;

        int dmg = Mathf.Max(1, (int)(ctx.Config.stat.attackPower * Data.damageMultiplier));
        player.TakeDamage(dmg);

        Vector3 kb = (ctx.Runtime.PlayerTarget.position - _capturedCellCenter).normalized;
        kb.y = 0.2f;
        player.ApplyKnockback(kb * ctx.Config.stat.knockbackForce * Data.knockbackMultiplier);

        BossImpactFeedback.TriggerCameraShake(0.08f, 0.15f);
    }

    // ── 헬퍼 ──────────────────────────────────────────────────

    private static DKSwordColor GetSwordColor(MonsterContext ctx)
        => (ctx.Monster as DeathKnightBossMonster)?.DKBlackboard.SwordColor ?? DKSwordColor.White;

    private static float AnimSpeed(MonsterContext ctx)
        => (ctx.Monster as DeathKnightBossMonster)?.DKBlackboard.AnimSpeedMult ?? 1f;

    private static void PlayAnim(MonsterContext ctx, string stateName)
    {
        if (ctx.Animator == null) return;
        if (!ctx.Animator.HasState(0, Animator.StringToHash(stateName))) return;
        ctx.Animator.speed = AnimSpeed(ctx);
        ctx.Animator.CrossFade(stateName, 0.05f, 0, 0f);
    }

    private static void StopAgent(MonsterContext ctx)
    {
        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
        {
            ctx.Agent.isStopped = true;
            ctx.Agent.velocity  = Vector3.zero;
            ctx.Agent.ResetPath();
        }
    }

    private static void RestoreAgent(MonsterContext ctx)
    {
        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
            ctx.Agent.isStopped = false;
    }

    private static void FacePlayer(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return;
        Vector3 dir = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.001f)
            ctx.Transform.rotation = Quaternion.LookRotation(dir);
    }
}
}
