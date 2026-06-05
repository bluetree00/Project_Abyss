using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 리치 삼연 낫무 (Reaper's Cadence) 패턴 — Phase 2 일반 공격.
///
/// 흐름: 접근(DashClose) → 빠른 좁은 호 2연타 → 페인트 홀드 → 광각 딜레이드 피니셔 → 복귀
///
/// UX: 빠른 2타에 익숙해진 플레이어가 반격하러 붙는 순간, 한 박자 늦게 들어오는
///     광각 3타(피니셔)로 탐욕을 응징한다. 정답은 2타 후 바로 붙지 말고
///     피니셔 빨강을 보고 회피 → 그 후 반격(진짜 응징 창은 Recovery).
///
/// 액션별 애니: Sweep1=ScytheCombo1, Sweep2=ScytheCombo2, Finisher=ScytheCombo3
///             (각각 Anim_Fly_Attack_01/02/03 배선).
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Boss/Lich/Lich_ReaperCadencePattern", fileName = "Lich_ReaperCadencePattern")]
public class LichReaperCadencePatternSO : BossPatternSO
{
    [Header("Reaper Cadence — Range")]
    [Tooltip("패턴 발동 최소 거리 (근접 전용)")]
    public float minTriggerRange = 2.5f;
    [Tooltip("패턴 발동 최대 거리 (근접 전용 — DashClose 도달 범위 내로 제한)")]
    public float maxTriggerRange = 8f;
    [Tooltip("접근 종료 거리 — 이 안에 들면 즉시 1타로 전환")]
    public float approachStopRange = 3f;

    [Header("Reaper Cadence — Timing")]
    [Tooltip("DashClose 접근 대기 시간 (초)")]
    public float approachDuration = 0.7f;
    [Tooltip("1·2타: 단계 진입 후 타격 프레임까지 선딜 (클립 임팩트에 맞춤)")]
    public float sweepHitDelay = 0.4f;
    [Tooltip("1·2타 단계 총 길이 (초). Attack 클립(60·70프레임)이 읽히도록 ~1s.")]
    public float sweepDuration = 1.0f;
    [Tooltip("1타와 2타 사이 간격 (초)")]
    public float gapDuration = 0.25f;
    [Tooltip("페인트 홀드 시간 (초). 길수록 탐욕 응징↑ (0.5~0.9 권장)")]
    public float feintHoldDuration = 0.7f;
    [Tooltip("피니셔 타격 선딜 (초)")]
    public float finisherHitDelay = 0.55f;
    [Tooltip("피니셔 단계 총 길이 (초). 큰 스윙이 끝까지 읽히도록 길게.")]
    public float finisherDuration = 1.3f;
    [Tooltip("복귀(반격 창) 대기 시간 (초)")]
    public float recoveryDuration = 0.7f;

    [Header("Reaper Cadence — Hit")]
    [Tooltip("윈드업 중 추적 각속도(도/초). 타격 시 방향 고정 → 측면 회피 가능.")]
    public float windupTrackSpeed = 240f;
    [Tooltip("1·2타 판정 반경 (m)")]
    public float sweepRadius = 4f;
    [Tooltip("1·2타 전방 호 반각 (도)")]
    public float sweepHalfAngle = 60f;
    [Tooltip("피니셔 반경 배율")]
    public float finisherRadiusMult = 1.4f;
    [Tooltip("피니셔 전방 호 반각 (도)")]
    public float finisherHalfAngle = 120f;

    [Header("Reaper Cadence — Damage")]
    [Tooltip("1·2타 데미지 배율")]
    public float sweepDamageMult = 1.0f;
    [Tooltip("피니셔 데미지 배율")]
    public float finisherDamageMult = 2.0f;
    [Tooltip("1·2타 넉백 배율")]
    public float sweepKnockbackMult = 1.2f;
    [Tooltip("피니셔 넉백 배율")]
    public float finisherKnockbackMult = 2.5f;

    [Header("Reaper Cadence — Cooldown")]
    [Tooltip("패턴 완료 후 재사용 대기 시간 (초)")]
    public float patternCooldown = 11f;

    [Header("Reaper Cadence — VFX (추후 가이드에 맞춰 적용)")]
    [Tooltip("1·2타 임팩트 시 보스 위치·전방으로 스폰할 베기 VFX. null이면 미사용.")]
    public GameObject slashVfxPrefab;
    [Tooltip("피니셔 임팩트 전용 VFX. null이면 slashVfxPrefab으로 폴백.")]
    public GameObject finisherVfxPrefab;
    [Tooltip("스폰한 VFX 자동 파괴 시간 (초)")]
    public float vfxLifetime = 2f;

    // ── 런타임 ───────────────────────────────────────────
    private LichReaperCadenceState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new LichReaperCadenceState(this);
    public override void OnRecycled()                       => _state = new LichReaperCadenceState(this);

    public override bool CanExecute(BossPatternContext ctx)
    {
        if (ctx.Ctx.Runtime.PlayerTarget == null) return false;
        var lichBB = (ctx.Boss as LichMonster)?.LichBB;
        if (lichBB == null || !lichBB.IsPhase2) return false;
        if (lichBB.ReaperCadenceCooldown > 0f) return false;
        float dist = Vector3.Distance(ctx.Ctx.Transform.position, ctx.Ctx.Runtime.PlayerTarget.position);
        return dist >= minTriggerRange && dist <= maxTriggerRange;
    }

    public override SpecialStateBase GetRuntimeState() => _state;
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// LichReaperCadenceState — UnInterruptible (이동 자유)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class LichReaperCadenceState : UnInterruptibleState<LichReaperCadencePatternSO>
{
    private enum Phase { Approach, Sweep1, Gap, Sweep2, FeintHold, Finisher, Recovery }

    private Phase      _phase;
    private float      _timer;
    private bool       _hitDone;
    private GameObject _guide;

    public LichReaperCadenceState(LichReaperCadencePatternSO data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        _phase   = Phase.Approach;
        _timer   = 0f;
        _hitDone = false;

        var mc = (ctx.Monster as LichMonster)?.MovementController;
        mc?.RequestMovementState(LichMovementState.DashClose);
        mc?.SetLocked(true);

        // Approach 시작 시 Telegraph disc — 돌진 중 보스를 따라오며 Active로 전환
        _guide = PatternGuideHelper.Disc(
            ctx.Transform.position, Data.sweepRadius, PatternGuideHelper.Telegraph);
    }

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime;
        FollowGuide(ctx);

        switch (_phase)
        {
            case Phase.Approach:
            {
                float d = ctx.Runtime.PlayerTarget != null
                    ? Vector3.Distance(ctx.Transform.position, ctx.Runtime.PlayerTarget.position)
                    : 999f;
                if (d <= Data.approachStopRange || _timer >= Data.approachDuration)
                {
                    (ctx.Monster as LichMonster)?.MovementController?
                        .RequestMovementState(LichMovementState.IdleHover);
                    BeginSwing(ctx, Phase.Sweep1, "ScytheCombo1");
                }
                break;
            }

            case Phase.Sweep1:
                if (!_hitDone) FaceTracking(ctx, Data.windupTrackSpeed);
                TrySweepHit(ctx, Data.sweepHitDelay, Data.sweepRadius, Data.sweepHalfAngle,
                            Data.sweepDamageMult, Data.sweepKnockbackMult, Data.slashVfxPrefab);
                if (_timer >= Data.sweepDuration)
                {
                    PatternGuideHelper.SetColor(_guide, PatternGuideHelper.Telegraph);
                    _phase = Phase.Gap;
                    _timer = 0f;
                }
                break;

            case Phase.Gap:
                if (_timer >= Data.gapDuration)
                    BeginSwing(ctx, Phase.Sweep2, "ScytheCombo2");
                break;

            case Phase.Sweep2:
                if (!_hitDone) FaceTracking(ctx, Data.windupTrackSpeed);
                TrySweepHit(ctx, Data.sweepHitDelay, Data.sweepRadius, Data.sweepHalfAngle,
                            Data.sweepDamageMult, Data.sweepKnockbackMult, Data.slashVfxPrefab);
                if (_timer >= Data.sweepDuration)
                    EnterFeint(ctx);
                break;

            case Phase.FeintHold:
                FacePlayerSlow(ctx);
                if (_timer >= Data.feintHoldDuration)
                    BeginSwing(ctx, Phase.Finisher, "ScytheCombo3");
                break;

            case Phase.Finisher:
                TrySweepHit(ctx, Data.finisherHitDelay, Data.sweepRadius * Data.finisherRadiusMult,
                            Data.finisherHalfAngle, Data.finisherDamageMult, Data.finisherKnockbackMult,
                            Data.finisherVfxPrefab != null ? Data.finisherVfxPrefab : Data.slashVfxPrefab);
                if (_timer >= Data.finisherDuration)
                {
                    _phase = Phase.Recovery;
                    _timer = 0f;
                }
                break;

            case Phase.Recovery:
                if (_timer >= Data.recoveryDuration)
                    ctx.Monster.ChangeState<ChaseState>();
                break;
        }
    }

    public override void Exit(MonsterContext ctx)
    {
        PatternGuideHelper.SafeDestroy(ref _guide);
        (ctx.Monster as LichMonster)?.MovementController?.SetLocked(false);

        var lich = ctx.Monster as LichMonster;
        if (lich?.LichBB != null)
            lich.LichBB.ReaperCadenceCooldown = Data.patternCooldown;
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 헬퍼
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private void BeginSwing(MonsterContext ctx, Phase phase, string clip)
    {
        _phase   = phase;
        _timer   = 0f;
        _hitDone = false;
        ctx.Animator?.CrossFade(clip, 0.1f);
    }

    private void EnterFeint(MonsterContext ctx)
    {
        _phase   = Phase.FeintHold;
        _timer   = 0f;
        _hitDone = false;
        // 피니셔 예고 — 더 큰 노란 disc로 교체해 광각 강타를 미리 알린다.
        PatternGuideHelper.SafeDestroy(ref _guide);
        _guide = PatternGuideHelper.Disc(
            ctx.Transform.position, Data.sweepRadius * Data.finisherRadiusMult,
            PatternGuideHelper.Telegraph);
    }

    private void TrySweepHit(MonsterContext ctx, float hitDelay, float radius, float halfAngle,
                             float dmgMult, float kbMult, GameObject vfx)
    {
        if (_hitDone || _timer < hitDelay) return;
        _hitDone = true;
        PatternGuideHelper.SetColor(_guide, PatternGuideHelper.Active);
        DealArc(ctx, radius, halfAngle, dmgMult, kbMult);

        // 추후 가이드(전방 호)에 맞춰 적용할 임팩트 VFX 훅 — 보스 위치·전방 기준 스폰.
        if (vfx != null)
        {
            var go = Object.Instantiate(vfx, ctx.Transform.position, ctx.Transform.rotation);
            if (Data.vfxLifetime > 0f) Object.Destroy(go, Data.vfxLifetime);
        }
    }

    private void FollowGuide(MonsterContext ctx)
    {
        if (_guide != null)
            _guide.transform.position = ctx.Transform.position;
    }

    private void DealArc(MonsterContext ctx, float radius, float halfAngle, float dmgMult, float kbMult)
    {
        if (ctx.Config?.stat == null || ctx.Runtime.PlayerTarget == null) return;

        Vector3 toPlayer = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        if (toPlayer.magnitude > radius) return;

        Vector3 flat = toPlayer;
        flat.y = 0f;
        if (flat.sqrMagnitude > 0.001f &&
            Vector3.Angle(ctx.Transform.forward, flat) > halfAngle)
            return;

        var player = ctx.Runtime.PlayerTarget.GetComponent<PlayerController>();
        if (player == null) return;

        int dmg = Mathf.Max(1, (int)(ctx.Config.stat.attackPower * dmgMult));
        player.TakeDamage(dmg);

        Vector3 dir = toPlayer.normalized;
        dir.y = 0.3f;
        if (dir.sqrMagnitude > 0.001f) dir.Normalize();
        player.ApplyKnockback(dir * ctx.Config.stat.knockbackForce * kbMult);
    }

    /// <summary>윈드업 중 각속도 제한 추적 — 타격 시점엔 호출하지 않아 방향이 고정된다.</summary>
    private static void FaceTracking(MonsterContext ctx, float degPerSec)
    {
        if (ctx.Runtime.PlayerTarget == null) return;
        Vector3 dir = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) return;
        ctx.Transform.rotation = Quaternion.RotateTowards(
            ctx.Transform.rotation, Quaternion.LookRotation(dir), degPerSec * Time.deltaTime);
    }

    private static void FacePlayerSlow(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return;
        Vector3 dir = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) return;
        ctx.Transform.rotation = Quaternion.Slerp(
            ctx.Transform.rotation, Quaternion.LookRotation(dir), 2f * Time.deltaTime);
    }
}
}
