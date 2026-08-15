using UnityEngine;

/// <summary>
/// 상점 진열 1칸의 표현-독립 데이터.
/// ShopRoomController가 등급 롤 결과(ShopEntry)를 표시용 메타와 함께 캐싱해 보관하고,
/// UI(UI_ShopPanel)가 이를 읽어 그린다.
///
/// 기존 월드 매대(ShopStallInteraction)의 _entry/_sold/_owned/_pending 상태를
/// 데이터 객체 1개로 옮긴 것 — 표현만 교체하고 데이터/계산 로직은 재사용한다.
/// </summary>
public sealed class ShopSlot
{
    /// <summary>추첨된 후보. null이면 빈 슬롯(풀 고갈) 또는 레거시 경로.</summary>
    public ShopEntry Entry { get; }

    /// <summary>레거시 경로(ShopCatalogSO 폴백) 전용. Entry 사용 시 null.</summary>
    public ShopItemSO LegacyItem { get; }

    public ShopCategory Category { get; }
    public int Price { get; }
    public string DisplayName { get; }
    public ItemRarity Rarity { get; }

    /// <summary>아이템 아이콘. 무기/조회 실패 시 null.</summary>
    public Sprite Icon { get; }

    /// <summary>한 줄 요약(등급·카테고리).</summary>
    public string Description { get; }

    /// <summary>구매 완료(되돌릴 수 없음).</summary>
    public bool Sold { get; set; }

    /// <summary>이미 보유 중(인벤토리/무기 매니저 기준 실시간). 구매 차단.</summary>
    public bool Owned { get; set; }

    /// <summary>비동기 구매 확정 대기(무기 교체 팝업 등). 이중 구매 차단.</summary>
    public bool Pending { get; set; }

    /// <summary>정비소 서비스 슬롯이면 해당 종류. 아이템/무기/포션 슬롯이면 null.</summary>
    public ShopServiceKind? Service { get; }

    /// <summary>서비스 진열이 '준비 중'(정제소 대기 등)이라 구매 불가일 때 true.</summary>
    public bool Locked { get; }

    /// <summary>지금 구매 가능한 상태인지.</summary>
    public bool Purchasable =>
        (Entry != null || LegacyItem != null || Service.HasValue) && !Sold && !Owned && !Pending && !Locked;

    public ShopSlot(ShopEntry entry, ShopItemSO legacyItem, ShopCategory category, int price,
                    string displayName, ItemRarity rarity, Sprite icon, string description,
                    ShopServiceKind? service = null, bool locked = false)
    {
        Entry = entry;
        LegacyItem = legacyItem;
        Category = category;
        Price = price;
        DisplayName = displayName;
        Rarity = rarity;
        Icon = icon;
        Description = description;
        Service = service;
        Locked = locked;
    }

    /// <summary>정비소 서비스 슬롯 생성(가격/이름은 ShopServiceRunner·Catalog에서 계산).</summary>
    public static ShopSlot ForService(ShopServiceKind kind, int price, string name, string desc, bool locked)
        => new ShopSlot(null, null, ShopCategory.Item, price, name, ItemRarity.Common, null, desc,
                        service: kind, locked: locked);

    /// <summary>풀 고갈로 채우지 못한 빈 슬롯.</summary>
    public static ShopSlot Empty(ShopCategory category)
        => new ShopSlot(null, null, category, 0, "비어있음", ItemRarity.Common, null, string.Empty) { Sold = true };
}
