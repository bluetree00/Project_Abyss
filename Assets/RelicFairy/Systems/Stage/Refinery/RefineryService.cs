using UnityEngine;

/// <summary>
/// 정제소 — 원석을 넣고 돌리면 <b>룬 하나를 무작위로</b> 뽑는다. 런 스코프 상태(누진 비용).
/// 설계: 바탕화면 기획/RelicFairy_기획_재련소_정제소.md §2.2(원석 → 룬) · 09-28 사용자 「간단하게 줄여서 룬을 랜덤으로 뽑는 시스템으로」.
///
/// 흐름: <see cref="Craft"/> → 등급 굴림(희귀 이상 · 기억의 제단 해금을 넘지 않음) → 그 등급 룬 풀에서 하나
/// (ItemSORegistry — 방 보상과 같은 풀) → 원석 소비 → 보관함.
/// 예전의 속성 선택 · 존핵 · 피버 · 돌발 이벤트 · 재점화 · 방 특전은 걷었다.
/// 이미 만들어진 존핵(이어하기 세이브)은 <see cref="IsZoneCore"/>로 계속 알아본다(룬판 중앙 금지 · 얼굴).
/// </summary>
public sealed class RefineryService
{
    // ── 비용/확률 ──
    // 누진 비용은 그대로(과잉 억제, 기획 §2.8): 12,12,20,20,28,28,36… → 원석 ≈160이면 런당 6회 안팎.
    private const int BaseCost = 12;
    private const int CostStep = 8;   // 2회마다 +8

    // 등급 확률(고정) — 원석을 내고 돌리는 곳이라 <b>정예방 보상(희귀 52 · 영웅 36 · 전설 12)보다 좋게</b>:
    // 희귀 45 · 영웅 40 · 전설 15(09-28 사용자 「정제소에서 좋은 확률을」 — 첫 값 70/25/5는 정예방보다 나빴다).
    private const float EpicOdds   = 0.40f;
    private const float LegendOdds = 0.15f;

    /// <summary>「정제 등급 상승」(기억의 제단 룬 갈래) 해금 시 Epic 확률에 더해지는 몫. Rare에서 넘어온다.</summary>
    public const float QualityUnlockEpic = 0.12f;

    private const string ZoneCoreIdPrefix = "special_zonecore_";

    // ── 런 상태 ──
    private readonly RunFuelBank      _fuel;
    private readonly RunItemInventory _inventory;

    public int CraftCount { get; private set; }

    public RefineryService(RunFuelBank fuel, RunItemInventory inventory)
    {
        _fuel      = fuel;
        _inventory = inventory;
    }

    // ── 조회 ──

    public int  CurrentCost => BaseCost + (CraftCount / 2) * CostStep;
    public int  OreOwned    => _fuel?.RuneOre ?? 0;
    public bool CanAfford   => OreOwned >= CurrentCost;

    /// <summary>
    /// 이번 돌리기 등급 확률. 등급 해금(기억의 제단)을 넘지 않는다 — 잠긴 등급의 몫은 한 단계 아래로 접힌다.
    /// 표시(확률 막대)와 굴림이 이 한 곳을 같이 보므로 「전설 5%」를 보여 주고 영웅을 주는 일이 없다.
    /// </summary>
    public (float rare, float epic, float legend) CurrentOdds()
    {
        float epic = EpicOdds;
        if (MemoryAltarService.IsUnlocked(MemoryAltarCatalog.RefineQuality))
            epic += QualityUnlockEpic;
        return MemoryAltarService.FoldOdds(Mathf.Max(0f, 1f - epic - LegendOdds), epic, LegendOdds);
    }

    // ── 돌리기 ──

    /// <summary>룬 하나를 뽑아 보관함에 넣는다. 원석 부족/보관함 만차면 실패(Success=false) — 그땐 원석을 쓰지 않는다.</summary>
    public RefineryOutcome Craft()
    {
        if (_inventory != null && _inventory.IsStagingFull)
            return RefineryOutcome.Fail("보관함이 가득 찼습니다");
        if (!CanAfford)
            return RefineryOutcome.Fail("원석이 부족합니다");

        var rune = DrawRune(RollRarity());
        if (rune == null)
            return RefineryOutcome.Fail("뽑을 룬이 없습니다");

        int cost = CurrentCost;
        if (!(_fuel?.TrySpend(FuelKind.RuneOre, cost) ?? false))
            return RefineryOutcome.Fail("원석이 부족합니다");

        _inventory?.AddToStaging(rune);
        CraftCount++;
        return new RefineryOutcome { Success = true, Rarity = rune.rarity, Rune = rune };
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
    private static RuntimeItemData DrawRune(ItemRarity rarity)
    {
        for (var r = rarity; r >= ItemRarity.Rare; r--)
        {
            var pool = ItemSORegistry.GetByRarity(r);
            if (pool == null || pool.Count == 0) continue;
            var rune = RuntimeItemData.FromSO(pool[Random.Range(0, pool.Count)]);
            if (rune != null) return rune;
        }
        Debug.LogWarning($"[Refinery] {rarity} 이하 룬 풀이 비었다 — 뽑기 생략");
        return null;
    }
}

/// <summary>정제소 돌리기 결과.</summary>
public struct RefineryOutcome
{
    public bool            Success;
    public string          FailReason;
    public ItemRarity      Rarity;
    public RuntimeItemData Rune;

    public static RefineryOutcome Fail(string reason) => new RefineryOutcome { Success = false, FailReason = reason };
}
