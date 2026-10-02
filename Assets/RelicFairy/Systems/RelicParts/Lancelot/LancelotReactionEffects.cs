using System.Collections.Generic;
using RelicFairy.Monster;
using UnityEngine;

/// <summary>
/// 랜슬롯 「타락 반응」 — 배신의 낙인 × 룬 속성(설계 v2 §3-4 · §5). 조건은 그 속성 1단계 룬 효과가 켜져 있는가.
/// 룬 판을 바꿔 조건이 꺼지면 조각은 남고 반응만 쉰다(휴면).
/// 반응이 일어날 때마다 <see cref="LancelotMemoryHub.NotifyReaction"/> — 서로 다른 반응 둘이 10초 안이면 「엇갈린 빛」.
/// </summary>
public abstract class LancelotReaction : LancelotFragment
{
    private readonly string _tier1Key;
    private readonly string _label;
    private readonly RuneElement _element;
    private readonly float _textGap;
    private float _textGate;

    protected LancelotReaction(string key, string tier1Key, string label, RuneElement element, float textGap = 0.5f) : base(key)
    { _tier1Key = tier1Key; _label = label; _element = element; _textGap = textGap; }

    /// <summary>그 속성 1단계 룬이 켜져 있는가(휴면 판정).</summary>
    protected bool RuneAwake => Player != null && Player.RuneEffectsOrNull != null && Player.RuneEffectsOrNull.IsActive(_tier1Key);
    protected bool RuneTier(string key) => Player != null && Player.RuneEffectsOrNull != null && Player.RuneEffectsOrNull.IsActive(key);
    protected RuneResourceState Res => Player != null ? Player.RuneEffectsOrNull?.Resources : null;

    /// <summary>반응 표시 + 엇갈린 빛 집계. 같은 반응 글자는 <see cref="_textGap"/>초에 한 번.</summary>
    protected void Fire(Vector3 at)
    {
        Hub?.NotifyReaction(EffectKey);
        if (Time.time < _textGate) return;
        _textGate = Time.time + _textGap;
        RelicFloatText.Show(at, _label, ElementPalette.Bright(_element));   // 반응 이름 = 룬 속성색
        RelicMemoryHints.TryShowOnce(EffectKey);
        ElementVfxPlayer.PlayBurst(_element, at, 1.2f);
    }
}

/// <summary>LR1 전이(번개) — 정전기 10스택일 때 적중으로 새긴 낙인이 가까운 적 2체에 1씩 튄다 · ② 튄 낙인이 넘치면 원한 · ③ 감전 적의 낙인은 찢길 때 두 번 찢긴다.</summary>
public sealed class LRxTransferEffect : LancelotReaction
{
    private const string Static = "ElecStatic", Shocked = "shocked";
    private const float ChainRadius = 5f;
    public LRxTransferEffect() : base("l_rx_transfer", "ElecStatic", "전이", RuneElement.Electric) { }
    protected override void Bind()   { Hub.BrandAdded += OnBrand; Hub.PreTear += OnPreTear; }
    protected override void Unbind() { Hub.BrandAdded -= OnBrand; Hub.PreTear -= OnPreTear; }
    private void OnBrand(MonsterBase mb, int added, int overflow, LancelotMemoryHub.BrandSource src)
    {
        if (src != LancelotMemoryHub.BrandSource.Hit || added <= 0 || mb == null || !RuneAwake) return;
        if (Res == null || Res.GetStack(Static) < 10) return;
        Vector3 from = mb.transform.position;
        var near = new List<MonsterBase>(Near(from, ChainRadius, mb.gameObject, 2));
        if (near.Count == 0) return;
        Fire(from);
        foreach (var t in near)
        {
            ElementVfxPlayer.PlayBeam(RuneElement.Electric, from + Vector3.up, t.transform.position + Vector3.up);
            Hub.AddBrand(t, 1, overflowToGrudge: Line2, ignoreGap: true, src: LancelotMemoryHub.BrandSource.Reaction);
        }
    }
    private void OnPreTear(LancelotMemoryHub.TearContext ctx)
    {
        if (!Line3 || !RuneAwake || ctx.Target == null || !ctx.Target.HasDamageTakenAmpSlot(Shocked)) return;
        ctx.XMultiplier *= 2;
        Fire(ctx.Target.transform.position);
    }
}

/// <summary>LR2 파쇄(얼음) — 광기 20+에서 서리 적을 치면 서리가 낙인으로 바뀐다(서리 중첩 수만큼) · ② 상한에 찬 서리 적은 거의 멈춘다 · ③ 빙결 적은 심판에서 낙인을 두 배로 센다.</summary>
public sealed class LRxShatterEffect : LancelotReaction
{
    private const string Frost = "frost", Freeze = "freeze", DeepSlow = "lancelot_shatter";
    public LRxShatterEffect() : base("l_rx_shatter", "IceFrost", "파쇄", RuneElement.Ice) { }
    protected override void Bind()   { Hub.Hit += OnHubHit; Hub.PreTear += OnPreTear; }
    protected override void Unbind() { Hub.Hit -= OnHubHit; Hub.PreTear -= OnPreTear; }
    private void OnHubHit(HitInfo hit)
    {
        if (!RuneAwake || !Hub.AtRung(2)) return;
        var mb = AsMonster(hit.Target);
        if (mb == null || mb.IsDead || mb.Status.GetSlowStacks(Frost) <= 0) return;
        if (LancelotMemoryHub.BrandOf(mb) >= Hub.BrandCap)
        {
            if (!Line2) return;
            mb.Status.ApplySlow(DeepSlow, 0.9f, 1.5f);   // 거의 멈춤
            Fire(mb.transform.position);
            return;
        }
        int taken = mb.Status.ConsumeSlow(Frost);
        if (taken <= 0) return;
        Hub.AddBrand(mb, taken, overflowToGrudge: false, ignoreGap: true, src: LancelotMemoryHub.BrandSource.Reaction);
        Fire(mb.transform.position);
    }
    private void OnPreTear(LancelotMemoryHub.TearContext ctx)
    {
        if (!Line3 || !RuneAwake || ctx.Cause != LancelotMemoryHub.TearCause.Judgment || ctx.Target == null) return;
        if (!ctx.Target.Status.HasCc(Freeze)) return;
        ctx.Stacks *= 2;
        Fire(ctx.Target.transform.position);
    }
}

/// <summary>LR3 부패(독) — 독안개 안의 낙인 적은 출혈(낙인 수만큼) · ② 출혈 적이 죽으면 그 자리에 독안개 · ③ 겹친 안개(독 2단계)에선 낙인이 2씩.</summary>
public sealed class LRxDecayEffect : LancelotReaction
{
    private const float BleedRatio = 0.04f, MistRatio = 0.04f, MistLife = 6f;
    private readonly List<MonsterBase> _seen = new(32);
    private float _tick;
    public LRxDecayEffect() : base("l_rx_decay", "GrassMist", "부패", RuneElement.Grass, 2f) { }
    protected override void Bind()   => Hub.BrandAdded += OnBrand;
    protected override void Unbind() => Hub.BrandAdded -= OnBrand;
    public override void Tick(float dt, PlayerController player)
    {
        if (Hub == null || !RuneAwake) return;
        _tick -= dt;
        if (_tick > 0f) return;
        _tick = 0.5f;
        _seen.Clear();
        var fields = GroundFieldBase.ActiveFields;
        for (int f = 0; f < fields.Count; f++)
        {
            var field = fields[f];
            if (field is not PoisonField || !field.IsActive) continue;
            int n = CombatQuery.GetNearbyEnemies(field.Center, field.Radius, null, 16, Buf);
            for (int i = 0; i < n; i++)
            {
                var mb = Buf[i];
                if (mb == null || mb.IsDead || _seen.Contains(mb)) continue;
                int b = LancelotMemoryHub.BrandOf(mb);
                if (b <= 0) continue;
                _seen.Add(mb);
                MonsterBleed.Apply(mb.gameObject, Atk * BleedRatio * b, 1f, Owner);
            }
        }
        if (_seen.Count > 0) Fire(_seen[0].transform.position);
    }
    public override void OnKill(GameObject deadEnemy, PlayerController player)
    {
        if (!Line2 || !RuneAwake || deadEnemy == null || MonsterBleed.Remaining(deadEnemy) <= 0f) return;
        var cfg = new PoisonField.Config { perTick = Atk * MistRatio, life = MistLife, radiusMult = 1f, dotMult = 1f };
        PoisonField.SpawnAt(player, deadEnemy.transform.position, cfg);
        Fire(deadEnemy.transform.position);
        Debug.Log("[LancelotMemory] 부패 ② — 출혈 적 처치 자리에 독안개");
    }
    private void OnBrand(MonsterBase mb, int added, int overflow, LancelotMemoryHub.BrandSource src)
    {
        if (!Line3 || src != LancelotMemoryHub.BrandSource.Hit || added <= 0 || mb == null) return;
        if (!RuneAwake || !RuneTier("GrassMistPlus") || FieldsAround(mb.transform.position) < 2) return;
        Hub.AddBrand(mb, 1, overflowToGrudge: false, ignoreGap: true, src: LancelotMemoryHub.BrandSource.Reaction);
        Fire(mb.transform.position);
    }
    private static int FieldsAround(Vector3 at)
    {
        int n = 0;
        var fields = GroundFieldBase.ActiveFields;
        for (int f = 0; f < fields.Count; f++)
        {
            var field = fields[f];
            if (field is not PoisonField || !field.IsActive) continue;
            Vector3 d = field.Center - at; d.y = 0f;
            if (d.sqrMagnitude <= field.Radius * field.Radius) n++;
        }
        return n;
    }
}

/// <summary>LR4 낙철(불) — 잔불이 낙인 적에게 터지면 낙인 +1(광기 20 미만이어도) · ② 상한을 넘는 몫은 원한 · ③ 점화가 끝난 낙인 적 = 원한 +1(작열이면 3 m 안 낙인 적도).</summary>
public sealed class LRxBrandIronEffect : LancelotReaction
{
    private const string Ignite = "ignite";
    private const float  ScorchRadius = 3f;
    private readonly HashSet<MonsterBase> _ignited = new();
    private readonly List<MonsterBase> _gone = new(8);
    private float _tick;
    public LRxBrandIronEffect() : base("l_rx_brand_iron", "FireEmber", "낙철", RuneElement.Fire) { }
    protected override void Bind()   => Hub.Hit += OnHubHit;
    protected override void Unbind() { Hub.Hit -= OnHubHit; _ignited.Clear(); }
    private void OnHubHit(HitInfo hit)
    {
        if (!RuneAwake || Hub.AtRung(2)) return;   // 20 이상이면 허브 기본 규칙이 새기고 넘침도 원한으로 간다
        var mb = AsMonster(hit.Target);
        if (mb == null || mb.IsDead || LancelotMemoryHub.BrandOf(mb) <= 0) return;
        if (Hub.AddBrand(mb, 1, overflowToGrudge: Line2, ignoreGap: false, src: LancelotMemoryHub.BrandSource.Reaction) > 0)
            Fire(mb.transform.position);
    }
    public override void Tick(float dt, PlayerController player)
    {
        if (!Line3 || Hub == null || !RuneAwake || !RuneTier("FireIgnite")) return;
        _tick -= dt;
        if (_tick > 0f) return;
        _tick = 0.25f;
        int n = CombatQuery.GetNearbyEnemies(player.transform.position, LancelotMemoryHub.ScreenRadius, player.gameObject, 32, Buf);
        for (int i = 0; i < n; i++) if (Buf[i] != null && Buf[i].Status.HasDot(Ignite)) _ignited.Add(Buf[i]);
        _gone.Clear();
        foreach (var mb in _ignited)
            if (mb == null || mb.IsDead || !mb.isActiveAndEnabled || !mb.Status.HasDot(Ignite)) _gone.Add(mb);
        bool scorch = RuneTier("FireScorch");
        foreach (var mb in _gone)
        {
            _ignited.Remove(mb);
            if (mb == null || !mb.isActiveAndEnabled) continue;
            int grudge = LancelotMemoryHub.BrandOf(mb) > 0 && !mb.IsDead ? 1 : 0;
            if (scorch)
            {
                foreach (var e in new List<MonsterBase>(Near(mb.transform.position, ScorchRadius, mb.gameObject, 8)))
                    if (LancelotMemoryHub.BrandOf(e) > 0) grudge++;
            }
            if (grudge <= 0) continue;
            Hub.SaveGrudge(grudge);
            Fire(mb.transform.position);
        }
    }
}

/// <summary>LR5 폭로(빛) — 치명타가 낙인 적을 맞히면 그 적의 낙인 상한 +2(8초) · ② 폭로된 적은 찢길 때 X자가 두 줄 · ③ 광폭발(빛 2단계)이 맞힌 적은 낙인 상한까지.</summary>
public sealed class LRxExposeEffect : LancelotReaction
{
    private const string Radiance = "LightRadiance";
    private const int    CapBonus = 2;
    private const float  ExposeDur = 8f, BurstRange = 7f, BurstHalf = 50f;
    private int _prevGauge;
    public LRxExposeEffect() : base("l_rx_expose", "LightRadiance", "폭로", RuneElement.Light) { }
    protected override void Bind()   => Hub.PreTear += OnPreTear;
    protected override void Unbind() => Hub.PreTear -= OnPreTear;
    public override void OnCrit(in HitInfo hit, PlayerController player)
    {
        if (!RuneAwake || Hub == null) return;
        var mb = AsMonster(hit.Target);
        var st = mb != null ? RelicMarkStatus.Of(mb.gameObject, false) : null;
        if (st == null || st.Brand <= 0) return;
        st.SetBrandCapBonus(CapBonus, ExposeDur);
        Fire(mb.transform.position);
    }
    private void OnPreTear(LancelotMemoryHub.TearContext ctx)
    {
        if (!Line2 || !RuneAwake || ctx.Target == null) return;
        var st = RelicMarkStatus.Of(ctx.Target.gameObject, false);
        if (st != null && st.IsExposed) ctx.XMultiplier *= 2;
    }
    public override void Tick(float dt, PlayerController player)
    {
        if (!Line3 || Hub == null || Res == null) return;
        int g = Res.GetGauge(Radiance);
        // 광폭발은 광채가 문턱에 닿는 그 순간 터지고 게이지를 비운다 — 줄어든 걸 보고 안다
        if (g < _prevGauge && RuneAwake && RuneTier("LightBurst"))
        {
            Vector3 pos = player.transform.position;
            Vector3 fwd = player.transform.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude > 0.001f)
            {
                int n = CombatQuery.GetEnemiesInCone(pos, fwd.normalized, BurstRange, BurstHalf, 16, Buf, showGuide: false);
                var list = new List<MonsterBase>(n);
                for (int i = 0; i < n; i++) if (Buf[i] != null) list.Add(Buf[i]);
                foreach (var mb in list) Hub.FillBrand(mb);
                if (list.Count > 0) Fire(pos + fwd.normalized * 2f);
            }
        }
        _prevGauge = g;
    }
}

/// <summary>LR6 타락(어둠) — 잠식 게이지 50+ 동안 원한 상한 +3 · ② 원한을 저축할 때 잠식 +5 · ③ 암흑 해방 순간 화면 안 낙인을 모두 원한으로 거둬들인다.</summary>
public sealed class LRxCorruptEffect : LancelotReaction
{
    private const string Gauge = "darkGauge", ReleaseActive = "darkReleaseActive";
    private const int    CapBonus = 3, GaugeOnSave = 5, GaugeMax = 100;
    private bool _bonusOn, _wasRelease;
    public LRxCorruptEffect() : base("l_rx_corrupt", "DarkErosion", "타락", RuneElement.Dark) { }
    protected override void Bind()   => Hub.GrudgeSaved += OnSaved;
    protected override void Unbind()
    {
        Hub.GrudgeSaved -= OnSaved;
        if (_bonusOn) { _bonusOn = false; Hub.Grudge?.SetCapBonus(0); }
    }
    public override void Tick(float dt, PlayerController player)
    {
        if (Hub == null || Hub.Grudge == null) return;
        bool awake = RuneAwake && Res != null;
        bool on = awake && Res.GetGauge(Gauge) >= 50;
        if (on != _bonusOn)
        {
            _bonusOn = on;
            Hub.Grudge.SetCapBonus(on ? CapBonus : 0);
            if (on) Fire(player.transform.position);
        }
        bool release = awake && Line3 && Res.GetRegister(ReleaseActive) != 0;
        if (release && !_wasRelease) Harvest(player);
        _wasRelease = release;
    }
    private void OnSaved(int n)
    {
        if (!Line2 || !RuneAwake || Res == null || n <= 0) return;
        Res.AddGauge(Gauge, GaugeOnSave * n, GaugeMax);
    }
    private void Harvest(PlayerController player)
    {
        Vector3 me = player.transform.position;
        int total = 0;
        foreach (var mb in new List<MonsterBase>(Near(me, LancelotMemoryHub.ScreenRadius, player.gameObject, 32)))
        {
            var st = RelicMarkStatus.Of(mb.gameObject, false);
            int b = st != null ? st.ConsumeBrand() : 0;
            if (b <= 0) continue;
            total += b;
            ElementVfxPlayer.PlayBeam(RuneElement.Dark, mb.transform.position + Vector3.up, me + Vector3.up, 0.35f);
        }
        if (total <= 0) return;
        Hub.SaveGrudge(total);
        Fire(me);
        Debug.Log($"[LancelotMemory] 타락 ③ — 암흑 해방: 낙인 {total} → 원한");
    }
}
