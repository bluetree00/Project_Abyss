using System.Collections.Generic;

/// <summary>서약서 절 사이의 이음 — 앞 절이 일어난 뒤 다음 절이 <b>언제</b> 일어나는가(설계서 §1-3).</summary>
public enum ClauseLink
{
    /// <summary>즉시 — 「~하고」 · 「~하며」. 같은 순간, 앞 절이 넘긴 대상 · 자리 · 나에게.</summary>
    Immediate,
    /// <summary>쓰러지면 — 앞 절이 건드린 적이 그 상태가 남아 있는 동안(최대 6초) 쓰러질 때, 그 자리 + 가장 가까운 적에게.</summary>
    OnDeath,
    /// <summary>그동안 — 앞 절이 연 창(격노 · 박차 · 결계) 안에서 처치할 때(창당 1회).</summary>
    During,
    /// <summary>견디면 — 보호막이 깨지거나 무적이 끝나는 순간, 나를 중심으로.</summary>
    Endure,
}

/// <summary>절이 무엇을 받아 일어나는가 — 연쇄 실행이 대상을 몇 번 · 어디에 넘길지 정한다.</summary>
public enum ClauseReceive
{
    /// <summary>나 — 격노 · 박차 · 보호막 · 성역 · 결계 · 숨결. 대상을 보지 않는다.</summary>
    Me,
    /// <summary>한 자리 — 초신성 · 방전 · 정지 · 기폭 · 수확: 받은 적 가운데 첫째(없으면 받은 자리)에서 한 번.</summary>
    Place,
    /// <summary>적마다 — 저주 · 잔불 · 출혈 · 처형: 받은 적 각자에게(최대 3).</summary>
    EachEnemy,
}

/// <summary>
/// 「한 장의 서약서」 문법(설계서 §1-2 · §1-3) — 효과마다 받는 것 · 넘기는 것, 이을 수 있는 짝.
/// 정적 데이터 — 효과 id는 <see cref="CovenantPalette"/>와 같다(별칭은 해석 뒤 id로 본다).
/// </summary>
public static class CovenantGrammar
{
    public const int MaxResults      = 5;   // 결과절 절대 상한(제단 노드 「다섯째 절」 포함) — 읽기 · 복원은 이것
    /// <summary>지금 새로 쓸 수 있는 결과절 수 — 기본 4, 기억의 제단 「다섯째 절」(옛 「서약 칸 +1」, 칸 3 → 4)이면 5.</summary>
    public static int MaxResultsNow => MemoryAltarService.CovenantSlots + 1;
    public const int MaxSameEffect   = 2;   // 같은 결과는 한 문장에 두 번까지(S3)

    /// <summary>절 위치 배율 — 결과 1 ×1.0 · 2 ×0.85 · 3 ×0.7 · 4 ×0.6 · 5 ×0.5(설계서 §2).</summary>
    public static float PositionMult(int resultIndex)
        => resultIndex switch { 0 => 1f, 1 => 0.85f, 2 => 0.7f, 3 => 0.6f, _ => 0.5f };

    private struct Shape
    {
        public ClauseReceive receive;
        public bool passesEnemies;   // 즉시 이음으로 적(또는 자리)을 넘긴다
        public bool passesMeOnly;    // 즉시 이음으로 「나」만 넘긴다(수확)
        public bool passesDeath;     // 「쓰러지면」 이음
        public bool passesWindow;    // 「그동안」 이음
        public bool passesEndure;    // 「견디면」 이음
        public bool terminal;        // 마침표(뒤에 이을 수 없다)
    }

    private static readonly Dictionary<string, Shape> _shapes = new()
    {
        ["supernova"]  = new Shape { receive = ClauseReceive.Place,     passesEnemies = true, passesDeath = true },
        ["ember"]      = new Shape { receive = ClauseReceive.EachEnemy, passesEnemies = true, passesDeath = true },
        ["hemorrhage"] = new Shape { receive = ClauseReceive.EachEnemy, passesEnemies = true, passesDeath = true },
        ["curse"]      = new Shape { receive = ClauseReceive.EachEnemy, passesEnemies = true, passesDeath = true },
        ["arcflash"]   = new Shape { receive = ClauseReceive.Place,     passesEnemies = true },
        ["execute"]    = new Shape { receive = ClauseReceive.EachEnemy, passesEnemies = true },   // 「처형하면」 — 그 자리
        ["detonate"]   = new Shape { receive = ClauseReceive.Place,     passesEnemies = true },
        ["harvest"]    = new Shape { receive = ClauseReceive.Place,     passesMeOnly  = true },
        ["stasis"]     = new Shape { receive = ClauseReceive.Place,     passesEnemies = true, passesDeath = true },
        ["fury"]       = new Shape { receive = ClauseReceive.Me,        passesWindow  = true },
        ["momentum"]   = new Shape { receive = ClauseReceive.Me,        passesWindow  = true },
        ["ward"]       = new Shape { receive = ClauseReceive.Me,        passesWindow  = true },
        ["bloodmark"]  = new Shape { receive = ClauseReceive.Me,        passesEndure  = true },
        ["aegis"]      = new Shape { receive = ClauseReceive.Me,        passesEndure  = true },
        ["lastbreath"] = new Shape { receive = ClauseReceive.Me,        terminal      = true },
        ["goldrain"]   = new Shape { receive = ClauseReceive.Me },
    };

    /// <summary>효과가 받는 것(모르는 id는 「나」).</summary>
    public static ClauseReceive Receive(string effectId)
        => _shapes.TryGetValue(Normalize(effectId), out var s) ? s.receive : ClauseReceive.Me;

    /// <summary>마침표 절(뒤에 이을 수 없다 — 마지막 숨결).</summary>
    public static bool IsTerminal(string effectId)
        => _shapes.TryGetValue(Normalize(effectId), out var s) && s.terminal;

    /// <summary>
    /// <paramref name="prevEffectId"/> 뒤에 <paramref name="nextEffectId"/>를 <paramref name="link"/>로 이을 수 있는가(문법만 —
    /// 소모형 먹이 · 같은 결과 두 번은 <see cref="CanAppend"/>가 본다).
    /// 「나」 쪽 결과는 어느 절 뒤에도 즉시 이음으로 잇는다(마침표 뒤 제외).
    /// </summary>
    public static bool CanLink(string prevEffectId, string nextEffectId, ClauseLink link)
    {
        if (!_shapes.TryGetValue(Normalize(prevEffectId), out var prev) || prev.terminal) return false;
        var nextReceive = Receive(nextEffectId);
        switch (link)
        {
            case ClauseLink.Immediate:
                if (nextReceive == ClauseReceive.Me) return true;
                return prev.passesEnemies;
            case ClauseLink.OnDeath: return prev.passesDeath  && nextReceive != ClauseReceive.Me;
            case ClauseLink.During:  return prev.passesWindow && nextReceive != ClauseReceive.Me;
            case ClauseLink.Endure:  return prev.passesEndure && nextReceive != ClauseReceive.Me;
            default: return false;
        }
    }

    /// <summary>
    /// 문장 끝에 (결과, 이음)을 이어 쓸 수 있는가 — 문법 · 마침표 · 길이 · 같은 결과 두 번 · 소모형 먹이(문장 앞 절 또는 빌드 상태).
    /// </summary>
    /// <param name="effectIds">지금 문장의 결과 id들(순서대로).</param>
    /// <param name="buildStatus">빌드(유물 · 룬)가 거는 상태 — 소모형 먹이로 인정(설계서 §5).</param>
    public static bool CanAppend(IReadOnlyList<string> effectIds, string nextEffectId, ClauseLink link,
                                 ICollection<StatusCurrency> buildStatus = null)
    {
        if (effectIds == null || effectIds.Count == 0 || effectIds.Count >= MaxResultsNow) return false;
        if (!CanLink(effectIds[effectIds.Count - 1], nextEffectId, link)) return false;
        if (CountOf(effectIds, nextEffectId) >= MaxSameEffect) return false;
        return HasFeed(effectIds, effectIds.Count, nextEffectId, buildStatus);
    }

    /// <summary>
    /// 소모형(기폭 · 수확 · 정지 · 처형)의 먹이 — 문장에서 <paramref name="before"/> 앞 절 또는 빌드가 그 상태를 거는가.
    /// 처형은 「상태에 물든 적」 — 어느 상태든.
    /// </summary>
    public static bool HasFeed(IReadOnlyList<string> effectIds, int before, string effectId, ICollection<StatusCurrency> buildStatus)
    {
        if (!CovenantPalette.TryGetEffect(effectId, out var e)) return false;
        bool execute = e.kind == EffectKind.Execute;
        if (e.role != StatusRole.Consume && !execute) return true;

        if (buildStatus != null)
            foreach (var s in buildStatus)
                if (execute ? s != StatusCurrency.None : EffectTaxonomy.SameFamily(e.status, s)) return true;

        for (int i = 0; i < before && i < effectIds.Count; i++)
        {
            if (!CovenantPalette.TryGetEffect(effectIds[i], out var p) || p.role != StatusRole.Apply) continue;
            if (execute ? p.status != StatusCurrency.None && p.status != StatusCurrency.Shield && p.status != StatusCurrency.Momentum
                        : EffectTaxonomy.SameFamily(e.status, p.status))
                return true;
        }
        return false;
    }

    private static int CountOf(IReadOnlyList<string> ids, string id)
    {
        string n = Normalize(id);
        int c = 0;
        for (int i = 0; i < ids.Count; i++) if (Normalize(ids[i]) == n) c++;
        return c;
    }

    /// <summary>별칭(옛 id)을 해석 뒤 id로.</summary>
    private static string Normalize(string id)
        => CovenantPalette.TryGetEffect(id, out var e) ? e.id : id ?? string.Empty;

    // ── 문장 문구(설계서 §1-2 동사 · §1-3 이음) ───────────
    /// <summary>결과의 동사구 — 서약서 줄 · 카드에 쓴다.</summary>
    public static string Verb(string effectId) => Normalize(effectId) switch
    {
        "supernova"  => "그 자리가 터진다",
        "ember"      => "적이 불탄다",
        "hemorrhage" => "적이 피를 흘린다",
        "curse"      => "적이 저주받는다",
        "arcflash"   => "번개가 옮겨붙는다",
        "execute"    => "숨이 끊긴다",
        "detonate"   => "상처가 터진다",
        "harvest"    => "상처를 거둔다",
        "stasis"     => "적이 멈춰 선다",
        "fury"       => "나는 날이 선다",
        "momentum"   => "걸음이 빨라진다",
        "bloodmark"  => "피가 굳어 나를 막는다",
        "aegis"      => "성역이 나를 감싼다",
        "ward"       => "결계가 선다",
        "lastbreath" => "나는 쓰러지지 않는다",
        "goldrain"   => "금이 쏟아진다",
        _            => effectId,
    };

    /// <summary>이음 머리말 — 즉시는 없음. 견디면은 앞 절(성역 = 무적이 끝나면 · 보호막 = 깨지면)을 따른다.</summary>
    public static string Lead(ClauseLink link, string prevEffectId) => link switch
    {
        ClauseLink.OnDeath => "그 적이 쓰러지면 ",
        ClauseLink.During  => "그동안 처치하면 ",
        ClauseLink.Endure  => Normalize(prevEffectId) == "aegis" ? "무적이 끝나면 " : "보호막이 깨지면 ",
        _                  => string.Empty,
    };

    /// <summary>카드 머리표 — 즉시 「곧바로」 · 쓰러지면 · 그동안 · 견디면.</summary>
    public static string LinkTag(ClauseLink link) => link switch
    {
        ClauseLink.OnDeath => "쓰러지면",
        ClauseLink.During  => "그동안",
        ClauseLink.Endure  => "견디면",
        _                  => "곧바로",
    };

    /// <summary>서약서 한 줄(결과절) — 머리말 + 동사구.</summary>
    public static string Line(string effectId, ClauseLink link, string prevEffectId)
        => Lead(link, prevEffectId) + Verb(effectId);

    // ── id 기호 ─────────────────────────────────────────
    public static char Code(ClauseLink link) => link switch
    {
        ClauseLink.OnDeath => 'd',
        ClauseLink.During  => 'w',
        ClauseLink.Endure  => 'e',
        _                  => 'i',
    };

    public static ClauseLink ParseLink(char c) => c switch
    {
        'd' => ClauseLink.OnDeath,
        'w' => ClauseLink.During,
        'e' => ClauseLink.Endure,
        _   => ClauseLink.Immediate,
    };
}
