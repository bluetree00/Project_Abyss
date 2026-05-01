using PixPlays.ElementalVFX;
using UnityEngine;

namespace Abyss.Monster
{
/// <summary>
/// ForestGuardian 땅 가르기 (GroundSlash) 패턴.
///
/// ━━ 흐름 (3회 반복) ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
///  Warning  : AttackReady + 디스크 경고장판 확장 표시
///             (1타: 0→scale0 / 2·3타: scale0→scaleN 으로 시작)
///  Strike   : VerticalAttack + vfxDelay 후 VFX + 데미지 + 발사
///  판정     : 링(ring) 형태 — 1타[0,range0], 2타(range0,range1], 3타(range1,range2]
///  발사     : 맞은 플레이어를 위로 띄우고 보스에서 멀리 날려 다음 링으로 유도
/// </summary>
[CreateAssetMenu(menuName = "Abyss/Boss/ForestGuardian/FG_GroundSlashPattern", fileName = "FG_GroundSlashPattern")]
public class FGGroundSlashPatternSO : BossPatternSO
{
    // ── 조건 ──────────────────────────────────────────────
    [Header("GroundSlash — Condition")]
    [Tooltip("패턴 발동 최대 거리 (m)")]
    public float maxRange = 8f;

    // ── 타이밍 ────────────────────────────────────────────
    [Header("GroundSlash — Timing")]
    [Tooltip("경고 장판 표시 시간 (초)")]
    public float warningDuration = 0.5f;

    [Tooltip("VerticalAttack 애니메이션 지속 시간 (초)")]
    public float strikeAnimDuration = 0.8f;

    [Tooltip("타격 시작 후 VFX·데미지 발생까지 딜레이 (초)")]
    public float vfxDelay = 0.4f;

    [Tooltip("한 번의 타격 완료 후 다음 경고장판 시작까지 대기 시간 (초)")]
    public float hitInterval = 0.4f;

    [Tooltip("마지막 타격 후 ChaseState 전환까지 대기 시간 (초)")]
    public float recoveryDuration = 0.5f;

    // ── 히트 데이터 (배열 인덱스 = 타격 순서 0·1·2) ──────
    [Header("GroundSlash — Hits")]
    [Tooltip("데미지 판정 링 외곽 반경 (m). 기본: 5·10·15")]
    public float[] hitRanges = { 5f, 10f, 15f };

    [Tooltip("VFX·경고장판 균일 스케일. 기본: 3·6·9")]
    public float[] hitVfxScales = { 3f, 6f, 9f };

    // ── 데미지 ────────────────────────────────────────────
    [Header("GroundSlash — Damage")]
    [Tooltip("기본 attackPower 배율 (타격당)")]
    public float damageMultiplier = 1.2f;

    // ── 연쇄 발사 ─────────────────────────────────────────
    [Header("GroundSlash — Launch")]
    [Tooltip("타격 시 위쪽 발사 속도 (m/s)")]
    public float launchUpForce = 4f;

    [Tooltip("타격 시 보스에서 멀어지는 발사 속도 (m/s)")]
    public float launchOutForce = 3f;

    [Tooltip("발사 후 플레이어 이동 차단 시간 (초) — 공중 체공 유지용")]
    public float launchAirTime = 0.7f;

    // ── 비주얼 ────────────────────────────────────────────
    [Header("GroundSlash — Visual")]
    [Tooltip("디스크 경고 장판 프리팹 (DiscMeshWarning 포함)")]
    public GameObject warningDiscPrefab;

    [Tooltip("내려찍기 임팩트 VFX 프리팹")]
    public GameObject slamVfxPrefab;

    [Tooltip("임팩트 VFX 자동 소멸 시간 (초). ParticleSystem 없는 경우 적용.")]
    public float vfxLifetime = 2f;

    // ── 런타임 ────────────────────────────────────────────
    private FGGroundSlashState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new FGGroundSlashState(this);
    public override void OnRecycled()                       => _state = new FGGroundSlashState(this);

    public override bool CanExecute(BossPatternContext ctx)
    {
        if (ctx.Ctx.Runtime.PlayerTarget == null) return false;
        float dist = Vector3.Distance(ctx.Ctx.Transform.position, ctx.Ctx.Runtime.PlayerTarget.position);
        return dist <= maxRange;
    }

    public override SpecialStateBase GetRuntimeState() => _state;

    public int   HitCount             => hitRanges?.Length ?? 0;
    public float GetHitRange(int i)    => (hitRanges    != null && i < hitRanges.Length)    ? hitRanges[i]    : 5f;
    public float GetHitVfxScale(int i) => (hitVfxScales != null && i < hitVfxScales.Length) ? hitVfxScales[i] : 1f;
    public float GetHitInnerRange(int i) => i > 0 ? GetHitRange(i - 1) : 0f;
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// FGGroundSlashState
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class FGGroundSlashState : FullLockState<FGGroundSlashPatternSO>
{
    private const string AnimAttackReady = "AttackReady";
    private const string AnimStrike      = "VerticalAttack";

    private enum Phase { Warning, Strike, Recover }

    private Phase      _phase;
    private float      _timer;
    private int        _hitIndex;
    private bool       _vfxFired;
    private GameObject _warningGO;
    private Vector3    _warningStartScale;
    private Vector3    _warningTargetScale;

    public FGGroundSlashState(FGGroundSlashPatternSO data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        _hitIndex = 0;

        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
        {
            ctx.Agent.isStopped = true;
            ctx.Agent.ResetPath();
        }

        BeginWarning(ctx);
    }

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime * SpeedMult(ctx);

        switch (_phase)
        {
            case Phase.Warning:
                if (_warningGO != null && Data.warningDuration > 0f)
                {
                    float t = Mathf.Clamp01(_timer / Data.warningDuration);
                    _warningGO.transform.localScale = Vector3.Lerp(_warningStartScale, _warningTargetScale, t);
                }

                if (_timer >= Data.warningDuration)
                {
                    DespawnWarning();
                    _timer    = 0f;
                    _vfxFired = false;
                    _phase    = Phase.Strike;
                    PlayAnim(ctx, AnimStrike, 0.05f);
                }
                break;

            case Phase.Strike:
                if (!_vfxFired && _timer >= Data.vfxDelay)
                {
                    _vfxFired = true;
                    SpawnSlamVfx(ctx, _hitIndex);
                    TryDealDamage(ctx, _hitIndex);
                }

                bool  isLastHit   = _hitIndex >= Data.HitCount - 1;
                float phaseEnd    = Data.strikeAnimDuration + (isLastHit ? 0f : Data.hitInterval);

                if (_timer >= phaseEnd)
                {
                    _hitIndex++;
                    if (!isLastHit)
                        BeginWarning(ctx);
                    else
                    {
                        _timer = 0f;
                        _phase = Phase.Recover;
                    }
                }
                break;

            case Phase.Recover:
                if (_timer >= Data.recoveryDuration)
                    ctx.Monster.ChangeState<ChaseState>();
                break;
        }
    }

    public override void Exit(MonsterContext ctx)
    {
        DespawnWarning();

        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
        {
            ctx.Agent.isStopped = false;
            ctx.Agent.Warp(ctx.Transform.position);
        }
    }

    // ── Warning 시작 ──────────────────────────────────────
    private void BeginWarning(MonsterContext ctx)
    {
        _phase    = Phase.Warning;
        _timer    = 0f;
        _vfxFired = false;

        FacePlayer(ctx);
        SpawnWarningDisc(ctx, _hitIndex);
        PlayAnim(ctx, AnimAttackReady, 0.1f);
    }

    // ── 데미지 (링 판정) ──────────────────────────────────
    // hitIndex별 링: [innerRange, outerRange]
    // 맞은 플레이어를 위로 띄우고 다음 링 방향으로 발사
    private void TryDealDamage(MonsterContext ctx, int hitIndex)
    {
        if (ctx.Config?.stat == null || ctx.Runtime.PlayerTarget == null) return;

        // XZ 평면 거리로 판정 — 공중에 뜬 플레이어의 Y가 링 범위를 벗어나는 문제 방지
        Vector3 bossPos   = ctx.Transform.position;
        Vector3 playerPos = ctx.Runtime.PlayerTarget.position;
        float   dx        = playerPos.x - bossPos.x;
        float   dz        = playerPos.z - bossPos.z;
        float   dist      = Mathf.Sqrt(dx * dx + dz * dz);

        float outerRange = Data.GetHitRange(hitIndex);
        float innerRange = Data.GetHitInnerRange(hitIndex);

        if (dist > outerRange || dist < innerRange) return;

        var player = ctx.Runtime.PlayerTarget.GetComponent<PlayerController>();
        if (player == null) return;

        int dmg = Mathf.Max(1, (int)(ctx.Config.stat.attackPower * Data.damageMultiplier));
        player.TakeDamage(dmg);

        // 위로 + 보스에서 멀어지는 방향으로 발사
        Vector3 outDir = playerPos - bossPos;
        outDir.y = 0f;
        if (outDir.sqrMagnitude > 0.001f) outDir.Normalize();

        Vector3 launchVel = outDir * Data.launchOutForce + Vector3.up * Data.launchUpForce;
        if (player.Rigid != null)
            player.Rigid.linearVelocity = launchVel;

        player.ApplyKnockback(Vector3.zero, Data.launchAirTime);
    }

    // ── 경고 장판 ─────────────────────────────────────────
    private void SpawnWarningDisc(MonsterContext ctx, int hitIndex)
    {
        if (Data.warningDiscPrefab == null) return;

        // 1타: 0에서 targetScale로 확장
        // 2·3타: 1타(scale0)에서 targetScaleN으로 확장 → 이전 링 바깥부터 보이게
        float startS  = hitIndex > 0 ? Data.GetHitVfxScale(0) : 0f;
        float targetS = Data.GetHitVfxScale(hitIndex);
        _warningStartScale  = new Vector3(startS,  1f, startS);
        _warningTargetScale = new Vector3(targetS, 1f, targetS);

        Vector3 pos = ctx.Transform.position;
        pos.y += 0.02f;
        _warningGO = Object.Instantiate(Data.warningDiscPrefab, pos, Quaternion.identity);
        _warningGO.transform.localScale = _warningStartScale;
    }

    private void DespawnWarning()
    {
        if (_warningGO == null) return;
        Object.Destroy(_warningGO);
        _warningGO = null;
    }

    // ── 임팩트 VFX ────────────────────────────────────────
    private void SpawnSlamVfx(MonsterContext ctx, int hitIndex)
    {
        if (Data.slamVfxPrefab == null) return;

        float s     = Data.GetHitVfxScale(hitIndex);
        var   vfxGO = Object.Instantiate(Data.slamVfxPrefab, ctx.Transform.position, ctx.Transform.rotation);
        vfxGO.transform.localScale = new Vector3(s, s, s);

        if (vfxGO.TryGetComponent<PlayableVfx>(out var pvfx))
            pvfx.Play();

        float lifetime = Data.vfxLifetime;
        if (vfxGO.TryGetComponent<ParticleSystem>(out var ps))
            lifetime = ps.main.duration + ps.main.startLifetime.constantMax;
        Object.Destroy(vfxGO, lifetime);
    }

    // ── 유틸 ──────────────────────────────────────────────
    private static void FacePlayer(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return;
        Vector3 dir = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.001f)
            ctx.Transform.rotation = Quaternion.LookRotation(dir);
    }

    private static void PlayAnim(MonsterContext ctx, string stateName, float crossFade)
    {
        if (ctx.Animator == null) return;
        if (!ctx.Animator.HasState(0, Animator.StringToHash(stateName)))
        {
            Debug.LogWarning($"[FGGroundSlash] Animator state not found: '{stateName}'", ctx.Monster);
            return;
        }
        ctx.Animator.CrossFade(stateName, crossFade, 0, 0f);
    }

    private static float SpeedMult(MonsterContext ctx)
    {
        var fg = ctx.Monster as ForestGuardianMonster;
        return fg?.FGBlackboard.AnimSpeedMult ?? 1f;
    }
}
}
