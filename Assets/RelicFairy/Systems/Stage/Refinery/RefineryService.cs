using UnityEngine;

/// <summary>
/// 정제소 — 원석을 넣고 돌리면 <b>룬 하나를 무작위로</b> 뽑는다. 런 스코프 상태(천장 · 기록) — 비용은 방마다 처음부터 누진(10-18).
/// 설계: 바탕화면 기획/RelicFairy_기획_재련소_정제소.md §2.2(원석 → 룬) · 09-28 사용자 「간단하게 줄여서 룬을 랜덤으로 뽑는 시스템으로」.
///
/// 흐름: <see cref="Craft"/> → 등급 굴림(희귀 이상 · 영웅 · 전설도 처음부터 — 10-14 사용자) → 그 등급 룬 풀에서 하나
/// (ItemSORegistry — 방 보상과 같은 풀) → 원석 소비 → 보관함.
/// 예전의 속성 선택 · 존핵 · 피버 · 돌발 이벤트 · 재점화 · 방 특전은 걷었다.
/// 이미 만들어진 존핵(이어하기 세이브)은 <see cref="IsZoneCore"/>로 계속 알아본다(룬판 중앙 금지 · 얼굴).
/// </summary>
public sealed class RefineryService
{
    // ── 비용/확률 ──
    // 10-15 사용자 「재화는 잘 주되 확률을 내려 더 뽑게 — 도박하는 재미」: 값 12 · 12 · 20 · 20 · 28 … → 10 · 10 · 14 · 14 · 18 …,
    // 원석 수입 ↑(방 보상 4 → 6 등) → 런당 정제 ≈6회 → ≈11회. 등급 확률은 낮추고 천장으로 바닥을 받친다.
    private const int BaseCost = 10;
    private const int CostStep = 4;   // 2회마다 +4

    // 등급 확률(고정): 희귀 58 · 영웅 32 · 전설 10(예전 45 · 40 · 15 — 09-28 「정예방보다 좋게」에서 횟수를 늘리는 쪽으로 옮겼다).
    // 기대 전설 수는 비슷하다(6회 × 15% ≈ 0.9 → 11회 × 10% + 천장 ≈ 1.1).
    private const float EpicOdds   = 0.32f;
    private const float LegendOdds = 0.10f;

    /// <summary>천장 — 전설 없이 이만큼 돌리면 다음 한 번은 전설(10-15). 런 단위 · 이어하기에 남는다.</summary>
    public const int PityLimit = 10;

    /// <summary>「정제 등급」(기억의 제단 룬 갈래 · 10-14 강화형 +3%p/단계)이 Epic 확률에 더하는 몫. Rare에서 넘어온다.</summary>
    public static float QualityUnlockEpic => MemoryAltarService.RefineEpicBonus;

    private const string ZoneCoreIdPrefix = "special_zonecore_";

    // 정제 두 장의 둘째 장 — 보유 계열 추림(할당 없이 재사용).
    private static readonly System.Collections.Generic.List<BuildFamily> s_topFamilies = new(2);
    private static readonly System.Collections.Generic.List<ItemSO>      s_familyPool  = new(16);
    private static readonly System.Collections.Generic.List<ItemSO>      s_obtainable  = new(32);

    // ── 런 상태 ──
    private readonly RunFuelBank      _fuel;
    private readonly RunItemInventory _inventory;

    // ── 방 돌발 이벤트(10-15) — 방 컨트롤러가 들어갈 때 걸고 나갈 때 푼다(이 방에서만) ──
    private object _eventOwner;
    private bool   _purityUsed;
    public RefineryEvent RoomEvent { get; private set; }

    public void BeginRoomEvent(object owner, RefineryEvent ev)
    {
        _eventOwner = owner;
        RoomEvent   = ev;
        _purityUsed = false;
        VisitCraftCount = 0;   // 비용은 방마다 처음부터(10-18)
    }

    /// <summary>건 방만 푼다 — 다음 방(정제소가 이어 나올 수 있다)이 먼저 걸었으면 그대로 둔다.</summary>
    public void EndRoomEvent(object owner)
    {
        if (!ReferenceEquals(owner, _eventOwner)) return;
        _eventOwner = null;
        RoomEvent   = RefineryEvent.None;
    }

    /// <summary>「고순도 원석」 — 이번 한 번은 Epic 이상(쓰면 꺼진다).</summary>
    public bool PurityReady => RoomEvent == RefineryEvent.HighPurity && !_purityUsed;

    /// <summary>두 장 중 하나를 고르나 — 기억의 제단 「정제 두 장」 또는 이 방 「두 장 정제」.</summary>
    public bool TwoCards => MemoryAltarService.IsRefinePickUnlocked || RoomEvent == RefineryEvent.TwoCards;

    public static string EventTitle(RefineryEvent ev) => ev switch
    {
        RefineryEvent.HighPurity => "고순도 원석",
        RefineryEvent.HalfPrice  => "반값 정제",
        RefineryEvent.TwoCards   => "두 장 정제",
        _                        => string.Empty,
    };

    public static string EventLine(RefineryEvent ev) => ev switch
    {
        RefineryEvent.HighPurity => "이번 한 번 Epic 이상",
        RefineryEvent.HalfPrice  => "원석 절반",
        RefineryEvent.TwoCards   => "룬 두 장 중 하나",
        _                        => string.Empty,
    };

    private int EventPrice(int cost) => RoomEvent == RefineryEvent.HalfPrice ? Mathf.Max(1, Mathf.RoundToInt(cost * 0.5f)) : cost;

    /// <summary>이번 런에 정제한 횟수(기록 · 이어하기 세이브). 비용은 이것이 아니라 <see cref="VisitCraftCount"/>로 정한다.</summary>
    public int CraftCount { get; private set; }

    /// <summary>
    /// 이번 방문(정제소 방 하나)에서 정제한 횟수 — 비용 단계. 방에 들어올 때(<see cref="BeginRoomEvent"/>) 0으로 돌아간다
    /// (10-18 사용자 「누를수록 원석이 많이 들지만 다시 방에 진입할 때는 값을 초기화」 — 예전엔 런 전체에 쌓여 뒤 챕터 정제소가 비쌌다).
    /// 천장(<see cref="PityCount"/>)은 그대로 런 단위다.
    /// </summary>
    public int VisitCraftCount { get; private set; }

    /// <summary>전설 없이 돌린 수(천장 · 0 ~ <see cref="PityLimit"/>). 전설이 나오면 0.</summary>
    public int PityCount { get; private set; }

    /// <summary>이번 한 번이 천장(전설 확정)인가 — 화면 「다음 정제 전설 확정」.</summary>
    public bool PityReady => PityCount >= PityLimit;

    /// <summary>이어하기 — 천장 진척을 되살린다.</summary>
    public void RestorePity(int count) => PityCount = Mathf.Clamp(count, 0, PityLimit);

    /// <summary>이어하기 — 저장된 런 정제 횟수(기록)를 되살린다. 비용 단계는 방마다 처음부터라 따로 되살리지 않는다(10-18).</summary>
    public void RestoreCraftCount(int count) => CraftCount = Mathf.Max(0, count);

    public RefineryService(RunFuelBank fuel, RunItemInventory inventory)
    {
        _fuel      = fuel;
        _inventory = inventory;
    }

    // ── 조회 ──

    public int  CurrentCost => EventPrice(BaseCost + (VisitCraftCount / 2) * CostStep);
    /// <summary>이번 정제를 한 뒤의 비용 — 제단 화면 「다음 정제부터 원석 N」(반값 정제 방이면 그 값도 절반). 방을 나가면 다시 처음 값.</summary>
    public int  NextCost    => EventPrice(BaseCost + ((VisitCraftCount + 1) / 2) * CostStep);
    public int  OreOwned    => _fuel?.RuneOre ?? 0;
    public bool CanAfford   => OreOwned >= CurrentCost;

    // ── 재료 녹이기(10-17 · 파워밸런스 진단 안 C-②) ──
    // 재련소의 강화재료 1 → 원석 1(이 방에서만 · 되돌리기 없음). 재련소를 건너뛴 챕터의 재료가 룬이 될 길 —
    // 「지금 룬 vs 다음 재련소에서 무기」가 진짜 맞바꿈이 되게. 상점 값으로 재료 20G · 원석 10G라 녹이면 절반을 잃는다.

    public int MaterialOwned => _fuel?.EnhanceMaterial ?? 0;

    /// <summary>이번 정제에 모자란 원석을 채울 강화재료 수 — 원석이 모자라고 재료로 다 채울 수 있을 때만(아니면 0).
    /// 누를 때마다 한 번 정제할 만큼만 녹인다 — 나머지 재료는 다음 재련소를 위해 남는다.</summary>
    public int MeltForNext
    {
        get
        {
            int need = CurrentCost - OreOwned;
            return need > 0 && MaterialOwned >= need ? need : 0;
        }
    }

    /// <summary>강화재료 <paramref name="material"/>을 같은 수의 원석으로 녹인다. 모자라면 false(무변경).</summary>
    public bool TryMelt(int material) => _fuel != null && _fuel.Convert(FuelKind.EnhanceMaterial, FuelKind.RuneOre, material);

    /// <summary>
    /// 이번 돌리기 등급 확률. 표시(확률 막대)와 굴림이 이 한 곳을 같이 본다.
    /// <para>정제소는 원석을 내고 돌리는 곳이라 기억의 제단 등급 해금(영웅 · 전설)으로 <b>접지 않는다</b> — 예전엔 해금 전이면
    /// 영웅 · 전설 몫이 전부 희귀로 접혀 「희귀 100%」만 나왔다(10-14 사용자 「정제소에서 높은 등급의 룬도 나올 수 있게」).
    /// 방 보상 · 상점은 그대로 해금을 따른다.</para>
    /// </summary>
    public (float rare, float epic, float legend) CurrentOdds()
    {
        if (PurityReady) return (0f, 1f - LegendOdds, LegendOdds);   // 고순도 원석 — Rare 몫이 Epic으로(10-15)
        float epic = EpicOdds + QualityUnlockEpic;
        return (Mathf.Max(0f, 1f - epic - LegendOdds), epic, LegendOdds);
    }

    // ── 돌리기 ──

    /// <summary>
    /// 룬 하나를 뽑아 보관함에 넣는다. 원석 부족/보관함 만차면 실패(Success=false) — 그땐 원석을 쓰지 않는다.
    /// <para>「정제 두 장」(기억의 제단) 해금이면 2장을 굴리고 <b>보관함엔 넣지 않는다</b> — 좋은 쪽을 <see cref="RefineryOutcome.Rune"/>,
    /// 다른 쪽을 <see cref="RefineryOutcome.Alt"/>로 돌려주고, 고르기는 룬 선택 화면이 맡는다(09-29).</para>
    /// </summary>
    public RefineryOutcome Craft()
    {
        if (_inventory != null && _inventory.IsStagingFull)
            return RefineryOutcome.Fail("보관함이 가득 찼습니다");
        if (!CanAfford)
            return RefineryOutcome.Fail("원석이 부족합니다");

        var (rune, so) = DrawRune(PityReady ? ItemRarity.Legendary : RollRarity());
        if (rune == null)
            return RefineryOutcome.Fail("뽑을 룬이 없습니다");

        RuntimeItemData alt = null; ItemSO altSo = null;
        if (TwoCards)
        {
            // 두 번째 장 — 보유 발동 계열 쪽으로 굴린다(한 장은 넓게 · 한 장은 내 빌드). 같은 룬이면 고르는 의미가 없어 몇 번 다시 굴린다.
            for (int i = 0; i < 4 && alt == null; i++)
            {
                var (a, aso) = DrawRune(RollRarity(), preferOwnedFamily: true);
                if (a != null && a.itemId != rune.itemId) { alt = a; altSo = aso; }
            }
        }

        int cost = CurrentCost;
        if (!(_fuel?.TrySpend(FuelKind.RuneOre, cost) ?? false))
            return RefineryOutcome.Fail("원석이 부족합니다");

        CraftCount++;
        VisitCraftCount++;
        if (RoomEvent == RefineryEvent.HighPurity) _purityUsed = true;
        // 천장 — 보여 주는 쪽(좋은 장)이 전설이면 처음부터 · 아니면 한 칸
        bool gotLegend = rune.rarity >= ItemRarity.Legendary || (alt != null && alt.rarity >= ItemRarity.Legendary);
        PityCount = gotLegend ? 0 : Mathf.Min(PityLimit, PityCount + 1);
        if (alt == null)
        {
            _inventory?.AddToStaging(rune);
            return new RefineryOutcome { Success = true, Rarity = rune.rarity, Rune = rune, RuneSo = so, Spent = cost };
        }

        // 좋은 쪽을 먼저 공개한다(등급이 같으면 먼저 굴린 쪽).
        if (alt.rarity > rune.rarity) { (rune, alt) = (alt, rune); (so, altSo) = (altSo, so); }
        return new RefineryOutcome { Success = true, Rarity = rune.rarity, Rune = rune, RuneSo = so, Alt = alt, AltSo = altSo, Spent = cost };
    }

    /// <summary>「정제 두 장」에서 넘겼을 때 — 쓴 원석을 돌려준다(고른 것이 없으니 값도 없다).</summary>
    public void Refund(int ore)
    {
        if (ore > 0) _fuel?.Add(FuelKind.RuneOre, ore);
    }

    /// <summary>정제소가 예전에 만든 존핵인가(아이템 차트에 없는 특수 룬 — id 접두로 가른다). 이어하기 세이브 호환용.</summary>
    public static bool IsZoneCore(RuntimeItemData item)
        => item != null && item.itemId != null
        && item.itemId.StartsWith(ZoneCoreIdPrefix, System.StringComparison.Ordinal);

    // ── 내부 ──

    private ItemRarity RollRarity()
    {
        var (_, epic, leg) = CurrentOdds();
        float r = Random.value;
        if (r < leg)        return ItemRarity.Legendary;
        if (r < leg + epic) return ItemRarity.Epic;
        return ItemRarity.Rare;
    }

    /// <summary>그 등급 풀에서 하나. 풀이 비면 한 단계씩 내린다(방 보상 RoomClearGate와 같은 폴백) — 희귀 아래로는 내리지 않는다.</summary>
    private static (RuntimeItemData data, ItemSO so) DrawRune(ItemRarity rarity, bool preferOwnedFamily = false)
    {
        for (var r = rarity; r >= ItemRarity.Rare; r--)
        {
            var all = ItemSORegistry.GetByRarity(r);
            // 이미 가진 룬(상한 1)은 뺀다(10-09 출시 점검)
            var refineInv = GameRunBootstrapper.Instance?.Run?.ItemInventory;
            System.Collections.Generic.IReadOnlyList<ItemSO> pool = all;
            if (refineInv != null && all != null) { refineInv.FilterObtainable(all, s_obtainable); pool = s_obtainable; }
            if (pool == null || pool.Count == 0) continue;
            var so   = pool[Random.Range(0, pool.Count)];
            if (preferOwnedFamily)
            {
                // 보유 각인 1 · 2위 계열의 룬만 남긴다 — 없으면(각인 0 · 그 등급에 그 계열 없음) 넓게 뽑은 그대로.
                BuildImprint.TopFamilies(2, s_topFamilies);
                s_familyPool.Clear();
                foreach (var c in pool)
                    if (c != null && s_topFamilies.Contains(BuildFamilyRules.OfItemId(c.itemId))) s_familyPool.Add(c);
                if (s_familyPool.Count > 0) so = s_familyPool[Random.Range(0, s_familyPool.Count)];
            }
            var rune = RuntimeItemData.FromSO(so);
            if (rune != null) return (rune, so);
        }
        Debug.LogWarning($"[Refinery] {rarity} 이하 룬 풀이 비었다 — 뽑기 생략");
        return (null, null);
    }
}

/// <summary>정제소 방 돌발 이벤트(10-15 · 방 들어갈 때 1회 굴림 · 이 방에서만).</summary>
public enum RefineryEvent
{
    None,
    HighPurity,   // 고순도 원석 — 이번 한 번 Epic 이상
    HalfPrice,    // 반값 정제 — 원석 ×0.5
    TwoCards,     // 두 장 정제 — 두 장 중 하나(제단 「정제 두 장」을 이미 열었으면 굴리지 않는다)
}

/// <summary>정제소 돌리기 결과.</summary>
public struct RefineryOutcome
{
    public bool            Success;
    public string          FailReason;
    public ItemRarity      Rarity;
    public RuntimeItemData Rune;
    public ItemSO          RuneSo;
    /// <summary>「정제 두 장」의 다른 장(없으면 한 장 정제 — 이미 보관함에 들어갔다).</summary>
    public RuntimeItemData Alt;
    public ItemSO          AltSo;
    /// <summary>쓴 원석(두 장을 넘기면 돌려준다).</summary>
    public int             Spent;

    public static RefineryOutcome Fail(string reason) => new RefineryOutcome { Success = false, FailReason = reason };
}
