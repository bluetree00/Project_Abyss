using RelicFairy.UI;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 리치 원소 격류 (Arcane Torrent) 패턴 — Phase 2 시그니처.
///
/// 컨셉: 대마법사의 힘을 흡수한 리치가 공중에서 채널링하며, 원소 마력의 격류를
///       부채꼴로 휩쓴다. "레이저"가 아니라 흐르는 마법 기류 — 바닥 평면을 따라
///       회전하는 격류 라인 위로 마력 모트(보라 구체)가 흘러나가는 연출로 표현한다.
///
/// 흐름: 상승(DeathRayStart) → 차징 예고 → 회전 격류 발사(DeathRayLoop)
///       → 강하(DeathRayEnd) → 복귀
///
/// UX: 회전하는 기류의 안전 구간을 따라 계속 이동해 회피(완급 최고압·공간 통제).
///     긴 차징 예고(노랑)로 공정성을 확보하고, 발사 구간만 빨강 판정으로 전환한다.
///
/// 가이드/VFX: 텔레그래프는 바닥 빔(플레이어가 회피하는 평면)에 표시한다.
///             chargeVfxPrefab/beamVfxPrefab을 비워두면 가이드(프리미티브/데칼)만 표시되며,
///             추후 이 가이드 위치·방향에 맞춰 정식 VFX를 꽂는다.
///
/// 액션별 애니: 이미 배선된 DeathRayStart / DeathRayLoop / DeathRayEnd 재사용.
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Boss/Lich/Lich_ArcaneTorrentPattern", fileName = "Lich_ArcaneTorrentPattern")]
public class LichArcaneTorrentPatternSO : BossPatternSO
{
    [Header("Arcane Torrent — Range")]
    [Tooltip("패턴 발동 최대 거리 (m). 격류가 실제 닿도록 beamRange 이내로 둔다.")]
    public float maxTriggerRange = 16f;

    [Header("Arcane Torrent — Timing")]
    [Tooltip("고도 상승 시간 (초)")]
    public float riseDuration = 0.8f;
    [Tooltip("격류 발사 전 차징 예고 시간 (초) — 길수록 공정")]
    public float chargeDuration = 1.2f;
    [Tooltip("회전 격류 지속 시간 (초) — 회전 속도를 결정")]
    public float sweepDuration = 2.5f;
    [Tooltip("강하·복귀 시간 (초)")]
    public float descendDuration = 0.6f;
    [Tooltip("복귀(반격 창) 대기 시간 (초)")]
    public float recoveryDuration = 1.0f;

    [Header("Arcane Torrent — Beam")]
    [Tooltip("격류 사정거리 (m)")]
    public float beamRange = 16f;
    [Tooltip("격류 폭 (m)")]
    public float beamWidth = 1.2f;
    [Tooltip("격류 회전 총 각도 (도)")]
    public float sweepArc = 200f;

    [Header("Arcane Torrent — Magic Flow VFX")]
    [Tooltip("마력 모트 방출 간격 (초). 0이면 미사용.")]
    public float flowEmitInterval = 0.06f;
    [Tooltip("모트 1회당 개수 (격류 길이를 따라 산포)")]
    public int flowMotesPerEmit = 2;
    [Tooltip("모트 반경 (m)")]
    public float flowMoteRadius = 0.25f;
    [Tooltip("모트 잔존 시간 (초)")]
    public float flowMoteLifetime = 0.3f;

    [Header("Arcane Torrent — VFX 훅 (추후 가이드에 맞춰 적용)")]
    [Tooltip("차징~발사 동안 보스 시전 지점에 스폰할 VFX. null이면 미사용.")]
    public GameObject chargeVfxPrefab;
    [Tooltip("발사 중 바닥 격류를 따라 표시할 빔 VFX. 매 프레임 격류 방향으로 갱신. null이면 미사용.")]
    public GameObject beamVfxPrefab;

    [Header("Arcane Torrent — Damage")]
    [Tooltip("공격력 대비 틱 데미지 배율")]
    public float damageMultiplier = 0.6f;
    [Tooltip("격류 안에 있는 동안 데미지 틱 간격 (초)")]
    public float tickInterval = 0.4f;
    [Tooltip("틱당 넉백 배율. 0이면 넉백 없음.")]
    public float knockbackMultiplier = 1.0f;

    [Header("Arcane Torrent — Cooldown")]
    [Tooltip("패턴 완료 후 재사용 대기 시간 (초)")]
    public float patternCooldown = 22f;

    // ── 런타임 ───────────────────────────────────────────
    private LichArcaneTorrentState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new LichArcaneTorrentState(this);
    public override void OnRecycled()                       => _state = new LichArcaneTorrentState(this);

    public override bool CanExecute(BossPatternContext ctx)
    {
        if (ctx.Ctx.Runtime.PlayerTarget == null) return false;
        var lichBB = (ctx.Boss as LichMonster)?.LichBB;
        if (lichBB == null || !lichBB.IsPhase2) return false;
        if (lichBB.ArcaneTorrentCooldown > 0f) return false;
        return Vector3.Distance(ctx.Ctx.Transform.position, ctx.Ctx.Runtime.PlayerTarget.position) <= maxTriggerRange;
    }

    public override SpecialStateBase GetRuntimeState() => _state;
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// LichArcaneTorrentState — UnInterruptible (이동 잠금, 제자리 회전)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class LichArcaneTorrentState : UnInterruptibleState<LichArcaneTorrentPatternSO>
{
    private enum Phase { Rise, Charge, Sweep, Descend, Recovery }

    private Phase      _phase;
    private float      _timer;
    private float      _groundY;    // 격류 바닥 평면 Y
    private float      _beamAngle;  // 현재 격류 yaw (도)
    private float      _sweepDir;   // +1 / -1
    private float      _tickTimer;
    private float      _flowTimer;
    private GameObject _beam;       // 텔레그래프 가이드
    private GameObject _chargeVfx;  // 시전 VFX 인스턴스
    private GameObject _beamVfx;    // 빔 VFX 인스턴스

    public LichArcaneTorrentState(LichArcaneTorrentPatternSO data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        _phase     = Phase.Rise;
        _timer     = 0f;
        _tickTimer = 0f;
        _flowTimer = 0f;
        _groundY   = (ctx.Monster as LichMonster)?.SpawnGroundY ?? ctx.Transform.position.y;

        ctx.Animator?.CrossFade("DeathRayStart", 0.1f);

        var mc = (ctx.Monster as LichMonster)?.MovementController;
        mc?.RequestMovementState(LichMovementState.AltitudeRise);
        mc?.SetLocked(true);

        UI_BossBark.Show("원소의 격류가 너를 삼킨다!", BossBarkType.PatternAnnounce);
    }

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime;

        switch (_phase)
        {
            case Phase.Rise:
                FacePlayer(ctx);
                if (_timer >= Data.riseDuration) BeginCharge(ctx);
                break;

            case Phase.Charge:
                UpdateBeamVisual(ctx);
                UpdateChargeVfx(ctx);
                if (_timer >= Data.chargeDuration) BeginSweep(ctx);
                break;

            case Phase.Sweep:
                _beamAngle += (Data.sweepArc / Mathf.Max(0.01f, Data.sweepDuration)) * _sweepDir * Time.deltaTime;
                AimBossToBeam(ctx);
                UpdateBeamVisual(ctx);
                UpdateChargeVfx(ctx);
                UpdateBeamVfx(ctx);
                EmitFlowMotes(ctx);
                TickBeamDamage(ctx);
                if (_timer >= Data.sweepDuration) BeginDescend(ctx);
                break;

            case Phase.Descend:
                if (_timer >= Data.descendDuration) { _phase = Phase.Recovery; _timer = 0f; }
                break;

            case Phase.Recovery:
                if (_timer >= Data.recoveryDuration)
                    ctx.Monster.ChangeState<ChaseState>();
                break;
        }
    }

    public override void Exit(MonsterContext ctx)
    {
        PatternGuideHelper.SafeDestroy(ref _beam);
        DestroyVfx(ref _chargeVfx);
        DestroyVfx(ref _beamVfx);

        var mc = (ctx.Monster as LichMonster)?.MovementController;
        mc?.RequestMovementState(LichMovementState.AltitudeDescend);
        mc?.SetLocked(false);

        var lich = ctx.Monster as LichMonster;
        if (lich?.LichBB != null)
            lich.LichBB.ArcaneTorrentCooldown = Data.patternCooldown;
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 단계 전이
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private void BeginCharge(MonsterContext ctx)
    {
        _phase    = Phase.Charge;
        _timer    = 0f;
        _sweepDir = Random.value > 0.5f ? 1f : -1f;

        // 시작각: 플레이어 yaw에서 스윕 반대편으로 오프셋 → 격류가 플레이어 위치를 가로지른다.
        _beamAngle = YawToPlayer(ctx) - _sweepDir * (Data.sweepArc * 0.5f);

        _beam = PatternGuideHelper.Beam(
            BeamOrigin(ctx), BeamDir(), Data.beamRange, Data.beamWidth, PatternGuideHelper.Telegraph);
        AimBossToBeam(ctx);
        UpdateBeamVisual(ctx);

        // 시전 VFX 훅 — 보스 시전 지점(공중)에 스폰.
        if (Data.chargeVfxPrefab != null && _chargeVfx == null)
            _chargeVfx = Object.Instantiate(Data.chargeVfxPrefab, CastPoint(ctx), ctx.Transform.rotation);
    }

    private void BeginSweep(MonsterContext ctx)
    {
        _phase     = Phase.Sweep;
        _timer     = 0f;
        _tickTimer = 0f;
        _flowTimer = 0f;
        ctx.Animator?.CrossFade("DeathRayLoop", 0.1f);
        PatternGuideHelper.SetColor(_beam, PatternGuideHelper.Active);

        // 빔 VFX 훅 — 바닥 격류를 따라 스폰.
        if (Data.beamVfxPrefab != null && _beamVfx == null)
            _beamVfx = Object.Instantiate(Data.beamVfxPrefab, BeamOrigin(ctx), Quaternion.LookRotation(BeamDir()));
    }

    private void BeginDescend(MonsterContext ctx)
    {
        _phase = Phase.Descend;
        _timer = 0f;
        ctx.Animator?.CrossFade("DeathRayEnd", 0.1f);
        PatternGuideHelper.SafeDestroy(ref _beam);
        DestroyVfx(ref _chargeVfx);
        DestroyVfx(ref _beamVfx);
        (ctx.Monster as LichMonster)?.MovementController?
            .RequestMovementState(LichMovementState.AltitudeDescend);
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 격류 기하 / 비주얼
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    /// <summary>보스 시전 지점(공중) — 채널링 VFX 기준.</summary>
    private Vector3 CastPoint(MonsterContext ctx) => ctx.Transform.position + Vector3.up * 1.2f;

    /// <summary>격류 시작점 — 보스 XZ를 바닥 평면에 투영(플레이어가 회피하는 평면).</summary>
    private Vector3 BeamOrigin(MonsterContext ctx)
    {
        Vector3 p = ctx.Transform.position;
        return new Vector3(p.x, _groundY, p.z);
    }

    private Vector3 BeamDir()
    {
        Vector3 d = Quaternion.Euler(0f, _beamAngle, 0f) * Vector3.forward;
        d.y = 0f;
        return d.sqrMagnitude > 0.001f ? d.normalized : Vector3.forward;
    }

    private void UpdateBeamVisual(MonsterContext ctx)
    {
        if (_beam == null) return;
        Vector3 origin = BeamOrigin(ctx);
        Vector3 dir    = BeamDir();
        _beam.transform.position   = origin + dir * (Data.beamRange * 0.5f) + Vector3.up * 0.04f;
        _beam.transform.rotation   = Quaternion.LookRotation(Vector3.up, dir);
        _beam.transform.localScale = new Vector3(Data.beamWidth, Data.beamRange, 1f);
    }

    private void UpdateChargeVfx(MonsterContext ctx)
    {
        if (_chargeVfx == null) return;
        _chargeVfx.transform.position = CastPoint(ctx);
        _chargeVfx.transform.rotation = ctx.Transform.rotation;
    }

    private void UpdateBeamVfx(MonsterContext ctx)
    {
        if (_beamVfx == null) return;
        _beamVfx.transform.position = BeamOrigin(ctx);
        _beamVfx.transform.rotation = Quaternion.LookRotation(BeamDir());
    }

    private void AimBossToBeam(MonsterContext ctx)
    {
        Vector3 dir = BeamDir();
        if (dir.sqrMagnitude > 0.001f)
            ctx.Transform.rotation = Quaternion.LookRotation(dir);
    }

    /// <summary>격류 라인을 따라 마력 모트를 방출 — 레이저가 아닌 "흐르는 마법 기류" 연출.</summary>
    private void EmitFlowMotes(MonsterContext ctx)
    {
        if (Data.flowEmitInterval <= 0f) return;
        _flowTimer += Time.deltaTime;
        if (_flowTimer < Data.flowEmitInterval) return;
        _flowTimer = 0f;

        Vector3 origin = BeamOrigin(ctx);
        Vector3 dir    = BeamDir();
        int count = Mathf.Max(1, Data.flowMotesPerEmit);
        for (int i = 0; i < count; i++)
        {
            float along = Random.Range(2f, Data.beamRange);
            Vector3 pos = origin + dir * along + Vector3.up * 0.3f;
            PatternGuideHelper.Sphere(pos, Data.flowMoteRadius, PatternGuideHelper.Summon,
                lifetime: Data.flowMoteLifetime);
        }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 판정
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private void TickBeamDamage(MonsterContext ctx)
    {
        if (ctx.Config?.stat == null || ctx.Runtime.PlayerTarget == null) return;

        _tickTimer += Time.deltaTime;
        if (_tickTimer < Data.tickInterval) return;

        Vector3 toPlayer = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        toPlayer.y = 0f;
        float dist = toPlayer.magnitude;
        if (dist > Data.beamRange || dist < 0.01f) return;

        // 거리에 따른 격류 폭의 각도 환산 — 가까울수록 넓게 판정.
        float halfWidthAngle = Mathf.Atan2(Data.beamWidth * 0.5f, dist) * Mathf.Rad2Deg;
        if (Vector3.Angle(BeamDir(), toPlayer) > halfWidthAngle) return;

        _tickTimer = 0f;

        var player = ctx.Runtime.PlayerTarget.GetComponent<PlayerController>();
        if (player == null) return;

        int dmg = Mathf.Max(1, (int)(ctx.Config.stat.attackPower * Data.damageMultiplier));
        player.TakeDamage(dmg);

        if (Data.knockbackMultiplier > 0f)
        {
            Vector3 kb = toPlayer.normalized;
            kb.y = 0.2f;
            if (kb.sqrMagnitude > 0.001f) kb.Normalize();
            player.ApplyKnockback(kb * ctx.Config.stat.knockbackForce * Data.knockbackMultiplier);
        }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 헬퍼
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private static void DestroyVfx(ref GameObject go)
    {
        if (go == null) return;
        Object.Destroy(go);
        go = null;
    }

    private static float YawToPlayer(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return ctx.Transform.eulerAngles.y;
        Vector3 d = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        d.y = 0f;
        if (d.sqrMagnitude < 0.001f) return ctx.Transform.eulerAngles.y;
        return Quaternion.LookRotation(d).eulerAngles.y;
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
