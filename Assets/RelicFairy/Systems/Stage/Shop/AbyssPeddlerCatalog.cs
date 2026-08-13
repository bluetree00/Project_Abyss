using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Cysharp.Threading.Tasks;
using LitJson;
using UnityEngine;

/// <summary>상점 판매 버프 1행(SHOP_BUFF_DATA.csv) — 효과별로 정리된 목록.</summary>
public sealed class ShopBuffRow
{
    public string buffId;
    public string buffName;
    public StatType statType;
    public float value;
    public bool isPercent;
    public int tier;
    public int price;
    public string description;
}

/// <summary>
/// 상점 전용 버프 표. Addressable TextAsset "SHOP_BUFF_DATA"(CSV)를 1회 로드해 캐싱한다.
/// 미등록/실패면 비어 있고 카탈로그가 그 카테고리를 건너뛴다(안전 폴백).
/// 런 중 방버프(BUFF_DATA)와 분리 — 상점은 "눈으로 고르는" 고정 목록이라 별도 표를 쓴다.
/// </summary>
public static class ShopBuffTable
{
    private const string Address = "SHOP_BUFF_DATA";
    private static readonly List<ShopBuffRow> _rows = new();
    private static bool _tried;

    public static IReadOnlyList<ShopBuffRow> Rows => _rows;
    public static bool IsLoaded => _rows.Count > 0;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { _rows.Clear(); _tried = false; }

    /// <summary>
    /// 앱 부트에서 1회 호출. <b>뒤끝 CDN 차트 우선</b>(서버에서 관리) → 실패 시 Addressable CSV 폴백.
    /// 다른 차트(BUFF_DATA/ITEM_DATA)와 동일한 관례.
    /// </summary>
    public static async UniTask PreloadAsync()
    {
        if (_tried) return;
        _tried = true;

        // 1) 서버(뒤끝 CDN) 차트 — 운영 중 밸런스 수정은 여기서
        try
        {
            _rows.Clear();
            int loaded = ChartLoader.Load(Address, row =>
            {
                var r = ParseRow(row);
                if (r != null) _rows.Add(r);
            });
            if (loaded > 0 && _rows.Count > 0)
            {
                Debug.Log($"[ShopBuffTable] CDN 로드 {_rows.Count}종");
                return;
            }
        }
        catch (Exception e) { Debug.LogWarning($"[ShopBuffTable] CDN 예외: {e.Message}"); }

        // 2) 오프라인/미등록 폴백 — 프로젝트 내 CSV
        try
        {
            var csv = await Managers.AddressableManager.TryLoadAssetAsync<TextAsset>(Address);
            if (csv != null) { Parse(csv.text); Debug.Log("[ShopBuffTable] CDN 미사용 — 로컬 CSV 폴백"); }
            else Debug.Log($"[ShopBuffTable] '{Address}' 미등록 — 상점 버프 진열 생략(정상)");
        }
        catch (Exception e) { Debug.LogWarning($"[ShopBuffTable] 로드 예외: {e.Message}"); }
    }

    /// <summary>CDN 차트 1행 → ShopBuffRow. 컬럼명은 CSV 헤더와 동일해야 한다.</summary>
    private static ShopBuffRow ParseRow(JsonData row)
    {
        try
        {
            if (!Enum.TryParse<StatType>(row.TryGetString("stat_type"), true, out var st)) return null;
            string pct = row.TryGetString("is_percent");
            return new ShopBuffRow
            {
                buffId      = row.TryGetString("buff_id"),
                buffName    = row.TryGetString("buff_name"),
                statType    = st,
                value       = row.TryGetFloat("value"),
                isPercent   = pct == "true" || pct == "1",
                tier        = Mathf.Max(1, row.TryGetInt("tier")),
                price       = Mathf.Max(1, row.TryGetInt("price")),
                description = row.TryGetString("description"),
            };
        }
        catch { return null; }
    }

    private static void Parse(string text)
    {
        _rows.Clear();
        if (string.IsNullOrEmpty(text)) return;

        var lines = text.Split('\n');
        for (int i = 1; i < lines.Length; i++)   // 0행 = 헤더
        {
            var line = lines[i].Trim();
            if (string.IsNullOrEmpty(line)) continue;

            var c = line.Split(',');
            if (c.Length < 8) continue;
            if (!Enum.TryParse<StatType>(c[2].Trim(), true, out var st)) continue;

            _rows.Add(new ShopBuffRow
            {
                buffId      = c[0].Trim(),
                buffName    = c[1].Trim(),
                statType    = st,
                value       = ParseF(c[3]),
                isPercent   = ParseI(c[4]) != 0,
                tier        = Mathf.Max(1, ParseI(c[5])),
                price       = Mathf.Max(1, ParseI(c[6])),
                description = c[7].Trim(),
            });
        }
        Debug.Log($"[ShopBuffTable] 상점 버프 {_rows.Count}종 로드");
    }

    private static float ParseF(string s)
        => float.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f;
    private static int ParseI(string s)
        => int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
}

/// <summary>
/// 심연의 행상 진열 롤. "확신을 파는 곳" — 매 방문 무작위 구성 정규 상품(버프·룬·재료·포션) +
/// 전 상품 중 무작위 1개를 할인해 상단에 띄우는 <b>오늘의 특가</b>.
///
/// 가격·개수는 기본값(밸런스는 나중 CSV화). 지급은 <see cref="ShopProduct.Grant"/>로 런 시스템에 위임한다.
/// </summary>
public static class AbyssPeddlerCatalog
{
    /// <summary>매 방문 뽑는 품목 수. 이 중 1개가 오늘의 특가, 나머지가 정규 진열(2×3).</summary>
    public const int DrawCount    = 7;
    public const int ProductCount = DrawCount - 1;   // 정규 진열 6칸
    private const int BuffRunDuration = 99;          // 상점 버프 = 사실상 런 유지("유지")
    private const float SpecialDiscount = 0.5f;      // 특가 50% 할인

    public sealed class Result
    {
        public List<ShopProduct> Products = new();
        public ShopProduct Special;                   // 오늘의 특가(뽑은 7종 중 1개의 할인본)
        public int SpecialOriginalPrice;              // 취소선 표시용 원가
    }

    /// <summary>
    /// 진열을 새로 롤. 룬/포션/버프/재료 <b>풀에서 7종을 뽑고</b>, 그중 무작위 1개를 할인해
    /// 오늘의 특가로 올린다(나머지 6종이 정규 진열). 소스가 없는 카테고리는 자동으로 다른 풀이 메운다.
    /// </summary>
    public static Result Build(System.Random rng)
    {
        rng ??= new System.Random();
        var res = new Result();

        // 포션은 CSV 없이 <b>1칸 고정</b> — 언제 와도 회복 수단은 살 수 있어야 한다.
        var draw = new List<ShopProduct>(DrawCount);
        var potion = BuildPotion(rng);
        if (potion != null) draw.Add(potion);

        // 나머지는 룬(아이템 테이블) + 버프(상점 CSV) + 재료 풀에서 뽑는다.
        var pool = BuildCandidatePool(rng);
        if (pool.Count > 0)
        {
            Shuffle(pool, rng);
            for (int i = 0; draw.Count < DrawCount && i < DrawCount * 2; i++)
                draw.Add(pool[i % pool.Count]);
        }
        if (draw.Count == 0) return res;

        // 뽑은 7종 중 1개를 특가로
        int specialIdx = rng.Next(draw.Count);
        var pick = draw[specialIdx];
        res.SpecialOriginalPrice = pick.Price;
        int dealPrice = Mathf.Max(1, Mathf.RoundToInt(pick.Price * (1f - SpecialDiscount)));
        res.Special = new ShopProduct(pick.Category, pick.DisplayName, pick.EffectText,
            "전 상품 중 무작위로 골라진 오늘의 매물", dealPrice, pick.Rarity, pick.Icon, pick.Grant);

        for (int i = 0; i < draw.Count; i++)
            if (i != specialIdx) res.Products.Add(draw[i]);

        return res;
    }

    /// <summary>룬·버프·재료·포션 풀을 넉넉히 만든다(품목 다양성). 데이터 미로드 카테고리는 자동 제외.</summary>
    private static List<ShopProduct> BuildCandidatePool(System.Random rng)
    {
        var pool = new List<ShopProduct>(24);

        for (int i = 0; i < 6; i++) { var p = BuildBuff(rng);     if (p != null) pool.Add(p); }
        for (int i = 0; i < 6; i++) { var p = BuildRune(rng);     if (p != null) pool.Add(p); }
        for (int i = 0; i < 3; i++) { var p = BuildMaterial(rng); if (p != null) pool.Add(p); }
        for (int i = 0; i < 3; i++) { var p = BuildOre(rng);      if (p != null) pool.Add(p); }
        for (int i = 0; i < 3; i++) { var p = BuildPotion(rng);   if (p != null) pool.Add(p); }

        return pool;
    }

    private static void Shuffle(List<ShopProduct> list, System.Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    // ── 버프 ── 상점 전용 CSV(SHOP_BUFF_DATA, 효과별 정리) → StatModifier → RoomBuffHandler.AddBuff
    private static ShopProduct BuildBuff(System.Random rng)
    {
        var rows = ShopBuffTable.Rows;
        if (rows == null || rows.Count == 0) return null;

        var row = rows[rng.Next(rows.Count)];
        var mod = new StatModifier(row.statType, row.value);
        string gradeLabel = row.tier >= 3 ? "상급" : row.tier == 2 ? "중급" : "하급";
        string valStr = row.isPercent ? $"+{row.value:0.##}%" : $"+{row.value:0.##}";

        return new ShopProduct(ShopProductCategory.Buff, row.buffName,
            string.IsNullOrEmpty(row.description) ? $"{StatLabel(row.statType)} 강화({gradeLabel})" : row.description,
            $"적용  런 유지 버프(버프창) · 값 {valStr}",
            row.price, TierRarity(row.tier), EffectIconRegistry.GetSprite(IconBuff),
            s =>
            {
                if (s?.BuffHandler == null) return false;
                s.BuffHandler.AddBuff(mod, BuffRunDuration, row.isPercent, false, row.buffId, row.tier);
                return true;
            });
    }

    // ── 룬 ── 아이템 테이블(ITEM_DATA)에서 shape_id>0 인 항목을 조회 → 보관함으로
    private static ShopProduct BuildRune(System.Random rng)
    {
        var itemData = Managers.ItemData;
        if (itemData == null || !itemData.IsInitialized) return null;

        var all = itemData.GetAllItems();
        if (all == null || all.Count == 0) return null;

        var runeIds = all.Where(kv => kv.Value != null && kv.Value.Count > 0 && kv.Value[0].shape_id > 0)
                         .Select(kv => kv.Key).ToList();
        if (runeIds.Count == 0) return null;

        string id = runeIds[rng.Next(runeIds.Count)];
        var entries = itemData.GetItem(id);
        if (entries == null || entries.Count == 0) return null;

        var meta = entries[0];
        var rarity = ParseRarity(meta.ResolvedRarity);
        string name = string.IsNullOrEmpty(meta.item_name) ? id : meta.item_name;
        string effect = string.IsNullOrEmpty(meta.description)
            ? $"{meta.effect_type} +{meta.value:0.##}"
            : meta.description;

        return new ShopProduct(ShopProductCategory.Rune, name, effect,
            "적용  구매 즉시 판에 배치", RarityPrice(rarity), rarity, EffectIconRegistry.GetSprite(IconRune),
            s =>
            {
                if (s?.ItemInventory == null) return false;
                var data = RuntimeItemData.FromServer(entries);
                if (data == null) return false;

                // 사자마자 바로 조합(배치)할 수 있게 룬판을 열어준다 — 룬 획득 보상과 같은 흐름.
                bool added = s.ItemInventory.AddToStaging(data);
                OpenGridForRune(data, added);
                return true;   // 보관함 만차여도 '보류'로 판이 들고 가므로 구매는 성립
            });
    }

    /// <summary>구매한 룬을 즉시 배치할 수 있도록 룬판을 연다(만차면 보류 아이템으로).</summary>
    private static void OpenGridForRune(RuntimeItemData data, bool added)
    {
        if (UI_GridPanel.Instance == null)
            Managers.UI?.ShowOverlayUI<UI_GridPanel>();
        if (UI_GridPanel.Instance == null) return;

        if (added) UI_GridPanel.Instance.ShowWithNewItem(data);
        else       UI_GridPanel.Instance.ShowWithPendingItem(data);
    }

    private static ItemRarity ParseRarity(string raw)
        => Enum.TryParse<ItemRarity>((raw ?? "").Trim(), true, out var r) ? r : ItemRarity.Common;

    // ── 재료 ── 강화재료(재련소 연료)
    private static ShopProduct BuildMaterial(System.Random rng)
    {
        int amt = 4 + rng.Next(0, 3);   // 4~6
        int price = 20 * amt;
        return new ShopProduct(ShopProductCategory.Material, $"강화재료 x{amt}",
            "재련소 무기강화 연료", "적용  강화재료 은행에 적립",
            price, ItemRarity.Common, EffectIconRegistry.GetSprite(IconMaterial),
            s => { s?.FuelBank?.Add(FuelKind.EnhanceMaterial, amt); return s?.FuelBank != null; });
    }

    // ── 포션 ── 체력 포션(퀵슬롯 소모품). PlayerRunState가 보유 수량을 들고 있다.
    private static ShopProduct BuildPotion(System.Random rng)
    {
        int amt = 1 + rng.Next(0, 2);   // 1~2
        int price = 60 * amt;
        return new ShopProduct(ShopProductCategory.Potion, $"체력 포션 x{amt}",
            "즉시 회복 소모품", "적용  퀵슬롯 포션 추가",
            price, ItemRarity.Common, EffectIconRegistry.GetSprite(IconPotion),
            s => { if (s?.PlayerState == null) return false; s.PlayerState.AddPotion(amt); return true; });
    }

    /// <summary>원석(정제소 연료) 상품 — 재료 카테고리의 다른 얼굴(완성본 특가 예시가 원석).</summary>
    private static ShopProduct BuildOre(System.Random rng)
    {
        int amt = 6 + rng.Next(0, 4);   // 6~9
        return new ShopProduct(ShopProductCategory.Material, $"원석 x{amt}",
            "정제소 룬 재련 연료", "적용  원석 은행에 적립",
            10 * amt, ItemRarity.Common, EffectIconRegistry.GetSprite(IconMaterial),
            s => { s?.FuelBank?.Add(FuelKind.RuneOre, amt); return s?.FuelBank != null; });
    }

    // ── 상품 아이콘 ──
    // EffectIconRegistry(IconKey → Sprite)를 그대로 쓴다 — 프로젝트의 아이콘 해석 단일 창구다.
    // 상점 전용 키를 쓰는 이유: heal/atk 같은 기존 어휘에 얹으면 HUD 버프칸 아이콘까지 같이 바뀐다.
    // 미등록 키는 조용히 색 토큰 플레이스홀더로 떨어진다(룬이 현재 그렇다 — 아트 재납품 예정).
    private const string IconPotion   = "shop_potion";     // 체력 물약 (확정 아트)
    private const string IconMaterial = "shop_material";   // 무기강화 재료 (임시 아트 — 원석과 공용)
    private const string IconBuff     = "shop_buff";       // 생명가호 (임시 아트 — 버프 전종 공용)
    private const string IconRune     = "shop_rune";       // 미납품 → 플레이스홀더

    // ── 헬퍼 ──
    private static ItemRarity TierRarity(int tier) => tier >= 3 ? ItemRarity.Epic : tier == 2 ? ItemRarity.Rare : ItemRarity.Common;

    private static int RarityPrice(ItemRarity r) => r switch
    {
        ItemRarity.Legendary => 320,
        ItemRarity.Epic      => 220,
        ItemRarity.Rare      => 140,
        _                    => 80,
    };

    private static string EffectSummary(ItemSO so)
    {
        if (so?.modifiers != null && so.modifiers.Count > 0)
        {
            var m = so.modifiers[0];
            return $"{StatLabel(m.Type)} +{m.Value:0.#}";
        }
        return so != null ? so.displayName : "";
    }

    private static string StatLabel(StatType t) => t switch
    {
        StatType.AttackPower => "공격력",
        StatType.Defense     => "방어력",
        StatType.MoveSpeed   => "이동속도",
        StatType.AttackSpeed => "공격속도",
        StatType.MaxHp       => "체력",
        _                    => t.ToString(),
    };
}
