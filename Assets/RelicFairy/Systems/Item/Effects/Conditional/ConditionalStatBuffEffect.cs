using System.Collections.Generic;
using UnityEngine;
using RelicFairy.Monster;

/// <summary>
/// 조건부/타임드 스탯 버프 아이템 효과 (데이터 구동).
///
/// 인코딩:
///  • effect_type = 어떤 스탯 채널을 버프하는가 (Cond*)
///  • trigger     = 어떤 조건일 때 (HPBelowN / AfterSkill / NoHit / DuringBoss / EnemiesNearby …)
///  • value       = 버프량 (0.2 = +20%, 치확은 %포인트)
///  • value2      = 보조 파라미터 (EnemiesNearby 요구 적 수 등)
///  • duration    = 타임드 윈도 길이(이벤트형) 또는 요구 유지시간(NoHit)
///
/// 동작: 매 틱 ContributeDynamicStats에서 조건 충족 시 value를 해당 채널에 가산
///       → ItemEffectManager가 합산해 PlayerRuntimeStats 동적 레이어로 push.
/// 복합 스탯 아이템(예: 공격력+방어력)은 슬롯(행) 2개로 표현 — 각자 이 효과 1개.
///
/// IsActive=true로 두어 이벤트 훅(OnSkillUse/OnRoomEnter/OnPostTakeDamage/OnBoss*)을 항상 수신,
/// 타임드 타이머를 갱신한다. ModifyStats(정적)에는 기여하지 않는다.
/// </summary>
public sealed class ConditionalStatBuffEffect : ItemEffectBase
{
    private enum Channel { AttackPercent, DefensePercent, AttackSpeed, MoveSpeed, CritChance, CritDamage, SkillDamage, AllDamage, MaxHpPercent, None }

    private const float NEARBY_RADIUS = 3f;
    private static readonly List<MonsterBase> s_buf = new();

    private readonly Channel _channel;

    // 타임드 상태
    private float _windowUntil = -999f;   // 이벤트형(AfterSkill/AfterRoomEnter/AfterHit) 활성 종료 시각
    private float _lastHitTime = -999f;   // NoHit 판정용 마지막 피격 시각
    private bool  _bossActive;            // DuringBoss

    // 연속 적중 카운터 (SameTarget / Consecutive)
    private const float STREAK_GAP = 2f;  // 이 시간 이상 미적중 시 연속 끊김
    private GameObject _streakTarget;     // 같은 대상 판정
    private int   _sameTargetCount;       // 같은 대상 연속 적중 수
    private int   _streakCount;           // 임의 대상 연속 적중 수
    private float _lastDealTime = -999f;  // 마지막 적중 시각

    public ConditionalStatBuffEffect(ItemEffectSlot slot) : base(slot)
    {
        _channel = slot.effectType switch
        {
            "CondAttackPercent"  => Channel.AttackPercent,
            "CondDefensePercent" => Channel.DefensePercent,
            "CondAttackSpeed"    => Channel.AttackSpeed,
            "CondMoveSpeed"      => Channel.MoveSpeed,
            "CondCritChance"     => Channel.CritChance,
            "CondCritDamage"     => Channel.CritDamage,
            "CondSkillDamage"    => Channel.SkillDamage,
            "CondAllDamage"      => Channel.AllDamage,
            "CondMaxHpPercent"   => Channel.MaxHpPercent,
            _                    => Channel.None,
        };
    }

    /// <summary>이벤트 훅/틱을 항상 수신하기 위해 활성 고정. 정적 스탯엔 기여하지 않는다.</summary>
    public override bool IsActive(ItemEffectContext ctx) => true;

    // ── 타임드 윈도 트리거 ──────────────────────────────────
    public override void OnSkillUse(ItemEffectContext ctx, SkillType skill)
    {
        if (_trigger == "AfterSkill") _windowUntil = Time.time + Mathf.Max(0f, _duration);
    }

    public override void OnRoomEnter(ItemEffectContext ctx)
    {
        if (_trigger == "AfterRoomEnter") _windowUntil = Time.time + Mathf.Max(0f, _duration);
        _bossActive = false;   // 일반 방 진입 시 보스 플래그 해제
    }

    public override void OnPostTakeDamage(ItemEffectContext ctx, DamageReport report)
    {
        _lastHitTime = Time.time;   // NoHit 리셋
        if (_trigger == "AfterHit") _windowUntil = Time.time + Mathf.Max(0f, _duration);
    }

    public override void OnBossEnter(ItemEffectContext ctx) => _bossActive = true;
    public override void OnBossClear(ItemEffectContext ctx) => _bossActive = false;

    // ── 연속 적중 카운터 ────────────────────────────────────
    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        float now = Time.time;
        if (now - _lastDealTime > STREAK_GAP) _streakCount = 0;   // 갭 초과 시 연속 끊김
        _lastDealTime = now;
        _streakCount++;

        if (report.Target != _streakTarget) { _streakTarget = report.Target; _sameTargetCount = 1; }
        else _sameTargetCount++;
    }

    // ── 동적 스탯 기여 ──────────────────────────────────────
    public override void ContributeDynamicStats(ItemEffectContext ctx, ref ItemDynamicStats dyn)
    {
        if (_channel == Channel.None || !ConditionMet(ctx)) return;

        switch (_channel)
        {
            case Channel.AttackPercent:  dyn.attackPercent  += _value; break;
            case Channel.DefensePercent: dyn.defensePercent += _value; break;
            case Channel.AttackSpeed:    dyn.attackSpeed    += _value; break;
            case Channel.MoveSpeed:      dyn.moveSpeed      += _value; break;
            case Channel.CritChance:     dyn.critChance     += _value; break;
            case Channel.CritDamage:     dyn.critDamage     += _value; break;
            case Channel.SkillDamage:    dyn.skillDamage    += _value; break;
            case Channel.AllDamage:      dyn.allDamage      += _value; break;
            case Channel.MaxHpPercent:   dyn.maxHpPercent   += _value; break;
        }
    }

    // ── 조건 평가 ───────────────────────────────────────────
    private bool ConditionMet(ItemEffectContext ctx)
    {
        switch (_trigger)
        {
            case "HPBelow50": return ctx.HpRatio <= 0.5f;
            case "HPBelow40": return ctx.HpRatio <= 0.4f;
            case "HPBelow30": return ctx.HpRatio <= 0.3f;

            case "AfterSkill":
            case "AfterRoomEnter":
            case "AfterHit":
                return Time.time < _windowUntil;

            case "NoHit":
                return Time.time - _lastHitTime >= Mathf.Max(0f, _duration);

            case "DuringBoss":
                return _bossActive;

            case "EnemiesNearby":
            {
                int required = _value2 > 0f ? (int)_value2 : 2;
                return CountNearby(ctx) >= required;
            }
            case "SingleEnemy":
                return CountNearby(ctx) <= 1;

            case "WhileMoving":
                return ctx?.Player != null && ctx.Player.MoveDirection.sqrMagnitude > 0.01f;

            case "WhileSkillCooldown":
            {
                var ct = ctx?.Player?.CooldownTracker;
                return ct != null && (!ct.IsReady(SkillType.Q) || !ct.IsReady(SkillType.E) || !ct.IsReady(SkillType.R));
            }

            case "DefenseAbove":
                return ctx?.Stats != null && ctx.Stats.Defense >= (int)_value2;

            case "SameTarget":
                return Time.time - _lastDealTime <= STREAK_GAP && _sameTargetCount >= (int)_value2;

            case "Consecutive":
                return Time.time - _lastDealTime <= STREAK_GAP && _streakCount >= (int)_value2;

            case "HasShield":
                return ctx?.Stats != null && ctx.Stats.HasShield;

            // 단발(Stationary/FirstAttackInRoom)은 FirstHitBonusEffect에서 처리.
            default:
                return false;
        }
    }

    private static int CountNearby(ItemEffectContext ctx)
    {
        if (ctx?.Player == null) return 0;
        return CombatQuery.GetNearbyEnemies(ctx.Player.transform.position, NEARBY_RADIUS, null, 32, s_buf);
    }
}
