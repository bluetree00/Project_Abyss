using System;
using System.Collections.Generic;
using RelicFairy.Monster;
using UnityEngine;

/// <summary>
/// 랜슬롯 유물 성장 v2 허브 — 「광기의 계단」(설계 v2 §3). 랜슬롯 첫 조각과 함께 플레이어에 붙는다.
/// <list type="bullet">
/// <item><b>배신의 낙인</b> — 광기 20+(또는 광란)에서 적중하면 1(대상당 0.5초). 상한 3(광기의 눈 5 · 7). 낙인 자체는 피해가 없다.</item>
/// <item><b>넘침 → 원한</b> — 적중으로 새긴 낙인이 상한을 넘치면 그 몫이 원한(최대 5)으로 저축된다.
///       조각 · 반응이 옮기거나 튀긴 낙인은 그 줄이 말할 때만 원한이 된다.</item>
/// <item><b>찢기</b> — 심판 막타가 낙인을 찢는다: X자 1회 = 유물 공격력 × 0.35(상태 피해 경로: 방어 무시 · 치명 없음).</item>
/// <item><b>원한 쓰기</b> — 막타 뒤 원한 1개당 참격파 1줄(8 m)이 부채꼴로 퍼진다.</item>
/// <item><b>계단 공명</b> — 이어진 계단 2(받침) · 3(재출발 30) · 4(두 번째 광란 + 심판 1회).</item>
/// <item><b>엇갈린 빛</b> — 반응 조각 2개 이상 + 서로 다른 반응 둘을 10초 안 → 다음 찢기 한 번(심판이면 그 심판 전체)은 낙인을 남긴다.</item>
/// </list>
/// 찢기 · 참격파로 죽은 적도 플레이어 처치로 잡힌다(TakeSynergyDamage가 instigator=플레이어면 처치 신호를 낸다).
/// </summary>
[DisallowMultipleComponent]
public sealed class LancelotMemoryHub : MonoBehaviour
{
    // ── 수치(설계 v2 · 계획 3) ──
    public const float TearAtkRatio     = 0.35f;   // X자 1회 = 유물 공격력 × 이 값
    public const float WaveAtkRatio     = 0.30f;   // 원한 참격파 1줄 — 10-02 예산 실측: 0.60이면 허브만으로 +18%(옛 3픽과 같음), 그중 ⅔가 원한이라 반으로
    public const float WaveLength       = 8f;
    public const float WaveHalfWidth    = 0.9f;
    public const float WaveSpreadDeg    = 18f;     // 참격파 사이 각
    public const int   BaseBrandCap     = 3;
    public const int   EyeBrandCap      = 5;       // 광기의 눈 ① (광기 30+)
    public const int   EyeBrandCapMax   = 7;       // 광기의 눈 ② (광기 40)
    public const int   GrudgeCap        = 5;
    public const float ScreenRadius     = 30f;     // 「화면 안」
    public const float CrossLightWindow = 10f;
    // X자 겹침 상한. 24였을 때 원한의 칼날 ②(원한 × 낙인)가 오래 싸우면 매 심판 상한에 붙어(원한은 늘 차 있다)
    // 「둘 다 높을 때 쏜다」는 판단 없이 고정 +22 DPS였다(10-03 예산 실측: 단독 +31% · 다른 조각 +2~8%) → 12.
    public const int   MaxXPerTear      = 12;
    private const int  MaxTearVfx       = 3;
    private const float RestartRatio    = 0.75f;   // 재출발 = 광기 30(최대 40 기준)
    public const string SlashVfxKey     = "vfx_lancelot_judgment";

    // ── 사건 ──
    public event Action<HitInfo> Hit;                                  // 허브가 받는 적중(조각이 같은 신호를 두 번 받지 않게 허브 경유)
    public event Action<HitInfo, int> Swing;                           // 평타 한 번의 첫 적중(인자: 몇 번째 평타인가 — 1부터)
    public event Action<MonsterBase, int, int, BrandSource> BrandAdded; // (대상, 새긴 수, 넘친 수, 출처)
    public event Action<TearContext> PreTear;                          // 찢기 직전 — 반응 · 조각이 셈을 바꾼다
    public event Action<MonsterBase, int, TearCause> Torn;             // (대상, 찢은 낙인, 원인)
    public event Action<int> RungCrossed;                              // 광기가 계단(1~4)을 위로 넘었다
    public event Action FrenzyStarted;
    public event Action FrenzyEnded;
    public event Action JudgmentBegan;
    public event Action<Vector3, Vector3> BeforeFinisher;
    public event Action<Vector3, Vector3> FinisherLanded;              // 막타 뒤 — 배신의 실 · 원한의 칼날 ①
    public event Action<Vector3, Vector3, int> JudgmentFinished;       // (위치, 방향, 이번 심판이 찢은 낙인)
    public event Action<int> GrudgeSaved;                              // 원한이 쌓였다(실제로 더한 수)
    public event Action<int> GrudgeSpent;                              // 원한을 썼다(참격파 줄 수)

    /// <summary>허브가 플레이어에 새로 묶였다(첫 조각) — 유물 HUD가 이때 깨어난다.</summary>
    public static event Action<LancelotMemoryHub> Bound;

    /// <summary>조각 · 허브 글자 색(랜슬롯 진홍).</summary>
    public static readonly Color LancelotColor = new(0.95f, 0.34f, 0.30f);

    public enum BrandSource { Hit, Fragment, Reaction }
    public enum TearCause { Judgment, SmallJudgment, Fragment, Reaction }

    /// <summary>찢기 한 번의 셈 — <see cref="PreTear"/>가 바꾼다. X자 수 = Stacks × XMultiplier(상한 24).</summary>
    public sealed class TearContext
    {
        public MonsterBase Target;
        public TearCause   Cause;
        public int         Stacks;        // 센 낙인 수(빙결 · 보스는 두 배로 센다)
        public int         XMultiplier;   // 낙인 하나당 X자 수(원한의 칼날 ② · 폭로 ② · 전이 ③)
    }

    private PlayerController     _player;
    private LancelotMadnessRelic _relic;
    private MadnessStack         _madness;
    private PlayerLoadout        _loadout;
    private GrudgeStore          _grudge;
    private int   _ladder;
    private int   _prevStacks;
    private int   _swingNo;
    private float _lastSwingAt = -1f;
    private int   _judgmentIndex;          // 이번 광란에서 몇 번째 심판인가(1부터)
    private int   _tornThisJudgment;
    private int   _grudgeAtFinisher;
    private bool  _keepThisJudgment;       // 엇갈린 빛 — 이번 심판은 낙인을 남긴다
    private bool  _crossLight;
    private readonly TearContext _ctx = new();
    private readonly List<MonsterBase> _buf = new(32);
    private readonly List<MonsterBase> _tornTargets = new(16);
    private readonly HashSet<int> _finisherHits = new();
    private readonly Dictionary<string, float> _lastReaction = new();

    // ── 조각이 켜는 스위치 ──
    /// <summary>광기의 눈 — 1: 광기 30+ 낙인 상한 5 · 2: 광기 40 상한 7.</summary>
    public int  EyeTier { get; set; }
    /// <summary>끝의 문턱이 광란을 미루는 중 — HUD 광기 막대가 떤다.</summary>
    public bool HoldingThreshold { get; set; }
    /// <summary>두 번째 심판 ② — 이번 심판은 원한을 쓰지 않는다(심판이 시작될 때마다 false로).</summary>
    public bool SkipGrudgeThisJudgment { get; set; }

    public PlayerController     Player  => _player;
    public LancelotMadnessRelic Relic   => _relic;
    public MadnessStack         Madness => _madness;
    public GrudgeStore          Grudge  => _grudge;
    public int  Ladder        => _ladder;
    public int  Stacks        => _madness != null ? _madness.Stacks : 0;
    public bool IsFrenzy      => _madness != null && _madness.IsFrenzy;
    public int  JudgmentIndex => _judgmentIndex;
    public int  GrudgeAtFinisher => _grudgeAtFinisher;
    public bool HasCrossLight => _crossLight;
    public IReadOnlyList<MonsterBase> TornTargets => _tornTargets;
    public float Atk => _player != null ? _player.RuntimeStats.GetEffectiveAttack(AttackStatKind.Melee) : 0f;

    /// <summary>계단 k(1~4)의 광기 값 — 최대 40이면 10 · 20 · 30 · 40.</summary>
    public int Rung(int k) => _madness != null ? Mathf.RoundToInt(_madness.MaxStacks * k / 4f) : k * 10;
    /// <summary>광기가 계단 k 이상인가(광란 중엔 늘 참).</summary>
    public bool AtRung(int k) => IsFrenzy || Stacks >= Rung(k);

    public int BrandCap
    {
        get
        {
            if (EyeTier >= 2 && AtRung(4)) return EyeBrandCapMax;
            if (EyeTier >= 1 && AtRung(3)) return EyeBrandCap;
            return BaseBrandCap;
        }
    }

    /// <summary>플레이어의 허브(없으면 붙이고 묶는다). 랜슬롯 유물이 아니면 null.</summary>
    public static LancelotMemoryHub Ensure(PlayerController player)
    {
        if (player == null || player.RelicBehavior is not LancelotMadnessRelic) return null;
        if (!player.TryGetComponent<LancelotMemoryHub>(out var hub)) hub = player.gameObject.AddComponent<LancelotMemoryHub>();
        hub.Bind(player);
        return hub;
    }

    public static LancelotMemoryHub Of(PlayerController player)
        => player != null && player.TryGetComponent<LancelotMemoryHub>(out var h) ? h : null;

    private void Bind(PlayerController player)
    {
        if (_player == player && _madness != null) { RecomputeTiers(); return; }
        Unbind();
        _player  = player;
        _relic   = player.RelicBehavior as LancelotMadnessRelic;
        _madness = _relic?.Madness;
        _loadout = AppBootstrapper.Instance?.Loadout;
        _grudge  = GrudgeStore.Of(player, true);
        _grudge.SetBaseCap(GrudgeCap);
        if (_madness != null)
        {
            _prevStacks = _madness.Stacks;
            _madness.OnChanged       += OnMadnessChanged;
            _madness.OnFrenzyChanged += OnFrenzyChanged;
            _madness.OnRefillFull    += OnRefillFull;
        }
        if (_relic != null)
        {
            _relic.JudgmentBegan    += OnJudgmentBegan;
            _relic.JudgmentHit      += OnJudgmentHit;
            _relic.BeforeFinisher   += OnBeforeFinisher;
            _relic.JudgmentFinished += OnJudgmentFinished;
        }
        if (_loadout != null) _loadout.RelicPartsChanged += RecomputeTiers;
        // 허브 몫의 신호(낙인 기본 규칙) — 파츠 허브에 시스템 효과로 한 번만 등록
        player.RuneEffects.Parts.AddSystemEffect(new HubRelay(this));
        RecomputeTiers();
        Bound?.Invoke(this);
    }

    private void Unbind()
    {
        if (_madness != null)
        {
            _madness.OnChanged       -= OnMadnessChanged;
            _madness.OnFrenzyChanged -= OnFrenzyChanged;
            _madness.OnRefillFull    -= OnRefillFull;
        }
        if (_relic != null)
        {
            _relic.JudgmentBegan    -= OnJudgmentBegan;
            _relic.JudgmentHit      -= OnJudgmentHit;
            _relic.BeforeFinisher   -= OnBeforeFinisher;
            _relic.JudgmentFinished -= OnJudgmentFinished;
        }
        if (_loadout != null) _loadout.RelicPartsChanged -= RecomputeTiers;
        _madness = null; _relic = null; _loadout = null;
    }

    private void OnDestroy()
    {
        var madness = _madness;
        Unbind();
        if (madness != null)
        {
            madness.Floor = 0;
            madness.SetRetainRatio(0f);
            madness.RefillDuringFrenzy = false;
        }
    }

    /// <summary>계단 공명을 다시 센다(조각 · 메아리 · 등급이 바뀔 때).</summary>
    public void RecomputeTiers()
    {
        if (_loadout == null) return;
        _ladder = RelicResonance.LancelotLadder(_loadout);
        int tier = RelicResonance.LancelotTier(_ladder);
        if (_madness != null)
        {
            // 받침 — 이어진 맨 위 계단(4 계단이면 30: 40은 광란 문턱이라 받치면 광란 뒤 곧장 다시 광란이다)
            _madness.Floor = tier >= 2 ? Rung(Mathf.Min(tier, 3)) : 0;
            _madness.SetRetainRatio(tier >= 3 ? RestartRatio : 0f);   // 재출발
            _madness.RefillDuringFrenzy = tier >= 4;                  // 두 번째 광란
        }
        Debug.Log($"[LancelotMemory] 계단 공명 — 이어진 계단 {_ladder} · 받침 {(_madness != null ? _madness.Floor : 0)} · 낙인 상한 {BrandCap} · 원한 상한 {(_grudge != null ? _grudge.Cap : 0)}");
    }

    // ── 광기 · 광란 ───────────────────────────────────────

    private void OnMadnessChanged()
    {
        if (_madness == null) return;
        int now = _madness.Stacks;
        for (int k = 1; k <= 4; k++)
        {
            int r = Rung(k);
            if (_prevStacks < r && now >= r) RungCrossed?.Invoke(k);
        }
        _prevStacks = now;
    }

    private void OnFrenzyChanged(bool on)
    {
        if (on)
        {
            _judgmentIndex = 0;
            FrenzyStarted?.Invoke();
        }
        else FrenzyEnded?.Invoke();
    }

    /// <summary>두 번째 광란 — 광란 중 다시 가득: 광란이 한 번 더 이어지고 심판 1회 더.</summary>
    private void OnRefillFull()
    {
        if (_madness == null || _relic == null) return;
        _madness.ExtendFrenzy(_relic.FrenzyDuration);
        _relic.GrantJudgmentCharge();
        Say(_player.transform.position, "두 번째 광란");
        Debug.Log($"[LancelotMemory] 두 번째 광란 — 광란 +{_relic.FrenzyDuration:0.#}초 · 심판 {_relic.JudgmentCharges}회");
    }

    // ── 낙인 ─────────────────────────────────────────────

    /// <summary>
    /// 낙인을 새긴다. 상한을 넘친 몫은 <paramref name="overflowToGrudge"/>일 때만 원한이 된다. 실제로 새긴 수를 돌려준다.
    /// </summary>
    public int AddBrand(MonsterBase mb, int n, bool overflowToGrudge, bool ignoreGap = false, BrandSource src = BrandSource.Fragment)
    {
        if (mb == null || mb.IsDead || n <= 0) return 0;
        var st = RelicMarkStatus.Of(mb.gameObject, true);
        int added = st.AddBrand(n, BrandCap, out int overflow, ignoreGap);
        if (added <= 0 && overflow <= 0) return 0;
        BrandAdded?.Invoke(mb, added, overflow, src);
        if (overflow > 0 && overflowToGrudge) SaveGrudge(overflow);
        return added;
    }

    /// <summary>낙인을 상한까지 채운다(배신자의 걸음 · 끝의 문턱 ③ · 폭로 ③).</summary>
    public int FillBrand(MonsterBase mb)
    {
        if (mb == null || mb.IsDead) return 0;
        int added = RelicMarkStatus.Of(mb.gameObject, true).FillBrand(BrandCap);
        if (added > 0) BrandAdded?.Invoke(mb, added, 0, BrandSource.Fragment);
        return added;
    }

    public static int BrandOf(MonsterBase mb)
    {
        var st = mb != null ? RelicMarkStatus.Of(mb.gameObject, false) : null;
        return st != null ? st.Brand : 0;
    }

    /// <summary>원한을 저축한다(넘침 · 조각). 실제로 더한 수.</summary>
    public int SaveGrudge(int n)
    {
        if (_grudge == null || n <= 0) return 0;
        int added = _grudge.Add(n);
        if (added > 0) GrudgeSaved?.Invoke(added);
        return added;
    }

    /// <summary>허브 기본 규칙 — 광기 20+ 적중 = 낙인 1(넘치면 원한) · 평타 묶음 세기.</summary>
    private void OnBaseHit(in HitInfo hit)
    {
        Hit?.Invoke(hit);
        if (hit.Target == null || !hit.Target.TryGetComponent<MonsterBase>(out var mb)) return;

        if (hit.ActionType == WeaponActionType.GroundLight && Time.time - _lastSwingAt > 0.12f)
        {
            _lastSwingAt = Time.time;
            _swingNo++;
            Swing?.Invoke(hit, _swingNo);
        }
        else if (hit.ActionType == WeaponActionType.GroundLight) _lastSwingAt = Time.time;

        if (AtRung(2)) AddBrand(mb, 1, overflowToGrudge: true, ignoreGap: false, src: BrandSource.Hit);
    }

    // ── 찢기 ─────────────────────────────────────────────

    /// <summary>
    /// 낙인을 찢는다 — X자 참격(유물 공격력 × 0.35 × X자 수). <paramref name="limit"/>개까지만 찢는다(작은 심판 = 1).
    /// 찢은(센) 낙인 수를 돌려준다. 엇갈린 빛이면 낙인을 남긴다.
    /// </summary>
    public int Tear(MonsterBase mb, TearCause cause, int limit = int.MaxValue)
    {
        if (mb == null) return 0;
        var st = RelicMarkStatus.Of(mb.gameObject, false);
        if (st == null || st.Brand <= 0) return 0;

        int stacks = Mathf.Min(st.Brand, Mathf.Max(1, limit));
        bool keep = cause == TearCause.Judgment ? _keepThisJudgment : _crossLight;
        if (!keep)
        {
            if (stacks >= st.Brand) st.ConsumeBrand();
            else for (int i = 0; i < stacks; i++) st.ConsumeOneBrand();
        }
        else if (cause != TearCause.Judgment)
        {
            _crossLight = false;
            Debug.Log("[LancelotMemory] 엇갈린 빛 — 이번 찢기는 낙인을 남겼다");
        }

        _ctx.Target = mb; _ctx.Cause = cause; _ctx.Stacks = stacks; _ctx.XMultiplier = 1;
        PreTear?.Invoke(_ctx);
        int counted = Mathf.Max(0, _ctx.Stacks);
        int x = Mathf.Clamp(counted * Mathf.Max(1, _ctx.XMultiplier), 0, MaxXPerTear);

        if (cause == TearCause.Judgment)
        {
            _tornThisJudgment += counted;
            if (!_tornTargets.Contains(mb)) _tornTargets.Add(mb);
        }

        Vector3 at = mb.transform.position;
        if (x > 0)
        {
            float dmg = Atk * TearAtkRatio * x;
            if (!mb.IsDead) CombatQuery.DealSynergyDamage(mb, dmg, _player.gameObject, 1f, false, RuneElement.Dark);
            PlayX(at, Mathf.Min(x, MaxTearVfx));
            Debug.Log($"[LancelotMemory] 찢기 {mb.name} 낙인 {stacks}(센 {counted}) · X자 {x} · 피해 {dmg:0} ({cause}){(keep ? " · 남김" : "")}");
        }
        Torn?.Invoke(mb, counted, cause);
        return counted;
    }

    /// <summary>X자 참격 이펙트 — 막타 참격을 작게 엇갈려 두 번(× 겹침 수).</summary>
    public static void PlayX(Vector3 at, int count)
    {
        for (int i = 0; i < count; i++)
        {
            float yaw = 45f + 90f * (i % 2) + 20f * (i / 2);
            Vector3 dir = Quaternion.AngleAxis(yaw, Vector3.up) * Vector3.forward;
            RelicStateVfx.PlayOneShot(SlashVfxKey, at + Vector3.up * (0.9f + 0.15f * i), 0.24f, dir, 1.2f);
        }
    }

    // ── 심판 ─────────────────────────────────────────────

    private void OnJudgmentBegan()
    {
        _judgmentIndex++;
        _tornThisJudgment = 0;
        _tornTargets.Clear();
        _finisherHits.Clear();
        SkipGrudgeThisJudgment = false;
        _keepThisJudgment = _crossLight;
        JudgmentBegan?.Invoke();
    }

    private void OnBeforeFinisher(Vector3 pos, Vector3 fwd)
    {
        _grudgeAtFinisher = _grudge != null ? _grudge.Value : 0;
        BeforeFinisher?.Invoke(pos, fwd);
    }

    private void OnJudgmentHit(GameObject target, int hitIndex, bool isLast)
    {
        if (!isLast || target == null || !target.TryGetComponent<MonsterBase>(out var mb)) return;
        _finisherHits.Add(mb.GetInstanceID());
        Tear(mb, TearCause.Judgment);
    }

    /// <summary>이번 막타가 이 적을 맞혔는가(원한의 칼날 ① — 화면 안 나머지 낙인 적).</summary>
    public bool WasHitByFinisher(MonsterBase mb) => mb != null && _finisherHits.Contains(mb.GetInstanceID());

    /// <summary>원한의 칼날 ① 등 — 막타 콘 밖의 적을 막타로 친 것으로 센다.</summary>
    public void MarkFinisherHit(MonsterBase mb) { if (mb != null) _finisherHits.Add(mb.GetInstanceID()); }

    private void OnJudgmentFinished(Vector3 pos, Vector3 fwd)
    {
        FinisherLanded?.Invoke(pos, fwd);
        JudgmentFinished?.Invoke(pos, fwd, _tornThisJudgment);
        if (_keepThisJudgment && _tornThisJudgment > 0)
        {
            _crossLight = false;
            Debug.Log("[LancelotMemory] 엇갈린 빛 — 이번 심판은 낙인을 남겼다");
        }
        _keepThisJudgment = false;
        Debug.Log($"[LancelotMemory] 심판 {_judgmentIndex}번째 — 찢은 낙인 {_tornThisJudgment} · 대상 {_tornTargets.Count} · 원한 {(_grudge != null ? _grudge.Value : 0)}{(SkipGrudgeThisJudgment ? " (쓰지 않음)" : "")}");
        if (!SkipGrudgeThisJudgment) SpendGrudge(pos, fwd);
    }

    /// <summary>원한 쓰기 — 1개당 참격파 1줄이 부채꼴로.</summary>
    private void SpendGrudge(Vector3 pos, Vector3 fwd)
    {
        if (_grudge == null) return;
        int n = _grudge.TakeAll();
        if (n <= 0) return;
        int hits = 0;
        for (int i = 0; i < n; i++)
        {
            float yaw = (i - (n - 1) * 0.5f) * WaveSpreadDeg;
            hits += Wave(pos, Quaternion.AngleAxis(yaw, Vector3.up) * fwd, WaveLength, Atk * WaveAtkRatio);
        }
        GrudgeSpent?.Invoke(n);
        Debug.Log($"[LancelotMemory] 원한 {n} → 참격파 {n}줄 · 적중 {hits} · 1줄 {Atk * WaveAtkRatio:0}");
    }

    /// <summary>
    /// 직선 참격파 — <paramref name="from"/>에서 <paramref name="dir"/>로 <paramref name="length"/> m, 폭 ±0.9 m 안의 적에게 상태 피해.
    /// 맞힌 적 수를 돌려준다(광란의 걸음 ③ 궤적 베기도 같은 판정).
    /// </summary>
    public int Wave(Vector3 from, Vector3 dir, float length, float damage)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f || length <= 0f) return 0;
        dir.Normalize();
        Vector3 to = from + dir * length;
        ElementVfxPlayer.PlayBeam(RuneElement.Dark, from + Vector3.up * 0.8f, to + Vector3.up * 0.8f, 0.3f);
        Vector3 mid = (from + to) * 0.5f;
        int n = CombatQuery.GetNearbyEnemies(mid, length * 0.5f + WaveHalfWidth + 0.5f, _player.gameObject, 32, _buf);
        var hit = new List<MonsterBase>(n);
        for (int i = 0; i < n; i++)
        {
            var e = _buf[i];
            if (e != null && !e.IsDead && DistToSegmentXZ(e.transform.position, from, to) <= WaveHalfWidth + 0.4f) hit.Add(e);
        }
        foreach (var e in hit) CombatQuery.DealSynergyDamage(e, damage, _player.gameObject, 1f, false, RuneElement.Dark);
        return hit.Count;
    }

    public static float DistToSegmentXZ(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector2 P = new(p.x, p.z), A = new(a.x, a.z), B = new(b.x, b.z);
        Vector2 ab = B - A;
        float t = ab.sqrMagnitude > 0.0001f ? Mathf.Clamp01(Vector2.Dot(P - A, ab) / ab.sqrMagnitude) : 0f;
        return Vector2.Distance(P, A + ab * t);
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
                Debug.Log($"[LancelotMemory] 엇갈린 빛 — {kv.Key} + {reactionKey}");
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
            if (e != null && e.relic_id == "lancelot" && e.IsReaction) n++;
        }
        return n;
    }

    /// <summary>허브 몫의 짧은 글자(두 번째 광란 등) — 출시 빌드에서도 뜬다.</summary>
    public static void Say(Vector3 at, string text) => RelicFloatText.Show(at, text, LancelotColor);

    /// <summary>허브 몫의 전투 신호 — 파츠 허브에 「시스템 효과」로 한 번만 등록된다.</summary>
    private sealed class HubRelay : RelicPartEffect
    {
        public const string Key = "~lancelot_hub";
        private readonly LancelotMemoryHub _hub;
        public HubRelay(LancelotMemoryHub hub) : base(Key) => _hub = hub;
        public override void OnHit(in HitInfo hit, PlayerController player) { if (_hub != null) _hub.OnBaseHit(hit); }
    }
}
