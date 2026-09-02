using System;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 상점·재련소·정제소 UI 공용 스타일/빌더.
/// 다크 판타지·유물(RelicFairy) 톤의 팔레트·여백·등급 위계를 한 곳에서 관리한다.
///
/// 아트 교체는 화면별 SkinSO(<see cref="ShopSkinSO"/> 등)가 담당하고, 여기서는
/// <see cref="Skin"/>이 그 스프라이트를 색 박스 위에 얹는 일만 한다.
/// 사운드는 <see cref="Sfx"/> 훅으로 교체한다.
/// </summary>
public static class ShopUIStyle
{
    /// <summary>
    /// 상점·재련소 SFX 훅. <b>기본 라우팅이 걸려 있다</b> — 예전엔 이 필드에 대입하는 코드가
    /// 프로젝트 어디에도 없어서, <c>PlaySfx</c>를 부르는 22곳(상점 구매·거부·리롤, 재련소 성공·잭팟·실패)이
    /// <b>한 번도 소리를 낸 적이 없었다</b>. 외부에서 대입하면 그쪽이 우선한다.
    /// </summary>
    public static Action<string> Sfx = DefaultSfx;

    public static void PlaySfx(string key) => Sfx?.Invoke(key);

    /// <summary>
    /// 기본 라우팅 — 전용 클립이 아직 없으므로 기존 UI 클립 1종을 <b>피치·볼륨으로 구분</b>해 쓴다.
    /// (성공은 올라가고, 거부·실패는 내려간다.) 전용 오디오가 들어오면 여기 키만 갈아끼우면 된다.
    /// </summary>
    private static void DefaultSfx(string key)
    {
        var sound = Managers.Sound;
        if (sound == null || string.IsNullOrEmpty(key)) return;

        (float volume, float pitch) = key switch
        {
            "crucible_jackpot" => (1.00f, 1.45f),
            "crucible_success" => (0.85f, 1.20f),
            "enhance_success"  => (0.85f, 1.20f),
            "shop_buy"         => (0.80f, 1.10f),
            "shop_reroll"      => (0.70f, 1.25f),
            "shop_open"        => (0.60f, 1.00f),
            "shop_close"       => (0.55f, 0.90f),
            "crucible_fail"    => (0.80f, 0.65f),
            "shop_reject"      => (0.65f, 0.70f),
            _                  => (0.65f, 1.00f),
        };

        sound.PlayUiAsync(SoundKey.Sfx.UiButton, volume, pitch).Forget();
    }

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
    public static readonly Color Gold         = UIPalette.Gold;
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
    /// <summary>
    /// 자손 중 이름이 같은 첫 오브젝트를 찾는다(비활성 포함).
    ///
    /// <para><b>왜 고정 경로를 쓰지 않는가</b> — 구워진 팝업을 다시 잇는 코드가
    /// <c>transform.Find("Window/ConfirmBtn")</c>처럼 경로를 박아 뒀는데,
    /// <see cref="MakeFrame"/>이 테두리(outer)와 채움(Fill) 두 겹을 만들기 때문에
    /// 실제 계층은 <c>Window/Fill/ConfirmBtn</c>로 한 단 더 깊었다. 경로가 어긋나면 <c>null</c>이
    /// 조용히 돌아오고 <b>버튼에 리스너가 안 붙은 채로 화면이 뜬다</b> —
    /// 눌러도 아무 일이 없어 "고장난 버튼"으로 보인다.</para>
    ///
    /// <para>이름은 빌더가 정하는 고유값이라, 깊이가 바뀌어도 이름으로 찾으면 안 깨진다.</para>
    /// </summary>
    public static Transform FindDeep(Transform root, string name)
    {
        if (root == null || string.IsNullOrEmpty(name)) return null;
        if (root.name == name) return root;

        for (int i = 0; i < root.childCount; i++)
        {
            var hit = FindDeep(root.GetChild(i), name);
            if (hit != null) return hit;
        }
        return null;
    }

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

        // 글자가 <b>판 밖으로 나가지 않게</b> 하는 안전망.
        // TMP 기본 넘침 설정은 상자를 넘는 글자를 잘라내지 않고 그대로 <b>바깥에 그린다</b> —
        // 그래서 긴 한글 이름이나 좁은 배지에서 글자가 배경을 벗어나 떠 있었다.
        // 최대를 설계 크기로 묶으므로 <b>커지지는 않고</b>, 안 들어갈 때만 줄어든다.
        tmp.enableAutoSizing = true;
        tmp.fontSizeMax = size;
        tmp.fontSizeMin = Mathf.Max(9f, size * 0.55f);
        return tmp;
    }

    /// <summary>
    /// 코드로 만든 버튼에 <b>눌림 연출</b>을 건다.
    ///
    /// 런타임 <c>AddComponent&lt;Button&gt;</c>에는 에디터의 Reset이 돌지 않아 <c>targetGraphic</c>이 비어 있다 —
    /// 그 상태에선 ColorBlock을 아무리 채워도 색이 한 번도 바뀌지 않는다(그래서 코드 생성 버튼들이
    /// 눌러도 아무 반응이 없었다). 그래픽을 여기서 함께 물려 둔다.
    ///
    /// 틴트는 <c>CanvasRenderer</c> 색이라 <c>Image.color</c> 위에 <b>곱해진다</b> — 아트를 얹은 버튼이나
    /// 상태색을 손수 칠하는 버튼(정제소 Tint 등)에 걸어도 평상시 모습은 그대로다.
    /// </summary>
    public static void ApplyButtonColors(Button btn, Graphic target = null)
    {
        if (btn == null) return;

        var g = target != null ? target : btn.GetComponent<Graphic>();
        if (g == null) return;   // 칠할 대상이 없으면 전환을 켜 봐야 의미가 없다

        btn.targetGraphic = g;
        btn.transition    = Selectable.Transition.ColorTint;

        var cb = btn.colors;
        cb.normalColor      = Color.white;
        cb.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
        cb.pressedColor     = new Color(0.85f, 0.85f, 0.85f, 1f);
        cb.disabledColor    = new Color(1f, 1f, 1f, 0.45f);   // 곱셈이라 알파로 죽인다(아트 버튼도 같은 규약)
        cb.fadeDuration     = 0.08f;
        btn.colors = cb;
    }

    public static void Stretch(RectTransform rt, float pad = 0f)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(pad, pad);
        rt.offsetMax = new Vector2(-pad, -pad);
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
