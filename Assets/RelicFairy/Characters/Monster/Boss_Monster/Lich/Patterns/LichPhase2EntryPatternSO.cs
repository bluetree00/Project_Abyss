using RelicFairy.UI;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 리치 Phase 2 진입 패턴 — 강제 발동 (forceExecute=true).
///
/// HP ≤ 40% 도달 시 BossPatternRunner가 강제 인터럽트.
/// 흐름: 해방 연출(entryDuration) → AoE 폭발(60% 시점) → Phase2 버프 적용 → ChaseState
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Boss/Lich/Lich_Phase2EntryPattern", fileName = "Lich_Phase2EntryPattern")]
public class LichPhase2EntryPatternSO : BossPatternSO
{
    [Header("Phase2 Entry — Timing")]
    [Tooltip("해방 연출 총 시간 (초)")]
    public float entryDuration = 2.0f;
    [Tooltip("복귀 대기 시간 (초)")]
    public float recoveryDuration = 0.5f;
    [Tooltip("entryDuration 중 AoE 폭발이 발동하는 비율 (0~1)")]
    [Range(0f, 1f)]
    public float blastTiming = 0.6f;

    [Header("Phase2 Entry — AoE")]
    [Tooltip("폭발 판정 반경 (m)")]
    public float blastRadius = 10f;
    [Tooltip("기본 attackPower에 곱할 배율")]
    public float damageMultiplier = 2.0f;
    [Tooltip("넉백 힘 배율")]
    public float knockbackMultiplier = 3.0f;

    // ── 런타임 ───────────────────────────────────────────
    private LichPhase2EntryState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new LichPhase2EntryState(this);
    public override void OnRecycled()                       => _state = new LichPhase2EntryState(this);

    // forceExecute 전용 — 항상 실행·인터럽트 가능
    public override bool CanExecute(BossPatternContext ctx)       => true;
    public override bool CanForceInterrupt(BossPatternContext ctx) => true;

    public override SpecialStateBase GetRuntimeState() => _state;
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// LichPhase2EntryState — FullLock (이동 + 중단 불가)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class LichPhase2EntryState : FullLockState<LichPhase2EntryPatternSO>
{
    private enum Phase { Entry, Recovery }

    private Phase      _phase;
    private float      _timer;
    private bool       _hasBlasted;
    private bool       _phase2Applied;
    private float      _blastThreshold;
    private GameObject _aoeGuide;

    public LichPhase2EntryState(LichPhase2EntryPatternSO data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        _phase         = Phase.Entry;
        _timer         = 0f;
        _hasBlasted    = false;
        _phase2Applied = false;
        _blastThreshold = Data.entryDuration * Data.blastTiming;

        ctx.Animator?.CrossFade("Phase2Entry", 0.1f);

        var mc = (ctx.Monster as LichMonster)?.MovementController;
        mc?.RequestMovementState(LichMovementState.IdleHover);
        mc?.SetLocked(true);

        UI_BossBark.Show("봉인 해제!", BossBarkType.Bark);

        // 폭발 범위 disc — 노란색으로 선경고, blast 시점에 빨간색으로 전환
        _aoeGuide = PatternGuideHelper.Disc(
            ctx.Transform.position,
            Data.blastRadius,
            PatternGuideHelper.Telegraph);

        if (Data.effectPrefab != null)
        {
            var go = Object.Instantiate(Data.effectPrefab, ctx.Transform.position, Quaternion.identity);
            Object.Destroy(go, Data.entryDuration + 1f);
        }

        Debug.Log("[Lich] Phase 2 진입 패턴 시작", ctx.Monster);
    }

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime;

        if (_phase == Phase.Entry)
        {
            if (!_hasBlasted && _timer >= _blastThreshold)
            {
                _hasBlasted = true;
                PatternGuideHelper.SetColor(_aoeGuide, PatternGuideHelper.Active);
                BlastAoE(ctx);
            }

            if (_timer >= Data.entryDuration)
            {
                if (!_phase2Applied)
                {
                    _phase2Applied = true;
                    (ctx.Monster as LichMonster)?.ApplyPhase2Buffs();
                }
                _phase = Phase.Recovery;
                _timer = 0f;
            }
        }
        else
        {
            if (_timer >= Data.recoveryDuration)
                ctx.Monster.ChangeState<ChaseState>();
        }
    }

    public override void Exit(MonsterContext ctx)
    {
        PatternGuideHelper.SafeDestroy(ref _aoeGuide);
        (ctx.Monster as LichMonster)?.MovementController?.SetLocked(false);
    }

    private void BlastAoE(MonsterContext ctx)
    {
        if (ctx.Config?.stat == null || ctx.Runtime.PlayerTarget == null) return;

        float dist = Vector3.Distance(ctx.Transform.position, ctx.Runtime.PlayerTarget.position);
        if (dist > Data.blastRadius) return;

        var player = ctx.Runtime.PlayerTarget.GetComponent<PlayerController>();
        if (player == null) return;

        int dmg = Mathf.Max(1, (int)(ctx.Config.stat.attackPower * Data.damageMultiplier));
        player.TakeDamage(dmg);

        Vector3 dir = (ctx.Runtime.PlayerTarget.position - ctx.Transform.position).normalized;
        dir.y = 0.5f;
        if (dir.sqrMagnitude > 0.001f) dir.Normalize();
        player.ApplyKnockback(dir * ctx.Config.stat.knockbackForce * Data.knockbackMultiplier);
    }
}
}
