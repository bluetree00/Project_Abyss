using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 효과 1줄 공통 위젯(표시 전용 레이어 D). [아이콘 Image] + [한글 라벨 + 수치(+트리거)]를
/// HorizontalLayout으로 묶은 재사용 행. 입력은 <see cref="EffectDisplay"/> + <see cref="EffectIconRegistry"/>뿐.
///
/// ■ 코드 절차 생성(프로젝트의 기존 동적 UI 패턴과 동일) — @UIRoot 프리팹 불필요.
/// ■ 등급/카테고리/IsRisk 색은 EffectDisplay에서 옴(스타일로 normal/risk 색 오버라이드 가능).
/// ■ 기존 화면별 수동 TMP 행 + C단계 들여쓰기 글루(EffectIconView)를 이 위젯으로 수렴.
/// </summary>
public sealed class EffectRowWidget : MonoBehaviour
{
    private Image _icon;
    private TMP_Text _label;
    private EffectRowStyle _style;

    public TMP_Text Label => _label;
    public Image Icon => _icon;

    /// <summary>부모 아래 빈 행 위젯 1개 생성(미바인드 상태).</summary>
    public static EffectRowWidget Create(Transform parent, in EffectRowStyle style)
    {
        var go = new GameObject("EffectRow", typeof(RectTransform));
        go.transform.SetParent(parent, false);

        // 부모 LayoutGroup의 childControl 설정과 무관하게 동작하도록:
        //  - 상단 가로 스트레치 + 명시 높이(childControl=0인 부모용, 기존 행 셋업과 동일)
        //  - 아래의 LayoutElement(childControl=1인 부모용)
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot     = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(0f, style.rowHeight);

        var widget = go.AddComponent<EffectRowWidget>();
        widget._style = style;

        var hlg = go.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = style.spacing;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;

        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = style.rowHeight;
        le.minHeight = style.rowHeight;

        // ── 아이콘 ──
        var iconGO = new GameObject("Icon", typeof(RectTransform), typeof(Image));
        iconGO.transform.SetParent(go.transform, false);
        var iconLE = iconGO.AddComponent<LayoutElement>();
        iconLE.preferredWidth  = style.iconSize;
        iconLE.minWidth        = style.iconSize;
        iconLE.preferredHeight = style.iconSize;
        widget._icon = iconGO.GetComponent<Image>();
        widget._icon.preserveAspect = true;
        widget._icon.raycastTarget  = false;

        // ── 라벨 ──
        var labelGO = new GameObject("Label", typeof(RectTransform));
        labelGO.transform.SetParent(go.transform, false);
        widget._label = labelGO.AddComponent<TextMeshProUGUI>();
        if (style.fontAsset != null) widget._label.font = style.fontAsset;
        widget._label.fontSize  = style.fontSize;
        widget._label.alignment = TextAlignmentOptions.MidlineLeft;
        widget._label.textWrappingMode = style.wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
        widget._label.raycastTarget = false;
        var labelLE = labelGO.AddComponent<LayoutElement>();
        labelLE.flexibleWidth = 1f;

        return widget;
    }

    /// <summary>생성 + 바인드 원샷.</summary>
    public static EffectRowWidget Create(Transform parent, in EffectRowStyle style, in EffectDisplay display)
    {
        var w = Create(parent, style);
        w.Bind(display);
        return w;
    }

    /// <summary>슬롯에서 바로 생성 + 바인드.</summary>
    public static EffectRowWidget Create(Transform parent, in EffectRowStyle style, ItemEffectSlot slot)
        => Create(parent, style, EffectDescriptionFormatter.Describe(slot));

    /// <summary>표시 데이터 적용.</summary>
    public void Bind(in EffectDisplay display)
    {
        if (_icon != null)
        {
            var sprite = EffectIconRegistry.GetSprite(display.IconKey);
            _icon.sprite  = sprite;
            _icon.enabled = sprite != null;
            _icon.gameObject.SetActive(sprite != null);
        }

        if (_label != null)
        {
            string core = _style.showTrigger ? display.Combined : display.LabelWithValue;
            string prefix = _style.usePrefixArrows ? (display.IsRisk ? "▼ " : "▲ ") : "";
            _label.text  = prefix + core;
            _label.color = display.IsRisk ? _style.riskColor : _style.normalColor;
        }
    }

    public void Bind(ItemEffectSlot slot) => Bind(EffectDescriptionFormatter.Describe(slot));
}

/// <summary>효과 행 위젯의 표시 스타일. <see cref="EffectRowStyle.Default"/>에서 시작해 필요한 값만 수정.</summary>
public struct EffectRowStyle
{
    public TMP_FontAsset fontAsset;
    public float fontSize;
    public float iconSize;
    public float rowHeight;
    public float spacing;
    public bool  usePrefixArrows;   // ▲/▼ 접두(아이템 목록용)
    public bool  showTrigger;       // (트리거) 표기 포함
    public bool  wrap;
    public Color normalColor;
    public Color riskColor;

    /// <summary>아이템 효과 목록 기본 스타일(다크 배경용 청백/적색 듀오톤).</summary>
    public static EffectRowStyle Default => new EffectRowStyle
    {
        fontAsset       = null,
        fontSize        = 14f,
        iconSize        = 16f,
        rowHeight       = 20f,
        spacing         = 4f,
        usePrefixArrows = false,
        showTrigger     = true,
        wrap            = false,
        normalColor     = EffectDescriptionFormatter.NormalColor,
        riskColor       = EffectDescriptionFormatter.RiskColor,
    };
}
