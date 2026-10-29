using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>원거리 재련 카드 한 장 — 파츠 · 지금 단계 → 고르면 단계 · 새로 켜는 파츠인가.</summary>
public readonly struct RangedPartOffer
{
    public readonly string partId;
    public readonly int fromLevel;
    public readonly int toLevel;
    public readonly bool isNew;

    public RangedPartOffer(string partId, int fromLevel, int toLevel)
    {
        this.partId = partId; this.fromLevel = fromLevel; this.toLevel = toLevel; isNew = fromLevel <= 0;
    }

    public WeaponPartEntry Def => Managers.WeaponParts?.GetById(partId);
}

/// <summary>
/// 원거리 재련 카드 뽑기(10-18 재련 개편 · 기획/RelicFairy_재련개편_설계_20261018.md §4).
/// 재련 1회 = 아직 상한(Lv10)이 아닌 파츠 중 무작위 3장 → 1장. 새 파츠 = Lv1로 켜짐 · 가진 파츠 = 다음 눈금(4 · 7 · 10)까지.
/// 분열 · 관통은 3단계마다 +1발 · +1관통이라 효과가 바뀌는 단계가 1 · 4 · 7 · 10이다 — 고를 때마다 한 눈금씩 오른다(체감 신호).
/// 고르기 창(UI)은 <see cref="OnOffered"/>를 듣고 <see cref="Choose"/> · <see cref="Reroll"/>을 부른다. 듣는 쪽이 없으면 첫 카드로 바로 정한다.
/// 이 런에 한 원거리 재련 수 · 쓴 다시 뽑기는 <see cref="RangedPartsState"/>가 들고 세이브된다.
/// </summary>
public static class RangedPartsDraft
{
    // ── Constants ─────────────────────────────────────────────────
    public const int CardCount = 3;
    public const int PartCap = 10;        // 파츠 상한 — 분열 · 관통의 Lv11 ~ 12는 효과가 그대로라 없앤다
    public const int Milestone = 3;       // 눈금 1 · 4 · 7 · 10
    public const int RerollsPerRun = 2;

    // ── Static ────────────────────────────────────────────────────
    private static readonly List<RangedPartOffer> s_offered = new(CardCount + 1);
    private static readonly List<WeaponPartEntry> s_pool = new(8);
    private static System.Random s_rng;
    private static int s_count = CardCount;

    // ── Properties ────────────────────────────────────────────────
    /// <summary>지금 열린 카드(없으면 빈 목록).</summary>
    public static IReadOnlyList<RangedPartOffer> Offered => s_offered;
    public static bool IsOpen => s_offered.Count > 0;
    /// <summary>이번 재련소 방문에만 쓰는 다시 뽑기(화로 「풍요」). 런 몫보다 먼저 쓴다. 방을 나가면 0.</summary>
    public static int VisitRerolls { get; set; }
    /// <summary>쓸 수 있는 다시 뽑기(런 몫 + 이번 방문 몫).</summary>
    public static int RerollsLeft => Mathf.Max(0, RerollsPerRun - (RangedPartsState.Current?.RerollsUsed ?? 0)) + VisitRerolls;

    /// <summary>카드가 열렸다(고르기 창이 듣는다).</summary>
    public static event Action OnOffered;
    /// <summary>골랐다.</summary>
    public static event Action<RangedPartOffer> OnChosen;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        s_offered.Clear(); s_rng = null; s_count = CardCount; VisitRerolls = 0;
        OnOffered = null; OnChosen = null;
    }

    // ── Public Methods ────────────────────────────────────────────
    /// <summary>이 파츠의 상한(데이터 상한과 <see cref="PartCap"/> 중 작은 쪽).</summary>
    public static int CapOf(WeaponPartEntry def) => def != null && def.max_level > 0 ? Mathf.Min(PartCap, def.max_level) : PartCap;

    /// <summary>고르면 오를 단계 — 꺼짐 = 1 · 켜짐 = 다음 눈금(4 · 7 · 10 · 상한까지). 성향(+1)으로 Lv2에서 시작했어도 다음 눈금까지.</summary>
    public static int NextLevel(int level, int cap) => level <= 0 ? 1 : Mathf.Min(cap, ((level - 1) / Milestone + 1) * Milestone + 1);

    /// <summary>카드로 낼 파츠가 남았나(모두 상한이면 원거리 재련을 막는다).</summary>
    public static bool HasCandidates(RangedPartsState state)
    {
        var data = Managers.WeaponParts;
        if (data == null || state == null) return false;
        foreach (var def in data.All)
            if (IsCandidate(def, state)) return true;
        return false;
    }

    // 파츠 id → 해금 노드(기억의 제단 원거리 갈래). 분열의 시위는 기본이라 노드가 없다 — 시작 파츠 작업대 · 재련 카드가 같은 표를 읽는다(10-23 W6).
    private static readonly Dictionary<string, string> UnlockNode = new()
    {
        { "part_pierce",  MemoryAltarCatalog.PartPierce },
        { "part_power",   MemoryAltarCatalog.PartPower },
        { "part_homing",  MemoryAltarCatalog.PartHoming },
        { "part_explode", MemoryAltarCatalog.PartExplode },
    };

    /// <summary>이 파츠를 연 제단 노드 id(기본 파츠면 null).</summary>
    public static string UnlockNodeOf(string partId) => UnlockNode.TryGetValue(partId ?? "", out var node) ? node : null;

    /// <summary>제단에서 연 파츠인가 — 기본 파츠(분열)는 늘 열림.</summary>
    public static bool IsUnlocked(string partId)
    {
        var node = UnlockNodeOf(partId);
        return node == null || MemoryAltarService.IsUnlocked(node);
    }

    /// <summary>
    /// 재련 카드로 낼 수 있나 — 상한 아래 + (제단에서 열었거나 이미 가진 파츠). 예전엔 열지 않은 파츠도 카드로 나왔다(10-23 W6 「확장으로」).
    /// 이미 가진 파츠는 열림과 상관없이 계속 올린다 — 손에 든 것을 빼앗지 않는다.
    /// </summary>
    private static bool IsCandidate(WeaponPartEntry def, RangedPartsState state)
    {
        if (def == null) return false;
        int lv = state.LevelOf(def.part_id);
        return lv < CapOf(def) && (lv > 0 || IsUnlocked(def.part_id));
    }

    /// <summary>
    /// 카드 <paramref name="count"/>장(기본 3 · 화로 「열기」면 +1)을 연다 — 이 런 원거리 재련 수를 하나 센다.
    /// 낼 파츠가 없으면 false(재료는 호출측이 아직 쓰지 않았어야 한다 — <see cref="HasCandidates"/>로 먼저 본다).
    /// </summary>
    public static bool Offer(System.Random rng, int count = CardCount)
    {
        var state = RangedPartsState.Current;
        if (rng == null || state == null || IsOpen) return false;
        s_rng = rng;
        s_count = Mathf.Max(1, count);
        if (!Draw(state, null)) return false;
        state.ReforgeCount++;
        Debug.Log($"[RangedParts] 재련 {state.ReforgeCount}번째 — {Describe()}");
        if (OnOffered != null) OnOffered.Invoke();
        else AutoChoose();
        return true;
    }

    /// <summary>카드를 고른다 — 파츠 단계를 올리고(새 파츠면 켠다) 저장한다.</summary>
    public static void Choose(int index)
    {
        var state = RangedPartsState.Current;
        if (state == null || index < 0 || index >= s_offered.Count) return;
        var card = s_offered[index];
        s_offered.Clear();
        int now = state.LevelOf(card.partId);
        if (card.toLevel > now) state.LevelUp(card.partId, card.toLevel - now);   // 꺼진 파츠는 LevelUp이 켠다(Lv = delta = 1)
        state.Log.Add(new RangedPartsState.Equipped { partId = card.partId, level = state.LevelOf(card.partId) });   // 10-19 재련 기록
        Debug.Log($"[RangedParts] 고름 — {card.Def?.part_name} Lv{now} → Lv{state.LevelOf(card.partId)}");

        // 원거리 스킬 단계는 파츠 합에서 나온다 — HUD가 다시 읽게(재련소 옛 파츠 강화와 같은 통지)
        var wm = GameRunBootstrapper.Instance?.Run?.Player?.WeaponManager;
        if (wm != null) wm.RaiseEquippedWeaponRefreshed();
        RunFlowController.Active?.SaveNow("ranged-parts-reforge");
        OnChosen?.Invoke(card);
    }

    /// <summary>카드를 모두 다시 뽑는다(지금 카드와 겹치지 않게 · 모자라면 겹쳐도). 다시 뽑기가 없거나 바뀌지 않으면 false.</summary>
    public static bool Reroll()
    {
        var state = RangedPartsState.Current;
        if (!IsOpen || s_rng == null || state == null || RerollsLeft <= 0) return false;
        var before = new HashSet<string>();
        foreach (var c in s_offered) before.Add(c.partId);
        if (!Draw(state, before)) return false;
        if (VisitRerolls > 0) VisitRerolls--;
        else state.RerollsUsed++;
        OnOffered?.Invoke();
        return true;
    }

    /// <summary>창 없이 정해야 할 때(창 미연결 · 세이브) — 첫 카드.</summary>
    public static void AutoChoose()
    {
        if (IsOpen) Choose(0);
    }

    // ── Private Methods ───────────────────────────────────────────
    /// <summary>상한이 아닌 파츠에서 <see cref="s_count"/>장 — <paramref name="avoid"/>(다시 뽑기 전 카드)는 다른 것이 모자랄 때만.</summary>
    private static bool Draw(RangedPartsState state, HashSet<string> avoid)
    {
        var data = Managers.WeaponParts;
        if (data == null) return false;
        s_pool.Clear();
        foreach (var def in data.All)
            if (IsCandidate(def, state)) s_pool.Add(def);
        if (s_pool.Count == 0) return false;

        // 섞기(Fisher–Yates) → 피할 카드는 뒤로
        for (int i = s_pool.Count - 1; i > 0; i--)
        {
            int j = s_rng.Next(i + 1);
            (s_pool[i], s_pool[j]) = (s_pool[j], s_pool[i]);
        }
        if (avoid != null) s_pool.Sort((a, b) => (avoid.Contains(a.part_id) ? 1 : 0).CompareTo(avoid.Contains(b.part_id) ? 1 : 0));

        s_offered.Clear();
        for (int i = 0; i < s_pool.Count && s_offered.Count < s_count; i++)
        {
            var def = s_pool[i];
            int lv = state.LevelOf(def.part_id);
            s_offered.Add(new RangedPartOffer(def.part_id, lv, NextLevel(lv, CapOf(def))));
        }
        return s_offered.Count > 0;
    }

    private static string Describe()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var c in s_offered)
        {
            if (sb.Length > 0) sb.Append(" | ");
            sb.Append(c.Def?.part_name ?? c.partId).Append(c.isNew ? " 새로 켬" : $" Lv{c.fromLevel} → Lv{c.toLevel}");
        }
        return sb.ToString();
    }
}
