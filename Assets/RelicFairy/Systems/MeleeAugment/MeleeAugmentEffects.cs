using System;
using System.Collections.Generic;
using RelicFairy.Monster;
using UnityEngine;

/// <summary>
/// 근접 강화 증강 효과의 바탕 — 룬 효과 틀(<see cref="ItemEffectBase"/>)에 얹어 같은 훅(피해 전 · 적중 후 · 처치 · 회피 끝 · 틱 · 동적 스탯)을 쓴다.
/// 증강은 그 무기의 것이라 <b>그 무기를 들고 있을 때만</b> 켜진다(<see cref="IsActive"/>).
/// 증강은 스탯 한 줄이 아니라 <b>규칙</b>이다 — 화면에서 무언가가 일어난다(사용자 10-15 「의미 있는 증강 — 그냥 스탯 증가만이 아닌」).
/// 「휘두름당 한 번 · 마지막 타」는 형 구동기(<see cref="WeaponFormRuntime"/>)와 같은 기준 — 슬래시 이벤트 때의 일련번호 · 콤보 단계 — 로 센다(검증 6 교훈: 연타 수만큼 터지지 않게).
/// 추가 피해는 2차 피해(<see cref="FormStrike"/> → DealSynergyDamage)라 다시 적중 훅을 부르지 않는다.
/// </summary>
public abstract class MeleeAugmentEffect : ItemEffectBase
{
    protected const string WaveVfx = "vfx_form_windcut", NovaVfx = "NovaOrangeHit";
    protected const float WaveVfxPerMeter = 1.3f / 8f, NovaVfxPerMeter = 3f / 3.5f;   // 칼바람 풍절(8 m ↔ 1.3) · 무게 내려찍기(3.5 m ↔ 3) — 이펙트 = 판정
    protected const float StripBody = 0.4f, StripSeconds = 0.35f, RingSeconds = 0.3f;

    protected readonly MeleeAugmentDef Def;
    protected readonly int Rank;
    protected readonly float V;    // 지금 단계 값
    protected readonly float V2;   // 지금 단계 둘째 값
    protected readonly List<MonsterBase> Buf = new(16);
    /// <summary>
    /// 슬래시 이벤트보다 피해가 먼저 들어온 타를 새 휘두름으로 보는 간격. 2초 쉬고 친 첫 타는 피해가 슬래시 이벤트보다 먼저 와
    /// 번호가 앞 휘두름 것으로 남았다(10-15 실측 59 — 튕기는 칼끝이 「같은 휘두름」으로 건너뜀). 판정 수명(0.5초)보다 짧으면 늦게 맞은 타를 새 휘두름으로 센다.
    /// </summary>
    protected const float EarlyHitGap = 0.45f;

    private WeaponData _weapon;
    private WeaponFormRuntime _runtime;
    private int _lastSwing = -1;
    private float _lastSwingHitAt = -999f;
    private bool _earlySwing;

    protected MeleeAugmentEffect(MeleeAugmentDef def, int rank)
        : base(new ItemEffectSlot { effectType = "MeleeAugment", trigger = "Always", value = def.ValueAt(rank) })
    {
        Def = def; Rank = rank; V = def.ValueAt(rank); V2 = def.Value2At(rank);
    }

    public string AugmentId => Def.Id;
    protected WeaponData Weapon => _weapon;

    public override bool IsActive(ItemEffectContext ctx)
        => _weapon != null && ctx?.Player != null && ctx.Player.WeaponManager != null && ctx.Player.WeaponManager.CurrentWeaponData == _weapon;

    public override void OnActivate(ItemEffectContext ctx) => _runtime = WeaponFormRuntime.Of(ctx?.Player);

    /// <summary>두 무기 칸의 증강 효과를 만들어 <paramref name="into"/>에 붙인다(<see cref="ItemEffectManager.Rebuild"/>가 룬 뒤에 부른다).</summary>
    public static void AppendFor(PlayerController player, List<IItemEffect> into, ItemEffectContext ctx)
    {
        var wm = player != null ? player.WeaponManager : null;
        if (wm == null) return;
        Append(wm.Weapon0Data, into, ctx);
        Append(wm.Weapon1Data, into, ctx);
    }

    private static void Append(WeaponData w, List<IItemEffect> into, ItemEffectContext ctx)
    {
        if (w == null || MeleeAugmentCatalog.KindOf(w) == MeleeWeaponKind.None) return;
        foreach (var (id, rank) in w.AugmentList())
        {
            var def = MeleeAugmentCatalog.Get(id);
            if (def == null || !MeleeAugmentCatalog.FitsWeapon(def, w)) continue;
            var e = def.Create(def, rank);
            if (e == null) continue;
            e._weapon = w;
            into.Add(e);
            e.OnActivate(ctx);
        }
    }

    // ── 공용 도우미 ──
    protected static MonsterBase Monster(GameObject go) => go != null ? go.GetComponentInParent<MonsterBase>() : null;
    protected static float Atk(ItemEffectContext ctx) => ctx?.Player != null ? ctx.Player.RuntimeStats.GetEffectiveAttack(AttackStatKind.Melee) : 0f;
    protected static bool IsBasic => DamageSourceScope.Current == DamageSource.Basic;

    /// <summary>이 휘두름의 콤보 단계 — 슬래시가 나올 때 단계(판정이 남은 동안 단계가 넘어가도 그대로). 슬래시보다 먼저 맞은 휘두름은 지금 콤보 단계.</summary>
    protected int SwingStep(ItemEffectContext ctx)
        => _runtime != null && !_earlySwing ? _runtime.SwingStep : ctx?.Player != null && ctx.Player.Combo != null ? ctx.Player.Combo.CurrentComboStep : -1;

    /// <summary>휘두름 일련번호(슬래시 이벤트마다 +1).</summary>
    protected int SwingSerial => _runtime != null ? _runtime.SwingSerial : -1;

    protected bool IsFinisherStep(ItemEffectContext ctx) => _weapon != null && SwingStep(ctx) >= Mathf.Max(1, _weapon.groundEndCount) - 1;

    /// <summary>
    /// 근접 평타 적중이 이 휘두름의 첫 번째인가(이 효과 기준 — 효과마다 따로 센다). 이 효과가 부른 적중끼리만 비교한다.
    /// 번호가 그대로인데 앞 적중과 <see cref="EarlyHitGap"/>보다 떨어졌으면 슬래시보다 먼저 맞은 새 휘두름 — 곧 올 슬래시 번호를 이 휘두름 것으로 잡아 둔다.
    /// </summary>
    protected bool FirstHitOfSwing(DamageReport report)
    {
        if (report.ActionType != WeaponActionType.GroundLight || report.IsRanged || report.Target == null) return false;
        int s = SwingSerial;
        if (s < 0) return false;
        float gap = Time.time - _lastSwingHitAt;
        _lastSwingHitAt = Time.time;
        if (s > _lastSwing) { _lastSwing = s; _earlySwing = false; return true; }   // 새 슬래시
        if (gap <= EarlyHitGap) return false;                                          // 같은 휘두름(여러 적 · 늦게 맞은 타)
        _lastSwing = s + 1;                                                            // 슬래시보다 먼저 맞은 새 휘두름 — 곧 올 슬래시도 이 휘두름 것
        _earlySwing = true;
        return true;
    }

    protected static void Wave(PlayerController p, Vector3 from, Vector3 dir, float length, float halfWidth)
    {
        FormStrike.PlayVfx(WaveVfx, from + Vector3.up, dir, WaveVfxPerMeter * length);
        FormStrike.GroundStrip(from, dir, length, halfWidth + StripBody, StripSeconds);
    }

    protected static void Nova(Vector3 at, float radius, Vector3 forward)
    {
        FormStrike.PlayVfx(NovaVfx, at, forward, NovaVfxPerMeter * radius);
        FormStrike.GroundRing(at, radius, RingSeconds);
    }

    /// <summary>실측 로그 — 에디터에서만(빌드에서는 호출과 문자열 조립이 통째로 빠진다). 실측 메뉴 「장비 형/58」이 센다.</summary>
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    protected static void Log(string msg) => Debug.Log("[MeleeAugment] " + msg);
}

/// <summary>값만 들고 있는 증강 — 다른 코드(스킬 · 형 · 각인 · 검기)가 <see cref="MeleeAugments"/>로 읽는다.</summary>
public sealed class MarkerAugment : MeleeAugmentEffect
{
    public MarkerAugment(MeleeAugmentDef d, int r) : base(d, r) { }
}

/// <summary>
/// 묵직한 손 — 평타 N번째 휘두름마다 그 휘두름의 적중이 강타. 카타나 · 무형검은 계수(0.7)가 문턱(1.5, 필살 각인 최대 0.75)에 닿지 않아
/// 문턱을 내리는 방식으로는 강타가 나오지 않았다(10-15 실측 전 검토) — 휘두름을 세어 강타를 예약한다(사전 보정 단계라 이 피해가 바로 쓴다).
/// 대검은 평타가 이미 강타라 제시하지 않는다.
/// </summary>
public sealed class HeavySwingAugment : MeleeAugmentEffect
{
    private int _seen = -1, _count;
    private float _lastAt = -999f;
    private bool _heavyNow;
    public HeavySwingAugment(MeleeAugmentDef d, int r) : base(d, r) { }
    public override void OnPreDealDamage(ItemEffectContext ctx, ref DamagePacket pkt)
    {
        if (!IsBasic) return;
        int s = SwingSerial;
        if (s < 0) return;
        float gap = Time.time - _lastAt;
        _lastAt = Time.time;
        if (s > _seen || gap > EarlyHitGap)   // 새 휘두름(슬래시보다 먼저 맞은 타도 — 바탕의 FirstHitOfSwing과 같은 기준)
        {
            _seen = s > _seen ? s : s + 1;
            _count++;
            _heavyNow = _count % Mathf.Max(1, Mathf.RoundToInt(V)) == 0;
            if (_heavyNow) Log($"묵직한 손 — 휘두름 {_seen} 강타");
        }
        if (_heavyNow) ProcCoefficient.ArmHeavyNext();
    }
}

/// <summary>튕기는 칼끝 — 평타 치명타가 둘레 5 m의 다른 적 N명(가까운 순)에게 튄다(치명 피해의 40% · 휘두름당 1번).</summary>
public sealed class RicochetAugment : MeleeAugmentEffect
{
    private const float Range = 5f, Ratio = 0.4f;
    public RicochetAugment(MeleeAugmentDef d, int r) : base(d, r) { }
    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (!report.IsCrit || !FirstHitOfSwing(report)) return;   // 치명 적중끼리만 견주니 「휘두름의 첫 치명」
        var p = ctx.Player;
        var src = Monster(report.Target);
        Vector3 from = report.Target.transform.position;
        int n = CombatQuery.GetNearbyEnemies(from, Range, p.gameObject, 16, Buf);
        int want = Mathf.RoundToInt(V), hit = 0;
        float dmg = report.DamageDealt * Ratio;
        for (int k = 0; k < want; k++)
        {
            int best = -1;
            float bestD = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var e = Buf[i];
                if (e == null || e.IsDead || e == src) continue;
                float d = (e.transform.position - from).sqrMagnitude;
                if (d < bestD) { bestD = d; best = i; }
            }
            if (best < 0) break;
            var t = Buf[best];
            Buf[best] = null;   // 다시 고르지 않게
            CombatQuery.DealSynergyDamage(t, dmg, p.gameObject, 1f, isCrit: true);
            Vector3 dir = t.transform.position - from;
            FormStrike.PlayVfx(WaveVfx, from + Vector3.up, dir, WaveVfxPerMeter * dir.magnitude);
            hit++;
        }
        if (hit > 0) Log($"튕기는 칼끝 — {hit}명");
    }
}

/// <summary>칼자국 — 평타 휘두름마다(헛쳐도) 앞 바닥에 1.5초 칼자국 · 0.5초마다 그 위 적을 벤다(바닥 띠 = 판정).</summary>
public sealed class BladeScarAugment : MeleeAugmentEffect
{
    private const float Life = 1.5f, Interval = 0.5f, Forward = 1.4f, Length = 2.2f, HalfWidth = 0.7f;
    private const int MaxScars = 6;
    private readonly List<(Vector3 from, Vector3 dir, float until, float next)> _scars = new(MaxScars);
    private int _seen = -1;
    public BladeScarAugment(MeleeAugmentDef d, int r) : base(d, r) { }
    public override void OnTick(ItemEffectContext ctx, float dt)
    {
        var p = ctx?.Player;
        if (p == null) return;
        int s = SwingSerial;
        if (s != _seen)
        {
            bool first = _seen < 0;
            _seen = s;
            if (!first && s >= 0 && IsActive(ctx))   // 처음 본 번호는 기준만 잡는다
            {
                if (_scars.Count >= MaxScars) _scars.RemoveAt(0);
                Vector3 dir = p.AimForward, from = p.transform.position + dir * (Forward - Length * 0.5f);
                _scars.Add((from, dir, Time.time + Life, Time.time + Interval));
                FormStrike.GroundStrip(from, dir, Length, HalfWidth + StripBody, Life);
            }
        }
        if (_scars.Count == 0) return;
        float dmg = Atk(ctx) * V * 0.01f;
        for (int i = _scars.Count - 1; i >= 0; i--)
        {
            var sc = _scars[i];
            if (Time.time >= sc.next)
            {
                int n = FormStrike.Line(p, sc.from, sc.dir, Length, HalfWidth, dmg, Buf);
                if (n > 0) Log($"칼자국 — 맞힘 {n}");
                sc.next += Interval;
                _scars[i] = sc;
            }
            if (Time.time >= sc.until) _scars.RemoveAt(i);
        }
    }
}

/// <summary>기습 — 평타를 2초 쉬었다 친 첫 타는 반드시 치명(사전 보정에서 확정 치명을 건다 — 이 피해의 치명 굴림이 쓴다) · 일반 몹 경직.</summary>
public sealed class AmbushAugment : MeleeAugmentEffect
{
    private const float Rest = 2f;
    private float _lastBasicAt = -999f;
    private bool _armedThisHit;
    public AmbushAugment(MeleeAugmentDef d, int r) : base(d, r) { }
    public override void OnPreDealDamage(ItemEffectContext ctx, ref DamagePacket pkt)
    {
        _armedThisHit = false;
        if (!IsBasic) return;
        bool ready = Time.time - _lastBasicAt >= Rest;
        _lastBasicAt = Time.time;
        var rs = ctx?.Player != null ? ctx.Player.RuntimeStats : null;
        if (!ready || rs == null || rs.ForceCritArmed) return;   // 다른 장치(무게 형)가 건 확정 치명은 건드리지 않는다
        rs.ArmForceCrit(0f);
        _armedThisHit = true;
    }
    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (!_armedThisHit) return;
        _armedThisHit = false;
        var mb = Monster(report.Target);
        if (mb != null && !mb.IsDead && mb.Grade != MonsterGrade.Boss && mb.Grade != MonsterGrade.Elite) mb.ApplyStun(V);
        Log($"기습 — 치명 {report.IsCrit}");
    }
}

/// <summary>사냥꾼의 숨 — 처치하면 둘레 3 m 적이 「사냥감」(2초 동안 받는 피해 +%).</summary>
public sealed class HuntMarkAugment : MeleeAugmentEffect
{
    private const float Radius = 3f, Seconds = 2f;
    public HuntMarkAugment(MeleeAugmentDef d, int r) : base(d, r) { }
    public override void OnKill(ItemEffectContext ctx, GameObject target)
    {
        var p = ctx?.Player;
        if (p == null || target == null) return;
        Vector3 at = target.transform.position;
        int n = CombatQuery.GetNearbyEnemies(at, Radius, p.gameObject, 16, Buf);
        int marked = 0;
        for (int i = 0; i < n; i++)
        {
            var e = Buf[i];
            if (e == null || e.IsDead) continue;
            e.ApplyDamageTakenAmp(V * 0.01f, Seconds, "aug_hunt");
            marked++;
        }
        FormStrike.GroundRing(at, Radius, RingSeconds);
        if (marked > 0) Log($"사냥꾼의 숨 — 사냥감 {marked}");
    }
}

/// <summary>회피 베기 — 회피로 지나간 길의 적을 벤다(시작 자리는 회피 시작 사건에서 잡는다).</summary>
public sealed class DodgeCutAugment : MeleeAugmentEffect
{
    private const float HalfWidth = 0.9f;
    private PlayerController _p;
    private Vector3 _from;
    public DodgeCutAugment(MeleeAugmentDef d, int r) : base(d, r) { }
    public override void OnActivate(ItemEffectContext ctx)
    {
        base.OnActivate(ctx);
        _p = ctx?.Player;
        if (_p != null) _p.OnDodgeStart += HandleDodgeStart;
    }
    public override void OnDeactivate()
    {
        if (_p != null) _p.OnDodgeStart -= HandleDodgeStart;
        _p = null;
    }
    public override void OnRollEnd(ItemEffectContext ctx)
    {
        var p = ctx?.Player;
        if (p == null) return;
        Vector3 dir = p.transform.position - _from;
        dir.y = 0f;
        float len = dir.magnitude;
        if (len < 0.5f) return;
        int n = FormStrike.Line(p, _from, dir, len, HalfWidth, Atk(ctx) * V * 0.01f, Buf);
        Wave(p, _from, dir / len, len, HalfWidth);
        Log($"회피 베기 — {len:0.0} m 맞힘 {n}");
    }
    private void HandleDodgeStart()
    {
        if (_p != null) _from = _p.transform.position;
    }
}

/// <summary>주문의 날 — E · R을 쓰면 다음 평타 3번(적중한 휘두름)이 앞 3 m 칼날 파동을 낸다.</summary>
public sealed class SpellBladeAugment : MeleeAugmentEffect
{
    private const int Charges = 3;
    private const float Length = 3f, HalfWidth = 0.9f;
    private int _charges;
    public SpellBladeAugment(MeleeAugmentDef d, int r) : base(d, r) { }
    public override void OnSkillUse(ItemEffectContext ctx, SkillType skill)
    {
        if (skill != SkillType.Q) _charges = Charges;
    }
    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (_charges <= 0 || !FirstHitOfSwing(report)) return;
        _charges--;
        var p = ctx.Player;
        int n = FormStrike.Line(p, p.transform.position, p.AimForward, Length, HalfWidth, Atk(ctx) * V * 0.01f, Buf);
        Wave(p, p.transform.position, p.AimForward, Length, HalfWidth);
        Log($"주문의 날 — 남은 {_charges} · 맞힘 {n}");
    }
}

/// <summary>마지막 의지 — 체력이 40% 아래로 떨어지면 1.5초 무적 · 둘레 3 m 적 경직(방마다 한 번 · 회복 없음).</summary>
public sealed class LastWillAugment : MeleeAugmentEffect
{
    private const float Below = 0.4f, Invuln = 1.5f, Radius = 3f;
    private bool _used;
    public LastWillAugment(MeleeAugmentDef d, int r) : base(d, r) { }
    public override void OnRoomEnter(ItemEffectContext ctx) => _used = false;
    public override void OnPostTakeDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (_used || ctx?.Player == null || report.DamageDealt <= 0f) return;
        ctx.RefreshHpRatio();
        if (ctx.HpRatio <= 0f || ctx.HpRatio >= Below) return;
        _used = true;
        var p = ctx.Player;
        p.SetInvincible(Invuln);
        int n = FormStrike.Radius(p, p.transform.position, Radius, 0f, Buf, V);
        Nova(p.transform.position, Radius, p.AimForward);
        Log($"마지막 의지 — 무적 {Invuln}초 · 경직 {n}");
    }
}

/// <summary>연격 표식 — 같은 적을 5번 이어 치면(2초 안 간격) 표식이 터진다.</summary>
public sealed class ComboMarkAugment : MeleeAugmentEffect
{
    private const int Needed = 5, MaxTracked = 32;
    private const float Gap = 2f, BurstRadius = 1.2f;
    private readonly Dictionary<MonsterBase, (int n, float at)> _hits = new();
    public ComboMarkAugment(MeleeAugmentDef d, int r) : base(d, r) { }
    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (report.IsRanged || report.ActionType != WeaponActionType.GroundLight) return;
        var mb = Monster(report.Target);
        if (mb == null || mb.IsDead) return;
        int n = _hits.TryGetValue(mb, out var h) && Time.time - h.at <= Gap ? h.n + 1 : 1;
        if (n < Needed)
        {
            if (_hits.Count >= MaxTracked) _hits.Clear();
            _hits[mb] = (n, Time.time);
            return;
        }
        _hits.Remove(mb);
        CombatQuery.DealSynergyDamage(mb, Atk(ctx) * V * 0.01f, ctx.Player.gameObject);
        Nova(mb.transform.position, BurstRadius, ctx.Player.AimForward);
        Log("연격 표식 — 터짐");
    }
}

/// <summary>처형인 — 체력이 문턱 아래인 일반 몹(정예 · 보스 제외)을 근접으로 치면 그 자리에서 처형.</summary>
public sealed class ExecutionerAugment : MeleeAugmentEffect
{
    public ExecutionerAugment(MeleeAugmentDef d, int r) : base(d, r) { }
    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (report.IsRanged) return;
        var mb = Monster(report.Target);
        if (mb == null || mb.IsDead || mb.Grade == MonsterGrade.Boss || mb.Grade == MonsterGrade.Elite || mb.EffectiveMaxHp <= 0) return;
        if ((float)mb.CurrentHp / mb.EffectiveMaxHp > V * 0.01f) return;
        CombatQuery.DealSynergyDamage(mb, mb.CurrentHp * 2f + 10f, ctx.Player.gameObject);
        FormStrike.PlayVfx(WaveVfx, mb.transform.position + Vector3.up, ctx.Player.AimForward, WaveVfxPerMeter * 2f);
        Log("처형인 — 처형");
    }
}

/// <summary>표식 퍼뜨리기 — 콤보 마지막 타로 유물 표식이 있는 적을 치면 그 표식 종류를 둘레 3 m 적 N명에게 1개씩 옮긴다(유물 빌드와 잇는다).</summary>
public sealed class MarkSpreadAugment : MeleeAugmentEffect
{
    private const float Radius = 3f;
    public MarkSpreadAugment(MeleeAugmentDef d, int r) : base(d, r) { }
    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (!FirstHitOfSwing(report) || !IsFinisherStep(ctx)) return;
        var src = RelicMarkStatus.Of(report.Target, false);
        if (src == null || src.IsEmpty) return;
        var p = ctx.Player;
        var srcMb = Monster(report.Target);
        Vector3 at = report.Target.transform.position;
        int n = CombatQuery.GetNearbyEnemies(at, Radius, p.gameObject, 16, Buf);
        int want = Mathf.RoundToInt(V), spread = 0;
        for (int i = 0; i < n && spread < want; i++)
        {
            var e = Buf[i];
            if (e == null || e.IsDead || e == srcMb) continue;
            var dst = RelicMarkStatus.Of(e.gameObject, true);
            if (dst == null) continue;
            if (src.Sunmark > 0)   dst.AddSunmark(1, src.SunmarkCap, ignoreGap: true);
            if (src.Blackspot > 0) dst.AddBlackspot(1, ignoreGap: true);
            if (src.Brand > 0)     dst.AddBrand(1, src.BrandCap, out _, ignoreGap: true);
            Vector3 dir = e.transform.position - at;
            FormStrike.PlayVfx(WaveVfx, at + Vector3.up, dir, WaveVfxPerMeter * dir.magnitude);
            spread++;
        }
        if (spread > 0) Log($"표식 퍼뜨리기 — {spread}명");
    }
}

/// <summary>학살의 연쇄 — 처치한 자리에서 칼바람이 일어 둘레를 벤다. 칼바람의 처치도 다시 일으킨다(0.25초 간격 · 대기 6개까지).</summary>
public sealed class SlaughterChainAugment : MeleeAugmentEffect
{
    private const float Radius = 2.5f, Spacing = 0.25f;
    private const int MaxQueue = 6;
    private readonly List<Vector3> _queue = new(MaxQueue);
    private float _nextAt;
    public SlaughterChainAugment(MeleeAugmentDef d, int r) : base(d, r) { }
    public override void OnKill(ItemEffectContext ctx, GameObject target)
    {
        if (target != null && _queue.Count < MaxQueue) _queue.Add(target.transform.position);
    }
    public override void OnTick(ItemEffectContext ctx, float dt)
    {
        if (_queue.Count == 0 || Time.time < _nextAt || ctx?.Player == null) return;
        Vector3 at = _queue[0];
        _queue.RemoveAt(0);
        _nextAt = Time.time + Spacing;
        int n = FormStrike.Radius(ctx.Player, at, Radius, Atk(ctx) * V * 0.01f, Buf);
        Nova(at, Radius, ctx.Player.AimForward);
        Log($"학살의 연쇄 — 맞힘 {n}");
    }
}

/// <summary>세 번째 칼(카타나) — 콤보 3타에 맞은 적마다(휘두름당 5명까지) 0.6초 뒤 X자 베기.</summary>
public sealed class ThirdBladeAugment : MeleeAugmentEffect
{
    private const int Step = 2, MaxPerSwing = 5;
    private const float Delay = 0.6f, CrossDeg = 45f, CrossLength = 2f;
    private readonly List<(MonsterBase m, float at, float dmg)> _pending = new(8);
    private readonly HashSet<MonsterBase> _seen = new();
    public ThirdBladeAugment(MeleeAugmentDef d, int r) : base(d, r) { }
    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (report.ActionType != WeaponActionType.GroundLight || report.IsRanged) return;
        if (FirstHitOfSwing(report)) _seen.Clear();
        if (SwingStep(ctx) != Step) return;
        var mb = Monster(report.Target);
        if (mb == null || mb.IsDead) return;
        if (_seen.Count >= MaxPerSwing || !_seen.Add(mb)) return;
        _pending.Add((mb, Time.time + Delay, Atk(ctx) * V * 0.01f));
    }
    public override void OnTick(ItemEffectContext ctx, float dt)
    {
        for (int i = _pending.Count - 1; i >= 0; i--)
        {
            var (m, at, dmg) = _pending[i];
            if (Time.time < at) continue;
            _pending.RemoveAt(i);
            if (m == null || m.IsDead || ctx?.Player == null) continue;
            CombatQuery.DealSynergyDamage(m, dmg, ctx.Player.gameObject);
            Vector3 c = m.transform.position + Vector3.up, f = ctx.Player.AimForward;
            Vector3 a = Quaternion.Euler(0f, CrossDeg, 0f) * f, b = Quaternion.Euler(0f, -CrossDeg, 0f) * f;
            FormStrike.PlayVfx(WaveVfx, c - a * (CrossLength * 0.5f), a, WaveVfxPerMeter * CrossLength);
            FormStrike.PlayVfx(WaveVfx, c - b * (CrossLength * 0.5f), b, WaveVfxPerMeter * CrossLength);
            Log("세 번째 칼 — X자 베기");
        }
    }
}

/// <summary>물 흐르듯(카타나) — 회피 뒤 1초 안 첫 적중이 회피 1칸을 돌려준다(V초에 한 번) — 회피와 평타를 끊김 없이 잇는다.</summary>
public sealed class FlowAugment : MeleeAugmentEffect
{
    private const float Window = 1f;
    private float _until = -999f, _readyAt;
    public FlowAugment(MeleeAugmentDef d, int r) : base(d, r) { }
    public override void OnRollEnd(ItemEffectContext ctx) => _until = Time.time + Window;
    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (Time.time > _until || Time.time < _readyAt || report.IsRanged) return;
        _until = -999f;
        _readyAt = Time.time + V;
        var p = ctx.Player;
        float max = p.RuntimeStats != null ? p.RuntimeStats.MaxStamina : 100f;
        float cost = p.CharacterData != null ? p.CharacterData.dodgeStaminaCost : max * 0.25f;
        p.Stamina?.Refund(cost, max);
        Log("물 흐르듯 — 회피 1칸");
    }
}

/// <summary>안개 칼날(무형검) — 무형참 칼날이 맞힌 자리에 안개가 1.5초 남아 0.5초마다 벤다(같은 자리 1 m 안은 하나만 · 6개까지).</summary>
public sealed class MistAugment : MeleeAugmentEffect
{
    private const float Life = 1.5f, Interval = 0.5f, Radius = 1.2f, Merge = 1f;
    private const int MaxZones = 6;
    private readonly List<(Vector3 c, float until, float next)> _zones = new(MaxZones);
    public MistAugment(MeleeAugmentDef d, int r) : base(d, r) { }
    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (report.ActionType != WeaponActionType.ESkill || report.Target == null) return;
        Vector3 c = report.Target.transform.position;
        for (int i = 0; i < _zones.Count; i++)
            if ((_zones[i].c - c).sqrMagnitude < Merge * Merge) return;   // 부채꼴 칼날 여럿이 같은 적에 — 안개는 하나
        if (_zones.Count >= MaxZones) _zones.RemoveAt(0);
        _zones.Add((c, Time.time + Life, Time.time + Interval));
        FormStrike.GroundRing(c, Radius, Life);
    }
    public override void OnTick(ItemEffectContext ctx, float dt)
    {
        if (_zones.Count == 0 || ctx?.Player == null) return;
        float dmg = Atk(ctx) * V * 0.01f;
        for (int i = _zones.Count - 1; i >= 0; i--)
        {
            var z = _zones[i];
            if (Time.time >= z.next)
            {
                int n = FormStrike.Radius(ctx.Player, z.c, Radius, dmg, Buf);
                if (n > 0) Log($"안개 칼날 — 맞힘 {n}");
                z.next += Interval;
                _zones[i] = z;
            }
            if (Time.time >= z.until) _zones.RemoveAt(i);
        }
    }
}

/// <summary>철벽 자세(대검) — 평타 동작 중 경직 면역 자세(받는 피해 −%). 스킬이 시작되면 내려놓아 스킬이 제 자세를 잡게 한다.</summary>
public sealed class BulwarkAugment : MeleeAugmentEffect
{
    private PlayerController _stanced;
    public BulwarkAugment(MeleeAugmentDef d, int r) : base(d, r) { }
    public override void OnTick(ItemEffectContext ctx, float dt)
    {
        var p = ctx?.Player;
        bool want = p != null && IsActive(ctx) && p.IsInAttackAction && !p.IsInSkillAction;
        if (want && _stanced == null)
        {
            p.BeginStance(this, V * 0.01f);
            if (p.IsStanceOwner(this)) { _stanced = p; Log("철벽 자세 — 섰다"); }
        }
        else if (!want && _stanced != null) Release();
    }
    public override void OnSkillUse(ItemEffectContext ctx, SkillType skill) => Release();
    public override void OnDeactivate() => Release();
    private void Release()
    {
        if (_stanced == null) return;
        _stanced.EndStance(this);
        _stanced = null;
    }
}

/// <summary>
/// 원소의 날 — 콤보 마지막 타가 룬 판에 가장 많이 놓인 속성으로 터진다(둘레 2 m · 그 속성 피해 · 휘두름당 1번) — 근접 평타와 룬 빌드를 잇는다.
/// 속성 상태: 불 화상 · 독 중독(둘 다 더 센 지속 피해만 남는다 — 룬 것을 덮지 않는다) · 번개 0.3초 기절(보스 제외) · 얼음 2초 30% 느림(룬 서리 쌓임과 따로) ·
/// 빛 = 다음 근접 타 확정 치명 · 어둠 = 3초 받는 피해 +15%.
/// 처음 안(마지막 타 적중 효과를 한 번 더 알림)은 룬마다 같은 대상 재발동 간격이 있어 룬 피해가 +7%에 그쳤다(10-15 실측 61) — 결정적인 폭발로 바꿨다.
/// </summary>
public sealed class ElementEdgeAugment : MeleeAugmentEffect
{
    private const float Radius = 2f, DotRatio = 0.15f, DotInterval = 0.5f, Stun = 0.3f, Slow = 0.3f, SlowSeconds = 2f, DarkAmp = 0.15f, DarkSeconds = 3f;
    private const int DotTicks = 4;
    private static readonly int[] s_count = new int[6];
    public ElementEdgeAugment(MeleeAugmentDef d, int r) : base(d, r) { }

    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (!FirstHitOfSwing(report) || !IsFinisherStep(ctx) || !TopElement(out var el)) return;
        var p = ctx.Player;
        Vector3 at = report.Target.transform.position;
        float atk = Atk(ctx), dmg = atk * V * 0.01f;
        int n = CombatQuery.GetNearbyEnemies(at, Radius, p.gameObject, 16, Buf), hits = 0;
        for (int i = 0; i < n; i++)
        {
            var e = Buf[i];
            if (e == null || e.IsDead) continue;
            CombatQuery.DealSynergyDamage(e, dmg, p.gameObject, 1f, false, el);
            if (!e.IsDead) ApplyStatus(e, el, atk, p);
            hits++;
        }
        if (el == RuneElement.Light && !p.RuntimeStats.ForceCritArmed) p.RuntimeStats.ArmForceCrit(0f);
        ElementVfxPlayer.PlayBurst(el, at, Radius);
        FormStrike.GroundRing(at, Radius, RingSeconds);
        Log($"원소의 날 — {el} 맞힘 {hits}");
    }

    /// <summary>룬 판에 가장 많이 놓인 속성(동점이면 먼저 센 것). 룬 판이 비었거나 속성이 없으면 false.</summary>
    public static bool TopElement(out RuneElement element)
    {
        element = RuneElement.Fire;
        var inv = GameRunBootstrapper.Instance?.Run?.ItemInventory;
        if (inv == null) return false;
        Array.Clear(s_count, 0, s_count.Length);
        foreach (var item in inv.PlacedItems)
            if (item != null && TryParse(item.element, out var e)) s_count[(int)e]++;
        int best = -1;
        for (int i = 0; i < s_count.Length; i++)
            if (s_count[i] > 0 && (best < 0 || s_count[i] > s_count[best])) best = i;
        if (best < 0) return false;
        element = (RuneElement)best;
        return true;
    }

    private static bool TryParse(string s, out RuneElement e)
    {
        switch (s)
        {
            case "FIRE":     e = RuneElement.Fire;     return true;
            case "ICE":      e = RuneElement.Ice;      return true;
            case "ELECTRIC": e = RuneElement.Electric; return true;
            case "GRASS":    e = RuneElement.Grass;    return true;
            case "LIGHT":    e = RuneElement.Light;    return true;
            case "DARK":     e = RuneElement.Dark;     return true;
            default:         e = RuneElement.Fire;     return false;
        }
    }

    private static void ApplyStatus(MonsterBase e, RuneElement el, float atk, PlayerController p)
    {
        switch (el)
        {
            case RuneElement.Fire:     e.Status.ApplyStrongestDot("burn", atk * DotRatio, DotInterval, DotTicks, p.gameObject); break;
            case RuneElement.Grass:    e.Status.ApplyStrongestDot("poison", atk * DotRatio, DotInterval, DotTicks, p.gameObject); break;
            case RuneElement.Electric: if (e.Grade != MonsterGrade.Boss) e.ApplyStun(Stun); break;
            case RuneElement.Ice:      e.Status.ApplySlow("aug_chill", Slow, SlowSeconds); break;
            case RuneElement.Dark:     e.ApplyDamageTakenAmp(DarkAmp, DarkSeconds, "vulnerable"); break;
        }
    }
}

/// <summary>검기 — 콤보 마지막 타 적중 때 앞으로 6 m 관통 검기(쌍검기면 ±20° 두 갈래 더).</summary>
public sealed class SwordWaveAugment : MeleeAugmentEffect
{
    private const float Length = 6f, HalfWidth = 0.9f, TwinSpreadDeg = 20f;
    public SwordWaveAugment(MeleeAugmentDef d, int r) : base(d, r) { }
    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (!FirstHitOfSwing(report) || !IsFinisherStep(ctx)) return;
        var p = ctx.Player;
        Vector3 from = p.transform.position, dir = p.AimForward;
        float dmg = Atk(ctx) * V * 0.01f;
        int n = FormStrike.Line(p, from, dir, Length, HalfWidth, dmg, Buf);
        Wave(p, from, dir, Length, HalfWidth);
        Log($"검기 — 맞힘 {n}");

        float twin = MeleeAugments.ValueOn(Weapon, MeleeAugmentCatalog.TwinWave);
        if (twin <= 0f) return;
        Vector3 a = Quaternion.Euler(0f, TwinSpreadDeg, 0f) * dir, b = Quaternion.Euler(0f, -TwinSpreadDeg, 0f) * dir;
        int n2 = FormStrike.Lines(p, from, a, b, Length, HalfWidth, dmg * twin * 0.01f, Buf);
        Log($"쌍검기 — 맞힘 {n2}");
        Wave(p, from, a, Length, HalfWidth);
        Wave(p, from, b, Length, HalfWidth);
    }
}

/// <summary>충격파 — 강타 · 치명 적중 때 대상 둘레 충격(휘두름당 1번).</summary>
public sealed class ImpactAugment : MeleeAugmentEffect
{
    public ImpactAugment(MeleeAugmentDef d, int r) : base(d, r) { }
    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (!(report.IsHeavy || report.IsCrit) || !FirstHitOfSwing(report)) return;
        Vector3 at = report.Target.transform.position;
        int n = FormStrike.Radius(ctx.Player, at, V2, Atk(ctx) * V * 0.01f, Buf);
        Nova(at, V2, ctx.Player.AimForward);
        Log($"충격파 — 맞힘 {n}");
    }
}

/// <summary>천둥 같은 일격 — 강타 적중 때 대상 뒤 4 m 관통 충격(휘두름당 1번).</summary>
public sealed class ThunderAugment : MeleeAugmentEffect
{
    private const float Length = 4f, HalfWidth = 0.8f;
    public ThunderAugment(MeleeAugmentDef d, int r) : base(d, r) { }
    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (!report.IsHeavy || !FirstHitOfSwing(report)) return;
        var p = ctx.Player;
        Vector3 at = report.Target.transform.position, dir = at - p.transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.01f) dir = p.AimForward;
        int n = FormStrike.Line(p, at, dir, Length, HalfWidth, Atk(ctx) * V * 0.01f, Buf);
        Wave(p, at, dir.normalized, Length, HalfWidth);
        Log($"천둥 — 맞힘 {n}");
    }
}

/// <summary>회전 베기 — 회피가 끝날 때 둘레 2.5 m 베기.</summary>
public sealed class SpinSlashAugment : MeleeAugmentEffect
{
    private const float Radius = 2.5f;
    public SpinSlashAugment(MeleeAugmentDef d, int r) : base(d, r) { }
    public override void OnRollEnd(ItemEffectContext ctx)
    {
        var p = ctx.Player;
        if (p == null) return;
        int n = FormStrike.Radius(p, p.transform.position, Radius, Atk(ctx) * V * 0.01f, Buf);
        Nova(p.transform.position, Radius, p.AimForward);
        Log($"회전 베기 — 맞힘 {n}");
    }
}

/// <summary>피격 반격 — 맞은 뒤 2초 안 첫 근접 적중 때 둘레 2.5 m 반격 베기.</summary>
public sealed class CounterAugment : MeleeAugmentEffect
{
    private const float Window = 2f, Radius = 2.5f;
    private float _until = -999f;
    public CounterAugment(MeleeAugmentDef d, int r) : base(d, r) { }
    public override void OnPostTakeDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (report.DamageDealt > 0f) _until = Time.time + Window;
    }
    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (Time.time > _until || report.IsRanged || report.Target == null) return;
        _until = -999f;
        var p = ctx.Player;
        int n = FormStrike.Radius(p, p.transform.position, Radius, Atk(ctx) * V * 0.01f, Buf);
        Nova(p.transform.position, Radius, p.AimForward);
        Log($"피격 반격 — 맞힘 {n}");
    }
}

/// <summary>바람 가르기(카타나) — 칼바람이 휘두름마다 V% 확률로 1칸 더 쌓인다(칼바람 형이 있을 때만 제시).</summary>
public sealed class GaleAugment : MeleeAugmentEffect
{
    public GaleAugment(MeleeAugmentDef d, int r) : base(d, r) { }
    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (!FirstHitOfSwing(report) || UnityEngine.Random.value * 100f >= V) return;
        if (WeaponFormRuntime.Of(ctx.Player)?.Current is WindbladeForm wf) { wf.AddStacks(1); Log($"바람 가르기 — 칼바람 {wf.Stacks}"); }
    }
}

/// <summary>지진(대검) — 콤보 마지막 타 적중 1초 뒤 그 앞 반경 3 m 땅울림(고리가 1초 동안 미리 보인다).</summary>
public sealed class QuakeAugment : MeleeAugmentEffect
{
    private const float Delay = 1f, Radius = 3f, Forward = 1.5f;
    private readonly List<(float at, Vector3 center, float dmg)> _pending = new(2);
    public QuakeAugment(MeleeAugmentDef d, int r) : base(d, r) { }
    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (!FirstHitOfSwing(report) || !IsFinisherStep(ctx)) return;
        var p = ctx.Player;
        Vector3 c = p.transform.position + p.AimForward * Forward;
        _pending.Add((Time.time + Delay, c, Atk(ctx) * V * 0.01f));
        FormStrike.GroundRing(c, Radius, Delay);   // 예고 = 판정
    }
    public override void OnTick(ItemEffectContext ctx, float dt)
    {
        for (int i = _pending.Count - 1; i >= 0; i--)
        {
            var (at, c, dmg) = _pending[i];
            if (Time.time < at) continue;
            _pending.RemoveAt(i);
            if (ctx?.Player == null) continue;
            int n = FormStrike.Radius(ctx.Player, c, Radius, dmg, Buf);
            FormStrike.PlayVfx(NovaVfx, c, ctx.Player.AimForward, NovaVfxPerMeter * Radius);
            Log($"지진 — 맞힘 {n}");
        }
    }
}

/// <summary>거인의 걸음(대검) — 평타 휘두름마다(맞히지 않아도) 발밑 2 m 진동파 · 보스 외 0.3초 경직.</summary>
public sealed class GiantStepAugment : MeleeAugmentEffect
{
    private const float Radius = 2f, Stun = 0.3f, Forward = 0.8f;
    private int _seen = -1;
    public GiantStepAugment(MeleeAugmentDef d, int r) : base(d, r) { }
    public override void OnTick(ItemEffectContext ctx, float dt)
    {
        int s = SwingSerial;
        if (s == _seen) return;
        bool first = _seen < 0;
        _seen = s;
        if (first || s < 0 || !IsActive(ctx)) return;   // 처음 본 번호는 기준만 잡는다
        var p = ctx.Player;
        Vector3 c = p.transform.position + p.AimForward * Forward;
        int n = FormStrike.Radius(p, c, Radius, Atk(ctx) * V * 0.01f, Buf, Stun);
        Nova(c, Radius, p.AimForward);
        Log($"거인의 걸음 — 휘두름 {s} 맞힘 {n}");
    }
}

/// <summary>형 없는 연타(무형검) — 콤보 마지막 타 적중 0.2초 뒤 앞 2.5 m를 한 번 더 벤다.</summary>
public sealed class FormlessFollowAugment : MeleeAugmentEffect
{
    private const float Delay = 0.2f, Length = 2.5f, HalfWidth = 1f;
    private float _at = -1f;
    private float _dmg;
    public FormlessFollowAugment(MeleeAugmentDef d, int r) : base(d, r) { }
    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (!FirstHitOfSwing(report) || !IsFinisherStep(ctx)) return;
        _at = Time.time + Delay;
        _dmg = Atk(ctx) * V * 0.01f;
    }
    public override void OnTick(ItemEffectContext ctx, float dt)
    {
        if (_at < 0f || Time.time < _at) return;
        _at = -1f;
        var p = ctx?.Player;
        if (p == null) return;
        int n = FormStrike.Line(p, p.transform.position, p.AimForward, Length, HalfWidth, _dmg, Buf);
        Wave(p, p.transform.position, p.AimForward, Length, HalfWidth);
        Log($"형 없는 연타 — 맞힘 {n}");
    }
}

/// <summary>무형일섬 잔상(무형검) — 무형일섬이 지나간 길을 도착 0.4초 뒤 잔상이 벤다.</summary>
public sealed class AfterimageAugment : MeleeAugmentEffect
{
    private const float Delay = 0.4f, HalfWidth = 1.2f;
    private float _at = -1f, _dmg;
    private Vector3 _from, _to;
    public AfterimageAugment(MeleeAugmentDef d, int r) : base(d, r) { }
    public override void OnActivate(ItemEffectContext ctx)
    {
        base.OnActivate(ctx);
        MeleeAugments.SkillDashed += HandleDash;
    }
    public override void OnDeactivate() => MeleeAugments.SkillDashed -= HandleDash;
    public override void OnTick(ItemEffectContext ctx, float dt)
    {
        if (_at < 0f || Time.time < _at) return;
        _at = -1f;
        var p = ctx?.Player;
        Vector3 dir = _to - _from;
        float len = dir.magnitude;
        if (p == null || len < 0.3f) return;
        int n = FormStrike.Line(p, _from, dir, len, HalfWidth, _dmg, Buf);
        Wave(p, _from, dir / len, len, HalfWidth);
        Log($"무형일섬 잔상 — {len:0.0} m 맞힘 {n}");
    }
    private void HandleDash(WeaponData w, SkillType slot, Vector3 from, Vector3 to, float arriveIn, float atk)
    {
        if (w != Weapon || slot != SkillType.R) return;
        _from = from; _to = to;
        _at = Time.time + arriveIn + Delay;
        _dmg = atk * V * 0.01f;
    }
}

/// <summary>지금 든 근접 무기의 증강 질의 — 스킬 · 형 · 각인 코드가 효과 객체 없이 바로 읽는 값.</summary>
public static class MeleeAugments
{
    /// <summary>스킬 돌진이 정해졌다(무기 · 칸 · 시작 · 끝 · 도착까지 초 · 그때 공격력) — 무형일섬 잔상이 듣는다.</summary>
    public static event Action<WeaponData, SkillType, Vector3, Vector3, float, float> SkillDashed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => SkillDashed = null;

    private static WeaponData Current()
    {
        var p = GameRunBootstrapper.Instance?.Run?.Player;
        return p != null && p.WeaponManager != null ? p.WeaponManager.CurrentWeaponData : null;
    }

    /// <summary>이 무기에서 이 증강의 단계(맞지 않는 갈래면 0).</summary>
    public static int RankOn(WeaponData w, string id)
    {
        int rank = w != null ? w.AugmentRank(id) : 0;
        if (rank <= 0) return 0;
        var def = MeleeAugmentCatalog.Get(id);
        return def != null && MeleeAugmentCatalog.FitsWeapon(def, w) ? rank : 0;
    }

    /// <summary>이 무기에서 이 증강의 단계 값(없으면 0).</summary>
    public static float ValueOn(WeaponData w, string id)
    {
        int rank = RankOn(w, id);
        return rank > 0 ? MeleeAugmentCatalog.Get(id).ValueAt(rank) : 0f;
    }

    /// <summary>이 무기에서 이 증강의 둘째 값(없으면 0).</summary>
    public static float Value2On(WeaponData w, string id)
    {
        int rank = RankOn(w, id);
        return rank > 0 ? MeleeAugmentCatalog.Get(id).Value2At(rank) : 0f;
    }

    /// <summary>지금 든 무기에서 이 증강의 단계 값(없으면 0).</summary>
    public static float ValueOnCurrent(string id) => ValueOn(Current(), id);

    /// <summary>스킬 한 갈래 피해 배율 — 환영 베기 강화(카타나 R). <see cref="SkillExecutionContext.CalculateDamage"/>가 곱한다.</summary>
    public static float SkillDamageMult(WeaponData w, SkillType slot)
        => slot == SkillType.R ? 1f + ValueOn(w, MeleeAugmentCatalog.KatanaPhantom) * 0.01f : 1f;

    /// <summary>스킬 돌진 알림(무형일섬 · 성광베기가 시작 때 부른다).</summary>
    public static void ReportSkillDash(WeaponData w, SkillType slot, Vector3 from, Vector3 to, float arriveIn, float atk)
        => SkillDashed?.Invoke(w, slot, from, to, arriveIn, atk);
}
