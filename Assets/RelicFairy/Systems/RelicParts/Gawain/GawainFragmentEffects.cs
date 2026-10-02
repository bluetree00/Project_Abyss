using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEngine;

/// <summary>
/// 가웨인 조각의 공통 바탕 — 허브(<see cref="GawainMemoryHub"/>)를 잡고 사건에 붙는다.
/// 줄 ②③은 <see cref="RelicPartEffect.Line2"/> · <see cref="RelicPartEffect.Line3"/>로 그때그때 본다(「선명하게」가 즉시 반영).
/// </summary>
public abstract class GawainFragment : RelicPartEffect
{
    protected GawainMemoryHub  Hub;
    protected PlayerController Player;
    protected readonly List<MonsterBase> Buf = new(16);

    protected GawainFragment(string key) : base(key) { }

    public override void OnAcquire(PlayerController player)
    {
        Player = player;
        Hub = GawainMemoryHub.Ensure(player);
        if (Hub != null) Bind();
    }

    public override void OnRemove(PlayerController player)
    {
        if (Hub != null) Unbind();
        Hub = null;
    }

    protected virtual void Bind() { }
    protected virtual void Unbind() { }

    protected float Atk => Player != null ? Player.RuntimeStats.GetEffectiveAttack(AttackStatKind.Melee) : 0f;
    protected GameObject Owner => Player != null ? Player.gameObject : null;

    protected static MonsterBase AsMonster(GameObject go) => go != null && go.TryGetComponent<MonsterBase>(out var mb) ? mb : null;

    /// <summary>반경 안 적에게 상태 피해 경로 폭발(치명 없음 · 방어 무시) + 불 이펙트(판정 = 크기).</summary>
    protected void SmallBlast(Vector3 at, float radius, float damage)
    {
        ElementVfxPlayer.PlayBurst(RuneElement.Fire, at, radius);
        int n = CombatQuery.GetNearbyEnemies(at, radius, null, 16, Buf);
        for (int i = 0; i < n; i++)
            if (Buf[i] != null && !Buf[i].IsDead) CombatQuery.DealSynergyDamage(Buf[i], damage, Owner, 1f, false, RuneElement.Fire);
    }

    /// <summary>조각 발동 글자(금빛) — 출시 빌드에서도 뜬다. 처음 켜진 조각이면 화면 아래 자막 한 줄(계정당 1회).</summary>
    protected void Say(Vector3 at, string text)
    {
        RelicFloatText.Show(at, text, GawainMemoryHub.GawainColor);
        RelicMemoryHints.TryShowOnce(EffectKey);
    }
}

// ── 여명 ────────────────────────────────────────────────────────

/// <summary>G1 새벽 파종 — 여명 중 대시로 지나간 적에게 태양흔 1 · ② 대시 끝 불씨 · ③ 대시로 새긴 적은 개화 때 한 번 더.</summary>
public sealed class GDawnSowingEffect : GawainFragment
{
    private const float Sweep = 0.35f, Radius = 1.2f;
    private float _until;
    private bool  _sweeping;
    private readonly HashSet<int> _hit = new();
    public GDawnSowingEffect() : base("g_dawn_sowing") { }
    protected override void Bind()   => Player.OnDodgeStart += OnDodge;
    protected override void Unbind() { if (Player != null) Player.OnDodgeStart -= OnDodge; }
    private void OnDodge()
    {
        if (Hub == null || !Hub.IsDawn) return;
        _until = Time.time + Sweep; _sweeping = true; _hit.Clear();
    }
    public override void Tick(float dt, PlayerController player)
    {
        if (!_sweeping || Hub == null) return;
        int n = CombatQuery.GetNearbyEnemies(player.transform.position, Radius, player.gameObject, 8, Buf);
        for (int i = 0; i < n; i++)
        {
            var mb = Buf[i];
            if (mb == null || !_hit.Add(mb.GetInstanceID())) continue;
            Hub.AddSunmark(mb, 1, ignoreGap: true);
            if (Line3) Hub.MarkSown(mb);
        }
        if (Time.time < _until) return;
        _sweeping = false;
        if (Line2) FireField.SpawnAt(player.gameObject, player.transform.position, 1.5f, 1.5f, Atk * 0.10f, 1.5f);
    }
}

/// <summary>G2 서광의 각인 — 각인 순간 5m 안 적을 끌어당김 · ② 끌려온 적 태양흔 +1 · ③ 각인 자리에 정오 개시와 함께 작은 해.</summary>
public sealed class GDaybreakMarkEffect : GawainFragment
{
    private const float PullRadius = 5f, PullDistance = 3f;
    private Vector3 _markAt;
    private bool    _pending;
    public GDaybreakMarkEffect() : base("g_daybreak_mark") { }
    protected override void Bind()   { Hub.Relic.MarkReached += OnMark; Hub.NoonStarted += OnNoon; }
    protected override void Unbind() { if (Hub.Relic != null) Hub.Relic.MarkReached -= OnMark; Hub.NoonStarted -= OnNoon; }
    private void OnMark()
    {
        if (Player == null) return;
        Vector3 me = Player.transform.position;
        int n = CombatQuery.GetNearbyEnemies(me, PullRadius, Owner, 16, Buf);
        for (int i = 0; i < n; i++)
        {
            var mb = Buf[i];
            if (mb == null) continue;
            RelicPullMotion.Pull(mb, me, PullDistance);
            if (Line2) Hub.AddSunmark(mb, 1, ignoreGap: true);
        }
        ElementVfxPlayer.PlayBurst(RuneElement.Light, me, PullRadius);
        _markAt = me; _pending = Line3;
        Debug.Log($"[GawainMemory] 서광의 각인 — 끌어당김 {n}");
    }
    private void OnNoon(bool shortNoon)
    {
        if (!_pending || Player == null) return;
        _pending = false;
        SolarDescentSkillRuntime.StrikeSmallSun(Player, Hub.Relic, _markAt, 0.30f, 0.6f);
    }
}

/// <summary>G3 여명의 맹세 — 여명 피해를 「축열」로 쌓아 정오에 발밑 폭발 · ② 폭발 = 태양흔 +1 · ③ 축열 가득이면 즉시 정오.</summary>
public sealed class GDawnOathEffect : GawainFragment
{
    private const float CapRatio = 0.30f, Radius = 3f, HeatMult = 1.5f, AtkMult = 0.5f;
    private float _heat;
    public float Heat => _heat;
    public GDawnOathEffect() : base("g_dawn_oath") { }
    protected override void Bind()   => Hub.PreHarvest += OnPreHarvest;
    protected override void Unbind() => Hub.PreHarvest -= OnPreHarvest;
    public override void OnDamaged(in HitInfo hit, PlayerController player)
    {
        if (Hub == null || !Hub.IsDawn || hit.Damage <= 0f) return;
        float cap = player.RuntimeStats.MaxHp * CapRatio;
        _heat = Mathf.Min(cap, _heat + hit.Damage);
        if (Line3 && _heat >= cap)
        {
            Say(player.transform.position, "축열");
            Hub.Gauge.OpenNoonNow();
        }
    }
    private void OnPreHarvest(List<MonsterBase> _)
    {
        if (_heat <= 0f || Player == null) return;
        Vector3 at = Player.transform.position;
        float dmg = _heat * HeatMult + Atk * AtkMult;
        ElementVfxPlayer.PlayBurst(RuneElement.Fire, at, Radius);
        int n = CombatQuery.GetNearbyEnemies(at, Radius, Owner, 16, Buf);
        for (int i = 0; i < n; i++)
        {
            var mb = Buf[i];
            if (mb == null || mb.IsDead) continue;
            CombatQuery.DealSynergyDamage(mb, dmg, Owner, 1f, false, RuneElement.Fire);
            if (Line2) Hub.AddSunmark(mb, 1, ignoreGap: true);
        }
        Debug.Log($"[GawainMemory] 여명의 맹세 — 축열 {_heat:0} → 폭발 {dmg:0} × {n}");
        _heat = 0f;
    }
}

/// <summary>G4 아침 사냥 — 여명 중 태양흔 3+ 적 처치 = 해 2초 앞당김 · ② 태양흔이 가까운 적에게 옮는다 · ③ 6초 이상 앞당기면 그 정오 수확 두 번.</summary>
public sealed class GMorningHuntEffect : GawainFragment
{
    private float _advanced;
    public GMorningHuntEffect() : base("g_morning_hunt") { }
    protected override void Bind()   => Hub.DawnStarted += OnDawn;
    protected override void Unbind() => Hub.DawnStarted -= OnDawn;
    private void OnDawn() => _advanced = 0f;
    public override void OnKill(GameObject deadEnemy, PlayerController player)
    {
        if (Hub == null || !Hub.IsDawn || deadEnemy == null) return;
        var st = RelicMarkStatus.Of(deadEnemy, false);
        if (st == null || st.Sunmark < 3) return;
        Hub.Gauge.AdvanceTime(2f);
        _advanced += 2f;
        if (Line2)
        {
            int n = CombatQuery.GetNearbyEnemies(deadEnemy.transform.position, 8f, deadEnemy, 1, Buf);
            if (n > 0 && Buf[0] != null)
            {
                ElementVfxPlayer.PlayBeam(RuneElement.Fire, deadEnemy.transform.position, Buf[0].transform.position);
                Hub.AddSunmark(Buf[0], st.Sunmark, ignoreGap: true);
            }
        }
        if (Line3 && _advanced >= 6f) Hub.DoubleHarvestNext = true;
        Debug.Log($"[GawainMemory] 아침 사냥 — 해 2초 앞당김(이번 여명 {_advanced:0}초)");
    }
}

// ── 정오 ────────────────────────────────────────────────────────

/// <summary>G5 정점 — 정오 개시 0.3초 느려짐 + 조준 표시 · ② 6m 안 태양흔을 가장 높은 적에 맞춤 · ③ 정점 동안 낙일 즉발 · 맞은 적 태양흔 +2.</summary>
public sealed class GZenithEffect : GawainFragment
{
    private const float SlowScale = 0.35f, SlowRealSeconds = 0.3f, EqualizeRadius = 6f, InstantWindow = 1.2f;
    private CancellationTokenSource _cts;
    public GZenithEffect() : base("g_zenith") { }
    protected override void Bind()
    {
        Hub.NoonStarted += OnNoon;
        Hub.PreHarvest  += OnPreHarvest;
        Hub.Relic.SunImpact += OnSunImpact;
    }
    protected override void Unbind()
    {
        Hub.NoonStarted -= OnNoon;
        Hub.PreHarvest  -= OnPreHarvest;
        if (Hub.Relic != null) Hub.Relic.SunImpact -= OnSunImpact;
        Release();
    }
    private void OnNoon(bool shortNoon)
    {
        if (Player == null) return;
        // 조준 표시 — 태양흔 적 위 작은 빛
        int n = CombatQuery.GetNearbyEnemies(Player.transform.position, GawainMemoryHub.HarvestRadius, Owner, 32, Buf);
        for (int i = 0; i < n; i++)
        {
            var st = Buf[i] != null ? RelicMarkStatus.Of(Buf[i].gameObject, false) : null;
            if (st != null && (st.Sunmark > 0 || st.Blackspot > 0)) ElementVfxPlayer.PlayBurst(RuneElement.Light, Buf[i].transform.position, 0.8f);
        }
        Release();
        _cts = new CancellationTokenSource();
        SlowAsync(_cts.Token).Forget();
        if (Line3) Hub.Relic.InstantCastUntil = Time.time + InstantWindow;
    }
    private async UniTaskVoid SlowAsync(CancellationToken ct)
    {
        TimeScaleArbiter.Acquire(this, SlowScale, TimeScaleArbiter.Priority.SlowMotion);
        try { await UniTask.Delay(System.TimeSpan.FromSeconds(SlowRealSeconds), DelayType.UnscaledDeltaTime, cancellationToken: ct); }
        catch (System.OperationCanceledException) { }
        finally { TimeScaleArbiter.Release(this); }
    }
    private void Release() { _cts?.Cancel(); _cts?.Dispose(); _cts = null; }
    private void OnPreHarvest(List<MonsterBase> targets)
    {
        if (!Line2 || Player == null) return;
        Vector3 me = Player.transform.position;
        int max = 0;
        foreach (var mb in targets)
        {
            if (mb == null || (mb.transform.position - me).sqrMagnitude > EqualizeRadius * EqualizeRadius) continue;
            var st = RelicMarkStatus.Of(mb.gameObject, false);
            if (st != null && st.Sunmark > max) max = st.Sunmark;
        }
        if (max <= 0) return;
        int raised = 0;
        foreach (var mb in targets)
        {
            if (mb == null || (mb.transform.position - me).sqrMagnitude > EqualizeRadius * EqualizeRadius) continue;
            var st = RelicMarkStatus.Of(mb.gameObject, true);
            if (st.Sunmark < max) { st.SetSunmark(max, Hub.SunmarkCap); raised++; }
        }
        Debug.Log($"[GawainMemory] 정점 — 6m 안 {raised}체를 태양흔 {max}로 맞춤");
    }
    private void OnSunImpact(Vector3 at, List<GameObject> hits, bool small)
    {
        if (!Line3 || small || Hub == null || !Hub.IsNoon) return;
        foreach (var go in hits) { var mb = AsMonster(go); if (mb != null) Hub.AddSunmark(mb, 2, ignoreGap: true); }
    }
}

/// <summary>G6 두 번째 해 — 정오 낙일 → 정오 끝에 같은 자리 작은 해 · ② 작은 해 = 태양흔 +2 · ③ 태양흔이 가장 많은 적을 따라간다.</summary>
public sealed class GSecondSunEffect : GawainFragment
{
    private Vector3 _at;
    private bool    _pending;
    private float   _expectUntil;
    private Vector3 _expectAt;
    public GSecondSunEffect() : base("g_second_sun") { }
    protected override void Bind()   { Hub.Relic.SunImpact += OnSunImpact; Hub.NoonEnded += OnNoonEnd; }
    protected override void Unbind() { if (Hub.Relic != null) Hub.Relic.SunImpact -= OnSunImpact; Hub.NoonEnded -= OnNoonEnd; }
    private void OnSunImpact(Vector3 at, List<GameObject> hits, bool small)
    {
        if (small)
        {
            if (Line2 && Time.time < _expectUntil && (at - _expectAt).sqrMagnitude < 1f)
                foreach (var go in hits) { var mb = AsMonster(go); if (mb != null) Hub.AddSunmark(mb, 2, ignoreGap: true); }
            return;
        }
        if (Hub.IsNoon) { _at = at; _pending = true; }
    }
    private void OnNoonEnd()
    {
        if (!_pending || Player == null) return;
        _pending = false;
        Vector3 at = _at;
        if (Line3)
        {
            int best = 0;
            int n = CombatQuery.GetNearbyEnemies(Player.transform.position, 15f, Owner, 32, Buf);
            for (int i = 0; i < n; i++)
            {
                var st = Buf[i] != null ? RelicMarkStatus.Of(Buf[i].gameObject, false) : null;
                if (st != null && st.Sunmark > best) { best = st.Sunmark; at = Buf[i].transform.position; }
            }
        }
        _expectAt = at; _expectUntil = Time.time + 2.5f;
        SolarDescentSkillRuntime.StrikeSmallSun(Player, Hub.Relic, at, 0.30f, 0.6f);
        Debug.Log("[GawainMemory] 두 번째 해");
    }
}

/// <summary>G7 해시계 — 낙일이 3체 이상 맞히면 정오 +2초(정오당 1번) · ② 늘어난 2초 동안 공격이 태양흔 · ③ 낙일 처치 시 한 번 더.</summary>
public sealed class GSundialEffect : GawainFragment
{
    private bool  _used, _extended, _bonusAllowed;
    private float _impactAt = -9f;
    public GSundialEffect() : base("g_sundial") { }
    protected override void Bind()   { Hub.Relic.SunImpact += OnSunImpact; Hub.NoonStarted += OnNoon; }
    protected override void Unbind() { if (Hub.Relic != null) Hub.Relic.SunImpact -= OnSunImpact; Hub.NoonStarted -= OnNoon; }
    private void OnNoon(bool shortNoon) { _used = _extended = _bonusAllowed = false; }
    private void OnSunImpact(Vector3 at, List<GameObject> hits, bool small)
    {
        if (Hub == null || !Hub.IsNoon) return;
        _impactAt = Time.time;
        int monsters = 0;
        foreach (var go in hits) if (AsMonster(go) != null) monsters++;
        if (monsters < 3 || (_used && !_bonusAllowed)) return;
        if (_used) _bonusAllowed = false;
        _used = true; _extended = true;
        Hub.Gauge.ExtendNoon(2f);
        Say(at, "정오 +2초");
        Debug.Log($"[GawainMemory] 해시계 — 낙일 {monsters}체 · 정오 +2초");
    }
    public override void OnKill(GameObject deadEnemy, PlayerController player)
    {
        if (Line3 && Hub != null && Hub.IsNoon && Time.time - _impactAt < 1.5f) _bonusAllowed = true;
    }
    public override void Tick(float dt, PlayerController player)
    {
        if (!Line2 || !_extended || Hub == null || !Hub.IsNoon) return;
        if (Hub.Gauge.PhaseRemaining <= 2f) Hub.NoonStackUntil = Time.time + 0.25f;
    }
}

/// <summary>G8 정오의 개화 — 정오 개시 발밑 화상 지대 3m · 4초 · ② 지대 안 2초마다 태양흔 +1 · ③ 지대가 나를 따라온다.</summary>
public sealed class GNoonBloomEffect : GawainFragment
{
    private FireField _field;
    private float _tick;
    public GNoonBloomEffect() : base("g_noon_bloom") { }
    protected override void Bind()   => Hub.NoonStarted += OnNoon;
    protected override void Unbind() => Hub.NoonStarted -= OnNoon;
    private void OnNoon(bool shortNoon)
    {
        if (Player == null) return;
        _field = FireField.SpawnAt(Owner, Player.transform.position, 3f, 4f, Atk * 0.15f, 2f);
        if (_field != null && Line3) _field.SetFollow(Player.transform);
        _tick = 0f;
    }
    public override void Tick(float dt, PlayerController player)
    {
        if (!Line2 || _field == null || !_field.IsActive) return;
        _tick -= dt;
        if (_tick > 0f) return;
        _tick = 2f;
        int n = CombatQuery.GetNearbyEnemies(_field.Center, _field.Radius, null, 16, Buf);
        for (int i = 0; i < n; i++) if (Buf[i] != null) Hub.AddSunmark(Buf[i], 1, ignoreGap: true);
    }
}

// ── 황혼 ────────────────────────────────────────────────────────

/// <summary>G9 잔염의 길 — 황혼 적중 자리 불씨 · ② 불씨 = 태양흔 +1(황혼에도) · ③ 불씨가 꺼질 때 작게 터진다.</summary>
public sealed class GEmberPathEffect : GawainFragment
{
    private const float SpawnCd = 0.4f;
    private float _cd, _tick;
    private readonly List<FireField> _fields = new();
    public GEmberPathEffect() : base("g_ember_path") { }
    public override void OnHit(in HitInfo hit, PlayerController player)
    {
        if (Hub == null || !Hub.IsDusk || _cd > 0f || hit.ActionType == WeaponActionType.QSkill) return;
        _cd = SpawnCd;
        var f = FireField.SpawnAt(player.gameObject, hit.HitPoint, 1.5f, 2.5f, Atk * 0.10f, 1.5f);
        if (f != null) _fields.Add(f);
    }
    public override void Tick(float dt, PlayerController player)
    {
        if (_cd > 0f) _cd -= dt;
        _tick -= dt;
        bool doTick = _tick <= 0f;
        if (doTick) _tick = 0.5f;
        for (int i = _fields.Count - 1; i >= 0; i--)
        {
            var f = _fields[i];
            if (f == null || !f.IsActive)
            {
                if (Line3 && f != null) SmallBlast(f.Center, 1.5f, Atk * 0.30f);
                _fields.RemoveAt(i);
                continue;
            }
            if (Line2 && doTick)
            {
                int n = CombatQuery.GetNearbyEnemies(f.Center, f.Radius, null, 8, Buf);
                for (int k = 0; k < n; k++) if (Buf[k] != null) Hub.AddSunmark(Buf[k], 1);
            }
        }
    }
}

/// <summary>G10 저무는 해 — 황혼 시작에 작은 낙일 1회 · ② 노을 지대: 4초마다 끌어당기며 태양흔 +1 · ③ 지대에서 처치 = 해 1초 앞당김.</summary>
public sealed class GSettingSunEffect : GawainFragment
{
    private const float FieldRadius = 4f, PullEvery = 4f;
    private bool    _field;
    private Vector3 _at;
    private float   _pullTimer;
    public GSettingSunEffect() : base("g_setting_sun") { }
    protected override void Bind()   { Hub.DuskStarted += OnDusk; Hub.DawnStarted += OnDawn; Hub.Relic.SunImpact += OnSunImpact; }
    protected override void Unbind() { Hub.DuskStarted -= OnDusk; Hub.DawnStarted -= OnDawn; if (Hub.Relic != null) Hub.Relic.SunImpact -= OnSunImpact; }
    private void OnDusk()
    {
        if (Hub.Relic.BonusCasts == 0) Hub.Relic.GrantBonusCast();
        if (Player != null) Say(Player.transform.position, "저무는 해");
    }
    private void OnDawn() => _field = false;
    private void OnSunImpact(Vector3 at, List<GameObject> hits, bool small)
    {
        if (!small || Hub == null || !Hub.IsDusk) return;
        if (Line2) { _field = true; _at = at; _pullTimer = 0f; }
    }
    public override void Tick(float dt, PlayerController player)
    {
        if (!_field || Hub == null || !Hub.IsDusk) return;
        _pullTimer -= dt;
        if (_pullTimer > 0f) return;
        _pullTimer = PullEvery;
        ElementVfxPlayer.PlayBurst(RuneElement.Fire, _at, FieldRadius);
        int n = CombatQuery.GetNearbyEnemies(_at, FieldRadius + 2f, null, 16, Buf);
        for (int i = 0; i < n; i++)
        {
            var mb = Buf[i];
            if (mb == null) continue;
            RelicPullMotion.Pull(mb, _at, 2.5f);
            Hub.AddSunmark(mb, 1, ignoreGap: true);
        }
    }
    public override void OnKill(GameObject deadEnemy, PlayerController player)
    {
        if (!Line3 || !_field || deadEnemy == null || Hub == null || !Hub.IsDusk) return;
        if ((deadEnemy.transform.position - _at).sqrMagnitude <= FieldRadius * FieldRadius) Hub.Gauge.AdvanceTime(1f);
    }
}

/// <summary>G11 불씨 옮기기 — 화상 적 사망 → 4m 안 1체(② 2체)에 남은 화상 · 태양흔이 옮는다 · ③ 옮겨 붙을 때 작은 폭발.</summary>
public sealed class GEmberCarryEffect : GawainFragment
{
    public GEmberCarryEffect() : base("g_ember_carry") { }
    public override void OnKill(GameObject deadEnemy, PlayerController player)
    {
        if (deadEnemy == null || Hub == null) return;
        bool burning = deadEnemy.TryGetComponent<MonsterBurnHandler>(out var burn) && burn.Remaining > 0f;
        var st = RelicMarkStatus.Of(deadEnemy, false);
        int marks = st != null ? st.Sunmark : 0;
        if (!burning && marks <= 0) return;
        int count = Line2 ? 2 : 1;
        int n = CombatQuery.GetNearbyEnemies(deadEnemy.transform.position, 4f, deadEnemy, count, Buf);
        for (int i = 0; i < n; i++)
        {
            var t = Buf[i];
            if (t == null) continue;
            if (burning) MonsterBurnHandler.SpreadTo(deadEnemy, t.gameObject, player.gameObject);
            if (marks > 0) Hub.AddSunmark(t, marks, ignoreGap: true);
            ElementVfxPlayer.PlayBeam(RuneElement.Fire, deadEnemy.transform.position, t.transform.position);
            if (Line3) SmallBlast(t.transform.position, 1.5f, Atk * 0.30f);
            else ElementVfxPlayer.PlayBurst(RuneElement.Fire, t.transform.position, 1f);
        }
    }
}

/// <summary>G12 심판의 노을 — 황혼 중 태양흔 2+ · 체력 15% 이하 적 처형 · ② 처형 적의 태양흔이 흩어짐 · ③ 보스: 처형 대신 태양흔 상한까지.</summary>
public sealed class GDuskJudgmentEffect : GawainFragment
{
    private const float Threshold = 0.15f;
    private float _bossCd;
    public GDuskJudgmentEffect() : base("g_dusk_judgment") { }
    public override void OnHit(in HitInfo hit, PlayerController player)
    {
        if (Hub == null || !Hub.IsDusk) return;
        var mb = AsMonster(hit.Target);
        if (mb == null || mb.IsDead) return;
        var st = RelicMarkStatus.Of(mb.gameObject, false);
        if (st == null || st.Sunmark < 2) return;
        if (mb.Grade == MonsterGrade.Boss)
        {
            if (!Line3 || Time.time < _bossCd) return;
            _bossCd = Time.time + 3f;
            st.SetSunmark(Hub.SunmarkCap, Hub.SunmarkCap);
            return;
        }
        if (mb.EffectiveMaxHp <= 0 || (float)mb.CurrentHp / mb.EffectiveMaxHp > Threshold) return;
        if (Line2)
        {
            int marks = st.Sunmark;
            int n = CombatQuery.GetNearbyEnemies(mb.transform.position, 5f, mb.gameObject, marks, Buf);
            for (int i = 0; i < n; i++) if (Buf[i] != null) Hub.AddSunmark(Buf[i], 1, ignoreGap: true);
        }
        ElementVfxPlayer.PlayBurst(RuneElement.Fire, mb.transform.position, 1.5f);
        mb.TakeDamage(mb.CurrentHp * 10f, player.gameObject, 0.3f);
        Debug.Log($"[GawainMemory] 심판의 노을 — 처형 {mb.name}");
    }
}

// ── 궤적 ────────────────────────────────────────────────────────

/// <summary>G13 새벽에서 한낮으로 — 수확 때 1스택 남김 · ② 정오 동안 적중마다 남은 스택 1 개화 · ③ 정오 끝에 남은 스택 한꺼번에 개화.</summary>
public sealed class GDawnToNoonEffect : GawainFragment
{
    public GDawnToNoonEffect() : base("g_dawn_to_noon") { }
    protected override void Bind()   { Hub.HarvestKeep = 1; Hub.Hit += OnHubHit; Hub.NoonEnded += OnNoonEnd; }
    protected override void Unbind() { Hub.HarvestKeep = 0; Hub.Hit -= OnHubHit; Hub.NoonEnded -= OnNoonEnd; }
    private void OnHubHit(HitInfo hit)
    {
        if (!Line2 || Hub == null || !Hub.IsNoon) return;
        var mb = AsMonster(hit.Target);
        var st = mb != null ? RelicMarkStatus.Of(mb.gameObject, false) : null;
        if (st == null || st.Sunmark <= 0) return;
        st.ConsumeSunmark(st.Sunmark - 1);
        Hub.Bloom(mb, 1, GawainMemoryHub.BloomCause.Fragment);
    }
    private void OnNoonEnd()
    {
        if (!Line3 || Player == null) return;
        int n = CombatQuery.GetNearbyEnemies(Player.transform.position, GawainMemoryHub.HarvestRadius, Owner, 32, Buf);
        var list = new List<MonsterBase>(n);
        for (int i = 0; i < n; i++) if (Buf[i] != null) list.Add(Buf[i]);
        foreach (var mb in list)
        {
            var st = RelicMarkStatus.Of(mb.gameObject, false);
            int s = st != null ? st.ConsumeSunmark() : 0;
            if (s > 0) Hub.Bloom(mb, s, GawainMemoryHub.BloomCause.Fragment);
        }
    }
}

/// <summary>G14 한낮에서 노을로 — 정오 끝에 정오 처치 수만큼 불씨(최대 6) · ② 불씨 = 태양흔 +1 · ③ 불씨가 모두 꺼지면 작은 해.</summary>
public sealed class GNoonToDuskEffect : GawainFragment
{
    private const int MaxEmbers = 6;
    private int _kills;
    private float _tick;
    private readonly List<FireField> _embers = new();
    private Vector3 _center;
    private bool _waiting;
    public GNoonToDuskEffect() : base("g_noon_to_dusk") { }
    protected override void Bind()   { Hub.NoonStarted += OnNoon; Hub.NoonEnded += OnNoonEnd; }
    protected override void Unbind() { Hub.NoonStarted -= OnNoon; Hub.NoonEnded -= OnNoonEnd; }
    private void OnNoon(bool shortNoon) => _kills = 0;
    public override void OnKill(GameObject deadEnemy, PlayerController player) { if (Hub != null && Hub.IsNoon) _kills++; }
    private void OnNoonEnd()
    {
        if (Player == null || _kills <= 0) return;
        int count = Mathf.Min(MaxEmbers, _kills);
        _center = Player.transform.position;
        for (int i = 0; i < count; i++)
        {
            float a = i * Mathf.PI * 2f / count;
            Vector3 p = _center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 2.5f;
            var f = FireField.SpawnAt(Owner, p, 1.2f, 3f, Atk * 0.10f, 1.5f);
            if (f != null) _embers.Add(f);
        }
        _waiting = Line3 && _embers.Count > 0;
        Debug.Log($"[GawainMemory] 한낮에서 노을로 — 불씨 {count}");
    }
    public override void Tick(float dt, PlayerController player)
    {
        if (_embers.Count == 0) return;
        _tick -= dt;
        bool doTick = Line2 && _tick <= 0f;
        if (doTick) _tick = 1f;
        for (int i = _embers.Count - 1; i >= 0; i--)
        {
            var f = _embers[i];
            if (f == null || !f.IsActive) { _embers.RemoveAt(i); continue; }
            if (!doTick) continue;
            int n = CombatQuery.GetNearbyEnemies(f.Center, f.Radius, null, 8, Buf);
            for (int k = 0; k < n; k++) if (Buf[k] != null) Hub.AddSunmark(Buf[k], 1);
        }
        if (_embers.Count == 0 && _waiting)
        {
            _waiting = false;
            SolarDescentSkillRuntime.StrikeSmallSun(player, Hub.Relic, _center, 0.25f, 0.5f);
        }
    }
}
