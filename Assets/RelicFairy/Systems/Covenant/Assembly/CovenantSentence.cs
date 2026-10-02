using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using RelicFairy.Monster;

/// <summary>
/// 서약서 — 「한 장의 서약서」(10-02 설계서 · 사용자 「한 서약을 플레이어가 작성하면서 확장되는 컨셉」).
/// 런마다 한 장, 한 문장: 조건절(원인) 1 + 결과절 최대 5. 조건이 한 번 맞으면 <b>문장 끝까지 연쇄로</b> 일어난다.
/// <para>id = <c>sen:cause@t&gt;eff@t&gt;eff@t~d&gt;eff@t~w</c> — 첫 결과는 조건에서 즉시, 이후 <c>~i ~d ~w ~e</c> = 즉시 · 쓰러지면 · 그동안 · 견디면.</para>
/// <list type="bullet">
/// <item>절 k는 절 k−1이 넘긴 것(건드린 적 ≤3 · 자리 · 나 · 창)에서만 일어난다 → 문장이 한 줄이라 되먹임이 없다(깊이 ≤ 문장 길이).</item>
/// <item>퍼짐 상한: 한 연쇄(조건 1회) 안에서 각 절은 최대 <see cref="PerChainCap"/>번. 연쇄 시작은 <see cref="ChainMinInterval"/>초 간격 이상.</item>
/// <item>연쇄 중 들어온 처치 · 귀(쓰러지면 · 그동안)는 줄에 세웠다가 연쇄가 끝난 뒤 차례로 — 훑기 버퍼 · 순회가 흔들리지 않게.</item>
/// </list>
/// 효과 실행 · 수치 · 연출은 <see cref="CovenantClause"/>(조립 서약과 같은 코드) × 절 위치 배율.
/// </summary>
public sealed class CovenantSentence : CovenantBase, ICovenantClauseHost
{
    public const string Prefix = "sen:";

    private const float ChainMinInterval = 0.5f;
    private const int   PerChainCap      = 3;
    private const float DeathEarMax      = 6f;    // 「쓰러지면」 — 상태가 남은 동안, 최대 6초
    private const float DeathEarMin      = 1.5f;  // 지속 없는 결과(폭발 · 정지 뒤)도 잠깐은 듣는다
    private const float ShieldEarSeconds = 8f;    // 「보호막이 깨지면」을 기다리는 상한
    private const float TargetSearchRadius = 8f;

    private struct Spec
    {
        public string id;
        public CovenantTier tier;
        public ClauseLink link;
        public CovenantPalette.EffectDef def;
    }

    private struct Pending
    {
        public int index;
        public int serial;
        public Vector3 place;
        public MonsterBase first;
    }

    private struct DeathEar  { public MonsterBase mb; public int next; public int serial; public float until; }
    private struct WindowEar { public int next; public int serial; public float until; }
    private struct EndureEar { public int next; public int serial; public float until; public bool shield; public float fireAt; }

    private readonly string _causeId;
    private readonly CovenantTier _causeTier;
    private readonly CovenantPalette.CauseDef _cause;
    private readonly bool _resolved;
    private readonly List<Spec> _specs = new();
    private readonly List<CovenantClause> _clauses = new();
    private readonly CovenantCauseTracker _tracker;

    // 연쇄 상태
    private bool  _inChain;
    private bool  _recordKills;
    private int   _serial;
    private float _lastChainStart = -999f;
    private readonly Queue<Pending> _pending = new();
    private readonly Dictionary<int, int[]> _chainCounts = new();
    private readonly List<MonsterBase> _killedDuringFire = new(4);
    private readonly List<MonsterBase> _passScratch = new(CovenantClause.PassCap);
    private readonly List<MonsterBase> _enemyScratch = new(CovenantClause.PassCap);

    // 귀(다음 절을 기다린다)
    private readonly List<DeathEar>  _deathEars  = new();
    private readonly List<WindowEar> _windowEars = new();
    private readonly List<EndureEar> _endureEars = new();
    private bool _shieldWas;

    /// <summary>절 하나가 일어났다(결과 번호 0~) — HUD 표식 · 소리 음계가 듣는다.</summary>
    public event Action<int> ClauseFired;

    public CovenantSentence(string body)
    {
        var parts = (body ?? string.Empty).Split('>');
        if (parts.Length < 2) return;

        (_causeId, _causeTier) = SplitTier(parts[0]);
        if (!CovenantPalette.TryGetCause(_causeId, out _cause)) return;

        for (int i = 1; i < parts.Length && _specs.Count < CovenantGrammar.MaxResults; i++)
        {
            string tok = parts[i];
            var link = ClauseLink.Immediate;
            int tilde = tok.IndexOf('~');
            if (tilde >= 0)
            {
                if (tilde + 1 < tok.Length) link = CovenantGrammar.ParseLink(tok[tilde + 1]);
                tok = tok.Substring(0, tilde);
            }
            var (effId, effTier) = SplitTier(tok);
            if (!CovenantPalette.TryGetEffect(effId, out var def)) return;   // 하나라도 못 읽으면 문장 전체를 해석 실패로
            _specs.Add(new Spec { id = effId, tier = effTier, link = _specs.Count == 0 ? ClauseLink.Immediate : link, def = def });
        }
        if (_specs.Count == 0) return;

        for (int i = 0; i < _specs.Count; i++)
        {
            var s = _specs[i];
            _clauses.Add(new CovenantClause(this, s.id, s.tier, s.def, _cause, _causeTier, CovenantGrammar.PositionMult(i)));
        }
        _tracker  = new CovenantCauseTracker(_cause, this, OnCauseFired, OnProximitySample);
        _resolved = true;
    }

    // ── 정체 ─────────────────────────────────────────────
    public bool Resolved => _resolved;
    public string CauseId => _causeId;
    public CovenantTier CauseTier => _causeTier;
    public int ResultCount => _specs.Count;
    public string ResultId(int i)          => _specs[i].id;
    public CovenantTier ResultTier(int i)  => _specs[i].tier;
    public ClauseLink ResultLink(int i)    => _specs[i].link;

    /// <summary>문장 안에서 <paramref name="status"/> 계열을 <b>거는</b> 결과가 있는가(소모형 페어링 · 빌드 계열).</summary>
    public bool Applies(StatusCurrency status)
    {
        for (int i = 0; i < _specs.Count; i++)
            if (_specs[i].def.role == StatusRole.Apply && EffectTaxonomy.SameFamily(status, _specs[i].def.status)) return true;
        return false;
    }

    public static string MakeId(string causeId, CovenantTier causeTier,
                                IReadOnlyList<(string id, CovenantTier tier, ClauseLink link)> results)
    {
        var sb = new StringBuilder(Prefix).Append(causeId).Append('@').Append(causeTier.Code());
        for (int i = 0; i < results.Count; i++)
        {
            sb.Append('>').Append(results[i].id).Append('@').Append(results[i].tier.Code());
            if (i > 0) sb.Append('~').Append(CovenantGrammar.Code(results[i].link));
        }
        return sb.ToString();
    }

    public override string CovenantId
    {
        get
        {
            var list = new List<(string, CovenantTier, ClauseLink)>(_specs.Count);
            foreach (var s in _specs) list.Add((s.id, s.tier, s.link));
            return MakeId(_causeId, _causeTier, list);
        }
    }

    public override CovenantCategory Category => _resolved ? _cause.category : base.Category;

    public override string DisplayName
    {
        get
        {
            if (!_resolved) return Prefix;
            var sb = new StringBuilder(_cause.name);
            foreach (var s in _specs) sb.Append(" → ").Append(s.def.name);
            return sb.ToString();
        }
    }

    public override string CauseText  => _resolved ? _cause.desc : null;
    public override string EffectText
    {
        get
        {
            if (!_resolved) return null;
            var sb = new StringBuilder();
            for (int i = 0; i < _specs.Count; i++)
            {
                if (i > 0) sb.Append(LinkWord(_specs[i].link));
                sb.Append(_specs[i].def.name);
            }
            return sb.ToString();
        }
    }

    private static string LinkWord(ClauseLink link) => link switch
    {
        ClauseLink.OnDeath => " → 쓰러지면 ",
        ClauseLink.During  => " → 그동안 ",
        ClauseLink.Endure  => " → 견디면 ",
        _                  => " → ",
    };

    private static (string id, CovenantTier tier) SplitTier(string part)
    {
        if (string.IsNullOrEmpty(part)) return (part, CovenantTier.Silver);
        int at = part.IndexOf('@');
        return at < 0
            ? (part, CovenantTier.Silver)
            : (part.Substring(0, at), CovenantTierUtil.Parse(part.Substring(at + 1)));
    }

    // ── 사건 → 원인 · 귀 ──────────────────────────────────
    public override void OnAttackHit(GameObject target, float dmg)
    {
        if (!_resolved || _inChain) return;
        _tracker.OnAttackHit(target);
    }

    public override void OnKill(GameObject target)
    {
        if (!_resolved) return;
        var mb = target != null ? target.GetComponentInParent<MonsterBase>() : null;
        if (_recordKills && mb != null && !_killedDuringFire.Contains(mb)) _killedDuringFire.Add(mb);

        // 귀 — 「쓰러지면」(그 적) · 「그동안」(창 안 처치). 연쇄 중이면 줄에 선다.
        if (mb != null) HearDeath(mb, target.transform.position);
        HearWindowKill(target != null ? target.transform.position : PlayerPos);

        if (!_inChain) _tracker.OnKill(target);
    }

    public override void OnWeaponSwap(WeaponData prev, WeaponData next)
    {
        if (_resolved && !_inChain) _tracker.OnWeaponSwap();
    }

    public override void OnRoomEnter()
    {
        if (!_resolved) return;
        _tracker.OnRoomEnter();
        // 방이 바뀌면 이전 방 적에게 단 귀는 뜻이 없다
        _deathEars.Clear();
        _windowEars.Clear();
    }

    public override void OnRoomClear()
    {
        if (_resolved && !_inChain) _tracker.OnRoomClear();
    }

    public override void OnSkillUse(SkillType skill)
    {
        if (_resolved && !_inChain) _tracker.OnSkillUse();
    }

    public override void Tick(float deltaTime)
    {
        if (!_resolved || _inChain) return;
        _tracker.Tick(deltaTime);
        for (int i = 0; i < _clauses.Count; i++) _clauses[i].Tick();
        TickEars();
    }

    private void OnProximitySample(int near)
    {
        for (int i = 0; i < _clauses.Count; i++) _clauses[i].OnProximitySample(near);
    }

    // ── 피해 · 스탯 · 사망 방지 · 버프창 → 절 전부 ─────────
    public override void ModifyOutgoingDamage(ref float damage, CombatContext ctx)
    {
        if (!_resolved) return;
        for (int i = 0; i < _clauses.Count; i++) _clauses[i].ModifyOutgoingDamage(ref damage);
    }

    public override void ModifyIncomingDamage(ref float damage, CombatContext ctx)
    {
        if (!_resolved) return;
        for (int i = 0; i < _clauses.Count; i++) _clauses[i].ModifyIncomingDamage(ref damage);
    }

    public override IEnumerable<StatModifier> GetStatModifiers()
    {
        if (!_resolved) yield break;
        for (int i = 0; i < _clauses.Count; i++)
            foreach (var m in _clauses[i].GetStatModifiers()) yield return m;
    }

    public override bool TryPreventDeath()
    {
        if (!_resolved) return false;
        for (int i = 0; i < _clauses.Count; i++)
            if (_clauses[i].TryPreventDeath()) return true;
        return false;
    }

    public override bool TryGetBuffView(out BuffViewItem item)
    {
        if (_resolved)
            for (int i = 0; i < _clauses.Count; i++)
                if (_clauses[i].TryGetBuffView(out item)) return true;
        item = default;
        return false;
    }

    // ── 연쇄 ─────────────────────────────────────────────
    private void OnCauseFired(GameObject target)
    {
        if (Ctx == null) return;
        if (Time.time - _lastChainStart < ChainMinInterval) return;
        _lastChainStart = Time.time;
        int serial = ++_serial;
        _chainCounts[serial] = new int[_specs.Count];
        PruneChainCounts();

        var first = CovenantQuery.Live(target);
        Vector3 place = target != null ? target.transform.position : PlayerPos;
        Run(new Pending { index = 0, serial = serial, place = place, first = first });
    }

    /// <summary>연쇄 하나(또는 귀가 연 이어 달리기)를 시작 — 이미 연쇄 중이면 줄에 세운다.</summary>
    private void Run(Pending p)
    {
        if (_inChain) { _pending.Enqueue(p); return; }
        _inChain = true;
        try
        {
            RunCore(p);
            int guard = 0;
            while (_pending.Count > 0 && guard++ < 64) RunCore(_pending.Dequeue());
            _pending.Clear();
        }
        finally
        {
            _inChain = false;
        }
    }

    private void RunCore(Pending p)
    {
        if (!_chainCounts.TryGetValue(p.serial, out var counts)) return;

        // 첫 절의 받는 적 — 넘겨받은 적(살았으면) 또는 자리 근처
        _enemyScratch.Clear();
        if (p.first != null && !p.first.IsDead) _enemyScratch.Add(p.first);

        Vector3 place = p.place;
        for (int i = p.index; i < _clauses.Count; i++)
        {
            if (counts[i] >= PerChainCap) return;
            if (!FireClause(i, _enemyScratch, place)) return;
            counts[i]++;
            ClauseFired?.Invoke(i);

            var clause = _clauses[i];
            if (i + 1 >= _clauses.Count) return;

            switch (_specs[i + 1].link)
            {
                case ClauseLink.Immediate:
                    _enemyScratch.Clear();
                    for (int k = 0; k < _passScratch.Count; k++) _enemyScratch.Add(_passScratch[k]);
                    place = clause.Place;
                    continue;

                case ClauseLink.OnDeath:
                {
                    // 이 절이 일어나는 동안 이미 쓰러진 적(폭발이 죽인 적) → 곧바로 다음 절(줄)
                    for (int k = 0; k < _killedDuringFire.Count; k++)
                    {
                        var dead = _killedDuringFire[k];
                        if (dead == null) continue;
                        Vector3 at = dead.transform.position;
                        _pending.Enqueue(new Pending { index = i + 1, serial = p.serial, place = at, first = NearestLive(at, dead) });
                    }
                    // 살아남은 적에게 귀 — 상태가 남은 동안
                    float hold = Mathf.Clamp(_specs[i].def.duration, DeathEarMin, DeathEarMax);
                    for (int k = 0; k < _passScratch.Count; k++)
                    {
                        var mb = _passScratch[k];
                        if (mb == null || mb.IsDead) continue;
                        _deathEars.Add(new DeathEar { mb = mb, next = i + 1, serial = p.serial, until = Time.time + hold });
                    }
                    return;
                }

                case ClauseLink.During:
                {
                    float until = clause.WindowEnd;
                    if (until > Time.time)
                        _windowEars.Add(new WindowEar { next = i + 1, serial = p.serial, until = until });
                    return;
                }

                case ClauseLink.Endure:
                {
                    bool shield = _specs[i].def.kind == EffectKind.Shield;
                    float fireAt = shield ? 0f : Time.time + Mathf.Max(0.05f, clause.EffectiveValue);
                    _endureEars.Add(new EndureEar
                    {
                        next = i + 1, serial = p.serial, shield = shield, fireAt = fireAt,
                        until = shield ? Time.time + ShieldEarSeconds : fireAt + 0.5f,
                    });
                    _shieldWas = Ctx?.Player?.RuntimeStats != null && Ctx.Player.RuntimeStats.HasShield;
                    return;
                }
            }
        }
    }

    /// <summary>
    /// 절 하나를 일으킨다. 「나」 = 한 번 · 「한 자리」 = 받은 첫 적(없으면 자리)에서 한 번 · 「적마다」 = 받은 적 각자(최대 3).
    /// 넘기는 것은 <see cref="_passScratch"/>(건드린 적 합집합 ≤3)에, 그동안 쓰러진 적은 <see cref="_killedDuringFire"/>에.
    /// </summary>
    private bool FireClause(int i, List<MonsterBase> enemies, Vector3 place)
    {
        var clause = _clauses[i];
        _passScratch.Clear();
        _killedDuringFire.Clear();
        _recordKills = true;
        bool any = false;
        try
        {
            switch (CovenantGrammar.Receive(_specs[i].id))
            {
                case ClauseReceive.Me:
                    any = clause.TryFireAt(null, place);
                    break;

                case ClauseReceive.Place:
                {
                    var first = enemies.Count > 0 ? enemies[0] : null;
                    any = clause.TryFireAt(first != null && !first.IsDead ? first.gameObject : null, place);
                    if (any) Gather(clause);
                    break;
                }

                case ClauseReceive.EachEnemy:
                {
                    if (enemies.Count == 0)
                    {
                        any = clause.TryFireAt(null, place);
                        if (any) Gather(clause);
                        break;
                    }
                    // 받은 적 목록을 먼저 복사 — 발동이 적을 죽이면 OnKill이 목록을 흔든다
                    int n = Mathf.Min(enemies.Count, CovenantClause.PassCap);
                    var targets = new MonsterBase[n];
                    for (int k = 0; k < n; k++) targets[k] = enemies[k];
                    for (int k = 0; k < n; k++)
                    {
                        var t = targets[k];
                        if (!clause.TryFireAt(t != null && !t.IsDead ? t.gameObject : null, t != null ? t.transform.position : place)) continue;
                        any = true;
                        Gather(clause);
                    }
                    break;
                }
            }
        }
        finally
        {
            _recordKills = false;
        }
        return any;
    }

    private void Gather(CovenantClause clause)
    {
        var t = clause.Touched;
        for (int k = 0; k < t.Count && _passScratch.Count < CovenantClause.PassCap; k++)
            if (t[k] != null && !_passScratch.Contains(t[k])) _passScratch.Add(t[k]);
    }

    // ── 귀 ───────────────────────────────────────────────
    private void HearDeath(MonsterBase mb, Vector3 at)
    {
        for (int i = _deathEars.Count - 1; i >= 0; i--)
        {
            var ear = _deathEars[i];
            if (ear.mb != mb) continue;
            _deathEars.RemoveAt(i);
            if (Time.time > ear.until) continue;
            Run(new Pending { index = ear.next, serial = ear.serial, place = at, first = NearestLive(at, mb) });
        }
    }

    private void HearWindowKill(Vector3 at)
    {
        for (int i = 0; i < _windowEars.Count; i++)
        {
            var ear = _windowEars[i];
            if (Time.time > ear.until) continue;
            _windowEars.RemoveAt(i);   // 창당 1회
            Run(new Pending { index = ear.next, serial = ear.serial, place = at, first = NearestLive(at, null) });
            return;
        }
    }

    private void TickEars()
    {
        float now = Time.time;
        _deathEars.RemoveAll(e => now > e.until || e.mb == null);
        _windowEars.RemoveAll(e => now > e.until);

        bool shieldNow = Ctx?.Player?.RuntimeStats != null && Ctx.Player.RuntimeStats.HasShield;
        bool broke = _shieldWas && !shieldNow;
        _shieldWas = shieldNow;

        for (int i = _endureEars.Count - 1; i >= 0; i--)
        {
            var ear = _endureEars[i];
            bool fire = ear.shield ? broke : now >= ear.fireAt;
            if (fire)
            {
                _endureEars.RemoveAt(i);
                Vector3 at = PlayerPos;
                Run(new Pending { index = ear.next, serial = ear.serial, place = at, first = NearestLive(at, null) });
                continue;
            }
            if (now > ear.until) _endureEars.RemoveAt(i);
        }
    }

    private MonsterBase NearestLive(Vector3 at, MonsterBase except)
        => CovenantQuery.Live(CovenantQuery.NearestLiveEnemy(at, TargetSearchRadius, except));

    /// <summary>오래된 연쇄 기록을 버린다(귀가 다 닫힌 뒤엔 쓸 일이 없다).</summary>
    private void PruneChainCounts()
    {
        if (_chainCounts.Count <= 16) return;
        int keepFrom = _serial - 16;
        var old = new List<int>();
        foreach (var k in _chainCounts.Keys) if (k < keepFrom) old.Add(k);
        foreach (var k in old) _chainCounts.Remove(k);
    }

    // ── ICovenantClauseHost ───────────────────────────────
    CovenantContext ICovenantClauseHost.Context => Ctx;
    Vector3 ICovenantClauseHost.PlayerPosition  => PlayerPos;
    void ICovenantClauseHost.RequestStatRefresh() => RefreshStats();
    int ICovenantClauseHost.DealAoe(Vector3 center, float radius, float multiplier) => DealAoe(center, radius, multiplier, 0.3f);
}
