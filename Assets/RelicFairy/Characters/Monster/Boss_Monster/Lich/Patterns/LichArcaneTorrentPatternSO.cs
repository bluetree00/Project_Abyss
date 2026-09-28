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

    private const float FlowBurstInterval = 0.12f;
    private const float FlowBurstScale    = 0.6f;

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
    private Transform  _beamFrom;   // 격류 광선 두 끝(LichVfx.PlayBeamTracked가 매 프레임 따라간다)
    private Transform  _beamTo;
    private bool       _signaled;

    public LichArcaneTorrentState(LichArcaneTorrentPatternSO data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        _phase     = Phase.Rise;
        _timer     = 0f;
        _tickTimer = 0f;
        _flowTimer = 0f;
        _groundY   = (ctx.Monster as LichMonster)?.SpawnGroundY ?? ctx.Transform.position.y;
        _signaled  = false;

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
                // 보라가 차오르고 발사 0.15초 전 빨강(리치 예고 규약).
                LichPatternUtil.TickTelegraph(_beam, _timer, Data.chargeDuration, 0.15f, ref _signaled);
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
        DestroyBeamEnds();

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

        _beam = LichPatternUtil.PrepareTelegraph(
            PatternGuideHelper.Beam(BeamOrigin(ctx), BeamDir(), Data.beamRange, Data.beamWidth, LichPatternUtil.Arcane),
            LichPatternUtil.Arcane);
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
        PatternGuideHelper.SetColor(_beam, LichPatternUtil.Lethal);
        LichPatternUtil.Impact(LichImpact.Heavy);
        LichSfx.Play(LichSfxSlot.FireBeam, ctx.Transform.position);

        // 격류 광선 — 바닥 높이로 쓸고 돈다(두 끝을 매 프레임 옮긴다).
        _beamFrom = new GameObject("LichTorrent_From").transform;
        _beamTo   = new GameObject("LichTorrent_To").transform;
        UpdateBeamEnds(ctx);
        // 굵기 ×2 — 판정 폭(1.2 m) 그대로면 화면에서 가는 흰 선으로만 보였다(09-19 실측).
        LichVfx.PlayBeamTracked(LichVfxSlot.ArcaneBeam, _beamFrom, _beamTo, Data.beamWidth * 2f, Data.sweepDuration + 0.1f);

        // 빔 VFX 훅(에셋 지정 시) — 바닥 격류를 따라 스폰.
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
        DestroyBeamEnds();
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
        // 앞면이 위를 보게(PatternGuideHelper.Beam과 같은 회전) — 반대로 두면 뒷면 컬링으로 안 보인다(09-19 감사).
        _beam.transform.rotation   = Quaternion.LookRotation(Vector3.down, dir);
        _beam.transform.localScale = new Vector3(Data.beamWidth, Data.beamRange, 1f);
    }

    private void UpdateChargeVfx(MonsterContext ctx)
    {
        if (_chargeVfx == null) return;
        _chargeVfx.transform.position = CastPoint(ctx);
        _chargeVfx.transform.rotation = ctx.Transform.rotation;
    }

    /// <summary>격류 광선 두 끝 — 리치 앞 바닥에서 사정거리 끝까지, 바닥 살짝 위.</summary>
    private void UpdateBeamEnds(MonsterContext ctx)
    {
        if (_beamFrom == null || _beamTo == null) return;
        Vector3 origin = BeamOrigin(ctx) + Vector3.up * 0.4f;
        Vector3 dir    = BeamDir();
        _beamFrom.position = origin + dir * 0.8f;
        _beamTo.position   = origin + dir * Data.beamRange;
    }

    private void DestroyBeamEnds()
    {
        if (_beamFrom != null) Object.Destroy(_beamFrom.gameObject);
        if (_beamTo   != null) Object.Destroy(_beamTo.gameObject);
        _beamFrom = null;
        _beamTo   = null;
    }

    private void UpdateBeamVfx(MonsterContext ctx)
    {
        UpdateBeamEnds(ctx);
        if (_beamVfx == null) return;
        _beamVfx.transform.position = BeamOrigin(ctx);
        _beamVfx.transform.rotation = Quaternion.LookRotation(BeamDir());
    }

    private void AimBossToBeam(MonsterContext ctx)
    {
        // 몸 방향을 격류에 쥐어 준다 — 이동 컨트롤러가 플레이어 쪽으로 되돌리지 않게.
        LichPatternUtil.HoldFacing(ctx, _beamAngle, 3600f);
    }

    /// <summary>격류 라인을 따라 마력 모트를 방출 — 레이저가 아닌 "흐르는 마법 기류" 연출.</summary>
    private void EmitFlowMotes(MonsterContext ctx)
    {
        if (Data.flowEmitInterval <= 0f) return;
        _flowTimer += Time.deltaTime;
        if (_flowTimer < FlowBurstInterval) return;
        _flowTimer = 0f;

        // 격류를 따라 터지는 보라 별 — 기본 도형 구체(회색 모트) 대신 실제 이펙트(09-19). 초당 약 8개로 제한.
        Vector3 origin = BeamOrigin(ctx);
        Vector3 dir    = BeamDir();
        float along = Random.Range(2f, Data.beamRange);
        LichVfx.Play(LichVfxSlot.DarkRainImpact, origin + dir * along + Vector3.up * 0.2f, Quaternion.identity, FlowBurstScale);
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 판정
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private void TickBeamDamage(MonsterContext ctx)
    {
        if (ctx.Config?.stat == null || ctx.Runtime.PlayerTarget == null) return;

        _tickTimer += Time.deltaTime;
        if (_tickTimer < Data.tickInterval) return;

        // 바닥 직선(사정거리 × 폭) — 공용 판정이라 회피 무적이면 맞지 않고 넉백도 없다.
        if (LichPatternUtil.HitBeam(ctx, BeamOrigin(ctx), BeamDir(), Data.beamRange, Data.beamWidth * 0.5f,
                                    Data.damageMultiplier, Data.knockbackMultiplier))
        {
            _tickTimer = 0f;
            LichPatternUtil.Impact(LichImpact.Light, true);
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
