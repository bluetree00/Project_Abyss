using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

// ── 불 전설 3종 — 「몇 초마다」를 없애고 빌드 행동으로 발동(10-14 룬 재설계 R3) ─────────────
// 마그마 분출 = 처치 · 불사조 강타 = 연격 · 화염 오브 = 기동. 존 단계마다 피해 +15%(개수형은 +1) — LegendaryTuning.

// ── F-1. 불바다 (처치 · 옛 마그마 분출) ──────────────────────────────────
/// <summary>
/// 10-24 개편 — 쓰러진 적 자리에 용암 웅덩이(반경 value3 = 2.5 m · 4초 · 최대 value2 = 4개). 위에 선 적은 점화(초당 공격력 × value = 35% · 존 배율).
/// 웅덩이 점화로 쓰러져도 처치라 그 자리에 또 웅덩이 — 무리를 웅덩이로 끌어들여 번지게 한다.
/// 각성(불 존 4단계) = 반경 3 m · 웅덩이 위 적 받는 피해 +15%.
/// </summary>
public sealed class FireLegendAoeEffect : ItemCombatEffectBase
{
    private const float PoolSeconds = 4f, AwakeRadius = 3f, AwakeAmp = 0.15f, AmpSeconds = 0.4f;
    private const string IgniteId = "lava_pool";   // 존 점화(ignite)와 따로 쌓인다 — 같은 이름이면 서로 덮어써 웅덩이 몫이 존 점화를 지웠다(10-24 실측 95)

    private readonly List<LegendGroundField> _pools = new();

    /// <summary>새 행(발동 「OnKillPool」)인가 — 옛 CDN 행이면 코드 기본값을 쓴다.</summary>
    private bool NewRow => _trigger == "OnKillPool";

    public FireLegendAoeEffect(ItemEffectSlot s) : base(s) { }

    public override void OnActivate(ItemEffectContext ctx) => LegendaryRuntime.EnsureExists();

    public override void OnDeactivate() => Clear();

    public override void OnRoomEnter(ItemEffectContext ctx) => Clear();

    public override void OnKill(ItemEffectContext ctx, GameObject target)
    {
        if (ctx?.Player == null || target == null) return;
        bool awake = LegendaryTuning.Awakened("FIRE");
        int max = Mathf.Max(1, Mathf.RoundToInt(LegendaryTuning.Pick(NewRow, _value2, 4f)));
        _pools.RemoveAll(p => p == null);
        while (_pools.Count >= max) { if (_pools[0] != null) Object.Destroy(_pools[0].gameObject); _pools.RemoveAt(0); }

        float radius = awake ? AwakeRadius : LegendaryTuning.Pick(NewRow, _value3, 2.5f);
        float perSec = LegendaryTuning.Atk(ctx) * LegendaryTuning.Pick(NewRow, _value, 0.35f) * LegendaryTuning.TierMul("FIRE");
        GameObject self = Self(ctx);
        Vector3 at = target.transform.position;
        _pools.Add(LegendGroundField.Spawn(this, ctx, at, at, radius, PoolSeconds, RuneElement.Fire, mb =>
        {
            mb.Status.ApplyDot(IgniteId, perSec, 1f, 1, self);   // 위에 있는 동안 초당 — 떠나면 한 번 더 타고 꺼진다
            if (awake) mb.ApplyDamageTakenAmp(AwakeAmp, AmpSeconds, "lava_pool");
        }));
    }

    private void Clear()
    {
        LegendaryRuntime.Instance?.ClearAgentsOf(this);
        _pools.Clear();
    }
}

// ── F-2. 불사조의 맥박 (연격 · 옛 불사조 강타) ───────────────────────────
/// <summary>
/// 10-24 개편 — 적중이 value3(1.2)초 안에 이어지면 열기 +1(최대 value2 = 20 · 같은 순간 여러 적은 한 번) — 열기마다 공격력 +1%(존 배율).
/// 가득 차면 불사조가 그 적에게 내리꽂혀 반경 3 m 공격력 × value(600% · 실측 95 맞춤) + 점화, 열기 0. 끊기면 초당 5씩 식는다 → 끊지 않고 치는 리듬.
/// 각성(불 존 4단계) = 내리꽂힌 뒤 열기가 0이 아니라 절반. HUD = LegendaryHud "heat".
/// </summary>
public sealed class FireLegendSingleEffect : ItemCombatEffectBase
{
    private static readonly string[] s_hitSfx = { SoundKey.Sfx.MonsterHit1, SoundKey.Sfx.MonsterHit2, SoundKey.Sfx.MonsterHit3 };
    private const float DiveRadius = 3f, DecayPerSecond = 5f, IgniteRatio = 0.15f, SameMoment = 0.08f, AtkPerHeat = 0.01f;
    private const int   IgniteTicks = 3;
    private const string HudKey = "heat", IgniteId = "ignite";

    private readonly List<GameObject> _buf = new();
    private float _heat, _lastHitAt = -999f, _lastCountAt = -999f;

    /// <summary>새 행(발동 「OnHitHeat」)인가 — 옛 CDN 행이면 코드 기본값을 쓴다.</summary>
    private bool NewRow => _trigger == "OnHitHeat";

    private int MaxHeat => Mathf.Max(1, Mathf.RoundToInt(LegendaryTuning.Pick(NewRow, _value2, 20f)));

    public FireLegendSingleEffect(ItemEffectSlot s) : base(s) { }

    public override void OnActivate(ItemEffectContext ctx)
    {
        LegendaryRuntime.EnsureExists();
        _heat = 0f;
        Publish();
    }

    public override void OnDeactivate() => LegendaryHud.Clear(HudKey);

    private void Publish() => LegendaryHud.Set(HudKey, Mathf.FloorToInt(_heat), MaxHeat);

    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (report.DamageDealt <= 0f || report.Target == null || ctx?.Player == null) return;   // 무적 대상 헛침은 안 센다
        _lastHitAt = Time.time;
        if (Time.time - _lastCountAt < SameMoment) return;   // 한 휘두름에 여러 적 — 한 번
        _lastCountAt = Time.time;
        _heat = Mathf.Min(MaxHeat, Mathf.Floor(_heat) + 1f);
        Publish();
        if (_heat < MaxHeat) return;
        Dive(ctx, report.Target);
        _heat = LegendaryTuning.Awakened("FIRE") ? Mathf.Floor(MaxHeat * 0.5f) : 0f;   // 각성 — 절반이 남는다
        Publish();
    }

    public override void OnTick(ItemEffectContext ctx, float deltaTime)
    {
        if (_heat <= 0f || Time.time - _lastHitAt <= LegendaryTuning.Pick(NewRow, _value3, 1.2f)) return;
        int before = Mathf.FloorToInt(_heat);
        _heat = Mathf.Max(0f, _heat - DecayPerSecond * deltaTime);   // 끊기면 식는다
        if (Mathf.FloorToInt(_heat) != before) Publish();
    }

    public override void ContributeDynamicStats(ItemEffectContext ctx, ref ItemDynamicStats dyn)
    {
        if (_heat >= 1f) dyn.attackPercent += Mathf.Floor(_heat) * AtkPerHeat * LegendaryTuning.TierMul("FIRE");
    }

    private void Dive(ItemEffectContext ctx, GameObject target)
    {
        var cat = LegendaryRuntime.Catalog;
        float atk  = LegendaryTuning.Atk(ctx);
        float tier = LegendaryTuning.TierMul("FIRE");
        float dmg  = atk * LegendaryTuning.Pick(NewRow, _value, 6f) * tier;
        Vector3 at = target.transform.position;
        LegendaryRuntime.SpawnVfx(cat?.fireSingleVfx, at + Vector3.up * 1.2f, Quaternion.LookRotation(Vector3.down), 1.5f);   // 위에서 내리꽂는 불사조
        int n = CombatQuery.GetNearbyDamageables(at, DiveRadius, Self(ctx), 16, _buf);
        for (int i = 0; i < n; i++)
        {
            CombatQuery.DealSynergyDamage(_buf[i], dmg, Self(ctx), element: RuneElement.Fire);
            var mb = _buf[i] != null ? _buf[i].GetComponentInParent<RelicFairy.Monster.MonsterBase>() : null;
            if (mb != null && !mb.IsDead) mb.Status.ApplyDot(IgniteId, atk * IgniteRatio * tier, 1f, IgniteTicks, Self(ctx));
        }
        ElementVfxPlayer.PlayBurst(RuneElement.Fire, at, DiveRadius);   // 내리꽂힘 범위 = 판정
        Managers.Sound?.PlayEffectAsync(s_hitSfx[Random.Range(0, s_hitSfx.Length)]).Forget();
    }
}

// ── F-3. 불꽃 질주 (기동 · 옛 화염 오브) ─────────────────────────────────
/// <summary>
/// 10-24 개편 — 대시가 지나간 길이 value3(2)초 불길(폭 1.6 m) — 지나간 적 점화(초당 공격력 × value = 20% · 3초 · 존 배율).
/// 대시 뒤 1초 안 첫 타격 = 불씨 일격: 그 타격 × value2(1.3) + 반경 2 m에 그 타격 25%(그동안 무기 끝에 불씨).
/// (실측 95: 설계값 ×2 · 50% · 40%는 대시 2.5초마다 무리 4에서 존 2 +127% · ×1.5 · 30% · 30%도 +83% — 광역 무기는 한 휘두름 모든 적이 불씨 일격이라 겹쳤다)
/// 둘레를 돌던 오브는 걷었다 — 가만히 있어도 도는 효과 대신 몸으로 파고드는 행동. 각성(불 존 4단계) = 불길 4초 · 불길 위 이동속도 +15%.
/// </summary>
public sealed class FireLegendProjectileEffect : ItemCombatEffectBase
{
    private const float TrailHalfWidth = 0.8f, EmberWindow = 1f, EmberRadius = 2f, EmberSplash = 0.25f, SameSwing = 0.08f;
    private const float AwakeTrailSeconds = 4f, AwakeMove = 0.15f;
    private const int   IgniteTicks = 3, MaxTrails = 3;
    private const string IgniteId = "ignite";

    private readonly List<LegendGroundField> _trails = new();
    private readonly List<GameObject> _buf = new();
    private PlayerController _player;
    private Vector3 _dashFrom;
    private bool _dashing;
    private float _emberUntil = -999f, _emberAt = -999f;
    private GameObject _emberTarget;
    private int _emberFx;

    /// <summary>새 행(발동 「OnDashTrail」)인가 — 옛 CDN 행이면 코드 기본값을 쓴다.</summary>
    private bool NewRow => _trigger == "OnDashTrail";

    public FireLegendProjectileEffect(ItemEffectSlot s) : base(s) { }

    public override void OnActivate(ItemEffectContext ctx)
    {
        LegendaryRuntime.EnsureExists();
        if (ctx?.Player == null) return;
        _player = ctx.Player;
        _player.OnDodgeStart += HandleDodgeStart;
    }

    public override void OnDeactivate()
    {
        if (_player != null) _player.OnDodgeStart -= HandleDodgeStart;
        _player = null;
        EndEmber();
        ClearTrails();
    }

    public override void OnRoomEnter(ItemEffectContext ctx) => ClearTrails();

    public override void OnRollEnd(ItemEffectContext ctx)
    {
        if (ctx?.Player == null) return;
        Vector3 to   = ctx.Player.transform.position;
        Vector3 from = _dashing ? _dashFrom : to;
        _dashing = false;

        bool awake   = LegendaryTuning.Awakened("FIRE");
        float life   = awake ? AwakeTrailSeconds : LegendaryTuning.Pick(NewRow, _value3, 2f);
        float perSec = LegendaryTuning.Atk(ctx) * LegendaryTuning.Pick(NewRow, _value, 0.2f) * LegendaryTuning.TierMul("FIRE");
        GameObject self = Self(ctx);
        _trails.RemoveAll(t => t == null);
        while (_trails.Count >= MaxTrails) { if (_trails[0] != null) Object.Destroy(_trails[0].gameObject); _trails.RemoveAt(0); }
        _trails.Add(LegendGroundField.Spawn(this, ctx, from, to, TrailHalfWidth, life, RuneElement.Fire,
                                            mb => mb.Status.ApplyDot(IgniteId, perSec, 1f, IgniteTicks, self)));

        EndEmber();
        _emberUntil = Time.time + EmberWindow;
        var wi = ctx.Player.WeaponManager != null ? ctx.Player.WeaponManager.GetCurrentWeaponComponent<WeaponInstance>() : null;
        _emberFx = ElementVfxPlayer.AttachStatus(RuneElement.Fire, wi != null && wi.tipPoint != null ? wi.tipPoint : ctx.Player.transform, EmberWindow);
    }

    public override void OnPreDealDamage(ItemEffectContext ctx, ref DamagePacket pkt)
    {
        if (pkt.Negated || pkt.Attacker == null || pkt.Attacker != Self(ctx)) return;
        bool first = Time.time <= _emberUntil;
        if (!first && Time.time - _emberAt > SameSwing) return;   // 같은 휘두름에 맞은 다른 적도 불씨 일격
        if (first)
        {
            _emberAt     = Time.time;
            _emberTarget = pkt.Target;
            EndEmber();
        }
        pkt.FinalDamage *= LegendaryTuning.Pick(NewRow, _value2, 1.3f);
    }

    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (_emberTarget == null || report.Target != _emberTarget) return;
        _emberTarget = null;
        if (report.DamageDealt <= 0f) return;
        float splash = report.DamageDealt * EmberSplash;
        Vector3 at = report.Target.transform.position;
        int n = CombatQuery.GetNearbyDamageables(at, EmberRadius, Self(ctx), 12, _buf);
        for (int i = 0; i < n; i++)
            if (_buf[i] != report.Target) CombatQuery.DealSynergyDamage(_buf[i], splash, Self(ctx), element: RuneElement.Fire);
        ElementVfxPlayer.PlayBurst(RuneElement.Fire, at, EmberRadius);   // 불씨 일격 범위 = 판정
    }

    public override void ContributeDynamicStats(ItemEffectContext ctx, ref ItemDynamicStats dyn)
    {
        if (ctx?.Player == null || _trails.Count == 0 || !LegendaryTuning.Awakened("FIRE")) return;
        Vector3 p = ctx.Player.transform.position;
        foreach (var t in _trails)
            if (t != null && t.Contains(p)) { dyn.moveSpeed += AwakeMove; return; }   // 각성 — 불길 위 이동속도
    }

    private void HandleDodgeStart()
    {
        if (_player == null) return;
        _dashFrom = _player.transform.position;
        _dashing  = true;
    }

    private void EndEmber()
    {
        _emberUntil = -999f;
        ElementVfxPlayer.ReleaseAura(_emberFx);
        _emberFx = 0;
    }

    private void ClearTrails()
    {
        LegendaryRuntime.Instance?.ClearAgentsOf(this);
        _trails.Clear();
    }
}
