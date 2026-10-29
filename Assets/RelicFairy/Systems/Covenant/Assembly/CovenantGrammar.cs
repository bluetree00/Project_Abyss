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
    /// <summary>
    /// 결과의 동사구 — 조립 미리보기 「→ …」 줄(수치 없이 무슨 일이 일어나는가).
    /// 10-23 U8 사용자 「컨셉 말로는 아무도 못 알아듣는다」 — 비유(「나는 날이 선다」 · 「숨이 끊긴다」 · 「상처를 거둔다」)를 걷고 일어나는 일로.
    /// </summary>
    public static string Verb(string effectId) => Normalize(effectId) switch
    {
        "supernova"  => "그 자리에서 폭발이 터진다",
        "ember"      => "적이 불탄다",
        "hemorrhage" => "적이 피를 흘린다",
        "curse"      => "적이 받는 피해가 커진다",
        "arcflash"   => "적과 둘레 적에게 감전이 걸린다",
        "execute"    => "체력이 낮은 적이 바로 쓰러진다",
        "detonate"   => "적의 화상 · 출혈이 바로 터진다",
        "harvest"    => "적의 화상 · 출혈을 걷어 스킬 대기가 준다",
        "stasis"     => "감전된 적이 기절한다",
        "fury"       => "내가 주는 피해가 커진다",
        "momentum"   => "내 이동 · 공격 속도가 오른다",
        "bloodmark"  => "보호막을 얻는다",
        "aegis"      => "잠시 무적이 된다",
        "ward"       => "내가 받는 피해가 준다",
        "lastbreath" => "죽을 피해를 1번 버티는 힘을 모은다",
        "goldrain"   => "골드를 얻는다",
        _            => effectId,
    };

    /// <summary>
    /// 이음 머리말(시간 없이 — 동사구 줄). 즉시는 없음. 견디면은 앞 절(성역 = 무적이 끝나면 · 보호막 = 깨지면)을 따른다.
    /// 10-23 U8 사용자 「『그 적이 쓰러지면』은 감도 안 잡힌다」 — 앞 절이 건드린 적을 이름으로(「불붙은 적」 · 「폭발에 맞은 적」 …).
    /// 수치 줄(<see cref="NumericLine"/>)은 귀가 듣는 시간까지 붙인다.
    /// </summary>
    public static string Lead(ClauseLink link, string prevEffectId) => link switch
    {
        ClauseLink.OnDeath => $"{TouchedNoun(prevEffectId)}이 쓰러지면 ",
        ClauseLink.During  => $"{WindowWhat(prevEffectId)} 동안 적을 쓰러뜨리면 ",
        ClauseLink.Endure  => Normalize(prevEffectId) == "aegis" ? "무적이 끝나면 " : "보호막이 깨지면 ",
        _                  => string.Empty,
    };

    /// <summary>카드 머리표 — 즉시 「곧바로」 · 쓰러지면 · 그동안 · 견디면(10-23: 「그동안 처치」 · 「보호막·무적 뒤」 — 무엇이 일어나면 이어지는지. 카드 설명 두 줄 안이라 짧게 · 자세한 말은 서약서 줄).</summary>
    public static string LinkTag(ClauseLink link) => link switch
    {
        ClauseLink.OnDeath => "쓰러지면",
        ClauseLink.During  => "그동안 처치",
        ClauseLink.Endure  => "보호막·무적 뒤",
        _                  => "곧바로",
    };

    /// <summary>서약서 한 줄(결과절) — 머리말 + 동사구.</summary>
    public static string Line(string effectId, ClauseLink link, string prevEffectId)
        => Lead(link, prevEffectId) + Verb(effectId);

    /// <summary>
    /// 서약서 한 줄(결과절) — 머리말 + <b>누구에게</b> + 수치(10-18 사용자 「『적이 피를 흘린다』보다 『출혈 피해 몇』이 직관적」).
    /// 10-23 U8 사용자 「『그 적이 쓰러지면 화상』은 감도 안 잡힌다」 — 받는 쪽을 연쇄 실행과 같은 규칙으로 이름 붙인다
    /// (「맞힌 적이」 · 「8m 안에서 가장 가까운 적이」 · 「불붙은 적이 4초 안에 쓰러지면 그 자리에서 가장 가까운 적이」 …).
    /// 수치는 카드와 같은 식(<see cref="CovenantAssemblePreview"/> — 원인 계수 · 등급 · 절 위치 배율, 런타임과 같은 값).
    /// 원인 · 효과를 못 찾으면 동사구로 돌아간다.
    /// </summary>
    /// <param name="resultIndex">결과 몇째 절인가(0부터) — 위치 배율(<see cref="PositionMult"/>).</param>
    public static string NumericLine(string causeId, CovenantTier causeTier, string effectId, CovenantTier effectTier,
                                     ClauseLink link, string prevEffectId, int resultIndex)
    {
        var p = CovenantAssemblePreview.Build(causeId, causeTier, effectId, effectTier, PositionMult(resultIndex));
        if (!p.valid) return Line(effectId, link, prevEffectId);
        Receiver(p, link, prevEffectId, resultIndex, out var who, out var at, out var area);
        return TimedLead(causeId, link, prevEffectId) + p.Clause(who, at, area);
    }

    /// <summary>
    /// 절이 받는 쪽 이름 — 연쇄 실행(<see cref="CovenantSentence"/> RunCore · FireClause · 귀)과 같은 규칙.
    /// <list type="bullet">
    /// <item>첫 결과 = 원인이 가리킨 쪽(<see cref="CovenantAssemblePreview.SubjectAtCause"/>).</item>
    /// <item>곧바로 = 앞 절이 건드린 적(최대 3) · 처형 뒤는 처형한 자리.</item>
    /// <item>쓰러지면 · 그동안 = 쓰러진 자리 + 그 자리에서 가장 가까운 적(폭발은 그 적 자리, 없으면 쓰러진 자리).</item>
    /// <item>견디면 = 나를 중심으로.</item>
    /// </list>
    /// </summary>
    private static void Receiver(CovenantAssemblePreview p, ClauseLink link, string prevEffectId, int resultIndex,
                                 out string who, out string at, out string area)
    {
        if (resultIndex <= 0) { p.SubjectAtCause(out who, out at, out area); return; }
        switch (link)
        {
            case ClauseLink.OnDeath:
            case ClauseLink.During:
                who = "그 자리에서 가장 가까운 적"; at = "그 자리 근처"; area = "그 자리";
                return;
            case ClauseLink.Endure:
                who = CovenantAssemblePreview.NearMe; at = "내 근처"; area = "내 둘레";
                return;
            default:
                if (Normalize(prevEffectId) == "execute") { who = "처형한 자리에서 가장 가까운 적"; at = area = "처형한 자리"; return; }
                // 「나」 쪽 절 뒤(옛 저장 문장) — 넘겨받은 적이 없어 나를 중심으로 찾는다
                if (Receive(prevEffectId) == ClauseReceive.Me) { who = CovenantAssemblePreview.NearMe; at = "내 자리"; area = "내 둘레"; return; }
                who = TouchedNoun(prevEffectId); at = area = who + " 자리";
                return;
        }
    }

    /// <summary>
    /// 이음 머리말 + 귀가 듣는 시간 — 쓰러지면 = 앞 절 지속(1.5~6초 · <see cref="CovenantSentence.DeathEarMin"/>),
    /// 그동안 = 앞 절 창(박차 · 처치 원인은 두 배) · 창당 1번, 견디면 = 무적이 끝나면 / 보호막이 8초 안에 깨지면.
    /// </summary>
    private static string TimedLead(string causeId, ClauseLink link, string prevEffectId)
    {
        if (link == ClauseLink.Immediate || !CovenantPalette.TryGetEffect(prevEffectId, out var prev)) return Lead(link, prevEffectId);
        switch (link)
        {
            case ClauseLink.OnDeath:
            {
                float hold = UnityEngine.Mathf.Clamp(prev.duration, CovenantSentence.DeathEarMin, CovenantSentence.DeathEarMax);
                return $"{TouchedNoun(prev.id)}이 {hold:0.#}초 안에 쓰러지면 ";
            }
            case ClauseLink.During:
            {
                float win = prev.kind == EffectKind.StatBuff && CovenantPalette.TryGetCause(causeId, out var c) && c.cls == CauseClass.Kill
                    ? prev.duration * CovenantMath.MomentumKillDurationMult
                    : prev.duration;
                return $"{WindowWhat(prev.id)} {win:0.#}초 안에 적을 쓰러뜨리면(1번) ";
            }
            case ClauseLink.Endure:
                return prev.kind == EffectKind.Invincible ? "무적이 끝나면 " : $"보호막이 {CovenantSentence.ShieldEarSeconds:0}초 안에 깨지면 ";
            default:
                return Lead(link, prevEffectId);
        }
    }

    /// <summary>앞 절이 건드린 적 — 「쓰러지면」의 주어 · 「곧바로」의 받는 적.</summary>
    private static string TouchedNoun(string effectId) => Normalize(effectId) switch
    {
        "supernova"  => "폭발에 맞은 적",
        "ember"      => "불붙은 적",
        "hemorrhage" => "피 흘리는 적",
        "curse"      => "저주 걸린 적",
        "arcflash"   => "감전된 적",
        "detonate"   => "터진 적",
        "stasis"     => "기절한 적",
        _            => "앞 줄의 적",
    };

    /// <summary>앞 절이 연 창 — 「그동안」의 그동안(격노 · 박차 · 결계).</summary>
    private static string WindowWhat(string effectId) => Normalize(effectId) switch
    {
        "fury"     => "피해가 오른",
        "momentum" => "빨라진",
        "ward"     => "받는 피해가 준",
        _          => "효과가 이어지는",
    };

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
