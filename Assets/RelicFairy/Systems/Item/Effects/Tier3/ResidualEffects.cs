using System.Collections.Generic;
using UnityEngine;
using RelicFairy.Monster;

// ═══════════════════════════════════════════════════════════════════════════
// 잔첨형 T3/T4 아이템 전투 효과 (설계 docs/item-tier3-tier4-design.md §3)
//
// 공통: ItemCombatEffectBase 상속(IsActive=true 상시 수신).
//      즉발 추가타는 CombatQuery.DealSynergyDamage(방어우회·OnPost 미재귀)로 처리.
//      발동 시 ItemGuide 가이드라인(플래시/토스트/마커) 통지.
// per-target Dict는 방 진입(OnRoomEnter) 시 정리해 stale 누적을 막는다.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>누적의 인장 / 무한의 사슬 — 같은 대상 연속 적중마다 추가 피해 누적.
/// value=스택당 비율, max_stack=상한, value3≥1이면 대상이 바뀌어도 리셋하지 않음(T4).</summary>
public sealed class StackDamagePerTargetEffect : ItemCombatEffectBase
{
    private const float StreakGap = 2f;
    private GameObject _lastTarget;
    private int _stack;
    private float _lastTime = -999f;

    public StackDamagePerTargetEffect(ItemEffectSlot s) : base(s) { }

    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (report.Target == null || report.DamageDealt <= 0f) return;
        float now = Time.time;
        bool keepOnSwitch = _value3 >= 1f;

        if (now - _lastTime > StreakGap) _stack = 0;
        if (report.Target != _lastTarget && !keepOnSwitch) _stack = 0;
        _lastTarget = report.Target;
        _lastTime = now;

        int cap = _maxStack > 0 ? _maxStack : 10;
        _stack = Mathf.Min(_stack + 1, cap);

        float bonus = report.DamageDealt * _stack * _value;
        if (bonus >= 1f)
        {
            CombatQuery.DealSynergyDamage(report.Target, bonus, Self(ctx), 1f, report.IsCrit);
            ItemGuide.Flash(report.Target.transform.position, report.IsCrit);
        }
    }

    public override void OnRoomEnter(ItemEffectContext ctx) { _stack = 0; _lastTarget = null; }
}

/// <summary>연쇄의 잔영 / 끝없는 메아리 — 확률로 지연 추가타. value=확률, value2=피해비, duration=지연.
/// value3≥1이면 메아리 적중 시 재롤(T4, 깊이 제한).</summary>
public sealed class EchoStrikeEffect : ItemCombatEffectBase
{
    private const int MaxDepth = 5;
    private struct Pending { public MonsterBase target; public float dmg; public float fireAt; public int depth; public bool crit; }
    private readonly List<Pending> _queue = new();
    private readonly List<Pending> _scratch = new();

    public EchoStrikeEffect(ItemEffectSlot s) : base(s) { }

    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (report.Target == null || report.DamageDealt <= 0f) return;
        if (Random.value >= _value) return;
        var mb = report.Target.GetComponentInParent<MonsterBase>();
        if (mb == null) return;
        Enqueue(mb, report.DamageDealt * (_value2 > 0f ? _value2 : 1f), 0, report.IsCrit);
    }

    private void Enqueue(MonsterBase target, float dmg, int depth, bool crit)
    {
        float delay = _duration > 0f ? _duration : 0.3f;
        _queue.Add(new Pending { target = target, dmg = dmg, fireAt = Time.time + delay, depth = depth, crit = crit });
    }

    public override void OnTick(ItemEffectContext ctx, float deltaTime)
    {
        if (_queue.Count == 0) return;
        float now = Time.time;
        _scratch.Clear();
        for (int i = _queue.Count - 1; i >= 0; i--)
        {
            if (now < _queue[i].fireAt) continue;
            _scratch.Add(_queue[i]);
            _queue.RemoveAt(i);
        }
        for (int i = 0; i < _scratch.Count; i++)
        {
            var p = _scratch[i];
            if (p.target == null || p.target.CurrentHp <= 0) continue;
            CombatQuery.DealSynergyDamage(p.target, p.dmg, Self(ctx), 1f, p.crit);
            ItemGuide.Flash(p.target.transform.position, p.crit);
            // T4: 메아리가 메아리를 낳음(깊이 제한 + 재롤)
            if (_value3 >= 1f && p.depth + 1 < MaxDepth && Random.value < _value)
                Enqueue(p.target, p.dmg, p.depth + 1, p.crit);
        }
    }

    public override void OnRoomEnter(ItemEffectContext ctx) => _queue.Clear();
}

/// <summary>집요한 흔적 / 심연의 표식(취약分) — 같은 대상 연속 적중마다 받는피해 증폭 중첩.
/// value=스택당 증폭, max_stack=상한, duration=증폭 유지시간.</summary>
public sealed class TargetVulnStackEffect : ItemCombatEffectBase
{
    private const float StreakGap = 2f;
    private GameObject _lastTarget;
    private int _stack;
    private float _lastTime = -999f;

    public TargetVulnStackEffect(ItemEffectSlot s) : base(s) { }

    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (report.Target == null) return;
        float now = Time.time;
        if (now - _lastTime > StreakGap) _stack = 0;
        if (report.Target != _lastTarget) _stack = 0;
        _lastTarget = report.Target;
        _lastTime = now;

        int cap = _maxStack > 0 ? _maxStack : 10;
        _stack = Mathf.Min(_stack + 1, cap);

        var mb = report.Target.GetComponentInParent<MonsterBase>();
        if (mb == null) return;
        float amp = _stack * _value;
        mb.ApplyDamageTakenAmp(amp, _duration > 0f ? _duration : 3f, "vulnerable");
    }

    public override void OnRoomEnter(ItemEffectContext ctx) { _stack = 0; _lastTarget = null; }
}

/// <summary>쌍타의 검 — value2회 적중마다 분할 추가타. value=분할 각 비율(0.7), value2=주기.</summary>
public sealed class SplitStrikeEffect : ItemCombatEffectBase
{
    private int _hits;

    public SplitStrikeEffect(ItemEffectSlot s) : base(s) { }

    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (report.Target == null || report.DamageDealt <= 0f) return;
        int period = _value2 > 0f ? (int)_value2 : 5;
        if (++_hits < period) return;
        _hits = 0;

        // "2회 분할 각 value" 순증 = DamageDealt×(2×value - 1)
        float bonus = report.DamageDealt * (2f * _value - 1f);
        if (bonus < 1f) return;
        CombatQuery.DealSynergyDamage(report.Target, bonus, Self(ctx), 1f, report.IsCrit);
        ItemGuide.Toast(report.Target.transform.position, "쌍타");
    }
}

/// <summary>메아리 화살 — 원거리 전용, 확률로 인근 적에 메아리 피해. value=확률, value2=피해비.
/// (실제 투사체 스폰은 P5, 현재는 즉발 근사.)</summary>
public sealed class EchoArrowEffect : ItemCombatEffectBase
{
    private static readonly List<MonsterBase> s_buf = new();

    public EchoArrowEffect(ItemEffectSlot s) : base(s) { }

    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (report.Target == null || report.DamageDealt <= 0f) return;
        if (ctx.WeaponType.GetAttackStatKind() != AttackStatKind.Ranged) return;
        if (Random.value >= _value) return;

        float dmg = report.DamageDealt * (_value2 > 0f ? _value2 : 0.5f);
        Vector3 origin = report.Target.transform.position;
        int n = CombatQuery.GetNearbyEnemies(origin, 4f, report.Target, 1, s_buf);
        var hit = n > 0 ? s_buf[0] : report.Target.GetComponentInParent<MonsterBase>();
        if (hit == null) return;

        CombatQuery.DealSynergyDamage(hit, dmg, Self(ctx), 1f, report.IsCrit);
        ItemGuide.Chain(origin, hit.transform.position);
        ItemGuide.Flash(hit.transform.position, report.IsCrit);
    }
}

/// <summary>독니의 자국 — 대상별 표식 누적, max_stack 도달 시 광역 폭발 후 리셋.
/// value=표식당 폭발 피해비(EffAtk), max_stack=폭발 임계.</summary>
public sealed class MarkExplodeEffect : ItemCombatEffectBase
{
    private static readonly List<MonsterBase> s_buf = new();
    private readonly Dictionary<GameObject, int> _marks = new();

    public MarkExplodeEffect(ItemEffectSlot s) : base(s) { }

    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (report.Target == null) return;
        int cap = _maxStack > 0 ? _maxStack : 5;

        _marks.TryGetValue(report.Target, out int m);
        m++;
        ItemGuide.Status(report.Target.transform, "item_mark", 5f);

        if (m < cap) { _marks[report.Target] = m; return; }
        _marks[report.Target] = 0;

        // 광역 폭발: 표식수 × EffAtk × value
        float dmg = cap * EffAtk(ctx) * _value;
        Vector3 center = report.Target.transform.position;
        ItemGuide.Aoe(center, 3.5f);
        int n = CombatQuery.GetNearbyEnemies(center, 3.5f, null, 16, s_buf);
        for (int i = 0; i < n; i++)
            CombatQuery.DealSynergyDamage(s_buf[i], dmg, Self(ctx), 1f, false);
    }

    // 처치된 대상의 표식 제거 — 죽은 GameObject 키가 방 끝까지 누수되는 것을 방지.
    public override void OnKill(ItemEffectContext ctx, GameObject target)
    {
        if (target != null) _marks.Remove(target);
    }

    public override void OnRoomEnter(ItemEffectContext ctx) => _marks.Clear();
}


/// <summary>끝나지 않는 일격 / 천 번의 칼날 — 확률로 동일 피해 재발동(연쇄). value=확률, value2=재발동마다 확률 감소(T4).</summary>
public sealed class RepeatChanceEffect : ItemCombatEffectBase
{
    private const int SafetyCap = 20;

    public RepeatChanceEffect(ItemEffectSlot s) : base(s) { }

    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (report.Target == null || report.DamageDealt <= 0f) return;
        float chance = _value;
        int guard = 0;
        while (chance > 0f && Random.value < chance && guard++ < SafetyCap)
        {
            if (report.Target == null) break;
            CombatQuery.DealSynergyDamage(report.Target, report.DamageDealt, Self(ctx), 1f, report.IsCrit);
            ItemGuide.Flash(report.Target.transform.position, report.IsCrit);
            chance -= _value2;   // T4: 재발동마다 확률 하락(value2=0이면 동일 확률 유지)
        }
    }
}

/// <summary>잔재의 칼날 — 치명 연속 시 치확 상승, 비치명 시 하락. value=증감폭, value2=상한.</summary>
public sealed class CritMomentumEffect : ItemCombatEffectBase
{
    private float _bonus;

    public CritMomentumEffect(ItemEffectSlot s) : base(s) { }

    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        _bonus = report.IsCrit
            ? Mathf.Min(_bonus + _value, _value2)
            : Mathf.Max(0f, _bonus - _value);
    }

    public override void ContributeDynamicStats(ItemEffectContext ctx, ref ItemDynamicStats dyn)
    {
        dyn.critChance += _bonus;
    }

    public override void OnRoomEnter(ItemEffectContext ctx) => _bonus = 0f;
}

/// <summary>낙인의 사슬 — 대상별 낙인 누적(받는피해 증폭, 무소멸). 처치 시 낙인 만렙이면 주변에 절반 전파.
/// value=낙인 스택당 받피 증폭, max_stack=상한, duration=증폭 유지.</summary>
public sealed class BrandChainEffect : ItemCombatEffectBase
{
    private static readonly List<MonsterBase> s_buf = new();
    private readonly Dictionary<GameObject, int> _brands = new();

    public BrandChainEffect(ItemEffectSlot s) : base(s) { }

    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (report.Target == null) return;
        int cap = _maxStack > 0 ? _maxStack : 8;
        _brands.TryGetValue(report.Target, out int b);
        b = Mathf.Min(b + 1, cap);
        _brands[report.Target] = b;

        var mb = report.Target.GetComponentInParent<MonsterBase>();
        if (mb != null)
            mb.ApplyDamageTakenAmp(b * _value, _duration > 0f ? _duration : 4f, "brand");
    }

    public override void OnKill(ItemEffectContext ctx, GameObject target)
    {
        if (target == null) return;
        int cap = _maxStack > 0 ? _maxStack : 8;
        if (!_brands.TryGetValue(target, out int b)) return;
        _brands.Remove(target);
        if (b < cap) return;

        // 절반 전파: 주변 적에게 cap/2 낙인 초기 부여
        int spread = Mathf.Max(1, cap / 2);
        Vector3 center = target.transform.position;
        int n = CombatQuery.GetNearbyEnemies(center, 4f, target, 8, s_buf);
        for (int i = 0; i < n; i++)
        {
            var mb = s_buf[i];
            _brands[mb.gameObject] = spread;
            mb.ApplyDamageTakenAmp(spread * _value, _duration > 0f ? _duration : 4f, "brand");
            ItemGuide.Chain(center, mb.transform.position);
        }
    }

    public override void OnRoomEnter(ItemEffectContext ctx) => _brands.Clear();
}
