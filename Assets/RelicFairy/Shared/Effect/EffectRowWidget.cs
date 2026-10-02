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
        // 여러 줄로 접히는 행은 위 정렬 — 가운데 정렬이면 색 점이 문단 세로 가운데(둘째 줄)에 붙었다(09-28 UI 전수).
        hlg.childAlignment = style.wrap ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;

        var le = go.AddComponent<LayoutElement>();
        le.minHeight = style.rowHeight;
        // wrap이면 높이를 고정하지 않는다 — 두 줄로 접힌 라벨만큼 행이 자라야 한다(고정하면 둘째 줄이 상자 밖으로 나간다).
        // 부모 LayoutGroup이 childControlHeight=true여야 이 값이 산다. wrap이 아니면 예전과 같이 고정 높이.
        if (!style.wrap) le.preferredHeight = style.rowHeight;

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
    public static EffectRowWidget Create(Transform parent, in EffectRowStyle style, in EffectDisplay display,
                                         string effectType = null)
    {
        var w = Create(parent, style);
        w.Bind(display, effectType);
        return w;
    }

    /// <summary>슬롯에서 바로 생성 + 바인드.</summary>
    public static EffectRowWidget Create(Transform parent, in EffectRowStyle style, ItemEffectSlot slot)
        => Create(parent, style, EffectDescriptionFormatter.Describe(slot), slot.effectType);

    /// <summary>
    /// 표시 데이터 적용. <paramref name="effectType"/>은 <b>지금은 쓰지 않는다</b>(아래 주석 참고) —
    /// 호출부 시그니처를 깨지 않으려고 남겨 둔 자리다.
    /// </summary>
    public void Bind(in EffectDisplay display, string effectType = null)
    {
        if (_icon != null)
        {
            // ⚠️ 효과 줄에 <b>룬 보석 문양</b>을 쓰지 않는다(2026-09-14).
            //    "공격력 +6%" 앞에 룬 모양이 붙으니 그 룬이 무슨 아이템인지와 효과가 무엇인지가 뒤섞였다.
            //    효과는 <b>글로 읽는 것</b>이고, 아이콘은 <b>의미가 있을 때만</b> 붙인다.
            //    계열 아이콘 세트(공격=검·방어=방패 …)가 실제로 들어와 있을 때만 그리고,
            //    없으면 색 점 플레이스홀더 대신 <b>아이콘 자체를 숨겨</b> 글자만 남긴다.
            var sprite = EffectIconRegistry.HasIconSet
                       ? EffectIconRegistry.GetSprite(display.IconKey)
                       : null;
            _icon.sprite  = sprite;
            _icon.color   = EffectIconRegistry.TintFor(display.IconKey);   // 흰 글리프에 계열색(10-02)
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

    public void Bind(ItemEffectSlot slot) => Bind(EffectDescriptionFormatter.Describe(slot), slot.effectType);
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
