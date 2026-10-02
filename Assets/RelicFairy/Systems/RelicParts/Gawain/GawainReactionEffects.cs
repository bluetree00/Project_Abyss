using System.Collections.Generic;
using RelicFairy.Monster;
using UnityEngine;

/// <summary>
/// 가웨인 「일광 반응」 — 태양흔 × 룬 속성(설계 v2 §2-4 · §5). 조건은 그 속성 1단계 룬 효과가 켜져 있는가.
/// 룬 판을 바꿔 조건이 꺼지면 조각은 남고 반응만 쉰다(휴면).
/// 반응이 일어날 때마다 <see cref="GawainMemoryHub.NotifyReaction"/> — 서로 다른 반응 둘이 10초 안이면 「엇갈린 빛」.
/// </summary>
public abstract class GawainReaction : GawainFragment
{
    private readonly string _tier1Key;
    private readonly string _label;
    private readonly RuneElement _element;
    private float _textGate;

    protected GawainReaction(string key, string tier1Key, string label, RuneElement element) : base(key)
    { _tier1Key = tier1Key; _label = label; _element = element; }

    /// <summary>그 속성 1단계 룬이 켜져 있는가(휴면 판정).</summary>
    protected bool RuneAwake => Player != null && Player.RuneEffectsOrNull != null && Player.RuneEffectsOrNull.IsActive(_tier1Key);
    protected bool RuneTier(string key) => Player != null && Player.RuneEffectsOrNull != null && Player.RuneEffectsOrNull.IsActive(key);
    protected RuneResourceState Res => Player != null ? Player.RuneEffectsOrNull?.Resources : null;

    /// <summary>반응 표시 + 엇갈린 빛 집계. 같은 반응 글자는 0.5초에 한 번.</summary>
    protected void Fire(Vector3 at)
    {
        Hub?.NotifyReaction(EffectKey);
        if (Time.time < _textGate) return;
        _textGate = Time.time + 0.5f;
        RelicFloatText.Show(at, _label, ElementPalette.Bright(_element));   // 반응 이름 = 룬 속성색
        RelicMemoryHints.TryShowOnce(EffectKey);
        ElementVfxPlayer.PlayBurst(_element, at, 1.2f);
    }
}

/// <summary>GR1 해빙(얼음) — 서리 2중첩 적에 태양흔 → 그 자리 개화 ×2(서리 녹음) · ② 수증기: 3m 서리 적 태양흔 +1 · ③ 빙결 적은 1스택만으로.</summary>
public sealed class GRxThawEffect : GawainReaction
{
    private const string Frost = "frost", Freeze = "freeze";
    public GRxThawEffect() : base("g_rx_thaw", "IceFrost", "해빙", RuneElement.Ice) { }
    protected override void Bind()   => Hub.SunmarkAdded += OnSunmark;
    protected override void Unbind() => Hub.SunmarkAdded -= OnSunmark;
    private void OnSunmark(MonsterBase mb, int added)
    {
        if (!RuneAwake || mb == null || mb.IsDead) return;
        bool frosted = mb.Status.GetSlowStacks(Frost) >= 2;
        bool frozen  = Line3 && mb.Status.HasCc(Freeze);
        if (!frosted && !frozen) return;
        var st = RelicMarkStatus.Of(mb.gameObject, false);
        int stacks = st != null ? st.ConsumeSunmark() : 0;
        if (stacks <= 0) return;
        mb.Status.ConsumeSlow(Frost);
        Vector3 at = mb.transform.position;
        Fire(at);
        Hub.Bloom(mb, stacks * 2, GawainMemoryHub.BloomCause.Reaction);
        if (!Line2) return;
        int n = CombatQuery.GetNearbyEnemies(at, 3f, mb.gameObject, 8, Buf);
        var list = new List<MonsterBase>(n);
        for (int i = 0; i < n; i++) if (Buf[i] != null && Buf[i].Status.GetSlowStacks(Frost) > 0) list.Add(Buf[i]);
        foreach (var e in list) Hub.AddSunmark(e, 1, ignoreGap: true);
    }
}

/// <summary>GR2 과열(번개) — 정전기 10스택일 때 개화가 사슬로 튄다(최대 3) · ② 튄 적 태양흔 +1 · ③ 방전 순간 태양흔 적 즉시 개화.</summary>
public sealed class GRxOverheatEffect : GawainReaction
{
    private const string Static = "ElecStatic";
    private const float ChainRadius = 5f, ChainShare = 0.5f;
    public GRxOverheatEffect() : base("g_rx_overheat", "ElecStatic", "과열", RuneElement.Electric) { }
    protected override void Bind()   => Hub.Bloomed += OnBloomed;
    protected override void Unbind() => Hub.Bloomed -= OnBloomed;
    private void OnBloomed(MonsterBase mb, int stacks, GawainMemoryHub.BloomCause cause)
    {
        if (!RuneAwake || mb == null || cause == GawainMemoryHub.BloomCause.Reaction) return;
        if (Res == null || Res.GetStack(Static) < 10) return;
        Vector3 from = mb.transform.position;
        int n = CombatQuery.GetNearbyEnemies(from, ChainRadius, mb.gameObject, 3, Buf);
        if (n <= 0) return;
        Fire(from);
        float dmg = Atk * GawainMemoryHub.BloomAtkRatio * stacks * ChainShare;
        var list = new List<MonsterBase>(n);
        for (int i = 0; i < n; i++) if (Buf[i] != null) list.Add(Buf[i]);
        foreach (var t in list)
        {
            ElementVfxPlayer.PlayBeam(RuneElement.Electric, from + Vector3.up, t.transform.position + Vector3.up);
            CombatQuery.DealSynergyDamage(t, dmg, Owner, 1f, false, RuneElement.Electric);
            if (Line2) Hub.AddSunmark(t, 1, ignoreGap: true);
        }
    }
    public override void OnSkillUsed(PlayerController player)
    {
        if (!Line3 || !RuneAwake || !RuneTier("ElecDischarge") || Hub == null) return;
        int n = CombatQuery.GetNearbyEnemies(player.transform.position, 8f, player.gameObject, 16, Buf);
        var list = new List<MonsterBase>(n);
        for (int i = 0; i < n; i++) if (Buf[i] != null) list.Add(Buf[i]);
        bool any = false;
        foreach (var mb in list)
        {
            var st = RelicMarkStatus.Of(mb.gameObject, false);
            int s = st != null ? st.ConsumeSunmark() : 0;
            if (s <= 0) continue;
            any = true;
            Hub.Bloom(mb, s, GawainMemoryHub.BloomCause.Reaction);
        }
        if (any) Fire(player.transform.position);
    }
}

/// <summary>GR3 들불(독) — 개화가 독안개 장판에 닿으면 「불안개」(2초마다 태양흔 +1) · ② 불안개 지속 2배 · ③ 불안개 안 처치 = 작은 해.</summary>
public sealed class GRxWildfireEffect : GawainReaction
{
    private readonly HashSet<GroundFieldBase> _fireMist = new();
    private readonly List<GroundFieldBase> _scratch = new();
    private float _tick;
    public GRxWildfireEffect() : base("g_rx_wildfire", "GrassMist", "들불", RuneElement.Fire) { }
    protected override void Bind()   => Hub.Bloomed += OnBloomed;
    protected override void Unbind() => Hub.Bloomed -= OnBloomed;
    private void OnBloomed(MonsterBase mb, int stacks, GawainMemoryHub.BloomCause cause)
    {
        if (!RuneAwake || mb == null) return;
        Vector3 at = mb.transform.position;
        foreach (var f in GroundFieldBase.ActiveFields)
        {
            if (f is not PoisonField || !f.IsActive || _fireMist.Contains(f)) continue;
            float r = f.Radius + GawainMemoryHub.BloomRadius;
            if ((f.Center - at).sqrMagnitude > r * r) continue;
            _fireMist.Add(f);
            if (Line2) f.ExtendLife(f.Remaining);
            ElementVfxPlayer.PlayBurst(RuneElement.Fire, f.Center, f.Radius);
            Fire(f.Center);
        }
    }
    public override void Tick(float dt, PlayerController player)
    {
        if (_fireMist.Count == 0) return;
        _tick -= dt;
        if (_tick > 0f) return;
        _tick = 2f;
        _scratch.Clear();
        foreach (var f in _fireMist) if (f == null || !f.IsActive) _scratch.Add(f);
        foreach (var dead in _scratch) _fireMist.Remove(dead);
        foreach (var f in _fireMist)
        {
            ElementVfxPlayer.PlayBurst(RuneElement.Fire, f.Center, f.Radius * 0.6f);
            int n = CombatQuery.GetNearbyEnemies(f.Center, f.Radius, null, 16, Buf);
            for (int i = 0; i < n; i++) if (Buf[i] != null) Hub.AddSunmark(Buf[i], 1, ignoreGap: true);
        }
    }
    public override void OnKill(GameObject deadEnemy, PlayerController player)
    {
        if (!Line3 || deadEnemy == null || Hub == null) return;
        Vector3 at = deadEnemy.transform.position;
        foreach (var f in _fireMist)
        {
            if (f == null || !f.IsActive || (f.Center - at).sqrMagnitude > f.Radius * f.Radius) continue;
            SolarDescentSkillRuntime.StrikeSmallSun(player, Hub.Relic, at, 0.20f, 0.4f);
            break;
        }
    }
}

/// <summary>GR4 겹해(불) — 잔불이 태양흔 적에게 터지면 시간대 무관 태양흔 +1 · ② 개화 = 잔불 1회 · ③ 점화 적 개화 = 점화 즉시 만료 → 작열 폭발.</summary>
public sealed class GRxTwinSunEffect : GawainReaction
{
    private const float EmberRatio = 0.08f, ScorchRatio = 1.5f;
    public GRxTwinSunEffect() : base("g_rx_twin_sun", "FireEmber", "겹해", RuneElement.Fire) { }
    protected override void Bind()   { Hub.Hit += OnHubHit; Hub.Bloomed += OnBloomed; }
    protected override void Unbind() { Hub.Hit -= OnHubHit; Hub.Bloomed -= OnBloomed; }
    private void OnHubHit(HitInfo hit)
    {
        if (!RuneAwake) return;
        var mb = AsMonster(hit.Target);
        var st = mb != null ? RelicMarkStatus.Of(mb.gameObject, false) : null;
        if (st == null || st.Sunmark <= 0) return;   // 「태양흔 적에게」
        if (Hub.AddSunmark(mb, 1) > 0) Fire(mb.transform.position);
    }
    private void OnBloomed(MonsterBase mb, int stacks, GawainMemoryHub.BloomCause cause)
    {
        if (!RuneAwake || mb == null || mb.IsDead) return;
        if (Line2) CombatQuery.DealSynergyDamage(mb, Atk * EmberRatio, Owner, 1f, false, RuneElement.Fire);
        if (Line3 && mb.Status.HasDot("ignite"))
        {
            mb.Status.ConsumeDot("ignite", 1f);
            FireScorchEffect.Explode(mb.gameObject, mb.transform.position, Atk * ScorchRatio, Owner);
            Fire(mb.transform.position);
        }
    }
}

/// <summary>GR5 일륜(빛) — 치명이 태양흔 적을 맞히면 그 적만 1스택 미리 개화 · ② 개화마다 광채 +1 · ③ 개화가 치명 가능 · 광폭발 원뿔 안 태양흔 적 즉시 개화.</summary>
public sealed class GRxCoronaEffect : GawainReaction
{
    private const string Radiance = "LightRadiance";
    private const float  BurstRange = 7f, BurstHalf = 50f;   // 광폭발 원뿔(LightBurstEffect와 같은 값)
    private int _prevGauge;
    public GRxCoronaEffect() : base("g_rx_corona", "LightRadiance", "일륜", RuneElement.Light) { }
    protected override void Bind()   { Hub.Bloomed += OnBloomed; Hub.BloomCanCrit = Line3; }
    protected override void Unbind() { Hub.Bloomed -= OnBloomed; Hub.BloomCanCrit = false; }
    protected override void OnGradeChanged(RelicMemoryGrade before, PlayerController player)
    {
        if (Hub != null) Hub.BloomCanCrit = Line3 && RuneAwake;
    }
    public override void Tick(float dt, PlayerController player)
    {
        if (Hub == null) return;
        Hub.BloomCanCrit = Line3 && RuneAwake;   // 룬 판이 바뀌면 휴면
        if (Res == null) return;
        int g = Res.GetGauge(Radiance);
        // 광폭발은 광채가 문턱에 닿는 그 순간 터지고 게이지를 비운다 — 줄어든 걸 보고 안다
        if (Line3 && g < _prevGauge && RuneAwake && RuneTier("LightBurst")) BloomBurstCone(player);
        _prevGauge = g;
    }
    private void BloomBurstCone(PlayerController player)
    {
        Vector3 pos = player.transform.position;
        Vector3 fwd = player.transform.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.001f) return;
        int n = CombatQuery.GetEnemiesInCone(pos, fwd.normalized, BurstRange, BurstHalf, 16, Buf, showGuide: false);
        var list = new List<MonsterBase>(n);
        for (int i = 0; i < n; i++) if (Buf[i] != null) list.Add(Buf[i]);
        bool any = false;
        foreach (var mb in list)
        {
            var st = RelicMarkStatus.Of(mb.gameObject, false);
            int s = st != null ? st.ConsumeSunmark() : 0;
            if (s <= 0) continue;
            any = true;
            Hub.Bloom(mb, s, GawainMemoryHub.BloomCause.Reaction);
        }
        if (any) { Fire(pos + fwd.normalized * 2f); Debug.Log("[GawainMemory] 일륜 ③ — 광폭발 원뿔 태양흔 즉시 개화"); }
    }
    public override void OnCrit(in HitInfo hit, PlayerController player)
    {
        if (!RuneAwake || Hub == null) return;
        var mb = AsMonster(hit.Target);
        var st = mb != null ? RelicMarkStatus.Of(mb.gameObject, false) : null;
        if (st == null || st.Sunmark <= 0) return;
        st.ConsumeSunmark(st.Sunmark - 1);
        Fire(mb.transform.position);
        Hub.Bloom(mb, 1, GawainMemoryHub.BloomCause.Reaction);
    }
    private void OnBloomed(MonsterBase mb, int stacks, GawainMemoryHub.BloomCause cause)
    {
        if (!Line2 || !RuneAwake || Res == null) return;
        Res.AddGauge(Radiance, 1, 10);   // 광채와 같은 게이지 — 10이면 광폭발(빛 2단계)이 뜬다
    }
}

/// <summary>GR6 일식(어둠) — 잠식 50+ 동안 태양흔이 흑점(상한 없음) · ② 흑점 개화 2m 연쇄 · ③ 암흑 해방 동안 해가 멈추고 끝나는 순간 흑점 전부 개화.</summary>
public sealed class GRxEclipseEffect : GawainReaction
{
    private const string Gauge = "darkGauge", ReleaseActive = "darkReleaseActive";
    private bool _wasRelease;
    public GRxEclipseEffect() : base("g_rx_eclipse", "DarkErosion", "일식", RuneElement.Dark) { }
    protected override void Bind()   => Hub.Bloomed += OnBloomed;
    protected override void Unbind() { Hub.Bloomed -= OnBloomed; Hub.EclipseActive = false; if (Hub.Gauge != null) Hub.Gauge.Paused = false; }
    public override void Tick(float dt, PlayerController player)
    {
        if (Hub == null) return;
        bool awake = RuneAwake && Res != null;
        bool eclipse = awake && Res.GetGauge(Gauge) >= 50;
        if (eclipse != Hub.EclipseActive)
        {
            Hub.EclipseActive = eclipse;
            if (eclipse) Fire(player.transform.position);
        }
        bool release = awake && Line3 && Res.GetRegister(ReleaseActive) != 0;
        if (release != _wasRelease)
        {
            _wasRelease = release;
            Hub.Gauge.Paused = release;
            if (!release) BloomAllBlackspots(player);
        }
    }
    private void BloomAllBlackspots(PlayerController player)
    {
        int n = CombatQuery.GetNearbyEnemies(player.transform.position, GawainMemoryHub.HarvestRadius, player.gameObject, 32, Buf);
        var list = new List<MonsterBase>(n);
        for (int i = 0; i < n; i++) if (Buf[i] != null) list.Add(Buf[i]);
        foreach (var mb in list)
        {
            var st = RelicMarkStatus.Of(mb.gameObject, false);
            int s = st != null ? st.ConsumeBlackspot() : 0;
            if (s > 0) Hub.Bloom(mb, s, GawainMemoryHub.BloomCause.Reaction);
        }
        Fire(player.transform.position);
    }
    private void OnBloomed(MonsterBase mb, int stacks, GawainMemoryHub.BloomCause cause)
    {
        if (!Line2 || !Hub.EclipseActive || mb == null) return;
        int n = CombatQuery.GetNearbyEnemies(mb.transform.position, 2f, mb.gameObject, 6, Buf);
        var list = new List<MonsterBase>(n);
        for (int i = 0; i < n; i++) if (Buf[i] != null) list.Add(Buf[i]);
        foreach (var e in list) Hub.AddSunmark(e, 1, ignoreGap: true);   // 일식 중이라 흑점으로 쌓인다
    }
}
