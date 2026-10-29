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
    protected const float WaveVfxPerMeter = 1.3f / 8f;   // 칼바람 풍절(8 m ↔ 1.3) — 붉은 검기도 같은 모양 · 이펙트 = 판정
    private const string WaveBlue = "vfx_form_windcut", WaveRed = "vfx_form_windcut_red";
    private const string NovaOrange = "NovaOrangeHit", NovaKatana = "KatanaDelayedCut", NovaGreat = "GreatswordEFinish";
    private const float NovaOrangePerMeter = 3f / 3.5f, NovaKatanaPerMeter = 1f / 11.56f, NovaGreatPerMeter = 0.26f;   // 반경 1 m당 배율(무게 내려찍기 3.5 m ↔ 3 · 실측 80)
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
    /// <summary>분신(<see cref="PlayerGhostStrike"/>)을 쓰는가 — 켜질 때 미리 만들어 둔다(무기를 바꿀 때마다 사본 3개를 다시 뜨지 않게).</summary>
    protected virtual bool UsesGhost => false;
    protected WeaponData Weapon => _weapon;
    /// <summary>진화 뒤 등 뒤 무형검 환영이 내는 무형검 증강인가(⑧-기획 A · 10-18).</summary>
    protected bool Phantom { get; private set; }

    /// <summary>
    /// 무기 계열 색(10-22 B4 사용자 「카타나 · 대검 공격 · 피격 이펙트 톤이 붉은색 · 파란색 왔다 갔다 — 무기별 같은 계열로」) —
    /// 카타나 = 붉은 검기 · 붉은 X / 대검 = 푸른 검기 · 흙먼지 고리 / 그 밖(무형검) = 예전 그림(푸른 검기 · 주황 노바).
    /// </summary>
    private static MeleeWeaponKind Family => MeleeAugmentCatalog.KindOf(MeleeAugments.CurrentWeapon);
    protected static string WaveVfx => Family == MeleeWeaponKind.Katana ? WaveRed : WaveBlue;
    protected static string NovaVfx => Family switch
    {
        MeleeWeaponKind.Katana     => NovaKatana,
        MeleeWeaponKind.Greatsword => NovaGreat,
        _                          => NovaOrange,
    };
    protected static float NovaVfxPerMeter => Family switch
    {
        MeleeWeaponKind.Katana     => NovaKatanaPerMeter,
        MeleeWeaponKind.Greatsword => NovaGreatPerMeter,
        _                          => NovaOrangePerMeter,
    };

    public override bool IsActive(ItemEffectContext ctx)
        => _weapon != null && ctx?.Player != null && ctx.Player.WeaponManager != null && ctx.Player.WeaponManager.CurrentWeaponData == _weapon;

    public override void OnActivate(ItemEffectContext ctx)
    {
        _runtime = WeaponFormRuntime.Of(ctx?.Player);
        if (Phantom) NamelessPhantom.Ensure(ctx?.Player);
        if (UsesGhost) PlayerGhostStrike.Warm(ctx?.Player);   // 분신은 애니 이벤트 밖에서 미리(10-24 ④) — 분신을 쓰는 증강만(10-26: 일섬 · 진화 뒤 안개)
    }

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
            if (def == null) continue;
            bool phantom = MeleeAugmentCatalog.RunsAsPhantom(def, w);   // 진화 뒤 무형검 증강 — 환영이 낸다(⑧-기획 A)
            if (!phantom && !MeleeAugmentCatalog.FitsWeapon(def, w)) continue;
            var e = def.Create(def, rank);
            if (e == null) continue;
            e._weapon = w;
            e.Phantom = phantom;
            into.Add(e);
            e.OnActivate(ctx);
        }
    }

    // ── 공용 도우미 ──
    protected static MonsterBase Monster(GameObject go) => go != null ? go.GetComponentInParent<MonsterBase>() : null;
    protected static float Atk(ItemEffectContext ctx) => ctx?.Player != null ? ctx.Player.RuntimeStats.GetEffectiveAttack(AttackStatKind.Melee) : 0f;
    protected static bool IsBasic => DamageSourceScope.Current == DamageSource.Basic;
    /// <summary>근접 평타(지상 · 공중) 또는 장비 스킬(E · R) 적중인가.</summary>
    protected static bool IsMeleeOrSkill(WeaponActionType t)
        => t == WeaponActionType.GroundLight || t == WeaponActionType.AirLight || t == WeaponActionType.ESkill || t == WeaponActionType.RSkill;

    /// <summary>이 휘두름의 콤보 단계 — 슬래시가 나올 때 단계(판정이 남은 동안 단계가 넘어가도 그대로). 슬래시보다 먼저 맞은 휘두름은 지금 콤보 단계.</summary>
    protected int SwingStep(ItemEffectContext ctx)
        => _runtime != null && !_earlySwing ? _runtime.SwingStep : ctx?.Player != null && ctx.Player.Combo != null ? ctx.Player.Combo.CurrentComboStep : -1;

    /// <summary>휘두름 일련번호(슬래시 이벤트마다 +1).</summary>
    protected int SwingSerial => _runtime != null ? _runtime.SwingSerial : -1;

    protected bool IsFinisherStep(ItemEffectContext ctx) => _weapon != null && SwingStep(ctx) >= Mathf.Max(1, _weapon.groundEndCount) - 1;

    /// <summary>이 휘두름에 카타나 형 풍절이 나간다(가득 · 막타 — 아직 안 나갔거나 방금 나갔다) — 같은 줄 검기를 또 내지 않는다(10-24 ③).</summary>
    protected bool WindReleasesThisSwing(ItemEffectContext ctx)
        => _runtime?.Current is WindbladeForm wf && (wf.ReleasedSerial == SwingSerial || (wf.IsFull && IsFinisherStep(ctx)));

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

    /// <summary>환영 몫이면 등 뒤 환영이 그쪽으로 한 번 가른다(그림은 이 증강의 검기 · 바닥 띠가 맡는다).</summary>
    protected void PhantomSwing(PlayerController p, Vector3 dir)
    {
        if (Phantom) NamelessPhantom.Swing(p, dir);
    }



    /// <summary>
    /// 증강 검기 한 줄(10-23 W1 · W2) — 분신이 나와 휘두르고, 그 칼이 닿는 순간 검기 · 바닥 띠가 나가 앞 끝이 지나가는 적을 그때 벤다.
    /// 예전엔 발동 순간 피해를 한꺼번에 주고 검기 · 분신을 같은 순간에 띄워 「검기가 피해를 못 준다」 · 「분신이 휘두른 것 같지 않다」로 읽혔다(실측 86).
    /// 10-26 무기 개편: 「추가 검기」 증강을 걷어 일섬만 쓴다 — 분신은 그림자 실루엣(D3 · 그림자 파트너와 같은 모습).
    /// </summary>
    protected static void WaveStrike(PlayerController p, Vector3 from, Vector3 dir, float length, float halfWidth, float damage,
                                     HashSet<MonsterBase> hitOnce = null, Action<int> done = null)
    {
        float lead = PlayerGhostStrike.ForWave(p, from, dir, PlayerGhostStrike.ShadowTint);
        FormStrike.LineTravel(p, from, dir, length, halfWidth, damage, WaveVfx, lead, hitOnce, done, StripBody, StripSeconds);
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
        // 그 자리 베기 — 예전엔 날아가는 검기를 띄워 피해 없는 검기가 뒤로 날아갔다(10-23 W1 「나오는 검기가 데미지를 주지 못한다」)
        FormStrike.PlayVfx(NovaVfx, mb.transform.position + Vector3.up * 0.5f, ctx.Player.AimForward, NovaVfxPerMeter * 1.2f);
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
            // 표식이 옮겨 가는 줄기 — 날아가는 검기는 피해가 없어 「검기가 피해를 못 준다」로 읽혔다(10-23 W1)
            ElementVfxPlayer.PlayBeam(src.Brand > 0 ? RuneElement.Dark : RuneElement.Fire, at + Vector3.up * 0.8f, e.transform.position + Vector3.up * 0.8f);
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

/// <summary>안개 칼날(무형검) — E가 맞힌 자리에 안개가 1.5초 남아 0.5초마다 벤다(같은 자리 1 m 안은 하나만 · 6개까지). 어느 무기 E에나 — 진화 뒤엔 환영이 긋는다(10-24).</summary>
public sealed class MistAugment : MeleeAugmentEffect
{
    protected override bool UsesGhost => Phantom;   // 진화 뒤 환영 몫만 분신을 쓴다
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
        // 진화 뒤(환영 몫) — 다른 무형검 증강처럼 투명 분신이 안개 자리에서 긋는다(10-24 ⑤ · 떠 있는 무형검만 플레이어 앞에서 휘둘렀다)
        if (ctx?.Player != null) PhantomSwing(ctx.Player, c - ctx.Player.transform.position);   // 등 뒤 떠 있는 무형검도 그쪽으로 가른다
        if (Phantom && ctx?.Player != null) PlayerGhostStrike.ForWave(ctx.Player, c, c - ctx.Player.transform.position);
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
                if (n > 0) Log($"안개 칼날{(Phantom ? "(환영)" : "")} — 맞힘 {n}");
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
        // 바닥 띠 없이 검기만 — 대검 평타는 늘 강타라 매 타 띠가 깔렸다(10-16 사용자 「기본 공격마다 가이드라인」) · 날아가며 벤다(10-23 W1)
        FormStrike.LineTravel(p, at, dir, Length, HalfWidth, Atk(ctx) * V * 0.01f, WaveVfx, 0f, null, n => Log($"천둥 — 맞힘 {n}"), StripBody);   // 그림 폭 = 판정(몸 반경 포함 · 띠는 없음)
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

// ══ 전설(10-24 W7 · 설계 기획/RelicFairy_증강등급_전설_설계_20261023.md 권장 A) — 단계 없음 · 싸우는 방식을 바꾸는 규칙 ══

/// <summary>전설 바탕 — 발동마다 사건(e2 자막 · HUD) · 런에서 처음 터질 때만 머리 위 이름 한 번(매번 띄우면 화면이 시끄럽다).</summary>
public abstract class LegendAugment : MeleeAugmentEffect
{
    private static readonly HashSet<string> s_shown = new();
    private static readonly Color NameColor = new(1f, 0.62f, 0.38f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => s_shown.Clear();

    /// <summary>새 런 — 첫 발동 이름을 다시 띄운다.</summary>
    public static void ResetForRun() => s_shown.Clear();

    protected LegendAugment(MeleeAugmentDef d, int r) : base(d, r) { }

    protected void Proc(PlayerController p)
    {
        MeleeAugments.RaiseLegendProcced(Def.Id);
        if (p == null || !s_shown.Add(Def.Id)) return;
        RelicFloatText.Show(p.transform.position + Vector3.up * 0.6f, Def.Name, NameColor);
    }
}

/// <summary>
/// L2 멈춘 시간 — 저스트 회피 순간 둘레 8 m 적이 2초 멈춘다(보스 · 정예는 이동 · 공격 50% 느려짐) · 그동안 근접 평타 적중마다 같은 피해로 한 번 더 벤다.
/// 고리 = 멈추는 범위(판정).
/// </summary>
public sealed class StoppedTimeLegend : LegendAugment
{
    private const float Radius = 8f, Seconds = 2f, BossSlow = 0.5f;
    private const string StatusId = "aug_legend_time";
    private PlayerController _p;
    private float _until = -999f;
    public StoppedTimeLegend(MeleeAugmentDef d, int r) : base(d, r) { }
    public override void OnActivate(ItemEffectContext ctx)
    {
        base.OnActivate(ctx);
        _p = ctx?.Player;
        if (_p != null) _p.OnPerfectDodge += HandlePerfectDodge;
    }
    public override void OnDeactivate()
    {
        if (_p != null) _p.OnPerfectDodge -= HandlePerfectDodge;
        _p = null;
    }
    private void HandlePerfectDodge(float _)
    {
        var p = _p;
        if (p == null || p.WeaponManager == null || p.WeaponManager.CurrentWeaponData != Weapon) return;   // 이 무기를 들고 있을 때만
        _until = Time.time + Seconds;
        Vector3 at = p.transform.position;
        int n = CombatQuery.GetNearbyEnemies(at, Radius, p.gameObject, 32, Buf), frozen = 0;
        for (int i = 0; i < n; i++)
        {
            var e = Buf[i];
            if (e == null || e.IsDead) continue;
            if (e.Grade == MonsterGrade.Boss || e.Grade == MonsterGrade.Elite)
            {
                e.Status.ApplySlow(StatusId, BossSlow, Seconds);
                e.Status.ApplyAttackSlow(StatusId, BossSlow, Seconds);
            }
            else e.ApplyStun(Seconds);
            frozen++;
        }
        FormStrike.GroundRing(at, Radius, RingSeconds);
        Log($"멈춘 시간 — {frozen}명");
        Proc(p);
    }
    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (Time.time > _until || report.IsRanged || report.ActionType != WeaponActionType.GroundLight || report.Target == null || report.DamageDealt <= 0f) return;
        CombatQuery.DealSynergyDamage(report.Target, report.DamageDealt * V * 0.01f, ctx.Player.gameObject);   // 같은 칼로 한 번 더(2차 피해라 다시 이 훅을 부르지 않는다)
    }
}

/// <summary>
/// L3 끝없는 칼날 — 치명이 난 평타 휘두름마다 치명 피해 +V% 쌓임(최대 10) · 치명이 하나도 없는 휘두름은 쌓인 것의 절반을 날린다 · 스킬 치명도 하나 쌓는다(스킬은 깎지 않는다).
/// 휘두름이 끝났는지는 다음 휘두름 번호로 안다(그 휘두름의 적중을 다 모은 뒤 셈). 가득 차면 이름 한 번.
/// 10-24: 예전엔 적중마다 따져 여러 적을 한 번에 치면 치명 아닌 적중이 섞여 금방 깎였다(점검) — 평타 한 번 = 한 번 판정.
/// </summary>
public sealed class EndlessEdgeLegend : LegendAugment
{
    private const int MaxStacks = 10;
    private int _stacks, _swing = -1;
    private bool _swingHit, _swingCrit;
    public EndlessEdgeLegend(MeleeAugmentDef d, int r) : base(d, r) { }
    public int Stacks => _stacks;
    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (report.IsRanged || report.Target == null || report.DamageDealt <= 0f) return;
        if (report.ActionType == WeaponActionType.GroundLight)
        {
            Settle(ctx);   // 새 휘두름의 첫 적중이면 앞 휘두름부터 센다
            _swingHit = true;
            _swingCrit |= report.IsCrit;
            return;
        }
        if (report.IsCrit) Add(ctx.Player);   // 스킬 치명
    }
    public override void OnTick(ItemEffectContext ctx, float dt) => Settle(ctx);

    /// <summary>휘두름 번호가 바뀌었으면 앞 휘두름을 센다 — 치명이 있었으면 +1, 맞히고도 치명이 없었으면 절반(헛친 휘두름은 그대로).</summary>
    private void Settle(ItemEffectContext ctx)
    {
        int s = SwingSerial;
        if (s == _swing) return;
        if (_swingHit)
        {
            if (_swingCrit) Add(ctx?.Player);
            else _stacks /= 2;
        }
        _swing = s;
        _swingHit = _swingCrit = false;
    }

    private void Add(PlayerController p)
    {
        if (_stacks >= MaxStacks) return;
        _stacks++;
        if (_stacks == MaxStacks) { Log("끝없는 칼날 — 가득"); Proc(p); }
    }
    public override void ContributeDynamicStats(ItemEffectContext ctx, ref ItemDynamicStats dyn)
    {
        if (IsActive(ctx)) dyn.critDamage += _stacks * V * 0.01f;
    }
}

/// <summary>
/// L5 일섬(카타나) — E 돌진(성광베기)이 지나간 길을 도착 0.4초 뒤 분신이 한 번 더 벤다(공격력 V%) · 그 길에서 처치하면 E 재사용 대기 초기화.
/// 성광베기 추가타 · 폭발이 벤 적을 길 밖으로 밀어내므로(넉백) 돌진이 벤 적을 기억해 두었다가, 길 밖으로 밀려난 그 적도 그 자리에서 벤다(실측 90 ⑤).
/// </summary>
public sealed class IssenLegend : LegendAugment
{
    protected override bool UsesGhost => true;   // 지나간 길 다시 베기 = 그림자 분신
    private const float Delay = 0.4f, HalfWidth = 1.2f, CutLength = 2f;
    private float _at = -1f, _dmg;
    private Vector3 _from, _to;
    private readonly HashSet<MonsterBase> _dashed = new();   // 이번 돌진이 벤 적(일섬이 나갈 때까지 모은다)
    private float _resetUntil = -1f;   // 처치했다 — 스킬이 끝나면 E 초기화(스킬 끝의 쿨 시작이 덮지 않게 · 10-24 ⑥)
    public IssenLegend(MeleeAugmentDef d, int r) : base(d, r) { }
    public override void OnActivate(ItemEffectContext ctx)
    {
        base.OnActivate(ctx);
        MeleeAugments.SkillDashed += HandleDash;
    }
    public override void OnDeactivate() => MeleeAugments.SkillDashed -= HandleDash;
    private void HandleDash(WeaponData w, SkillType slot, Vector3 from, Vector3 to, float arriveIn, float atk)
    {
        if (w != Weapon || slot != SkillType.E) return;
        _from = from; _to = to;
        _at = Time.time + arriveIn + Delay;
        _dmg = atk * V * 0.01f;
        _dashed.Clear();
    }
    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (_at < 0f || report.ActionType != WeaponActionType.ESkill) return;
        var mb = Monster(report.Target);
        if (mb != null && !mb.IsDead) _dashed.Add(mb);
    }
    public override void OnTick(ItemEffectContext ctx, float dt)
    {
        var pl = ctx?.Player;
        if (_resetUntil > 0f && pl != null && !pl.IsInSkillAction)
        {
            if (Time.time <= _resetUntil) { pl.CooldownTracker.ResetCooldown(SkillType.E); Log("일섬 — E 초기화(스킬 끝)"); }
            _resetUntil = -1f;
        }
        if (_at < 0f || Time.time < _at) return;
        _at = -1f;
        var p = ctx?.Player;
        if (p == null) return;
        Vector3 dir = _to - _from;
        dir.y = 0f;
        float len = dir.magnitude;
        if (len < 0.3f) return;
        var once = new HashSet<MonsterBase>();
        var dashed = new List<MonsterBase>(_dashed);   // 다음 돌진이 지우기 전에 이번 몫을 떼어 둔다
        _dashed.Clear();
        float dmg = _dmg;
        Vector3 f = dir / len;
        WaveStrike(p, _from, f, len, HalfWidth, dmg, once, n =>
        {
            foreach (var m in dashed)   // 길 밖으로 밀려난 돌진 표적 — 그 자리 베기
            {
                if (m == null || m.IsDead || once.Contains(m)) continue;
                once.Add(m);
                CombatQuery.DealSynergyDamage(m, dmg, p.gameObject);
                FormStrike.PlayVfx(WaveVfx, m.transform.position + Vector3.up - f * (CutLength * 0.5f), f, WaveVfxPerMeter * CutLength);
                n++;
            }
            int kills = 0;
            foreach (var m in once) if (m != null && m.IsDead) kills++;
            if (kills > 0 && p != null)
            {
                if (p.IsInSkillAction) _resetUntil = Time.time + 3f;   // 아직 성광베기 중 — 끝나면(쿨 시작 뒤) 초기화
                else p.CooldownTracker.ResetCooldown(SkillType.E);
            }
            Log($"일섬 — {len:0.0} m 맞힘 {n} · 처치 {kills}{(kills > 0 ? " · E 초기화" : "")}");
        });
        Proc(p);
    }
}

/// <summary>
/// L6 대지 가르기(대검) — 콤보 마지막 타 적중 때 앞 8 m 균열(바닥 띠 = 판정)을 남기고 0.6초 뒤 솟구친다(공격력 V% · 보스 · 정예 외 0.8초 띄움(기절)).
/// 솟구침 그림은 균열을 따라 2 m마다 · 균열 폭에 맞춘다(이펙트 = 판정).
/// </summary>
public sealed class RiftLegend : LegendAugment
{
    private const float Length = 8f, HalfWidth = 1.2f, Delay = 0.6f, Launch = 0.8f, Spacing = 2f;
    private readonly List<(float at, Vector3 from, Vector3 dir, float dmg)> _pending = new(2);
    public RiftLegend(MeleeAugmentDef d, int r) : base(d, r) { }
    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (!FirstHitOfSwing(report) || !IsFinisherStep(ctx)) return;
        var p = ctx.Player;
        Vector3 from = p.transform.position, dir = p.AimForward;
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-4f) return;
        dir.Normalize();
        _pending.Add((Time.time + Delay, from, dir, Atk(ctx) * V * 0.01f));
        FormStrike.GroundStrip(from, dir, Length, HalfWidth, Delay);   // 예고 = 판정
    }
    public override void OnTick(ItemEffectContext ctx, float dt)
    {
        for (int i = _pending.Count - 1; i >= 0; i--)
        {
            var (at, from, dir, dmg) = _pending[i];
            if (Time.time < at) continue;
            _pending.RemoveAt(i);
            var p = ctx?.Player;
            if (p == null) continue;
            int n = FormStrike.Line(p, from, dir, Length, HalfWidth, dmg, Buf);
            for (int k = 0; k < Buf.Count; k++)
            {
                var e = Buf[k];
                if (e == null || e.IsDead || e.Grade == MonsterGrade.Boss || e.Grade == MonsterGrade.Elite) continue;
                if (FormStrike.DistToSegmentXZ(e.transform.position, from, from + dir * Length) > HalfWidth + CombatQuery.BodyReach(e)) continue;
                e.ApplyStun(Launch);
            }
            for (float d = Spacing * 0.5f; d < Length; d += Spacing)
                FormStrike.PlayVfx(NovaVfx, from + dir * d, dir, NovaVfxPerMeter * HalfWidth);
            Log($"대지 가르기 — 맞힘 {n}");
            Proc(p);
        }
    }
}

/// <summary>
/// 그림자 파트너(카타나 · 10-26 무기 개편) — 곁의 그림자(<see cref="ShadowPartner"/> · 동작을 따라 하는 분신)가 평타 · 스킬(E · R)을 조금 늦게 따라 벤다: 분신마다 그 타격의 값%(재련할수록 +2%p).
/// 재련 2 · 4회째에 분신이 하나씩 는다(<see cref="CloneCount"/> 1 → 3 · 10-26 사용자 「추가 재련 시 분신을 추가」).
/// 그림자는 애니메이터를 그대로 따라 하므로 스킬 동작도 같이 하고, 스킬 타격은 약한 계수(<see cref="SkillShare"/> = 평타 몫의 절반)로 따라 벤다(10-26 사용자 「분신도 따라 스킬을 쓰되 약한 계수」).
/// 추가 피해라 적중 효과(룬 · 유물 발동)는 붙지 않는다(D5). 평타는 맞힌 분신 자리에 옅은 어두운 베기가 난다(<see cref="ShadowPartner.EchoSlash"/>).
/// </summary>
public sealed class ShadowPartnerAugment : MeleeAugmentEffect
{
    private const float SkillShare = 0.5f;
    private PlayerController _player;
    private ShadowPartner _sp;   // 틱마다 찾지 않는다(10-26 검토 M5)

    public ShadowPartnerAugment(MeleeAugmentDef d, int r) : base(d, r) { }

    public override void OnActivate(ItemEffectContext ctx)
    {
        base.OnActivate(ctx);
        _player = ctx?.Player;
    }

    public override void OnDeactivate()
    {
        if (_player != null && _player.TryGetComponent<ShadowPartner>(out var sp)) sp.Wanted = false;
    }

    public override void OnTick(ItemEffectContext ctx, float deltaTime)
    {
        if (ctx?.Player == null) return;
        if (_sp == null) _sp = ShadowPartner.Ensure(ctx.Player);
        _sp.Wanted = IsActive(ctx);
        _sp.Count  = CloneCount(Weapon);
    }

    /// <summary>분신 수 — 진화 뒤 재련 0 ~ 1회 1 · 2 ~ 3회 2 · 4회 3.</summary>
    public static int CloneCount(WeaponData w) => Mathf.Min(ShadowPartner.MaxClones, 1 + MeleeAugmentCatalog.ReforgesSinceEvolution(w) / 2);

    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (!IsMeleeOrSkill(report.ActionType) || report.IsRanged || report.DamageDealt <= 0f || report.Target == null || ctx?.Player == null) return;
        var p = ctx.Player;
        var target = report.Target;
        bool skill = report.ActionType == WeaponActionType.ESkill || report.ActionType == WeaponActionType.RSkill;
        float dmg = report.DamageDealt * Def.ValueFor(Weapon) * 0.01f * (skill ? SkillShare : 1f);
        int clones = CloneCount(Weapon);
        for (int i = 0; i < clones; i++)
        {
            int slot = i;
            FormStrike.After(p, ShadowPartner.DelayOf(slot), () =>   // 그 분신의 칼이 닿을 때(분신마다 늦음이 다르다)
            {
                if (target == null || !target.activeInHierarchy) return;
                CombatQuery.DealSynergyDamage(target, dmg, p.gameObject);
                if (!skill && _sp != null) _sp.EchoSlash(slot);   // 맞힌 분신 자리에 옅은 어두운 베기(10-26 · 스킬은 적중이 많아 그림 없음)
                Log($"그림자 — {dmg:0.0}{(skill ? " · 스킬" : "")} · 분신 {slot + 1}");
            });
        }
    }
}

/// <summary>
/// 검풍(카타나 · 10-26 무기 개편) — 적을 맞힌 평타 휘두름 · 스킬(E · R) 한 번마다 값% 확률로 앞 6 m 관통 검기(공격력 둘째 값%) · 재련할수록 확률 +4%p · 피해 +5%p.
/// 스킬은 여러 타가 나와도 한 번 시전에 한 번만 굴린다(E · R을 쓸 때 걸어 두고 그 시전의 첫 적중에 쓴다 · 10-26 사용자 「카타나도 증강 내용이 스킬에 적용」).
/// 검기 분신(10-26 사용자 「분신 2종 — 검기를 날리는 분신 · 행동을 따라 하는 분신 · 엄청난 속도에 분신처럼 보인다는 컨셉」) — 발동하면 옆(좌우 번갈아)에
/// 잔상 분신(<see cref="PlayerGhostStrike.ForWave"/> · 카타나 붉은 잔상 — 그림자 파트너의 어두운 실루엣과 다른 모습)이 나타나 앞으로 휘두르고, 그 칼이 닿는 순간 검기가 나간다.
/// 풍절이 나가는 휘두름에는 내지 않는다.
/// </summary>
public sealed class SwordGustAugment : MeleeAugmentEffect
{
    private const float Length = 6f, HalfWidth = 0.9f, SideOffset = 0.9f;
#if UNITY_EDITOR
    /// <summary>실측 99 D — 확률을 이 값(%)으로(−1 = 증강 값).</summary>
    public static float ProbeChance = -1f;
#endif
    private bool _skillArmed;   // E · R을 썼다 — 그 시전의 첫 적중에 한 번 굴린다(10-26 검토 I5 · 옛 1초 창은 E 직후 R을 못 굴렸다)
    private bool _left;

    public SwordGustAugment(MeleeAugmentDef d, int r) : base(d, r) { }

    protected override bool UsesGhost => true;   // 잔상 분신을 미리 만들어 둔다(애니 이벤트 안에서 만들면 거부된다)

    public override void OnSkillUse(ItemEffectContext ctx, SkillType skill)
    {
        if (skill == SkillType.E || skill == SkillType.R) _skillArmed = true;
    }

    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (ctx?.Player == null) return;
        bool skill = report.ActionType == WeaponActionType.ESkill || report.ActionType == WeaponActionType.RSkill;
        if (skill)
        {
            if (report.IsRanged || report.Target == null || !_skillArmed) return;
            _skillArmed = false;
        }
        else
        {
            if (!FirstHitOfSwing(report)) return;
            if (WindReleasesThisSwing(ctx)) { Log("검풍 — 풍절 휘두름이라 건너뜀"); return; }
        }
        Log(skill ? "검풍 굴림(스킬)" : "검풍 굴림");
        float chance = Def.ValueFor(Weapon);
#if UNITY_EDITOR
        if (ProbeChance >= 0f) chance = ProbeChance;
#endif
        if (UnityEngine.Random.value * 100f >= chance) return;
        var p = ctx.Player;
        float atk = Atk(ctx);
        float dmg = atk * Def.Value2For(Weapon) * 0.01f;
        Vector3 fw = p.AimForward;
        fw.y = 0f;
        fw = fw.sqrMagnitude > 0.0001f ? fw.normalized : p.transform.forward;
        _left = !_left;
        Vector3 from = p.transform.position + Vector3.Cross(Vector3.up, fw) * (_left ? -SideOffset : SideOffset);
        float lead = PlayerGhostStrike.ForWave(p, from, fw);   // 옆에 잔상 분신 — 휘두른 칼이 닿을 때 검기가 나간다
        FormStrike.LineTravel(p, from, fw, Length, HalfWidth, dmg, WaveVfx, lead, null,
                              n => Log($"검풍 — 피해 {dmg:0.0} · 공격력 {atk:0.0} · 맞힘 {n}"), StripBody, StripSeconds);
    }
}

/// <summary>
/// 칼끝 베기(대검 · 10-26 R 개편 범위 규칙) — 평타가 판정 반경 바깥 35% 띠(칼끝)에 걸린 적을 맞히면 그 타격의 값%(재련할수록 +5%p)를 더 준다.
/// 띠는 반경에 비례하므로 범위가 넓을수록(속성 검 ×1.3 · 「거대한 칼날」) 넓고 멀다 — 거리를 잡고 칼끝으로 베는 플레이를 보상한다.
/// 거리 = 몸 가장자리까지(큰 몬스터도 칼끝에 닿으면 칼끝). 추가 피해라 적중 효과는 붙지 않는다.
/// 지상 평타만 — 공중 베기는 판정 모양(반경 · 앞으로 치우침)이 달라 띠가 맞지 않는다(10-26 검토 M4).
/// </summary>
public sealed class TipStrikeAugment : MeleeAugmentEffect
{
    private const float Band = 0.35f, FlashScale = 0.6f;
    private const string FlashKey = "SwordStrikeFlash";

    public TipStrikeAugment(MeleeAugmentDef d, int r) : base(d, r) { }

    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (report.ActionType != WeaponActionType.GroundLight
            || report.IsRanged || report.DamageDealt <= 0f || report.Target == null || ctx?.Player == null) return;
        var p = ctx.Player;
        float r = GiantSwordBuff.SwingRadiusFor(p, SwingStep(ctx));
        var mb = Monster(report.Target);
        Vector3 d = report.Target.transform.position - p.transform.position;
        d.y = 0f;
        float edge = d.magnitude - (mb != null ? mb.BodyRadius : 0f);
        if (edge < r * (1f - Band)) return;
        float bonus = report.DamageDealt * Def.ValueFor(Weapon) * 0.01f;
        CombatQuery.DealSynergyDamage(report.Target, bonus, p.gameObject);
        FormStrike.PlayVfx(FlashKey, report.Target.transform.position + Vector3.up * 0.8f, d, FlashScale);   // 칼끝 섬광 — 띠에 걸렸다는 신호
        Log($"칼끝 — 거리 {edge:0.00} / 반경 {r:0.00} · +{bonus:0.0}");
    }
}

/// <summary>
/// 휩쓸기(대검 · 10-26 R 개편 범위 규칙) — 한 휘두름에 2명 이상 맞히면 첫 적 뒤 한 명마다: 속성 검(R) 중이면 지속 +둘째 값초(시전마다 최대 3초 ·
/// <see cref="GiantSwordBuff.Extend"/>), 아니면 R 재사용 대기 −값초(휘두름마다 최대 1초). 재련할수록 −0.05초 · +0.03초.
/// 넓게 휩쓸수록 R이 자주 · 오래 켜지고, 켜지면 범위가 ×1.3 넓어져 더 많이 맞힌다(순환).
/// </summary>
public sealed class SweepAugment : MeleeAugmentEffect
{
    private const float MaxCutPerSwing = 1f;
    private readonly HashSet<int> _hitThisSwing = new();
    private int _serial = int.MinValue;
    private float _cutThisSwing;

    public SweepAugment(MeleeAugmentDef d, int r) : base(d, r) { }

    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if ((report.ActionType != WeaponActionType.GroundLight && report.ActionType != WeaponActionType.AirLight)
            || report.IsRanged || report.DamageDealt <= 0f || report.Target == null || ctx?.Player == null) return;
        int s = SwingSerial;
        if (s < 0) return;   // 휘두름 번호를 못 받는다(받는 쪽 없음) — 휘두름마다 셀 수 없다(10-26 검토 M3)
        if (s != _serial) { _serial = s; _hitThisSwing.Clear(); _cutThisSwing = 0f; }
        if (!_hitThisSwing.Add(report.Target.GetInstanceID()) || _hitThisSwing.Count < 2) return;   // 첫 적 · 같은 적은 세지 않는다
        var p = ctx.Player;
        if (GiantSwordBuff.IsActive(p))
        {
            float add = GiantSwordBuff.Extend(p, Def.Value2For(Weapon));
            if (add > 0f) Log($"휩쓸기 — {_hitThisSwing.Count}명째 · 속성 검 +{add:0.00}초");
            return;
        }
        float cut = Mathf.Min(Def.ValueFor(Weapon), MaxCutPerSwing - _cutThisSwing);
        if (cut <= 0f || p.CooldownTracker == null || p.CooldownTracker.IsReady(SkillType.R)) return;
        p.CooldownTracker.ReduceCooldown(SkillType.R, cut);
        _cutThisSwing += cut;
        Log($"휩쓸기 — {_hitThisSwing.Count}명째 · R 대기 −{cut:0.00}초");
    }
}

/// <summary>지금 든 근접 무기의 증강 질의 — 스킬 · 형 · 각인 코드가 효과 객체 없이 바로 읽는 값.</summary>
public static class MeleeAugments
{
    /// <summary>스킬 돌진이 정해졌다(무기 · 칸 · 시작 · 끝 · 도착까지 초 · 그때 공격력) — 잔상 일섬(R) · 일섬 전설(E)이 듣는다.</summary>
    public static event Action<WeaponData, SkillType, Vector3, Vector3, float, float> SkillDashed;

    /// <summary>전설 증강이 터졌다(id) — 첫 발동 자막 · HUD는 e2가 듣는다(10-24 W7).</summary>
    public static event Action<string> LegendProcced;
    internal static void RaiseLegendProcced(string id) => LegendProcced?.Invoke(id);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { SkillDashed = null; LegendProcced = null; }

    /// <summary>지금 든 근접 무기 — 증강 그림 색 계열이 읽는다.</summary>
    internal static WeaponData CurrentWeapon => Current();

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
