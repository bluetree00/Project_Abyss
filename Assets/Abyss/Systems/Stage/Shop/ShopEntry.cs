using System;
using System.Collections.Generic;

/// <summary>
/// 뒤끝 SHOP_PRICE_DATA 차트 1행 = 상점 후보 1개.
/// CSV 컬럼명과 1:1 매칭하기 위해 public 필드 사용 (다른 *Entry 클래스와 동일 패턴).
/// </summary>
[Serializable]
public class ShopEntry
{
    public int    index;          // 1101, 2101 등 (정렬/디버그용)
    public string shop_entry_id;  // PK
    public string category;       // "item" | "weapon"
    public string target_id;      // ItemSO.itemId 또는 EquipmentEntry.weapon_id
    public int    price_override; // 0이면 SO 기본가, >0이면 강제값
    public int    weight;         // 추첨 가중치
    public int    stat_version;
}

[Serializable]
public class ShopEntryCollection
{
    public List<ShopEntry> entries;
}
