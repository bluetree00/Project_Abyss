using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 상점 UI 공용 스타일/빌더 + 교체 훅.
/// 다크 판타지·유물(RelicFairy) 톤의 팔레트·여백·등급 위계를 한 곳에서 관리한다.
///
/// 아트/사운드 교체 슬롯:
///  - FrameSpriteKey / PanelSpriteKey / CoinSpriteKey: Addressable 스프라이트 키. 비우면 단색 폴백.
///  - Sfx: 사운드 훅(Action&lt;string&gt;). 외부(SoundManager 등)가 할당하면 구매/거부/리롤 시 호출.
/// 코드 수정 없이 이 필드만 채우면 실제 에셋/사운드로 승격된다.
/// </summary>
public static class ShopUIStyle
{
    // ── 교체 훅 (기본 비어있음) ─────────────────────────────
    public static string PanelSpriteKey = "";   // 윈도우 배경 9-slice
    public static string FrameSpriteKey = "";    // 카드 테두리 9-slice
    public static string CoinSpriteKey  = "";    // 골드 코인 아이콘
    public static Action<string> Sfx;            // "shop_open"/"shop_buy"/"shop_reject"/"shop_reroll"

    public static void PlaySfx(string key) => Sfx?.Invoke(key);

    // ── 팔레트 (다크 판타지/유물) ───────────────────────────
    public static readonly Color Veil        = new(0f, 0f, 0f, 0.80f);
    public static readonly Color WindowFill   = new(0.055f, 0.050f, 0.085f, 0.992f);
    public static readonly Color WindowBorder = new(0.52f, 0.40f, 0.20f, 1f);   // aged bronze
    public static readonly Color BandFill     = new(0.10f, 0.085f, 0.135f, 1f);
    public static readonly Color BronzeLine   = new(0.55f, 0.42f, 0.22f, 0.9f);
    public static readonly Color PortraitBg   = new(0.04f, 0.04f, 0.07f, 1f);

    public static readonly Color CardFill     = new(0.105f, 0.095f, 0.145f, 1f);
    public static readonly Color CardBorder   = new(0.24f, 0.22f, 0.30f, 1f);
    public static readonly Color IconBg       = new(0.045f, 0.045f, 0.075f, 1f);

    public static readonly Color GoldPillBg   = new(0.16f, 0.13f, 0.06f, 1f);
    public static readonly Color Gold         = new(1f, 0.82f, 0.28f, 1f);
    public static readonly Color TextPrimary  = new(0.93f, 0.91f, 0.85f, 1f);   // parchment
    public static readonly Color TextDim      = new(0.62f, 0.60f, 0.64f, 1f);
    public static readonly Color RejectRed    = new(1f, 0.32f, 0.30f, 1f);

    public static readonly Color BuyFill      = new(0.42f, 0.32f, 0.14f, 1f);   // bronze button
    public static readonly Color BuyHover     = new(0.58f, 0.45f, 0.20f, 1f);
    public static readonly Color BuyDisabled  = new(0.18f, 0.17f, 0.20f, 1f);

    public static readonly Color SoldVeil     = new(0.02f, 0.02f, 0.03f, 0.74f);
    public static readonly Color SoldStamp    = new(0.85f, 0.16f, 0.16f, 0.95f);
    public static readonly Color OwnedStamp   = new(0.45f, 0.62f, 0.85f, 0.95f);

    // ── 등급 위계 ───────────────────────────────────────────
    public static Color Rarity(ItemRarity r) => RarityColorTable.Get(r);

    /// <summary>등급 글로우(아이콘 뒤 후광). 낮은 알파.</summary>
    public static Color RarityGlow(ItemRarity r)
    {
        var c = RarityColorTable.Get(r);
        c.a = r switch
        {
            ItemRarity.Legendary => 0.42f,
            ItemRarity.Epic      => 0.34f,
            ItemRarity.Rare      => 0.26f,
            _                    => 0.14f,
        };
        return c;
    }

    public static string RarityLabel(ItemRarity r) => r switch
    {
        ItemRarity.Rare      => "RARE",
        ItemRarity.Epic      => "EPIC",
        ItemRarity.Legendary => "LEGENDARY",
        _                    => "COMMON",
    };

    /// <summary>전설일수록 두꺼운 테두리(시각 위계).</summary>
    public static float RarityBorder(ItemRarity r) => r switch
    {
        ItemRarity.Legendary => 4f,
        ItemRarity.Epic      => 3f,
        ItemRarity.Rare      => 2.5f,
        _                    => 2f,
    };

    // ── 빌더 ────────────────────────────────────────────────

    public static GameObject MakeRect(Transform parent, string name, params Type[] comps)
    {
        var all = new Type[comps.Length + 1];
        all[0] = typeof(RectTransform);
        Array.Copy(comps, 0, all, 1, comps.Length);
        var go = new GameObject(name, all);
        go.transform.SetParent(parent, false);
        return go;
    }

    public static Image MakeImage(Transform parent, string name, Color color, bool raycast = false)
    {
        var go = MakeRect(parent, name, typeof(Image));
        var img = go.GetComponent<Image>();
        img.color = color;
        img.raycastTarget = raycast;
        return img;
    }

    /// <summary>테두리 프레임: 바깥(테두리색) + 안쪽 인셋(채움색). 안쪽 Image를 반환.</summary>
    public static Image MakeFrame(Transform parent, string name, Color border, Color fill, float thickness, bool raycast = false)
    {
        var outer = MakeImage(parent, name, border, raycast);
        var inner = MakeImage(outer.transform, "Fill", fill, raycast);
        Stretch(inner.rectTransform, thickness);
        return inner; // 자식은 inner에 붙인다
    }

    public static TMP_Text MakeText(Transform parent, string name, float size, FontStyles style,
                                    TextAlignmentOptions align, Color color)
    {
        var go = MakeRect(parent, name);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.alignment = align;
        tmp.color = color;
        tmp.raycastTarget = false;
        return tmp;
    }

    /// <summary>골드 코인 글리프. 스프라이트 키 있으면 Image, 없으면 ● TMP 폴백.</summary>
    public static GameObject MakeCoin(Transform parent, float size)
    {
        if (!string.IsNullOrEmpty(CoinSpriteKey))
        {
            var img = MakeImage(parent, "Coin", Color.white);
            img.preserveAspect = true;
            var le = img.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = size; le.preferredHeight = size;
            // 스프라이트는 호출측에서 비동기 로드해 주입(현재는 단색 폴백 비표시 방지용 흰색)
            img.color = Gold;
            return img.gameObject;
        }
        var t = MakeText(parent, "Coin", size, FontStyles.Bold, TextAlignmentOptions.Center, Gold);
        t.text = "●";
        var le2 = t.gameObject.AddComponent<LayoutElement>();
        le2.preferredWidth = size; le2.preferredHeight = size;
        return t.gameObject;
    }

    public static void Stretch(RectTransform rt, float pad = 0f)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(pad, pad);
        rt.offsetMax = new Vector2(-pad, -pad);
    }

    public static void StretchOffsets(RectTransform rt, float left, float bottom, float right, float top)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(left, bottom);
        rt.offsetMax = new Vector2(-right, -top);
    }

    public static void Anchor(RectTransform rt, Vector2 min, Vector2 max, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = min;
        rt.anchorMax = max;
        rt.pivot = pivot;
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
    }

    /// <summary>
    /// 코드로 그린 색 박스에 디자이너 아트를 얹는다. <b>스프라이트가 없으면 아무것도 하지 않는다</b> —
    /// 아트가 한 장도 없는 상태에서도 화면이 지금과 똑같이 보이는 것이 이 레이어의 계약이다.
    ///
    /// tint를 주면 회색조 아트에 색을 곱한다(속성색이 걸리는 요소: 존 타일·응축 시퀀스 등).
    /// 주지 않으면 흰색 = 아트 그대로.
    /// </summary>
    public static void Skin(Image img, Sprite sprite, bool sliced = false, Color? tint = null)
    {
        if (img == null || sprite == null) return;

        img.sprite = sprite;
        img.type   = sliced ? Image.Type.Sliced : Image.Type.Simple;
        img.color  = tint ?? Color.white;
    }
}
