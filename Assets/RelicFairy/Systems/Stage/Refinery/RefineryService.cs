using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 정제소 — 원석을 넣고 돌려 판을 강화하는 특수 룬(존핵)을 만든다. 런 스코프 상태(피버·비용·버프).
/// 설계 정본: 바탕화면 기획/RelicFairy_기획_정제소_속성응축.md
///
/// 흐름: <see cref="Craft"/>(속성) → 원석 소비 → 등급 리빌 → 존핵 특수 룬을 보관함에 추가 → 돌발 이벤트 판정.
/// 결과는 한 번만 확정되며, 돌발 '재점화' 이벤트가 떴을 때만 <see cref="Reforge"/>로 1회 다시 굴린다.
///
/// ⚠️ 존핵의 <b>증폭 효과 적용(놓인 존 시너지 +%)은 P2</b> — 지금은 룬에 메타데이터만 실린다.
/// </summary>
public sealed class RefineryService
{
    // ── 비용/확률 튜닝 (상점·재련소 패리티 실측 기준) ──
    //
    // 실측: 재련소 = 강화재료로 레벨당 +8% 공격력, 비용 1→25 누진(런당 대략 15~20회).
    //       원석 수입 = 방당 4 × ~40방 ≈ 160 (+연료문 12).
    // 목표: 런당 존핵 5~6개. (존당 상한 +60% → 존핵 2개면 한 존 포화. 10개 넘게 나오면
    //       6존이 전부 포화되고 존핵만 20칸 이상 먹어 "최종 60칸" 설계가 깨진다.)
    // 곡선: 12,12,20,20,28,28,36… → 누적 12/24/44/64/92/120/156 → 160 원석 ≈ 6개.
    private const int   BaseCost      = 12;
    private const int   CostStep      = 8;   // 2회마다 +8
    private const float EventBaseRate = 0.18f;

    /// <summary>등급별 존핵 증폭치(%). Rare/Epic/Legendary.</summary>
    private static readonly (ItemRarity rarity, int amt)[] Tiers =
    {
        (ItemRarity.Rare,      20),
        (ItemRarity.Epic,      30),
        (ItemRarity.Legendary, 45),
    };

    /// <summary>존핵 고정 모양 = 가로2(shape_id 2).</summary>
    private const int ZoneCoreShapeId = 2;

    // ── 런 상태 ──
    private readonly RunFuelBank      _fuel;
    private readonly RunItemInventory _inventory;

    public int  Fever      { get; private set; }
    public int  CraftCount { get; private set; }
    public bool NextHeat   { get; private set; }   // 과열: 다음 돌리기 상위 확률↑
    public bool NextFree   { get; private set; }   // 불티: 다음 돌리기 무료
    public bool CanReforge { get; private set; }   // 재점화 이벤트로 열린 1회 재굴림

    /// <summary>정제소 '방'에서만 붙는 특전. 상시 탭(룬판 버튼)에서는 None.</summary>
    public RefineryPerk RoomPerk { get; private set; }
    private bool _perkFreeUsed;                    // FirstFree 특전 소진 여부

    private RuntimeItemData _lastRune;             // 재점화 시 교체 대상

    /// <summary>정제소 방 진입 시 특전 설정(방마다 1종). 패널을 닫을 때 <see cref="ClearRoomPerk"/>로 해제.</summary>
    public void SetRoomPerk(RefineryPerk perk)
    {
        RoomPerk = perk;
        _perkFreeUsed = false;
    }

    public void ClearRoomPerk()
    {
        RoomPerk = RefineryPerk.None;
        _perkFreeUsed = false;
    }

    /// <summary>현재 특전 설명(UI 표시용). 없으면 null.</summary>
    public string RoomPerkLabel => RoomPerk switch
    {
        RefineryPerk.Discount  => "◆ 방 특전 — 원석 비용 30% 할인",
        RefineryPerk.Lucky     => "◆ 방 특전 — Legendary 확률 2배",
        RefineryPerk.FirstFree => _perkFreeUsed ? "◆ 방 특전 — 무료 돌리기 사용됨" : "◆ 방 특전 — 첫 돌리기 무료",
        _                      => null,
    };

    public RefineryService(RunFuelBank fuel, RunItemInventory inventory)
    {
        _fuel      = fuel;
        _inventory = inventory;
    }

    // ── 조회 ──

    public int CurrentCost
    {
        get
        {
            // 불티(다음 무료) 또는 방 특전 '첫 돌리기 무료'
            if (NextFree) return 0;
            if (RoomPerk == RefineryPerk.FirstFree && !_perkFreeUsed) return 0;

            int cost = BaseCost + (CraftCount / 2) * CostStep;
            if (RoomPerk == RefineryPerk.Discount) cost = Mathf.CeilToInt(cost * 0.7f);   // 30% 할인
            return cost;
        }
    }

    public int  OreOwned  => _fuel?.RuneOre ?? 0;
    public bool CanAfford => OreOwned >= CurrentCost;

    /// <summary>이번 돌리기 등급 확률(피버·과열·방 특전 반영).</summary>
    public (float rare, float epic, float legend) CurrentOdds()
        => Odds(Fever, NextHeat, RoomPerk == RefineryPerk.Lucky);

    private static (float rare, float epic, float legend) Odds(int fever, bool heat, bool lucky)
    {
        float epic = Mathf.Min(0.55f, 0.15f + fever * 0.06f);
        float leg  = Mathf.Min(0.25f, 0.03f + fever * 0.025f);
        if (heat)  { epic = Mathf.Min(0.70f, epic * 2f); leg = Mathf.Min(0.40f, leg * 2f); }
        if (lucky) { leg  = Mathf.Min(0.45f, leg * 2f); }                                  // 방 특전: Legendary 2배
        return (Mathf.Max(0f, 1f - epic - leg), epic, leg);
    }

    // ── 돌리기 ──

    /// <summary>속성을 지정해 존핵을 벼린다. 원석 부족/보관함 만차면 실패(Success=false).</summary>
    public RefineryOutcome Craft(string elementId)
    {
        if (_inventory != null && _inventory.IsStagingFull)
            return RefineryOutcome.Fail("보관함이 가득 찼습니다");

        // 방 특전 '첫 돌리기 무료'를 이번에 쓰는지(불티가 우선이라 그때는 특전 미소진)
        bool usesPerkFree = !NextFree && RoomPerk == RefineryPerk.FirstFree && !_perkFreeUsed;

        int cost = CurrentCost;
        if (cost > 0 && !(_fuel?.TrySpend(FuelKind.RuneOre, cost) ?? false))
            return RefineryOutcome.Fail("원석이 부족합니다");
        if (usesPerkFree) _perkFreeUsed = true;   // 특전 무료 1회 소진

        bool heat = NextHeat;
        NextHeat = false; NextFree = false; CanReforge = false;

        var rarity = RollRarity(heat);
        var rune   = CreateZoneCore(elementId, rarity);
        _inventory?.AddToStaging(rune);
        _lastRune = rune;

        CraftCount++;
        Fever = rarity == ItemRarity.Legendary ? 0 : Fever + 1;

        var ev = RollEvent(rarity);
        ApplyEvent(ev, elementId, rarity);

        return new RefineryOutcome { Success = true, Rarity = rarity, Rune = rune, EventKind = ev };
    }

    /// <summary>재점화 — 방금 결과를 1회 다시 굴린다(무료). CanReforge일 때만.</summary>
    public RefineryOutcome Reforge(string elementId)
    {
        if (!CanReforge) return RefineryOutcome.Fail("재점화할 수 없습니다");
        CanReforge = false;

        // 직전 룬 제거 후 재추첨(피버는 직전 결과분 되돌리고 새로 반영)
        if (_lastRune != null) _inventory?.DiscardFromStaging(_lastRune);
        if (_lastRune != null && _lastRune.rarity == ItemRarity.Legendary) Fever = 0;
        else Fever = Mathf.Max(0, Fever - 1);

        var rarity = RollRarity(false);
        var rune   = CreateZoneCore(elementId, rarity);
        _inventory?.AddToStaging(rune);
        _lastRune = rune;
        Fever = rarity == ItemRarity.Legendary ? 0 : Fever + 1;

        return new RefineryOutcome { Success = true, Rarity = rarity, Rune = rune, EventKind = RefineryEventKind.None };
    }

    // ── 내부 ──

    private ItemRarity RollRarity(bool heat)
    {
        var (rare, epic, leg) = Odds(Fever, heat, RoomPerk == RefineryPerk.Lucky);
        float r = Random.value;
        if (r < leg)        return ItemRarity.Legendary;
        if (r < leg + epic) return ItemRarity.Epic;
        return ItemRarity.Rare;
    }

    private static int AmountFor(ItemRarity rarity)
    {
        foreach (var t in Tiers) if (t.rarity == rarity) return t.amt;
        return Tiers[0].amt;
    }

    /// <summary>존핵 특수 룬 생성 — 속성 고정, 모양 가로2, 등급별 증폭치. 증폭 효과 적용은 P2.</summary>
    public static RuntimeItemData CreateZoneCore(string elementId, ItemRarity rarity)
    {
        var e   = ElementDef.GetById(elementId);
        int amt = AmountFor(rarity);
        string elemName = e != null ? e.Name : elementId;

        var rune = new RuntimeItemData
        {
            itemId      = $"special_zonecore_{elementId}_{rarity}".ToLower(),
            displayName = $"{elemName}의 존핵",
            rarity      = rarity,
            category    = ItemCategory.Charm,
            shapeId     = ZoneCoreShapeId,
            element     = elementId,
        };
        rune.effects.Add(new ItemEffectSlot
        {
            effectType  = "AmplifyZone",
            trigger     = "Always",
            value       = amt,
            description  = $"{elemName} 존에 놓으면 그 존의 시너지 효과 +{amt}%",
        });
        return rune;
    }

    private RefineryEventKind RollEvent(ItemRarity rarity)
    {
        float p = EventBaseRate + Fever * 0.02f;
        if (Random.value > p) return RefineryEventKind.None;

        // 낮은 등급일수록 재점화(반전) 비중↑ / 이미 최고면 재점화 제외
        List<RefineryEventKind> pool = rarity == ItemRarity.Rare
            ? new List<RefineryEventKind> { RefineryEventKind.Reignite, RefineryEventKind.Reignite,
                                            RefineryEventKind.Overheat, RefineryEventKind.Spark, RefineryEventKind.Twin }
            : rarity == ItemRarity.Epic
                ? new List<RefineryEventKind> { RefineryEventKind.Reignite, RefineryEventKind.Overheat,
                                                RefineryEventKind.Spark, RefineryEventKind.Twin }
                : new List<RefineryEventKind> { RefineryEventKind.Overheat, RefineryEventKind.Spark,
                                                RefineryEventKind.Twin, RefineryEventKind.Twin };
        return pool[Random.Range(0, pool.Count)];
    }

    private void ApplyEvent(RefineryEventKind ev, string elementId, ItemRarity rarity)
    {
        switch (ev)
        {
            case RefineryEventKind.Reignite: CanReforge = true; break;
            case RefineryEventKind.Overheat: NextHeat   = true; break;
            case RefineryEventKind.Spark:    NextFree   = true; break;
            case RefineryEventKind.Twin:
                // 쌍생 — 같은 존핵을 하나 더(보관함 여유 있을 때만)
                if (_inventory != null && !_inventory.IsStagingFull)
                    _inventory.AddToStaging(CreateZoneCore(elementId, rarity));
                break;
        }
    }
}

/// <summary>
/// 돌발 이벤트 4종. 결과 확정 직후 약 18%(피버로 상승) 확률로 하나가 뜬다.
/// 넷은 <b>서로 다른 종류의 기쁨</b>이라 UI에서도 구별되어야 한다 —
/// 재점화=되돌리기(가장 큼) / 과열=예고 / 불티=선물 / 쌍생=즉시 이득.
/// </summary>
public enum RefineryEventKind
{
    None = 0,
    Reignite,   // 재점화 — 지금 결과를 1회 무료로 다시 굴린다
    Overheat,   // 과열   — 다음 1회 상위 등급 확률 2배
    Spark,      // 불티   — 다음 1회 무료
    Twin,       // 쌍생   — 같은 존핵을 하나 더
}

/// <summary>
/// 정제소 '방' 특전 — 방은 "정제할 수 있는 곳"이 아니라 <b>좋은 조건으로 하는 곳</b>(설계 §2.7).
/// 상시 탭(룬판 버튼)에서는 항상 None.
/// </summary>
public enum RefineryPerk
{
    None = 0,
    Discount,    // 원석 비용 30% 할인
    Lucky,       // Legendary 확률 2배
    FirstFree,   // 첫 돌리기 무료
}

/// <summary>정제소 돌리기 결과.</summary>
public struct RefineryOutcome
{
    public bool            Success;
    public string          FailReason;
    public ItemRarity      Rarity;
    public RuntimeItemData Rune;
    public RefineryEventKind EventKind;   // 돌발 이벤트(없으면 None)

    public static RefineryOutcome Fail(string reason) => new RefineryOutcome { Success = false, FailReason = reason };
}
