using System.Collections.Generic;

/// <summary>
/// 상점 정비소 서비스의 표시 메타 + 기본 가격. 순수 정의(동작은 ShopServiceRunner).
/// 가격/환율은 전부 여기 상수로 모아 밸런스 조정을 한 곳에서 한다.
/// </summary>
public static class ShopServiceCatalog
{
    // ── 밸런스 상수(튜닝 포인트) ─────────────────────────────
    public const int   RuneExtractBasePrice  = 40;    // 룬 제거 첫 가격
    public const int   RuneExtractPriceStep  = 30;    // 이후 1회당 가산(StS 방식)
    public const int   EssenceExchangeGold   = 100;   // 정수 교환: 골드
    public const int   EssenceExchangeYield  = 1;     // 정수 교환: 얻는 정수
    public const int   HpForGoldHpCost       = 20;    // HP→골드: 소모 HP(고정)
    public const int   HpForGoldGoldYield    = 60;    // HP→골드: 얻는 골드
    public const int   CovenantReforgePrice  = 80;    // 서약 재조립
    public const int   RuneRerollPrice       = 50;    // (정제소 이후) 정제 리롤권
    public const int   ExclusiveRunePrice    = 120;   // (정제소 이후) 상점 전용 룬

    public sealed class Def
    {
        public readonly ShopServiceKind Kind;
        public readonly string Id;
        public readonly string DisplayName;
        public readonly string Description;
        public readonly int    BasePrice;
        public readonly bool   Implemented;   // false면 진열은 하되 "준비 중"으로 표시(정제소 대기 등)

        public Def(ShopServiceKind kind, string id, string name, string desc, int price, bool implemented)
        {
            Kind = kind; Id = id; DisplayName = name; Description = desc;
            BasePrice = price; Implemented = implemented;
        }
    }

    private static readonly Def[] s_Defs =
    {
        new(ShopServiceKind.RuneExtraction,  "svc_rune_extract",  "룬 제거",
            "보드에 배치된 룬 1개를 뽑아 원석으로 되돌린다. 쓸 때마다 비싸진다.", RuneExtractBasePrice, false),
        new(ShopServiceKind.CovenantReforge, "svc_cov_reforge",   "서약 재조립",
            "보유한 서약의 부품(원인·결과)을 다시 뽑아 갈아끼운다.",              CovenantReforgePrice, false),
        new(ShopServiceKind.EssenceExchange, "svc_essence",       "정수로 환전",
            "남는 골드를 심연의 정수(각성 재화)로 바꾼다.",                        EssenceExchangeGold,  true),
        new(ShopServiceKind.HpForGold,       "svc_hp_gold",       "미다스의 값",
            "체력의 일부를 골드로 바꾼다. 무리하면 다음 전투가 위험하다.",         HpForGoldHpCost,      true),
        new(ShopServiceKind.RuneReroll,      "svc_rune_reroll",   "정제 리롤권",
            "[준비 중] 정제소가 열리면 원하는 룬을 노려 다시 굴린다.",             RuneRerollPrice,      false),
        new(ShopServiceKind.ExclusiveRune,   "svc_exclusive_rune","상점 전용 룬",
            "[준비 중] 드롭에 없는 특별한 룬. 정제소 이후 공개.",                  ExclusiveRunePrice,   false),
    };

    public static IReadOnlyList<Def> All => s_Defs;

    public static Def Get(ShopServiceKind kind)
    {
        for (int i = 0; i < s_Defs.Length; i++)
            if (s_Defs[i].Kind == kind) return s_Defs[i];
        return null;
    }
}
