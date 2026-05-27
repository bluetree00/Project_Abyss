using RelicFairy.UI;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 리치 섬광 타격 (Blink Strike) 패턴 — Phase 2 일반 공격.
///
/// 흐름: 3회 반복 [ 순간이동(플레이어 뒤) → 선딜(strikeDelay) → 타격 ]
///       → 복귀(recoveryDuration)
/// 각 연타 사이에는 blinkInterval 만큼 대기.
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Boss/Lich/Lich_BlinkStrikePattern", fileName = "Lich_BlinkStrikePattern")]
public class LichBlinkStrikePatternSO : BossPatternSO
{
    [Header("Blink Strike — Range")]
    [Tooltip("패턴 발동 최대 거리 (m)")]
    public float maxTriggerRange = 25f;

    [Header("Blink Strike — Timing")]
    [Tooltip("각 순간이동 후 타격까지 선딜 (초)")]
    public float strikeDelay = 0.2f;
    [Tooltip("타격 후 다음 순간이동까지 간격 (초)")]
    public float blinkInterval = 0.15f;
    [Tooltip("마지막 타격 후 복귀 시간 (초)")]
    public float recoveryDuration = 0.6f;

    [Header("Blink Strike — Blink")]
    [Tooltip("연타 횟수")]
    public int strikeCount = 3;
    [Tooltip("플레이어 뒤쪽 오프셋 거리 (m)")]
    public float backOffset = 1.8f;
    [Tooltip("순간이동 VFX 프리팹. null이면 이펙트 없음.")]
    public GameObject blinkVfxPrefab;

    [Header("Blink Strike — Damage")]
    [Tooltip("기본 attackPower에 곱할 배율 (타격당)")]
    public float damageMultiplier = 1.0f;
    [Tooltip("타격 판정 반경 (m)")]
    public float hitRadius = 2.0f;
    [Tooltip("넉백 힘 배율")]
    public float knockbackMultiplier = 1.5f;

    [Header("Blink Strike — Cooldown")]
    [Tooltip("패턴 완료 후 재사용 대기 시간 (초)")]
    public float patternCooldown = 9f;

    // ── 런타임 ───────────────────────────────────────────
    private LichBlinkStrikeState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new LichBlinkStrikeState(this);
    public override void OnRecycled()                       => _state = new LichBlinkStrikeState(this);

    public override bool CanExecute(BossPatternContext ctx)
    {
        if (ctx.Ctx.Runtime.PlayerTarget == null) return false;
        var lichBB = (ctx.Boss as LichMonster)?.LichBB;
        if (lichBB == null || !lichBB.IsPhase2) return false;
        if (lichBB.BlinkStrikeCooldown > 0f) return false;
        return Vector3.Distance(ctx.Ctx.Transform.position, ctx.Ctx.Runtime.PlayerTarget.position) <= maxTriggerRange;
    }

    public override SpecialStateBase GetRuntimeState() => _state;
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// LichBlinkStrikeState — UnInterruptible (이동 자유)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class LichBlinkStrikeState : UnInterruptibleState<LichBlinkStrikePatternSO>
{
    private enum Phase { Blink, StrikeDelay, Strike, BlinkInterval, Recovery }

    private Phase      _phase;
    private float      _timer;
    private int        _strikesDealt;
    private bool       _hasDealt;
    private GameObject _strikeGuide;

    public LichBlinkStrikeState(LichBlinkStrikePatternSO data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        _phase        = Phase.Blink;
        _timer        = 0f;
        _strikesDealt = 0;
        _hasDealt     = false;

        ctx.Animator?.CrossFade("BlinkStrike", 0.1f);

        UI_BossBark.Show("광속 타격!", BossBarkType.PatternAnnounce);

        (ctx.Monster as LichMonster)?.MovementController?.SetLocked(true);

        BlinkBehindPlayer(ctx);
        _phase = Phase.StrikeDelay;
    }

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime;

        switch (_phase)
        {
            case Phase.StrikeDelay:
                FacePlayer(ctx);
                if (_timer >= Data.strikeDelay)
                {
                    PatternGuideHelper.SetColor(_strikeGuide, PatternGuideHelper.Active);
                    _hasDealt = false;
                    _phase    = Phase.Strike;
                    _timer    = 0f;
                }
                break;

            case Phase.Strike:
                if (!_hasDealt)
                {
                    _hasDealt = true;
                    _strikesDealt++;
                    DealDamage(ctx);
                    PatternGuideHelper.SafeDestroy(ref _strikeGuide);
                }
                // 더 연타가 남았으면 BlinkInterval, 아니면 Recovery
                if (_strikesDealt < Data.strikeCount)
                {
                    _phase = Phase.BlinkInterval;
                    _timer = 0f;
                }
                else
                {
                    _phase = Phase.Recovery;
                    _timer = 0f;
                }
                break;

            case Phase.BlinkInterval:
                if (_timer >= Data.blinkInterval)
                {
                    BlinkBehindPlayer(ctx);
                    _phase = Phase.StrikeDelay;
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
        PatternGuideHelper.SafeDestroy(ref _strikeGuide);
        (ctx.Monster as LichMonster)?.MovementController?.SetLocked(false);

        var lich = ctx.Monster as LichMonster;
        if (lich?.LichBB != null)
            lich.LichBB.BlinkStrikeCooldown = Data.patternCooldown;
    }

    private void BlinkBehindPlayer(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return;

        Vector3 playerPos     = ctx.Runtime.PlayerTarget.position;
        Vector3 playerForward = ctx.Runtime.PlayerTarget.forward;
        Vector3 targetPos     = playerPos - playerForward * Data.backOffset;
        targetPos.y           = playerPos.y;

        SpawnVfx(ctx, ctx.Transform.position);
        ctx.Transform.position = targetPos;
        SpawnVfx(ctx, targetPos);

        PatternGuideHelper.SafeDestroy(ref _strikeGuide);
        _strikeGuide = PatternGuideHelper.Disc(targetPos, Data.hitRadius, PatternGuideHelper.Telegraph);
    }

    private void DealDamage(MonsterContext ctx)
    {
        if (ctx.Config?.stat == null || ctx.Runtime.PlayerTarget == null) return;

        float dist = Vector3.Distance(ctx.Transform.position, ctx.Runtime.PlayerTarget.position);
        if (dist > Data.hitRadius) return;

        var player = ctx.Runtime.PlayerTarget.GetComponent<PlayerController>();
        if (player == null) return;

        int dmg = Mathf.Max(1, (int)(ctx.Config.stat.attackPower * Data.damageMultiplier));
        player.TakeDamage(dmg);

        Vector3 dir = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        dir.y = 0.3f;
        if (dir.sqrMagnitude > 0.001f) dir.Normalize();
        player.ApplyKnockback(dir * ctx.Config.stat.knockbackForce * Data.knockbackMultiplier);
    }

    private void SpawnVfx(MonsterContext ctx, Vector3 pos)
    {
        if (Data.blinkVfxPrefab == null) return;
        var go = Object.Instantiate(Data.blinkVfxPrefab, pos, ctx.Transform.rotation);
        Object.Destroy(go, 1.5f);
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
