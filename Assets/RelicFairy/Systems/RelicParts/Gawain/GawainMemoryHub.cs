using System;
using System.Collections.Generic;
using RelicFairy.Monster;
using UnityEngine;

/// <summary>
/// 가웨인 유물 성장 v2 허브 — 「해의 궤적」(설계 v2 §2). 가웨인 첫 조각과 함께 플레이어에 붙는다.
/// <list type="bullet">
/// <item><b>시간대 사건</b> — 정오 게이지의 구간 전환을 여명 · 정오(짧은 정오 포함) · 정오 끝 · 황혼 사건으로 낸다.</item>
/// <item><b>태양흔</b> — 여명 적중마다 1(대상당 0.5초). 「한낮이 길다」면 정오에도 쌓이고 상한이면 그 자리에서 개화.</item>
/// <item><b>개화</b> — 스택마다 반경 2m 폭발(상태 피해 경로: 유물 공격력 × 0.45, 방어 무시, 치명 없음 — 일륜 ③만 치명).
///       정오 공명 2면 2m 안 다른 태양흔 적에게 1씩 튄다. 반응 조각은 <see cref="Bloomed"/>를 듣는다.</item>
/// <item><b>정오 수확</b> — 정오가 열리는 순간 30m 안 태양흔 · 흑점 적 전부 개화(<see cref="PreHarvest"/>가 먼저 돈다).</item>
/// <item><b>시간대 공명</b> — 여명 2(상한 5) · 4(붙듦) / 정오 2(연쇄) · 4(한낮이 길다) / 황혼 2(화상 유지) · 4(짧은 정오).</item>
/// <item><b>엇갈린 빛</b> — 반응 조각 2개 이상 + 서로 다른 반응 둘을 10초 안 → 다음 개화 한 번은 스택을 남긴다.</item>
/// </list>
/// 상태 피해로 죽은 적도 플레이어 처치로 잡힌다(TakeSynergyDamage가 instigator=플레이어면 처치 신호를 낸다).
/// </summary>
[DisallowMultipleComponent]
public sealed class GawainMemoryHub : MonoBehaviour
{
    // ── 수치(설계 v2 · 계획 2) ──
    // 개화 1스택 = 유물 공격력 × 이 값. 0.45였을 때 정오 수확이 45초에 ~1 DPS(전체의 1%)뿐이라 「때가 오면 거둔다」가 손에 안 잡혔다
    // (10-03 예산 실측: 허브 기본 +4% · 랜슬롯 허브 +17%) → 1.0으로 — 하루 한 번의 수확이 눈에 띄는 한 방이 되게.
    public const float BloomAtkRatio    = 1.0f;
    public const float BloomRadius      = 2f;      // 개화 폭발 반경 = 이펙트 크기
    public const float ChainRadius      = 2f;      // 정오 공명 2 연쇄
    public const float HarvestRadius    = 30f;     // 정오 수확 범위
    public const int   BaseCap          = 3;
    public const int   DawnTier2Cap     = 5;
    public const float ShortNoonSeconds = 3f;
    public const float CrossLightWindow = 10f;
    private const int  MaxBloomDepth    = 3;       // 연쇄 · 반응이 서로를 부르는 깊이 상한

    // ── 사건 ──
    public event Action DawnStarted;
    public event Action<bool> NoonStarted;          // (짧은 정오인가)
    public event Action NoonEnded;
    public event Action DuskStarted;
    public event Action<List<MonsterBase>> PreHarvest;   // 수확 직전 — 정점 「맞춰 올리기」 · 여명의 맹세 폭발
    public event Action<MonsterBase, int, BloomCause> Bloomed;
    public event Action<MonsterBase, int> SunmarkAdded;   // (대상, 실제로 쌓인 수) — 해빙 · 반응
    public event Action<HitInfo> Hit;                 // 허브가 받는 적중(조각이 같은 신호를 두 번 받지 않게 허브 경유)

    public enum BloomCause { Harvest, Chain, Instant, Reaction, Fragment }

    /// <summary>허브가 플레이어에 새로 묶였다(첫 조각) — 유물 HUD가 이때 깨어난다.</summary>
    public static event Action<GawainMemoryHub> Bound;

    /// <summary>조각 글자 색(가웨인 금빛).</summary>
    public static readonly Color GawainColor = new(1f, 0.82f, 0.35f);

    private PlayerController  _player;
    private GawainZenithRelic _relic;
    private ZenithGauge       _gauge;
    private PlayerLoadout     _loadout;
    private int _tierDawn, _tierNoon, _tierDusk;
    private int _bloomDepth;
    private float _duskBurnTimer;
    private readonly List<MonsterBase> _buf = new(32);
    private readonly List<MonsterBase> _buf2 = new(16);
    private readonly HashSet<int> _sown = new();     // 새벽 파종 ③ — 개화 때 한 번 더
    private readonly Dictionary<string, float> _lastReaction = new();
    private bool _crossLight;
    private bool _harvesting;   // 수확 중 — 「한낮이 길다」 즉석 개화를 막는다(정점 맞춤이 상한을 채워도 수확이 거둔다)

    // ── 조각이 켜는 스위치 ──
    /// <summary>정오 수확 때 남길 스택 수(새벽에서 한낮으로).</summary>
    public int  HarvestKeep { get; set; }
    /// <summary>개화가 치명으로 터질 수 있다(일륜 ③).</summary>
    public bool BloomCanCrit { get; set; }
    /// <summary>다음 정오의 수확이 두 번(아침 사냥 ③).</summary>
    public bool DoubleHarvestNext { get; set; }
    /// <summary>이 시각까지는 정오에도 적중이 태양흔을 쌓는다(해시계 ②).</summary>
    public float NoonStackUntil { get; set; }
    /// <summary>황혼에도 태양흔이 쌓인다(잔염의 길 ② 등 — 조각이 직접 AddSunmark를 부르면 시간대와 무관).</summary>
    public bool DuskStacking { get; set; }
    /// <summary>흑점 모드(일식 ① — 잠식 50+). 켜져 있으면 태양흔 대신 흑점이 쌓인다.</summary>
    public bool EclipseActive { get; set; }

    public int  TierDawn => _tierDawn;
    public int  TierNoon => _tierNoon;
    public int  TierDusk => _tierDusk;
    public int  SunmarkCap => _tierDawn >= 2 ? DawnTier2Cap : BaseCap;
    public bool HasCrossLight => _crossLight;
    public PlayerController   Player => _player;
    public GawainZenithRelic  Relic  => _relic;
    public ZenithGauge        Gauge  => _gauge;
    public ZenithGauge.ZPhase Phase  => _gauge != null ? _gauge.CurrentPhase : ZenithGauge.ZPhase.Charging;
    public bool IsDawn => Phase == ZenithGauge.ZPhase.Charging;
    public bool IsNoon => Phase == ZenithGauge.ZPhase.Noon;
    public bool IsDusk => Phase == ZenithGauge.ZPhase.Cooldown;

    /// <summary>플레이어의 허브(없으면 붙이고 묶는다). 가웨인 유물이 아니면 null.</summary>
    public static GawainMemoryHub Ensure(PlayerController player)
    {
        if (player == null || player.RelicBehavior is not GawainZenithRelic) return null;
        if (!player.TryGetComponent<GawainMemoryHub>(out var hub)) hub = player.gameObject.AddComponent<GawainMemoryHub>();
        hub.Bind(player);
        return hub;
    }

    public static GawainMemoryHub Of(PlayerController player)
        => player != null && player.TryGetComponent<GawainMemoryHub>(out var h) ? h : null;

    private void Bind(PlayerController player)
    {
        if (_player == player && _gauge != null) { RecomputeTiers(); return; }
        Unbind();
        _player  = player;
        _relic   = player.RelicBehavior as GawainZenithRelic;
        _gauge   = _relic?.Gauge;
        _loadout = AppBootstrapper.Instance?.Loadout;
        if (_gauge != null) _gauge.PhaseChanged += OnPhaseChanged;
        if (_loadout != null) _loadout.RelicPartsChanged += RecomputeTiers;
        // 허브 몫의 신호(태양흔 기본 규칙) — 파츠 허브에 시스템 효과로 한 번만 등록
        player.RuneEffects.Parts.AddSystemEffect(new HubRelay(this));
        RecomputeTiers();
        Bound?.Invoke(this);
    }

    private void Unbind()
    {
        if (_gauge != null) _gauge.PhaseChanged -= OnPhaseChanged;
        if (_loadout != null) _loadout.RelicPartsChanged -= RecomputeTiers;
        _gauge = null; _loadout = null;
    }

    private void OnDestroy()
    {
        Unbind();
        if (_gauge != null) { _gauge.HoldDawn = false; _gauge.Paused = false; }
    }

    /// <summary>공명 단계를 다시 센다(조각 · 메아리 · 등급이 바뀔 때).</summary>
    public void RecomputeTiers()
    {
        if (_loadout == null) return;
        _tierDawn = RelicResonance.GawainTierOf(_loadout, RelicPartAnchor.Dawn);
        _tierNoon = RelicResonance.GawainTierOf(_loadout, RelicPartAnchor.Noon);
        _tierDusk = RelicResonance.GawainTierOf(_loadout, RelicPartAnchor.Dusk);
        if (_gauge != null) _gauge.HoldDawn = _tierDawn >= 4;
        Debug.Log($"[GawainMemory] 시간대 공명 — 여명 {_tierDawn} · 정오 {_tierNoon} · 황혼 {_tierDusk} · 태양흔 상한 {SunmarkCap}");
    }

    // ── 시간대 사건 ──────────────────────────────────────

    private void OnPhaseChanged(ZenithGauge.ZPhase from, ZenithGauge.ZPhase to)
    {
        if (from == ZenithGauge.ZPhase.Noon) NoonEnded?.Invoke();
        switch (to)
        {
            case ZenithGauge.ZPhase.Charging:
                DawnStarted?.Invoke();
                break;
            case ZenithGauge.ZPhase.Noon:
                bool shortNoon = _gauge.IsShortNoon;
                NoonStarted?.Invoke(shortNoon);
                Harvest(shortNoon ? "짧은 정오" : "정오");
                break;
            case ZenithGauge.ZPhase.Cooldown:
                if (_tierDusk >= 4) _gauge.QueueShortNoon(ShortNoonSeconds);   // 밤이 오지 않는다
                DuskStarted?.Invoke();
                break;
        }
    }

    private void Update()
    {
        if (_gauge == null) return;
        // 황혼 공명 2 — 화상이 다음 여명까지 이어진다: 남은 화상이 황혼 남은 시간 + 1초보다 짧으면 늘린다(0.5초마다)
        if (_tierDusk >= 2 && IsDusk)
        {
            _duskBurnTimer -= Time.deltaTime;
            if (_duskBurnTimer <= 0f)
            {
                _duskBurnTimer = 0.5f;
                float need = _gauge.PhaseRemaining + 1f;
                int n = CombatQuery.GetNearbyEnemies(_player.transform.position, HarvestRadius, _player.gameObject, 32, _buf);
                for (int i = 0; i < n; i++)
                {
                    var mb = _buf[i];
                    if (mb != null && mb.TryGetComponent<MonsterBurnHandler>(out var burn) && burn.Remaining > 0f && burn.Remaining < need)
                        MonsterBurnHandler.ExtendOn(mb.gameObject, need - burn.Remaining);
                }
            }
        }
    }

    // ── 태양흔 ───────────────────────────────────────────

    /// <summary>
    /// 태양흔을 쌓는다(조각 · 반응이 부른다). 일식이면 흑점으로. 「한낮이 길다」면 정오 중 상한에서 그 자리 개화.
    /// 실제로 쌓인 수를 돌려준다.
    /// </summary>
    public int AddSunmark(MonsterBase mb, int n, bool ignoreGap = false)
    {
        if (mb == null || mb.IsDead || n <= 0) return 0;
        var st = RelicMarkStatus.Of(mb.gameObject, true);
        int added = EclipseActive ? st.AddBlackspot(n, ignoreGap) : st.AddSunmark(n, SunmarkCap, ignoreGap);
        if (added <= 0) return 0;
        SunmarkAdded?.Invoke(mb, added);
        if (!EclipseActive && !_harvesting && _tierNoon >= 4 && IsNoon && st.Sunmark >= SunmarkCap)
            Bloom(mb, st.ConsumeSunmark(), BloomCause.Instant);
        return added;
    }

    /// <summary>새벽 파종 ③ — 이 적은 개화 때 한 번 더.</summary>
    public void MarkSown(MonsterBase mb) { if (mb != null) _sown.Add(mb.GetInstanceID()); }

    /// <summary>허브 기본 규칙 — 여명 적중 = 태양흔 1(+「한낮이 길다」 · 해시계 ② 창).</summary>
    private void OnBaseHit(in HitInfo hit)
    {
        Hit?.Invoke(hit);
        if (hit.Target == null || !hit.Target.TryGetComponent<MonsterBase>(out var mb)) return;
        bool canStack = IsDawn || (IsNoon && (_tierNoon >= 4 || Time.time < NoonStackUntil)) || (IsDusk && DuskStacking);
        if (canStack) AddSunmark(mb, 1);
    }

    // ── 개화 ─────────────────────────────────────────────

    /// <summary>
    /// 개화 — 스택마다 반경 2m 폭발. 엇갈린 빛이 있으면 이번 개화는 스택을 소모하지 않는다(부르는 쪽이 소모 전에 확인).
    /// </summary>
    public void Bloom(MonsterBase target, int stacks, BloomCause cause)
    {
        if (target == null || stacks <= 0 || _player == null) return;
        if (_bloomDepth >= MaxBloomDepth) return;
        _bloomDepth++;
        try
        {
            Vector3 at = target.transform.position;
            float per = _player.RuntimeStats.GetEffectiveAttack(AttackStatKind.Melee) * BloomAtkRatio;
            for (int s = 0; s < stacks; s++)
            {
                ElementVfxPlayer.PlayBurst(RuneElement.Fire, at, BloomRadius);
                int n = CombatQuery.GetNearbyEnemies(at, BloomRadius, null, 16, _buf2);
                for (int i = 0; i < n; i++)
                {
                    var e = _buf2[i];
                    if (e == null || e.IsDead) continue;
                    float dmg = per;
                    bool crit = false;
                    if (BloomCanCrit) dmg = CombatCalculator.RollCrit(_player.WeaponManager?.Weapon0Data, per, out crit, false);
                    CombatQuery.DealSynergyDamage(e, dmg, _player.gameObject, 1f, crit, RuneElement.Fire);
                }
            }
            if (_tierNoon >= 2) ChainFrom(target, at);
            Bloomed?.Invoke(target, stacks, cause);
            Debug.Log($"[GawainMemory] 개화 {target.name} ×{stacks} ({cause}) · 1스택 {per:0} · 반경 {BloomRadius}m{(BloomCanCrit ? " · 치명 가능" : "")}");
        }
        finally { _bloomDepth--; }
    }

    private void ChainFrom(MonsterBase source, Vector3 at)
    {
        // 사본으로 돈다 — 연쇄가 「한낮이 길다」 개화를 부르면 공용 버퍼가 그 안에서 다시 쓰인다
        var near = new List<MonsterBase>(8);
        CombatQuery.GetNearbyEnemies(at, ChainRadius, source.gameObject, 8, near);
        for (int i = 0; i < near.Count; i++)
        {
            var e = near[i];
            if (e == null || e.IsDead) continue;
            var st = RelicMarkStatus.Of(e.gameObject, false);
            if (st != null && (st.Sunmark > 0 || st.Blackspot > 0)) AddSunmark(e, 1, ignoreGap: true);
        }
    }

    /// <summary>정오 수확 — 30m 안 태양흔 · 흑점 적 전부 개화.</summary>
    private void Harvest(string why)
    {
        if (_player == null) return;
        int n = CombatQuery.GetNearbyEnemies(_player.transform.position, HarvestRadius, _player.gameObject, 32, _buf);
        var targets = new List<MonsterBase>(n);
        for (int i = 0; i < n; i++) if (_buf[i] != null) targets.Add(_buf[i]);
        _harvesting = true;
        try { HarvestTargets(targets, why); }
        finally { _harvesting = false; }
    }

    private void HarvestTargets(List<MonsterBase> targets, string why)
    {
        PreHarvest?.Invoke(targets);

        bool twice = DoubleHarvestNext;
        DoubleHarvestNext = false;
        int bloomedTargets = 0, bloomedStacks = 0;
        foreach (var mb in targets)
        {
            if (mb == null || mb.IsDead) continue;
            var st = RelicMarkStatus.Of(mb.gameObject, false);
            if (st == null) continue;
            int sun = _crossLight ? st.Sunmark : st.ConsumeSunmark(HarvestKeep);
            int black = _crossLight ? st.Blackspot : st.ConsumeBlackspot();
            int stacks = sun + black;
            if (stacks <= 0) continue;
            bool sown = _sown.Remove(mb.GetInstanceID());
            Bloom(mb, stacks, BloomCause.Harvest);
            if (twice) Bloom(mb, stacks, BloomCause.Harvest);
            if (sown) Bloom(mb, 1, BloomCause.Fragment);
            bloomedTargets++; bloomedStacks += stacks;
        }
        if (_crossLight && bloomedTargets > 0) { _crossLight = false; Debug.Log("[GawainMemory] 엇갈린 빛 — 이번 수확은 스택을 남겼다"); }
        Debug.Log($"[GawainMemory] {why} 수확 — 대상 {bloomedTargets} · 스택 {bloomedStacks}{(twice ? " · 두 번" : "")}");
    }

    // ── 반응 · 엇갈린 빛 ─────────────────────────────────

    /// <summary>반응이 일어났다(반응 조각이 부른다) — 서로 다른 반응 둘이 10초 안이면 엇갈린 빛.</summary>
    public void NotifyReaction(string reactionKey)
    {
        float now = Time.time;
        _lastReaction[reactionKey] = now;
        if (_crossLight || ReactionFragmentCount() < 2) return;
        foreach (var kv in _lastReaction)
        {
            if (kv.Key == reactionKey) continue;
            if (now - kv.Value <= CrossLightWindow)
            {
                _crossLight = true;
                Debug.Log($"[GawainMemory] 엇갈린 빛 — {kv.Key} + {reactionKey}");
                break;
            }
        }
    }

    private int ReactionFragmentCount()
    {
        if (_loadout == null) return 0;
        int n = 0;
        var data = Managers.RelicParts;
        foreach (var id in _loadout.RelicPartIds)
        {
            var e = data?.GetById(id);
            if (e != null && e.relic_id == "gawain" && e.IsReaction) n++;
        }
        return n;
    }

    /// <summary>허브 몫의 전투 신호 — 파츠 허브에 「시스템 효과」로 한 번만 등록된다.</summary>
    private sealed class HubRelay : RelicPartEffect
    {
        public const string Key = "~gawain_hub";
        private readonly GawainMemoryHub _hub;
        public HubRelay(GawainMemoryHub hub) : base(Key) => _hub = hub;
        public override void OnHit(in HitInfo hit, PlayerController player) { if (_hub != null) _hub.OnBaseHit(hit); }
    }
}
