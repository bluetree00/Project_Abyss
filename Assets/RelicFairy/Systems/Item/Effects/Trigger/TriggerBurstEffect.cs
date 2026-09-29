using System.Collections.Generic;
using RelicFairy.Monster;
using UnityEngine;

/// <summary>
/// 발동 룬 — <b>언제</b>(trigger = 발동 계열)에 <b>무엇이</b>(룬 속성 = 탄두) 터진다. 빌드 컨셉 「발동 계열」 T2(09-29).
///
/// <para>같은 「대시가 끝난 자리 폭발」도 이 룬이 불로 나왔으면 화상, 얼음이면 둔화, 번개면 연쇄, 숲이면 중독,
/// 빛이면 강타, 어둠이면 취약이다. 희귀 룬의 속성은 드랍 때 무작위라 판마다 탄두가 바뀐다.</para>
///
/// 인코딩(ITEM_DATA):
///  • trigger  = 언제 — OnDash · AfterDashHit · OnPerfectDodge · OnCrit · OnCritCount · OnSkillHit · OnSkillChain · OnSkillUse ·
///               OnKill · OnMultiKill · OnExecute · OnRangedHit · SameTarget · AfterHit
///  • value    = 피해 계수(유효 공격력 ×) — 발동 계열 각인 단계가 이 값을 키운다(<see cref="BuildImprint"/>)
///  • value2   = 발동 인자(누적 횟수 N · 처형 체력 비율)
///  • value3   = 반경(m). 0이면 대상 한 명
///  • duration = 창(연속 처치 · 스킬 연속 · 대시 후 첫 적중의 제한 시간)
///
/// <para>피해는 2차 경로(<see cref="CombatQuery.DealSynergyDamage(MonsterBase,float,GameObject,float,bool,RuneElement?)"/>)라
/// 적중 이벤트를 다시 부르지 않는다. 처치 → 폭발 → 처치로 이어지는 동기 연쇄는 <see cref="s_bursting"/>이 끊는다.</para>
/// </summary>
public sealed class TriggerBurstEffect : ItemCombatEffectBase
{
    // ── Constants ────────────────────────────────────────
    private const float StreakGap     = 2f;     // 같은 적 연속 — 이 시간 넘게 쉬면 끊긴다
    private const float ChainRange    = 6f;     // 번개 연쇄 거리
    private const int   ChainCount    = 2;
    private const float ChainRatio    = 0.5f;
    private const float FrostSlow     = 0.25f;
    private const float FrostDuration = 2.5f;
    private const float BrandAmp      = 0.12f;
    private const float BrandDuration = 3f;
    private const float LightRatio    = 1.3f;   // 빛 — 부가 효과 대신 즉발이 가장 세다
    private const float GrassHitRatio = 0.6f;   // 숲 — 즉발은 약하고 독이 길게 남는다
    private const float GrassDotRatio = 0.8f;
    private const float FireDotRatio  = 0.4f;

    // ── Static ───────────────────────────────────────────
    private static readonly List<MonsterBase> s_hit   = new(24);
    private static readonly List<MonsterBase> s_chain = new(8);
    private static bool s_bursting;   // 폭발 중 들어온 이벤트(그 폭발이 낸 처치 등)는 무시 — 동기 연쇄 차단

    // ── Private ──────────────────────────────────────────
    private RuneElement _element = RuneElement.Light;
    private readonly float _icd;
    private float _icdUntil;
    private int   _count;
    private GameObject _streakTarget;
    private float _lastHitTime = -999f;
    private float _windowUntil = -999f;
    private readonly Queue<float> _killTimes = new();
    private readonly float[] _skillTimes = { -999f, -999f, -999f };
    private readonly HashSet<int> _executed = new();
    private PlayerController _dodgeSource;
    private ItemEffectContext _ctx;

    // ── Constructor ──────────────────────────────────────
    public TriggerBurstEffect(ItemEffectSlot s) : base(s)
    {
        // 발동마다 최소 간격 — 자주 터지는 발동일수록 길다(데이터로 두지 않는다: 룬마다 다르면 읽기 어렵다).
        _icd = _trigger switch
        {
            "OnDash"   => 1.2f,
            "AfterHit" => 1.5f,
            "OnCrit"   => 0.35f,
            "OnSkillHit" => 0.25f,
            "OnSkillUse" => 0.5f,
            "OnKill"   => 0.15f,
            _          => 0f,
        };
    }

    // ── Public Methods ───────────────────────────────────

    /// <summary>탄두 = 이 룬 인스턴스의 속성(ElementDef id). ItemEffectManager.Rebuild가 생성 직후 넣는다.</summary>
    public void SetElement(string elementId) => _element = elementId switch
    {
        "FIRE"     => RuneElement.Fire,
        "ICE"      => RuneElement.Ice,
        "ELECTRIC" => RuneElement.Electric,
        "GRASS"    => RuneElement.Grass,
        "DARK"     => RuneElement.Dark,
        _          => RuneElement.Light,
    };

    public override void OnActivate(ItemEffectContext ctx)
    {
        _ctx = ctx;
        if (_trigger == "OnPerfectDodge" && ctx?.Player != null)
        {
            _dodgeSource = ctx.Player;
            _dodgeSource.OnPerfectDodge += HandlePerfectDodge;
        }
    }

    public override void OnDeactivate()
    {
        if (_dodgeSource != null) _dodgeSource.OnPerfectDodge -= HandlePerfectDodge;
        _dodgeSource = null;
        _ctx = null;
    }

    public override void OnRoomEnter(ItemEffectContext ctx)
    {
        _count = 0;
        _streakTarget = null;
        _killTimes.Clear();
        _executed.Clear();
    }

    // ── 기동 ─────────────────────────────────────────────
    public override void OnRollEnd(ItemEffectContext ctx)
    {
        if (s_bursting || ctx?.Player == null) return;
        if (_trigger == "OnDash")
            TryBurst(ctx, ctx.Player.transform.position, null);
        else if (_trigger == "AfterDashHit")
            _windowUntil = Time.time + Mathf.Max(0.1f, _duration);
    }

    // ── 적중 계열(연격 · 필살 · 주문 · 사격 · 처형 · 대시 후 첫 적중) ─
    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (s_bursting || report.DamageDealt <= 0f || report.Target == null) return;
        Vector3 at = report.Target.transform.position;

        switch (_trigger)
        {
            case "AfterDashHit":
                if (Time.time > _windowUntil) return;
                _windowUntil = -999f;
                TryBurst(ctx, at, report.Target);
                return;

            case "OnCrit":
                if (report.IsCrit) TryBurst(ctx, at, report.Target);
                return;

            case "OnCritCount":
                if (report.IsCrit && ++_count >= Required()) { _count = 0; TryBurst(ctx, at, report.Target); }
                return;

            case "OnSkillHit":
                if (CombatDamage.IsSkillAction(report.ActionType)) TryBurst(ctx, at, report.Target);
                return;

            case "OnRangedHit":
                if (report.IsRanged && ++_count >= Required()) { _count = 0; TryBurst(ctx, at, report.Target); }
                return;

            case "SameTarget":
            {
                float now = Time.time;
                if (report.Target != _streakTarget || now - _lastHitTime > StreakGap) { _streakTarget = report.Target; _count = 0; }
                _lastHitTime = now;
                if (++_count >= Required()) { _count = 0; TryBurst(ctx, at, report.Target); }
                return;
            }

            case "OnExecute":
            {
                var mb = report.Target.GetComponentInParent<MonsterBase>();
                if (mb == null || mb.IsDead || mb.EffectiveMaxHp <= 0) return;
                float threshold = _value2 > 0f ? _value2 : 0.3f;
                if (mb.CurrentHp > mb.EffectiveMaxHp * threshold) return;
                if (!_executed.Add(mb.GetInstanceID())) return;   // 적마다 한 번
                TryBurst(ctx, at, report.Target);
                return;
            }
        }
    }

    // ── 처치 ─────────────────────────────────────────────
    public override void OnKill(ItemEffectContext ctx, GameObject target)
    {
        if (s_bursting || target == null) return;
        Vector3 at = target.transform.position;

        if (_trigger == "OnKill")
        {
            TryBurst(ctx, at, null);
        }
        else if (_trigger == "OnMultiKill")
        {
            float now = Time.time, window = Mathf.Max(0.5f, _duration);
            _killTimes.Enqueue(now);
            while (_killTimes.Count > 0 && now - _killTimes.Peek() > window) _killTimes.Dequeue();
            if (_killTimes.Count >= Required()) { _killTimes.Clear(); TryBurst(ctx, at, null); }
        }
    }

    // ── 주문 ─────────────────────────────────────────────
    public override void OnSkillUse(ItemEffectContext ctx, SkillType skill)
    {
        if (s_bursting || ctx?.Player == null) return;
        if (_trigger == "OnSkillUse")
        {
            TryBurst(ctx, ctx.Player.transform.position, null);
        }
        else if (_trigger == "OnSkillChain")
        {
            float now = Time.time, window = Mathf.Max(0.5f, _duration);
            _skillTimes[(int)skill] = now;
            int distinct = 0;
            foreach (var t in _skillTimes) if (now - t <= window) distinct++;
            if (distinct >= Required())
            {
                for (int i = 0; i < _skillTimes.Length; i++) _skillTimes[i] = -999f;
                TryBurst(ctx, ctx.Player.transform.position, null);
            }
        }
    }

    // ── 위기 ─────────────────────────────────────────────
    public override void OnPostTakeDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (s_bursting || _trigger != "AfterHit" || ctx?.Player == null) return;
        TryBurst(ctx, ctx.Player.transform.position, null);
    }

    // ── Private Methods ──────────────────────────────────

    private int Required() => Mathf.Max(1, Mathf.RoundToInt(_value2 > 0f ? _value2 : 1f));

    /// <summary>최소 간격을 지키며 탄두를 터뜨린다. 반경 0이면 <paramref name="primary"/> 한 명.</summary>
    private void TryBurst(ItemEffectContext ctx, Vector3 center, GameObject primary)
    {
        if (ctx?.Player == null) return;
        float now = Time.time;
        if (now < _icdUntil) return;
        _icdUntil = now + _icd;   // 피해보다 먼저 — 폭발이 낸 처치가 같은 프레임에 다시 들어와도 막힌다

        float dmg = EffAtk(ctx) * _value;
        if (dmg <= 0f) return;

        s_bursting = true;
        try { Detonate(ctx.Player.gameObject, center, primary, dmg); }
        finally { s_bursting = false; }
    }

    private void Detonate(GameObject player, Vector3 center, GameObject primary, float dmg)
    {
        s_hit.Clear();
        float radius = _value3;
        if (radius > 0f)
        {
            CombatQuery.GetNearbyEnemies(center, radius, null, 24, s_hit);
        }
        else if (primary != null)
        {
            var mb = primary.GetComponentInParent<MonsterBase>();
            if (mb != null && !mb.IsDead) s_hit.Add(mb);
        }

        ElementVfxPlayer.PlayBurst(_element, center, radius > 0f ? radius : 1f);
        if (s_hit.Count == 0) return;

        foreach (var mb in s_hit)
            if (mb != null && !mb.IsDead) Warhead(mb, dmg, player);

        // 번개 — 폭발 밖 가까운 적 둘에게 튄다.
        if (_element == RuneElement.Electric)
        {
            s_chain.Clear();
            CombatQuery.GetNearbyEnemies(center, Mathf.Max(radius, 0.5f) + ChainRange, null, 16, s_chain);
            int jumped = 0;
            Vector3 from = center + Vector3.up;
            foreach (var mb in s_chain)
            {
                if (jumped >= ChainCount) break;
                if (mb == null || mb.IsDead || s_hit.Contains(mb)) continue;
                Vector3 to = mb.transform.position + Vector3.up;
                ElementVfxPlayer.PlayBeam(RuneElement.Electric, from, to);
                CombatQuery.DealSynergyDamage(mb, dmg * ChainRatio, player, element: RuneElement.Electric);
                from = to;
                jumped++;
            }
        }
    }

    /// <summary>속성 탄두 한 명분. 흡혈 · 회복 없음(정책) — 피해 · 둔화 · 지속 피해 · 취약만.</summary>
    private void Warhead(MonsterBase mb, float dmg, GameObject player)
    {
        switch (_element)
        {
            case RuneElement.Fire:
                CombatQuery.DealSynergyDamage(mb, dmg, player, element: RuneElement.Fire);
                if (!mb.IsDead) mb.Status.ApplyDot("ignite", dmg * FireDotRatio / 6f, 0.5f, 6, player);
                break;
            case RuneElement.Ice:
                CombatQuery.DealSynergyDamage(mb, dmg, player, element: RuneElement.Ice);
                if (!mb.IsDead) mb.Status.ApplySlow("frost", FrostSlow, FrostDuration, 2);
                break;
            case RuneElement.Electric:
                CombatQuery.DealSynergyDamage(mb, dmg, player, element: RuneElement.Electric);
                break;
            case RuneElement.Grass:
                CombatQuery.DealSynergyDamage(mb, dmg * GrassHitRatio, player, element: RuneElement.Grass);
                if (!mb.IsDead) mb.Status.ApplyDot("poison", dmg * GrassDotRatio / 8f, 0.5f, 8, player);
                break;
            case RuneElement.Dark:
                CombatQuery.DealSynergyDamage(mb, dmg, player, element: RuneElement.Dark);
                if (!mb.IsDead) mb.ApplyDamageTakenAmp(BrandAmp, BrandDuration, "vulnerable");
                break;
            default:
                CombatQuery.DealSynergyDamage(mb, dmg * LightRatio, player, element: RuneElement.Light);
                break;
        }
    }

    // ── Event Handlers ───────────────────────────────────
    private void HandlePerfectDodge(float _)
    {
        if (s_bursting || _ctx?.Player == null) return;
        TryBurst(_ctx, _ctx.Player.transform.position, null);
    }
}
