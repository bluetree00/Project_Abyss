using System.Collections.Generic;
using RelicFairy.Monster;
using UnityEngine;

// ═══════════════════════════════════════════════════════════
// 룬 재설계 R1 · R2(10-14 빌드 시너지 3-R) — 「모든 룬은 빌드 하나를 받친다」
// 계열마다 보상 종류가 다르다: 연격 = 쌓이는 보너스 · 필살 = 치명 피해 · 강타 · 사격 = 원거리 명중 · 주문 = 쿨 · 스킬 ·
// 처치 = 폭발 반경 · 전이 · 기동 = 다음 일격 · 위기 = 받는 피해 감소 · 보호막(회복 금지 정책 그대로).
// 계열은 데이터 trigger 열이 정한다(BuildFamilyRules.FromTrigger) — 효과 코드는 발동 이름을 보지 않는 것도 있다.
// ═══════════════════════════════════════════════════════════

/// <summary>맞서는 분노(위기) — 정예 · 보스에게 주는 피해 +value(15%).</summary>
public sealed class TargetGradeBonusEffect : ItemEffectBase
{
    public TargetGradeBonusEffect(ItemEffectSlot s) : base(s) { }
    public override bool IsActive(ItemEffectContext ctx) => true;

    public override void OnPreDealDamage(ItemEffectContext ctx, ref DamagePacket pkt)
    {
        var mb = pkt.Target != null ? pkt.Target.GetComponentInParent<MonsterBase>() : null;
        if (mb != null && (mb.Grade == MonsterGrade.Elite || mb.Grade == MonsterGrade.Boss)) pkt.FinalDamage *= 1f + _value;
    }
}

/// <summary>겨누는 분노(사격) — value2(5 m) 밖의 적을 맞히면 피해 +value(25%). 석궁 저격의 축.</summary>
public sealed class FarHitBonusEffect : ItemEffectBase
{
    public FarHitBonusEffect(ItemEffectSlot s) : base(s) { }
    public override bool IsActive(ItemEffectContext ctx) => true;

    public override void OnPreDealDamage(ItemEffectContext ctx, ref DamagePacket pkt)
    {
        if (ctx?.Player == null || pkt.Target == null) return;
        Vector3 d = pkt.Target.transform.position - ctx.Player.transform.position; d.y = 0f;
        float far = _value2 > 0f ? _value2 : 5f;
        if (d.sqrMagnitude >= far * far) pkt.FinalDamage *= 1f + _value;
    }
}

/// <summary>질주하는 분노(기동) — value2(6 m) 움직일 때마다 다음 일격 +value(60%) 한 번(쌓이지 않는다).</summary>
public sealed class MoveChargeNextHitEffect : ItemEffectBase
{
    private Vector3 _last;
    private bool _hasLast, _armed;
    private float _moved;

    public MoveChargeNextHitEffect(ItemEffectSlot s) : base(s) { }
    public override bool IsActive(ItemEffectContext ctx) => true;

    public override void OnRoomEnter(ItemEffectContext ctx) { _hasLast = false; _moved = 0f; }

    public override void OnTick(ItemEffectContext ctx, float deltaTime)
    {
        if (ctx?.Player == null) return;
        Vector3 p = ctx.Player.transform.position; p.y = 0f;
        if (_hasLast && !_armed)
        {
            _moved += Vector3.Distance(p, _last);
            if (_moved >= (_value2 > 0f ? _value2 : 6f)) { _armed = true; _moved = 0f; }
        }
        _last = p; _hasLast = true;
    }

    public override void OnPreDealDamage(ItemEffectContext ctx, ref DamagePacket pkt)
    {
        if (!_armed) return;
        _armed = false;
        pkt.FinalDamage *= 1f + _value;
    }
}

/// <summary>
/// 고요한 집중(필살) — duration(4초) 동안 안 맞으면 다음 치명의 피해 +value(100%) 한 번(대검 한 방).
/// 치명 굴림은 사전 보정 뒤라, 치명이 난 타의 사후에 그 몫을 더 준다(치명 표시).
/// </summary>
public sealed class CalmNextCritEffect : ItemEffectBase
{
    private float _lastHurt = -999f;

    public CalmNextCritEffect(ItemEffectSlot s) : base(s) { }
    public override bool IsActive(ItemEffectContext ctx) => true;

    public override void OnRoomEnter(ItemEffectContext ctx) => _lastHurt = Time.time;
    public override void OnPostTakeDamage(ItemEffectContext ctx, DamageReport report) => _lastHurt = Time.time;

    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (!report.IsCrit || report.DamageDealt <= 0f || ctx?.Player == null || report.Target == null) return;
        if (Time.time - _lastHurt < (_duration > 0f ? _duration : 4f)) return;
        _lastHurt = Time.time;   // 한 번 쓰면 다시 4초를 버텨야 한다
        CombatQuery.DealSynergyDamage(report.Target, report.DamageDealt * _value, ctx.Player.gameObject, 1f, isCrit: true);
        ItemGuide.Toast(report.Target.transform.position, "고요한 일격");
    }
}

/// <summary>꿰찌르는 질풍(필살) — 치명마다 치명 피해 +value(25%) 한 겹(최대 value2 = 3 · duration 3초 · 치명이 오면 갱신).</summary>
public sealed class CritDamageStackEffect : ItemEffectBase
{
    private int _stacks;
    private float _until = -999f;

    public CritDamageStackEffect(ItemEffectSlot s) : base(s) { }
    public override bool IsActive(ItemEffectContext ctx) => true;

    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (!report.IsCrit || report.DamageDealt <= 0f) return;
        int max = _value2 > 0f ? Mathf.RoundToInt(_value2) : 3;
        if (Time.time > _until) _stacks = 0;
        _stacks = Mathf.Min(max, _stacks + 1);
        _until = Time.time + (_duration > 0f ? _duration : 3f);
    }

    public override void ContributeDynamicStats(ItemEffectContext ctx, ref ItemDynamicStats dyn)
    {
        if (Time.time > _until) { _stacks = 0; return; }
        dyn.critDamage += _value * _stacks;
    }
}

/// <summary>사냥하는 질풍(처치) — 처치 발동 룬(격발)의 폭발 반경 +value(20%).</summary>
public sealed class KillBurstRadiusEffect : ItemEffectBase
{
    /// <summary>지금 켜진 처치 폭발 반경 가산 합(격발 룬이 읽는다).</summary>
    public static float Bonus { get; private set; }
    private bool _on;

    public KillBurstRadiusEffect(ItemEffectSlot s) : base(s) { }
    public override bool IsActive(ItemEffectContext ctx) => true;

    public override void OnActivate(ItemEffectContext ctx) { if (!_on) { Bonus += _value; _on = true; } }
    public override void OnDeactivate() { if (_on) { Bonus = Mathf.Max(0f, Bonus - _value); _on = false; } }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Bonus = 0f;
}

/// <summary>굳건한 성광(필살 · 강타 룬) — 강타가 정예 포이즈를 깨면 그 적이 duration(2초) 취약(받는 피해 +value = 20%).</summary>
public sealed class HeavyBreakVulnerableEffect : ItemEffectBase
{
    public HeavyBreakVulnerableEffect(ItemEffectSlot s) : base(s) { }
    public override bool IsActive(ItemEffectContext ctx) => true;

    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (!report.IsHeavy || report.Target == null) return;
        var mb = report.Target.GetComponentInParent<MonsterBase>();
        var poise = mb != null ? mb.Poise : null;
        if (poise == null || !poise.IsBroken || mb.Status.IsOnCooldown("heavy_break_vuln")) return;
        float dur = _duration > 0f ? _duration : 2f;
        mb.Status.SetCooldown("heavy_break_vuln", MonsterPoise.BreakDuration);   // 한 번 깨질 때 한 번
        mb.ApplyDamageTakenAmp(_value > 0f ? _value : 0.2f, dur, "vulnerable");
        ItemGuide.Toast(mb.transform.position, "취약");
    }
}

/// <summary>견고한 파괴(필살 · 강타 룬) — 이 방 첫 강타의 피해 +value(100%).</summary>
public sealed class FirstHeavyInRoomEffect : ItemEffectBase
{
    private bool _armed;

    public FirstHeavyInRoomEffect(ItemEffectSlot s) : base(s) { }
    public override bool IsActive(ItemEffectContext ctx) => true;

    public override void OnRoomEnter(ItemEffectContext ctx) => _armed = true;

    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (!_armed || !report.IsHeavy || report.DamageDealt <= 0f || ctx?.Player == null || report.Target == null) return;
        _armed = false;
        CombatQuery.DealSynergyDamage(report.Target, report.DamageDealt * _value, ctx.Player.gameObject, 1f, report.IsCrit);
        ItemGuide.Toast(report.Target.transform.position, "첫 강타");
    }
}

/// <summary>
/// 굳히는 얼음(연격) — 적중마다 서리 1중첩(같은 대상 0.5초에 한 번 · 최대 2) · 서리 2중첩 적에게 주는 피해 +value(15%).
/// 빙결은 얼음 존(단계 효과)이 맡는다 — 이 룬은 걸지 않는다(K7).
/// </summary>
public sealed class FrostOnHitEffect : ItemEffectBase
{
    private const string FrostId = "frost", GapKey = "rune_frost_gap";
    private const float FrostSlow = 0.25f, FrostSeconds = 2.5f, Gap = 0.5f;

    public FrostOnHitEffect(ItemEffectSlot s) : base(s) { }
    public override bool IsActive(ItemEffectContext ctx) => true;

    public override void OnPreDealDamage(ItemEffectContext ctx, ref DamagePacket pkt)
    {
        var mb = pkt.Target != null ? pkt.Target.GetComponentInParent<MonsterBase>() : null;
        if (mb != null && mb.Status.GetSlowStacks(FrostId) >= 2) pkt.FinalDamage *= 1f + _value;
    }

    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (report.DamageDealt <= 0f || report.Target == null) return;
        var mb = report.Target.GetComponentInParent<MonsterBase>();
        if (mb == null || mb.Status.IsOnCooldown(GapKey)) return;
        mb.Status.SetCooldown(GapKey, Gap);
        mb.Status.ApplySlow(FrostId, FrostSlow, FrostSeconds, 2);
    }
}

/// <summary>
/// 피격 창(위기) 공용 — 맞으면(쿨 value3 = 6초) duration초 동안 받는 피해 −value(DamageReduction 동적 칸).
/// mode: 막아서는 얼음 = 둘레 3 m 적 서리 2중첩 · 단단한 그림자 = 창 동안 적중마다 그림자 추가타 value2(25%).
/// </summary>
public sealed class HurtGuardEffect : ItemEffectBase
{
    private const string FrostId = "frost";
    private static readonly List<MonsterBase> s_buf = new();
    private readonly bool _shadow;
    private float _until = -999f, _readyAt;

    public HurtGuardEffect(ItemEffectSlot s, bool shadow) : base(s) { _shadow = shadow; }
    public override bool IsActive(ItemEffectContext ctx) => true;

    public override void OnPostTakeDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (ctx?.Player == null || Time.time < _readyAt) return;
        _readyAt = Time.time + (_value3 > 0f ? _value3 : 6f);
        _until   = Time.time + (_duration > 0f ? _duration : 3f);
        if (_shadow) { ItemGuide.Toast(ctx.Player.transform.position, "그림자 수호"); return; }
        int n = CombatQuery.GetNearbyEnemies(ctx.Player.transform.position, 3f, null, 12, s_buf);
        for (int i = 0; i < n; i++) { s_buf[i].Status.ApplySlow(FrostId, 0.25f, 2.5f, 2); s_buf[i].Status.ApplySlow(FrostId, 0.25f, 2.5f, 2); }
        ElementVfxPlayer.PlayBurst(RuneElement.Ice, ctx.Player.transform.position, 3f);
    }

    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (!_shadow || Time.time > _until || report.DamageDealt <= 0f || ctx?.Player == null || report.Target == null) return;
        CombatQuery.DealSynergyDamage(report.Target, report.DamageDealt * (_value2 > 0f ? _value2 : 0.25f), ctx.Player.gameObject, element: RuneElement.Dark);
    }

    public override void ContributeDynamicStats(ItemEffectContext ctx, ref ItemDynamicStats dyn)
    {
        if (Time.time <= _until) dyn.damageReduction += _value;
    }
}

/// <summary>
/// 감싸는 숲(위기) — 방에 들어서면 보호막(최대 체력 × value = 8%). 그 보호막이 깨지면 둘레 3 m 독 폭발(공격력 × value2 = 150%) 한 번.
/// 회복이 아니라 보호막이다(정책).
/// </summary>
public sealed class RoomShieldBurstEffect : ItemEffectBase
{
    private static readonly List<MonsterBase> s_buf = new();
    private bool _watch, _hadShield;

    public RoomShieldBurstEffect(ItemEffectSlot s) : base(s) { }
    public override bool IsActive(ItemEffectContext ctx) => true;

    public override void OnRoomEnter(ItemEffectContext ctx)
    {
        if (ctx?.Stats == null) return;
        ctx.Stats.AddShield(Mathf.Max(1f, ctx.Stats.MaxHp * _value));
        _watch = true;
        _hadShield = true;
    }

    public override void OnTick(ItemEffectContext ctx, float deltaTime)
    {
        if (!_watch || ctx?.Stats == null || ctx.Player == null) return;
        bool has = ctx.Stats.HasShield;
        if (_hadShield && !has)
        {
            _watch = false;
            Vector3 at = ctx.Player.transform.position;
            float dmg = ctx.Stats.GetEffectiveAttack(ctx.WeaponType.GetAttackStatKind()) * (_value2 > 0f ? _value2 : 1.5f);
            int n = CombatQuery.GetNearbyEnemies(at, 3f, null, 16, s_buf);
            for (int i = 0; i < n; i++) CombatQuery.DealSynergyDamage(s_buf[i], dmg, ctx.Player.gameObject, element: RuneElement.Grass);
            ElementVfxPlayer.PlayBurst(RuneElement.Grass, at, 3f);
        }
        _hadShield = has;
    }
}

/// <summary>단단한 숲(처치) — 중독된 적을 쓰러뜨리면 그 독 중첩을 가까운 적 value2(2)명에게 옮긴다(+ 1중첩).</summary>
public sealed class PoisonSpreadOnKillEffect : ItemEffectBase
{
    private const string PoisonId = "item_poison";
    private static readonly List<MonsterBase> s_buf = new();
    private readonly Dictionary<int, int> _lastStacks = new();

    public PoisonSpreadOnKillEffect(ItemEffectSlot s) : base(s) { }
    public override bool IsActive(ItemEffectContext ctx) => true;

    // 처치 순간엔 죽은 적의 상태가 이미 비었을 수 있다 — 적중마다 그 적의 독 중첩을 기억해 둔다
    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        var mb = report.Target != null ? report.Target.GetComponentInParent<MonsterBase>() : null;
        if (mb != null) _lastStacks[mb.GetInstanceID()] = mb.Status.GetDotStacks(PoisonId);
    }

    public override void OnRoomEnter(ItemEffectContext ctx) => _lastStacks.Clear();

    public override void OnKill(ItemEffectContext ctx, GameObject target)
    {
        if (ctx?.Player == null || target == null) return;
        var mb = target.GetComponentInParent<MonsterBase>();
        if (mb == null) return;
        _lastStacks.TryGetValue(mb.GetInstanceID(), out int stacks);
        _lastStacks.Remove(mb.GetInstanceID());
        if (stacks <= 0) return;
        int count = _value2 > 0f ? Mathf.RoundToInt(_value2) : 2;
        float perStack = Mathf.Max(1f, ctx.Stats.GetEffectiveAttack(ctx.WeaponType.GetAttackStatKind()) * (_value > 0f ? _value : 0.04f));
        int n = CombatQuery.GetNearbyEnemies(target.transform.position, 4f, target, count, s_buf);
        for (int i = 0; i < n; i++)
            for (int k = 0; k <= stacks && k < 8; k++)
                s_buf[i].Status.ApplyStackingDot(PoisonId, perStack, 1f, 4, 8, ctx.Player.gameObject);
        ElementVfxPlayer.PlayBurst(RuneElement.Grass, target.transform.position, 2f);
    }
}

/// <summary>가벼운 그림자(기동) — 구르기가 지나간 길에 잔상 · 0.5초 뒤 그 길 위 적에게 공격력 × value(80%).</summary>
public sealed class DashAfterimageEffect : ItemEffectBase
{
    private const float Delay = 0.5f, HalfWidth = 1f, PathLookback = 0.45f;
    private static readonly List<MonsterBase> s_buf = new();
    private readonly Queue<(float t, Vector3 p)> _trail = new();
    private readonly List<(float at, Vector3 a, Vector3 b)> _pending = new();

    public DashAfterimageEffect(ItemEffectSlot s) : base(s) { }
    public override bool IsActive(ItemEffectContext ctx) => true;

    public override void OnTick(ItemEffectContext ctx, float deltaTime)
    {
        if (ctx?.Player == null) return;
        float now = Time.time;
        _trail.Enqueue((now, ctx.Player.transform.position));
        while (_trail.Count > 0 && now - _trail.Peek().t > 1f) _trail.Dequeue();

        for (int i = _pending.Count - 1; i >= 0; i--)
        {
            if (now < _pending[i].at) continue;
            var (_, a, b) = _pending[i];
            _pending.RemoveAt(i);
            float dmg = ctx.Stats.GetEffectiveAttack(ctx.WeaponType.GetAttackStatKind()) * (_value > 0f ? _value : 0.8f);
            int n = CombatQuery.GetNearbyEnemies((a + b) * 0.5f, Vector3.Distance(a, b) * 0.5f + HalfWidth + 0.5f, null, 16, s_buf);
            for (int k = 0; k < n; k++)
                if (FormStrike.DistToSegmentXZ(s_buf[k].transform.position, a, b) <= HalfWidth + 0.4f)
                    CombatQuery.DealSynergyDamage(s_buf[k], dmg, ctx.Player.gameObject, element: RuneElement.Dark);
            FormStrike.GroundStrip(a, b - a, Vector3.Distance(a, b), HalfWidth, 0.3f, new Color(0.45f, 0.35f, 0.6f));
        }
    }

    public override void OnRollEnd(ItemEffectContext ctx)
    {
        if (ctx?.Player == null) return;
        float now = Time.time;
        Vector3 start = ctx.Player.transform.position;
        foreach (var (t, p) in _trail) if (now - t <= PathLookback) { start = p; break; }
        Vector3 end = ctx.Player.transform.position;
        if ((end - start).sqrMagnitude < 0.25f) return;
        _pending.Add((now + Delay, start, end));
    }
}

/// <summary>꿰뚫는 얼음(사격) — 관통할 때마다 다음 대상 피해 +value(15%). 한 발이 0.15초 안에 잇달아 다른 적을 맞히면 같은 화살의 관통으로 본다.</summary>
public sealed class PierceChainBonusEffect : ItemEffectBase
{
    private const float ChainWindow = 0.15f;
    private float _lastAt = -999f;
    private GameObject _lastTarget;
    private int _chain;

    public PierceChainBonusEffect(ItemEffectSlot s) : base(s) { }
    public override bool IsActive(ItemEffectContext ctx) => true;

    public override void OnPreDealDamage(ItemEffectContext ctx, ref DamagePacket pkt)
    {
        if (ctx?.WeaponType is not (WeaponType.Bow or WeaponType.Crossbow) || pkt.Target == null) return;
        float now = Time.time;
        _chain = now - _lastAt <= ChainWindow && pkt.Target != _lastTarget ? _chain + 1 : 0;
        _lastAt = now; _lastTarget = pkt.Target;
        if (_chain > 0) pkt.FinalDamage *= 1f + _value * _chain;
    }
}
