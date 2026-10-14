using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 등급 확률 막대 — 룬 선택 팝업 · 정제소 공용.
///
/// 10-10 톤 v2(사용자 「확률 UI가 부실하다 — 얇고 간단하면서도 모던하고 깔끔하게」 · 「진행될수록 오르는 확률이 UI에도 반영」):
///  · 머리 한 줄 — 왼쪽 「등급 확률」, 오른쪽 금빛으로 무엇이 등급을 올렸나(「Ch3 +2 · 해방기 +1 · 정예」, <see cref="SetBoost"/>).
///  · 등급색 구간을 이어 붙인 얇은 누적 막대 한 줄 — 비율이 길이로 바로 읽힌다.
///  · 아래 등급별 이름 + %. 제단에서 아직 안 연 등급은 흐리게 「잠김」. 과열(정제소 다음 1회 상위 2배)은 상위 두 등급을 금빛으로.
/// 두 팝업 프리팹에 옛 막대 3줄(L · T · P)이 구워져 있다 — 처음 갱신 때 옛 자식을 걷고 그 자리(같은 크기)에 새로 짓는다.
/// 10-11 v2(비교 리서치 C §2) — 넓은 판(정제소)만: 등급 이름 17 · % 24 · 머리 15(영문 이름 유지 · 크기만), 테는 1px 금 30%(눈금 없음).
/// 좁은 판(룬 선택 236×78)은 예전 그대로다.
/// </summary>
public sealed class OddsBarView : MonoBehaviour
{
    // ── Constants ──
    private const int   Tiers   = 4;   // 0=Common 1=Rare 2=Epic 3=Legendary
    private const float PadX    = 12f;
    private const float SegGap  = 1f;  // 구간 사이 틈 — 바탕이 비쳐 칸이 갈린다

    private static readonly ItemRarity[] TierRarity =
        { ItemRarity.Common, ItemRarity.Rare, ItemRarity.Epic, ItemRarity.Legendary };
    private static readonly string[] TierName = { "Common", "Rare", "Epic", "Legend" };   // 룬 팝업 막대는 칸이 53px — 「Legendary」는 넘친다(카드 · 상점은 Legendary)
    private static readonly Color TrackColor = new(0.03f, 0.03f, 0.05f, 0.67f);

    // ── Private ──
    private bool _built;
    private bool _showCommon;
    private string _boost = string.Empty;
    private readonly float[] _p = new float[Tiers];
    private bool _heated;
    private float _padX = PadX;   // 넓은 판은 창 맞춤 배율만큼 커진다(LayoutColumns도 같은 값)

    private RectTransform _track;
    private readonly Image[]    _seg  = new Image[Tiers];
    private readonly TMP_Text[] _name = new TMP_Text[Tiers];
    private readonly TMP_Text[] _pct  = new TMP_Text[Tiers];
    private readonly RectTransform[] _col = new RectTransform[Tiers];
    private TMP_Text _boostText;

    // ── Properties ──
    /// <summary>Common 칸도 보이는가 — 룬 선택(방 보상은 Common부터)은 켜고, 정제소(Rare부터)는 끈다.</summary>
    public bool ShowCommon
    {
        get => _showCommon;
        set { if (_showCommon == value) return; _showCommon = value; if (_built) { LayoutColumns(); Apply(); } }
    }

    /// <summary>
    /// 창 맞춤 배율 — 판이 비율 앵커로 창을 따라 커졌으면(정제소 ×1.2 안팎) 넓은 판의 글자 · 간격도 같은 배율로 짓는다.
    /// 처음 지을 때(첫 <see cref="SetOdds"/>)만 쓴다. 좁은 판(룬 선택)은 쓰지 않는다.
    /// </summary>
    public float LayoutScale { get; set; } = 1f;

    // ── Public Methods ──

    public static OddsBarView Create(Transform parent, Vector2 anchorMin, Vector2 anchorMax,
                                     Vector2 pivot, Vector2 pos, Vector2 size)
    {
        var root = ShopUIStyle.MakeImage(parent, "OddsBar", UITheme.Thin.GlassFill).rectTransform;
        ShopUIStyle.Anchor(root, anchorMin, anchorMax, pivot, pos, size);
        var view = root.gameObject.AddComponent<OddsBarView>();
        view.SetOdds(1f, 0f, 0f, heated: false);
        return view;
    }

    /// <summary>확률 갱신(0~1, Common = 나머지). heated = 과열(다음 1회 상위 등급 2배)이면 상위 두 등급을 금빛으로.</summary>
    public void SetOdds(float rare, float epic, float legendary, bool heated)
    {
        _p[1] = Mathf.Clamp01(rare);
        _p[2] = Mathf.Clamp01(epic);
        _p[3] = Mathf.Clamp01(legendary);
        _p[0] = Mathf.Clamp01(1f - _p[1] - _p[2] - _p[3]);
        _heated = heated;
        EnsureBuilt();
        Apply();
    }

    /// <summary>머리 오른쪽 한 줄 — 등급을 올린 것(챕터 · 시기 · 정예). 빈 문자열이면 비운다.</summary>
    public void SetBoost(string text)
    {
        _boost = text ?? string.Empty;
        if (_boostText != null) _boostText.text = _boost;
    }

    // ── Private Methods ──

    /// <summary>한 번 — 옛(구운) 자식을 걷고 얇은 막대를 짓는다. 크기는 지금 자리 그대로.</summary>
    private void EnsureBuilt()
    {
        if (_built) return;
        _built = true;

        var root = (RectTransform)transform;
        for (int i = root.childCount - 1; i >= 0; i--) Destroy(root.GetChild(i).gameObject);
        if (TryGetComponent<Image>(out var bg))
        {
            bg.sprite = null;
            bg.type   = Image.Type.Simple;
            bg.color  = UITheme.Thin.GlassFill;
        }
        var size  = root.rect.size;
        bool roomy = size.y >= 110f;   // 정제소(150) · 룬 선택(78)
        // 넓은 판(정제소) 테는 1px 금 30% 한 겹(10-11 v2) — 회색 선 + 노란 꺾쇠가 확률판을 장식처럼 보이게 했다.
        UIThinBox.Create(root, "ThinFrame", Vector2.zero, Vector2.one, 0f,
                         roomy ? UITheme.Thin.LineGold : UITheme.Thin.InkA(0.30f),
                         roomy ? Color.clear : UITheme.Thin.WithA(UITheme.Thin.Gold, 0.7f), Color.clear);

        // 넓은 판은 창 맞춤 배율(s)을 글자 · 간격에 곱한다 — 판만 커지고 글자는 1배라 「Rare」가 화면에서 가장 작은 글(잉크 9.8)이었다.
        float s = roomy ? Mathf.Max(1f, LayoutScale) : 1f;
        _padX = PadX * s;
        // 좁은 자리(룬 선택 236×78)는 간격을 줄이고 숫자를 키운다 — 10-10 실측 사진에서 % 글자가 작았다.
        float titleY = (roomy ? 14f : 6f) * s, titleH = (roomy ? 20f : 16f) * s;
        float barY   = titleY + titleH + (roomy ? 12f : 5f) * s, barH = (roomy ? 8f : 7f) * s;
        float colY   = barY + barH + (roomy ? 14f : 5f) * s;
        float nameH  = (roomy ? 22f : 14f) * s, pctH = (roomy ? 32f : 22f) * s;

        var title = Text(root, "Title", roomy ? 15f * s : 13f, UITheme.Thin.InkA(0.7f), TextAlignmentOptions.MidlineLeft);
        PlaceTop(title.rectTransform, _padX, titleY, size.x * 0.45f, titleH);
        title.text = "등급 확률";

        _boostText = Text(root, "Boost", roomy ? 15f * s : 12f, UITheme.Thin.Gold, TextAlignmentOptions.MidlineRight);
        PlaceTop(_boostText.rectTransform, size.x * 0.40f, titleY, size.x * 0.60f - _padX, titleH);
        _boostText.text = _boost;

        _track = ShopUIStyle.MakeImage(root, "Track", TrackColor).rectTransform;
        PlaceTop(_track, _padX, barY, size.x - _padX * 2f, barH);
        for (int i = 0; i < Tiers; i++)
        {
            _seg[i] = ShopUIStyle.MakeImage(_track, $"Seg{i}", ShopUIStyle.Rarity(TierRarity[i]));
            _seg[i].raycastTarget = false;
        }

        for (int i = 0; i < Tiers; i++)
        {
            var col = ShopUIStyle.MakeRect(root, $"Col{i}").GetComponent<RectTransform>();
            col.anchorMin = col.anchorMax = new Vector2(0f, 1f);
            col.pivot     = new Vector2(0f, 1f);
            col.sizeDelta = new Vector2(0f, nameH + pctH);
            col.anchoredPosition = new Vector2(0f, -colY);
            _col[i] = col;

            _name[i] = Text(col, "Name", roomy ? 17f * s : 13f, ShopUIStyle.Rarity(TierRarity[i]), TextAlignmentOptions.BottomLeft);
            Stretch(_name[i].rectTransform, 1f - nameH / (nameH + pctH), 1f);
            _name[i].text = TierName[i];
            _pct[i] = Text(col, "Pct", roomy ? 24f * s : 20f, UITheme.Thin.Ink, TextAlignmentOptions.TopLeft);
            Stretch(_pct[i].rectTransform, 0f, 1f - nameH / (nameH + pctH));
        }
        LayoutColumns();
    }

    /// <summary>보이는 등급 수에 맞춰 칸을 고르게 나눈다(Common을 끄면 3칸).</summary>
    private void LayoutColumns()
    {
        var root = (RectTransform)transform;
        int first = _showCommon ? 0 : 1, n = Tiers - first;
        float w = (root.rect.width - _padX * 2f) / n;
        for (int i = 0; i < Tiers; i++)
        {
            if (_col[i] == null) continue;
            bool on = i >= first;
            _col[i].gameObject.SetActive(on);
            if (!on) continue;
            _col[i].sizeDelta        = new Vector2(w, _col[i].sizeDelta.y);
            _col[i].anchoredPosition = new Vector2(_padX + (i - first) * w, _col[i].anchoredPosition.y);
        }
    }

    private void Apply()
    {
        if (_track == null) return;
        bool epicLocked = !MemoryAltarService.IsEpicUnlocked;
        bool legLocked  = !MemoryAltarService.IsLegendaryUnlocked;

        int first = _showCommon ? 0 : 1;
        float shown = 0f;
        for (int i = first; i < Tiers; i++) shown += _p[i];
        if (shown <= 0f) shown = 1f;

        float x = 0f;
        for (int i = 0; i < Tiers; i++)
        {
            bool on = i >= first && _p[i] > 0f;
            float f = on ? _p[i] / shown : 0f;
            var rt = _seg[i].rectTransform;
            rt.anchorMin = new Vector2(x, 0f);
            rt.anchorMax = new Vector2(x + f, 1f);
            rt.offsetMin = new Vector2(x > 0f ? SegGap : 0f, 0f);
            rt.offsetMax = Vector2.zero;
            _seg[i].enabled = on;
            var c = ShopUIStyle.Rarity(TierRarity[i]);
            _seg[i].color = _heated && i >= 2 ? Color.Lerp(c, UITheme.Thin.Gold, 0.35f) : c;
            x += f;

            bool locked = (i == 2 && epicLocked) || (i == 3 && legLocked);
            if (_name[i] != null)
                _name[i].color = locked ? UITheme.Thin.InkA(0.35f) : ShopUIStyle.Rarity(TierRarity[i]);
            if (_pct[i] != null)
            {
                _pct[i].text  = locked ? "잠김" : Percent(_p[i]);
                _pct[i].color = locked ? UITheme.Thin.InkA(0.35f)
                              : _heated && i >= 2 ? UITheme.Thin.Gold
                              : UITheme.Thin.Ink;
            }
        }
    }

    /// <summary>정수 %, 0보다 크고 1% 미만이면 「&lt;1%」.</summary>
    private static string Percent(float p)
    {
        if (p <= 0f) return "0%";
        int v = Mathf.RoundToInt(p * 100f);
        return v < 1 ? "<1%" : v + "%";
    }

    private static TMP_Text Text(Transform parent, string name, float size, Color color, TextAlignmentOptions align)
    {
        var t = ShopUIStyle.MakeText(parent, name, size, FontStyles.Normal, align, color);
        UITheme.StyleThinText(t, UIWeight.Medium, size, color, shadow: true);
        t.alignment        = align;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode     = TextOverflowModes.Overflow;
        t.raycastTarget    = false;
        return t;
    }

    /// <summary>위에서 y만큼 내려온 자리(왼쪽 위 기준).</summary>
    private static void PlaceTop(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot            = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta        = new Vector2(w, h);
    }

    private static void Stretch(RectTransform rt, float yMin, float yMax)
    {
        rt.anchorMin = new Vector2(0f, yMin);
        rt.anchorMax = new Vector2(1f, yMax);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }
}
