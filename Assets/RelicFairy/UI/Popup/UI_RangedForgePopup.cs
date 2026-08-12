using System;
using System.Collections.Generic;
using System.Text;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 보조무기(원거리) 선택 팝업 — <b>캐러셀</b>.
///
/// 기획 피벗: 주무기는 무형검(근접)이 기본 지급이라 여기서는 보조 원거리만 고른다.
///
/// 목록으로 쌓지 않고 <b>한 번에 무기 하나를 크게</b> 보여주고 좌우로 넘긴다 —
/// 무기 하나에 시선이 집중되고, 넘기는 손맛이 생기며, 큰 아이콘/큰 글씨로 정보가 잘 읽힌다.
/// 잠금 무기(석궁=특전)는 카드에 표시하되 확정만 막는다.
///
/// 슬라이드는 위치가 아니라 알파+스케일 크로스페이드(레이아웃과 안 싸운다). 시간정지 중이라 unscaled.
/// 가독성 머티리얼은 UI_RelicInfoPopup과 동일(얇은 폰트 두께 보정).
/// </summary>
public class UI_RangedForgePopup : UI_Popup
{
    // ── Constants ─────────────────────────────────────────────
    private const float PanelWidth  = 620f;
    private const float PanelHeight = 640f;   // 카드 내용(아이콘+4줄 스탯)이 눌리지 않을 최소치
    private const float SlideTime   = 0.18f;

    private static readonly Color PanelBg   = new(0.07f, 0.06f, 0.10f, 0.97f);
    private static readonly Color PanelLine = new(0.55f, 0.72f, 0.95f, 1f);
    private static readonly Color Accent    = new(0.62f, 0.80f, 1f, 1f);

    private static readonly Color TitleColor = new(0.82f, 0.90f, 1f, 1f);
    private static readonly Color BodyColor  = new(0.88f, 0.88f, 0.84f, 1f);
    private static readonly Color SubColor   = new(0.62f, 0.64f, 0.70f, 1f);
    private static readonly Color LockColor  = new(0.52f, 0.42f, 0.40f, 1f);
    private static readonly Color ArrowOn    = new(0.75f, 0.85f, 1f, 1f);
    private static readonly Color ArrowOff   = new(0.35f, 0.37f, 0.42f, 0.5f);
    private static readonly Color DotOn      = new(0.70f, 0.84f, 1f, 1f);
    private static readonly Color DotOff     = new(0.40f, 0.42f, 0.48f, 1f);
    private const string NumberHex = "#8FC7FF";

    // ── Nested ────────────────────────────────────────────────
    public struct Entry
    {
        public WeaponSO Weapon;
        public bool     Locked;
        public string   LockReason;
    }

    // ── Private ───────────────────────────────────────────────
    private RectTransform _stage;        // 무대(레이아웃 자식, 고정) — 슬라이드로 만지지 않는다
    private RectTransform _slider;       // 무대 안에서 좌우로 밀려 들어오는 카드 컨테이너(레이아웃 무관)
    private Image         _stageGlow;    // 무대 배경 — 무기 테마색으로 은은하게 물든다
    private CanvasGroup   _cardGroup;    // 현재 카드 알파/스케일
    private Image         _icon;
    private TMP_Text      _name;
    private TMP_Text      _badge;
    private TMP_Text      _tag;
    private TMP_Text      _stats;
    private Button        _prev, _next;
    private RectTransform _dots;
    private Button        _confirm;
    private Image         _confirmBg;
    private TMP_Text      _confirmLabel;

    private readonly List<Entry> _entries = new();
    private readonly List<Image> _dotImgs = new();
    private readonly Dictionary<Button, TMP_Text> _arrowGlyphs = new();   // SetArrow가 색을 바꾼다(버튼 2개)
    private readonly StringBuilder _sb = new();
    private static Material s_textMat;

    private int _index;
    private int _slideGen;
    private int _slideDir;   // 넘긴 방향(+1 오른쪽 / -1 왼쪽) — 카드가 그쪽에서 밀려 들어온다
    private UniTaskCompletionSource<WeaponSO> _tcs;

    public override bool BlocksGameplay => true;

    // ── Init ──────────────────────────────────────────────────
    public override void Init()
    {
        base.Init();
        BuildLayout();
    }

    // ── Public API ────────────────────────────────────────────
    public void Setup(IReadOnlyList<Entry> entries)
    {
        _entries.Clear();
        if (entries != null)
            for (int i = 0; i < entries.Count; i++)
                if (entries[i].Weapon != null) _entries.Add(entries[i]);

        _index = 0;
        _tcs = new UniTaskCompletionSource<WeaponSO>();

        BuildDots();
        ShowCard(_index, instant: true);
    }

    public UniTask<WeaponSO> WaitForChoiceAsync() => _tcs.Task;

    /// <summary>
    /// 정상 경로(Complete) 없이 파괴돼도 대기를 끝낸다 — 씬 전환·CloseAllPopupUI 등.
    /// 이게 없으면 _tcs가 영구 미완료라 WaitForChoiceAsync가 무한 대기하고 모루가 영구히 잠긴다.
    /// null = Cancel과 같은 값이라 안전한 기본값이다. Complete가 먼저면 TrySetResult가 무시된다.
    /// </summary>
    protected override void OnDestroy()
    {
        base.OnDestroy();   // 차단 잠금 누수 방지(UI_Popup)
        _tcs?.TrySetResult(null);
    }

    // ── 캐러셀 ────────────────────────────────────────────────
    private void Step(int dir)
    {
        int next = _index + dir;
        if (next < 0 || next >= _entries.Count) return;
        _index = next;
        _slideDir = dir;
        ShowCard(_index, instant: false);
    }

    private void ShowCard(int i, bool instant)
    {
        if (i < 0 || i >= _entries.Count) return;
        var e = _entries[i];
        var w = e.Weapon;

        // 무기 테마색(없으면 기본 청색). 잠금이면 색을 죽인다.
        Color theme = w.uiThemeColor.a > 0.01f ? w.uiThemeColor : Accent;
        Color accent = e.Locked ? LockColor : theme;

        _icon.sprite = w.icon;
        _icon.color  = w.icon != null ? Color.white : new Color(1f, 1f, 1f, 0.12f);

        _name.text  = w.displayName;
        _name.color = e.Locked ? LockColor : accent;

        _badge.gameObject.SetActive(e.Locked);
        if (e.Locked) _badge.text = "■ " + (string.IsNullOrEmpty(e.LockReason) ? "잠금" : e.LockReason);

        _tag.text   = w.tagline ?? string.Empty;
        _tag.color  = e.Locked ? LockColor : SubColor;

        _stats.text = e.Locked ? "<color=#8A6B6B>특전으로 해금되는 무기</color>" : StatBlock(w);

        // 테마색으로 톤 통일 — 무대 배경 은은하게 물들이고, 확정 버튼도 이 색을 따른다.
        _stageGlow.color = e.Locked
            ? new Color(LockColor.r, LockColor.g, LockColor.b, 0.05f)
            : new Color(accent.r, accent.g, accent.b, 0.08f);

        // 화살표 활성/비활성 — 양 끝에서 못 넘어감을 색으로 알린다.
        SetArrow(_prev, i > 0);
        SetArrow(_next, i < _entries.Count - 1);
        UpdateDots(i);
        RefreshConfirm(e, theme);

        if (instant)
        {
            _cardGroup.alpha = 1f;
            _slider.localScale = Vector3.one;
            _slider.anchoredPosition = Vector2.zero;
            return;
        }
        SlideInAsync(++_slideGen, _slideDir).Forget();
    }

    /// <summary>
    /// 넘긴 방향에서 카드가 밀려 들어온다 — 오른쪽으로 넘기면 오른쪽에서 슬라이드.
    /// 위치+알파+스케일을 함께 움직여 "물리적으로 넘어왔다"는 방향감을 준다(레이아웃과 안 싸우게
    /// anchoredPosition만 만진다). 시간정지 중이라 unscaled.
    /// </summary>
    private async UniTaskVoid SlideInAsync(int gen, int dir)
    {
        float from = dir >= 0 ? 90f : -90f;   // 오른쪽 넘김 → 오른쪽(+x)에서 진입
        float t = 0f;
        while (t < 1f)
        {
            if (gen != _slideGen) return;
            t += Time.unscaledDeltaTime / SlideTime;
            float e = 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f);   // EaseOutCubic

            _cardGroup.alpha         = e;
            _slider.localScale       = Vector3.one * Mathf.Lerp(0.94f, 1f, e);
            _slider.anchoredPosition = new Vector2(Mathf.Lerp(from, 0f, e), 0f);
            await UniTask.Yield(PlayerLoopTiming.Update, destroyCancellationToken);
        }
        _slider.anchoredPosition = Vector2.zero;
    }

    private string StatBlock(WeaponSO w)
    {
        _sb.Clear();
        Row("공격력", w.baseAttack.ToString("0"));
        Row("연사",   w.attackSpeed.ToString("0.0") + " /초");
        Row("사거리", w.attackRange.ToString("0") + " m");
        Row("치명타", w.critChance.ToString("0") + "%");
        return _sb.ToString();
    }

    private void Row(string label, string value)
    {
        if (_sb.Length > 0) _sb.Append('\n');
        // 라벨 폭을 스페이스로 대충 맞춰 값을 정렬(모노 느낌). 숫자만 강조.
        _sb.Append("<color=#9A9C9F>").Append(label).Append("</color>   ")
           .Append("<color=").Append(NumberHex).Append('>').Append(value).Append("</color>");
    }

    // ── 상태 표시 ─────────────────────────────────────────────
    private void SetArrow(Button btn, bool on)
    {
        if (btn != null) btn.interactable = on;
        if (btn != null && _arrowGlyphs.TryGetValue(btn, out var glyph) && glyph != null)
            glyph.color = on ? ArrowOn : ArrowOff;
    }

    private void UpdateDots(int active)
    {
        for (int i = 0; i < _dotImgs.Count; i++)
            _dotImgs[i].color = i == active ? DotOn : DotOff;
    }

    private void RefreshConfirm(Entry e, Color theme)
    {
        bool ok = !e.Locked;
        if (_confirm != null) _confirm.interactable = ok;
        if (_confirmLabel != null)
            _confirmLabel.color = ok ? TitleColor : new Color(TitleColor.r, TitleColor.g, TitleColor.b, 0.35f);
        // 확정 버튼을 무기 테마색으로 눌러 깐다(선택 가능할 때만).
        if (_confirmBg != null)
            _confirmBg.color = ok
                ? new Color(theme.r * 0.32f, theme.g * 0.32f, theme.b * 0.40f, 1f)
                : new Color(0.14f, 0.15f, 0.18f, 1f);
    }

    private void Confirm()
    {
        if (_index < 0 || _index >= _entries.Count) return;
        var e = _entries[_index];
        if (e.Locked) return;
        Complete(e.Weapon);
    }

    private void Cancel() => Complete(null);

    private void Complete(WeaponSO result)
    {
        _tcs?.TrySetResult(result);
        ClosePopupUI();
    }

    // ── 레이아웃 ───────────────────────────────────────────────
    private void BuildLayout()
    {
        var root = (RectTransform)transform;

        var dim = NewImage("Dim", root, new Color(0f, 0f, 0f, 0.65f));
        Stretch(dim.rectTransform);
        dim.raycastTarget = true;

        var panel = NewImage("Panel", root, PanelBg);
        var prt = panel.rectTransform;
        prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
        prt.pivot = new Vector2(0.5f, 0.5f);
        prt.sizeDelta = new Vector2(PanelWidth, PanelHeight);

        var line = panel.gameObject.AddComponent<Outline>();
        line.effectColor = PanelLine;
        line.effectDistance = new Vector2(2f, -2f);

        var v = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(28, 28, 24, 22);
        v.spacing = 12f;
        v.childControlWidth = true;  v.childForceExpandWidth  = true;
        v.childControlHeight = true; v.childForceExpandHeight = false;
        v.childAlignment = TextAnchor.UpperCenter;

        // 제목 + 안내
        var title = NewText("Title", prt, 30f, TitleColor, FontStyles.Bold, TextAlignmentOptions.Center);
        title.text = "보조 무기 — 원거리";
        var sub = NewText("Sub", prt, 14f, SubColor, FontStyles.Normal, TextAlignmentOptions.Center);
        sub.text = "주무기 <color=#CFC0A0>무명의 형상</color>은 이미 손에 있다. 곁에 둘 하나를 고른다.";

        BuildViewer(prt);   // ◀  [카드]  ▶
        BuildDotsRow(prt);
        BuildButtons(prt);
    }

    /// <summary>◀ 화살표 · 카드 무대 · ▶ 화살표 — 가로 3분할.</summary>
    private void BuildViewer(RectTransform parent)
    {
        var row = NewRect("Viewer", parent);
        row.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;
        var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 6f;
        h.childAlignment = TextAnchor.MiddleCenter;
        h.childControlWidth = true;  h.childForceExpandWidth  = false;
        h.childControlHeight = true; h.childForceExpandHeight = true;

        _prev = BuildArrow(row, "◀", -1);

        // 무대 — 레이아웃이 폭을 정하는 자식. 이건 절대 슬라이드로 만지지 않는다.
        var stage = NewRect("Stage", row.transform);
        stage.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        _stage = stage;

        // 슬라이더 — 무대를 꽉 채운 오버레이. 레이아웃 그룹의 자식이 아니라(스트레치) 위치를
        // 마음대로 옮겨도 매 프레임 되돌려지지 않는다. 슬라이드/스케일/알파를 전부 이 위에 건다.
        _slider = NewRect("Slider", stage);
        Stretch(_slider);
        _cardGroup = _slider.gameObject.AddComponent<CanvasGroup>();

        // 무대 배경 — 무기 테마색으로 은은하게 물드는 판(카드보다 뒤).
        _stageGlow = NewImage("Glow", _slider, new Color(1f, 1f, 1f, 0f));
        Stretch(_stageGlow.rectTransform);
        _stageGlow.raycastTarget = false;
        // 배경 판이라 세로 레이아웃의 한 칸을 차지하면 안 된다 — 카드 내용이 그만큼 밀려 눌린다.
        _stageGlow.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

        BuildCardBody(_slider);

        _next = BuildArrow(row, "▶", +1);
    }

    /// <summary>화살표 버튼 — 색은 글리프 텍스트로 제어(SetArrow가 _arrowGlyphs를 통해 바꾼다).</summary>
    private Button BuildArrow(RectTransform parent, string glyph, int dir)
    {
        var box = NewImage("Arrow", parent, new Color(1f, 1f, 1f, 0.04f));
        var le = box.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = 52f; le.minWidth = 52f;

        var btn = box.gameObject.AddComponent<Button>();
        btn.targetGraphic = box;
        btn.onClick.AddListener(() => Step(dir));

        var t = NewText("G", box.rectTransform, 34f, ArrowOn, FontStyles.Bold, TextAlignmentOptions.Center);
        t.text = glyph;
        Stretch(t.rectTransform);

        _arrowGlyphs[btn] = t;
        return btn;
    }

    private void BuildCardBody(RectTransform stage)
    {
        var v = stage.gameObject.AddComponent<VerticalLayoutGroup>();
        v.spacing = 10f;
        v.padding = new RectOffset(6, 6, 6, 6);
        v.childAlignment = TextAnchor.UpperCenter;
        v.childControlWidth = true;  v.childForceExpandWidth  = true;
        v.childControlHeight = true; v.childForceExpandHeight = false;

        // 무대에 들어갈 세로 예산이 카드 내용보다 작으면 VerticalLayoutGroup이 자식을 최소높이까지
        // 눌러버린다. TMP의 최소높이는 0이라 글자는 그대로 그려지면서 칸만 사라져 서로 겹쳐 보였다
        // (이름·설명·스탯이 한 덩어리로 뭉치던 원인). 그래서 각 줄에 높이를 못 박는다.
        var iconBox = NewRect("IconBox", stage);
        FixHeight(iconBox.gameObject, 130f);
        _icon = NewImage("Icon", iconBox, Color.white);
        Stretch(_icon.rectTransform);
        _icon.preserveAspect = true;

        // 이름 + 잠금 배지
        _name = NewText("Name", stage, 26f, TitleColor, FontStyles.Bold, TextAlignmentOptions.Center);
        _name.textWrappingMode = TextWrappingModes.NoWrap;
        _name.overflowMode     = TextOverflowModes.Ellipsis;
        FixHeight(_name.gameObject, 34f);

        _badge = NewText("Badge", stage, 15f, LockColor, FontStyles.Bold, TextAlignmentOptions.Center);
        _badge.textWrappingMode = TextWrappingModes.NoWrap;
        FixHeight(_badge.gameObject, 20f);

        // 한 줄 설명
        _tag = NewText("Tag", stage, 14f, SubColor, FontStyles.Italic, TextAlignmentOptions.Center);
        _tag.textWrappingMode = TextWrappingModes.NoWrap;
        _tag.overflowMode     = TextOverflowModes.Ellipsis;
        FixHeight(_tag.gameObject, 22f);

        // 스탯 블록(4줄 세로 정렬)
        _stats = NewText("Stats", stage, 16f, BodyColor, FontStyles.Normal, TextAlignmentOptions.Center);
        _stats.lineSpacing = 6f;
        FixHeight(_stats.gameObject, 104f);
    }

    /// <summary>레이아웃 그룹이 눌러도 줄지 않도록 높이를 고정한다(min=preferred, flexible=0).</summary>
    private static void FixHeight(GameObject go, float height)
    {
        var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
        le.minHeight       = height;
        le.preferredHeight = height;
        le.flexibleHeight  = 0f;
    }

    private void BuildDotsRow(RectTransform parent)
    {
        _dots = NewRect("Dots", parent);
        _dots.gameObject.AddComponent<LayoutElement>().preferredHeight = 16f;
        var h = _dots.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 8f;
        h.childAlignment = TextAnchor.MiddleCenter;
        h.childControlWidth = true;  h.childForceExpandWidth  = false;
        h.childControlHeight = true; h.childForceExpandHeight = false;
    }

    private void BuildDots()
    {
        for (int i = 0; i < _dotImgs.Count; i++) Destroy(_dotImgs[i].gameObject);
        _dotImgs.Clear();

        for (int i = 0; i < _entries.Count; i++)
        {
            var dot = NewImage("Dot", _dots, DotOff);
            var le = dot.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = 10f; le.preferredHeight = 10f;
            le.minWidth = 10f; le.minHeight = 10f;
            _dotImgs.Add(dot);
        }
    }

    private void BuildButtons(RectTransform parent)
    {
        var row = NewRect("Buttons", parent);
        FixHeight(row.gameObject, 56f);   // 남는 세로를 버튼이 먹어 정사각형처럼 커지지 않게 고정
        var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 14f;
        h.childControlWidth = true;  h.childForceExpandWidth  = true;
        h.childControlHeight = true; h.childForceExpandHeight = true;

        _confirmBg = NewImage("Btn_Confirm", row, new Color(0.16f, 0.30f, 0.44f, 1f));
        _confirm = _confirmBg.gameObject.AddComponent<Button>();
        _confirm.targetGraphic = _confirmBg;
        _confirm.onClick.AddListener(Confirm);
        var cl = _confirmBg.gameObject.AddComponent<Outline>();
        cl.effectColor = new Color(Accent.r, Accent.g, Accent.b, 0.55f);
        cl.effectDistance = new Vector2(1.5f, -1.5f);
        _confirmLabel = NewText("L", _confirmBg.rectTransform, 22f, TitleColor, FontStyles.Bold, TextAlignmentOptions.Center);
        _confirmLabel.text = "확정";
        Stretch(_confirmLabel.rectTransform);

        var cancelBg = NewImage("Btn_Cancel", row, new Color(0.16f, 0.16f, 0.19f, 1f));
        var cancel = cancelBg.gameObject.AddComponent<Button>();
        cancel.targetGraphic = cancelBg;
        cancel.onClick.AddListener(Cancel);
        var t = NewText("L", cancelBg.rectTransform, 22f, BodyColor, FontStyles.Bold, TextAlignmentOptions.Center);
        t.text = "취소";
        Stretch(t.rectTransform);
    }

    // ── 생성 헬퍼 ─────────────────────────────────────────────
    private static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static Image NewImage(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = color;
        return img;
    }

    private TMP_Text NewText(string name, Transform parent, float size, Color color,
                             FontStyles style, TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.fontSize = size;
        t.color = color;
        t.fontStyle = style;
        t.alignment = align;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.Normal;
        ApplyReadableMaterial(t);
        return t;
    }

    private static void ApplyReadableMaterial(TMP_Text t)
    {
        if (t.font == null) return;
        if (s_textMat == null)
        {
            var src = t.fontSharedMaterial;
            if (src == null) return;
            s_textMat = new Material(src) { name = src.name + " (RangedForge)" };
            s_textMat.EnableKeyword("OUTLINE_ON");
            s_textMat.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.07f);
            s_textMat.SetColor(ShaderUtilities.ID_OutlineColor, new Color(0.03f, 0.03f, 0.05f, 1f));
            s_textMat.SetFloat(ShaderUtilities.ID_FaceDilate, 0.06f);
        }
        t.fontSharedMaterial = s_textMat;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
