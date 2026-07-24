using System;
using UnityEngine;

/// <summary>
/// 심연의 행상(신규 상점) 상품 카테고리. 스킨 배열 순서와 1:1 — 0=버프 1=룬 2=재료 3=포션.
/// 색: 버프=초록 · 룬=보라 · 재료=주황 · 포션=빨강 (완성본 0_상점_260724 기준).
/// </summary>
public enum ShopProductCategory
{
    Buff     = 0,
    Rune     = 1,
    Material = 2,
    Potion   = 3,
}

/// <summary>
/// 심연의 행상 상품 하나. "확신을 파는 곳" — 무작위 드롭과 달리 <b>보고 고르는</b> 결정.
///
/// 표시(카테고리·이름·효과/수치·가격)와 <b>구매 적용</b>(버프→버프창, 룬→보관함, 재료→연료, 포션→퀵슬롯)을
/// 한 객체에 담는다. 적용은 <see cref="Grant"/> 델리게이트로 런 시스템에 위임한다(컨트롤러가 골드 차감 후 호출).
/// </summary>
public sealed class ShopProduct
{
    public ShopProductCategory Category { get; }
    public string DisplayName { get; }
    /// <summary>효과 요약 한 줄(카드 표시용, 예: "공격력 강화(상급)").</summary>
    public string EffectText { get; }
    /// <summary>고른 물건 상세의 부가 설명(적용/고민 등, 없으면 빈 문자열).</summary>
    public string DetailText { get; }
    public int Price { get; }
    public ItemRarity Rarity { get; }
    public Sprite Icon { get; }

    /// <summary>구매 확정 시 실제 지급/적용. 컨트롤러가 골드 차감 뒤 호출. 성공 시 true.</summary>
    public Func<GameRunSession, bool> Grant { get; }

    /// <summary>1회성 상품이 팔렸는지(재료/포션 등 수량형은 별도 남은수량으로 관리 가능).</summary>
    public bool Sold { get; set; }

    public ShopProduct(ShopProductCategory category, string displayName, string effectText,
                       string detailText, int price, ItemRarity rarity, Sprite icon,
                       Func<GameRunSession, bool> grant)
    {
        Category    = category;
        DisplayName = displayName;
        EffectText  = effectText;
        DetailText  = detailText ?? string.Empty;
        Price       = price;
        Rarity      = rarity;
        Icon        = icon;
        Grant       = grant;
    }

    public bool Purchasable => !Sold && Grant != null;

    /// <summary>카테고리 한글 라벨(고른 물건 상세 헤더용).</summary>
    public string CategoryLabel => Category switch
    {
        ShopProductCategory.Buff     => "버프",
        ShopProductCategory.Rune     => "룬",
        ShopProductCategory.Material  => "재료",
        ShopProductCategory.Potion   => "포션",
        _                            => "",
    };
}
