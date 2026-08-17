using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 설정 화면 — 오디오(마스터/배경음/효과음) + 화면(해상도/창모드).
///
/// <para><b>기존 시스템에 붙기만 한다.</b> 볼륨은 <see cref="SoundManager"/>의 채널 API(믹서 있으면 믹서,
/// 없으면 폴백 곱셈)로, 화면은 <see cref="ScreenSettings"/>(Screen.SetResolution + PlayerPrefs)로 나간다.
/// 이 클래스는 값을 들고 있지 않다 — 표시와 입력 전달만 한다.</para>
///
/// <para><b>프리팹 대신 런타임 생성</b>인 이유는 <see cref="UI_EscMenu"/>와 같다. UIManager.ShowPopupUI는
/// "UI/Popup/{타입명}" Addressable 프리팹을 요구하는데, 항목을 추가하려면 Addressables 그룹 에셋을
/// 함께 건드려야 하고 번들 재빌드가 빠지면 조용히 안 뜬다. 앵커를 직접 잡으므로 21:9·4:3·저해상도에서
/// 레이아웃이 깨질 여지도 없다.</para>
///
/// <para>시간 정지는 <b>잡지 않는다</b>. 인게임에선 항상 ESC 메뉴(TimeScaleArbiter 보유) 위에 겹쳐 열리고,
/// 로비에선 멈출 게임플레이가 없다. 여기서 또 잡으면 로비의 연출까지 같이 멈춘다.</para>
/// </summary>
public sealed class UI_Settings : MonoBehaviour
{
    // ── Constants ────────────────────────────────────────────
    private const int SortingOrder = UISortingOrder.SystemModalTop;

    private const float PanelW = 800f, PanelH = 564f;
    private const float SidePad = 40f, TopPad = 30f;
    private const float RowH = 48f, RowGap = 6f;

    private const float LabelW = 200f;
    private const float CtrlW  = 380f;
    private const float ValueW = 110f;

    // 패널 로컬 x — 왼쪽 라벨 열 / 가운데 컨트롤 열 / 오른쪽 수치 열
    private const float LabelX = -PanelW * 0.5f + SidePad + LabelW * 0.5f;   // -260
    private const float CtrlX  = -PanelW * 0.5f + SidePad + LabelW + 20f + CtrlW * 0.5f; // 50
    private const float ValueX =  PanelW * 0.5f - SidePad - ValueW * 0.5f;   // 305

    private static readonly Color Backdrop    = new(0f, 0f, 0f, 0.78f);
    private static readonly Color PanelBg     = new(0.08f, 0.09f, 0.13f, 0.98f);
    private static readonly Color PanelEdge   = new(0.52f, 0.72f, 0.86f, 0.85f);
    private static readonly Color CtrlBg      = new(0.16f, 0.19f, 0.27f, 1f);
    private static readonly Color TrackBg     = new(0.05f, 0.06f, 0.09f, 1f);
    private static readonly Color TrackFill   = new(0.42f, 0.62f, 0.82f, 1f);
    private static readonly Color HandleColor = new(0.88f, 0.93f, 1f, 1f);
    private static readonly Color LabelColor  = new(0.94f, 0.96f, 1f, 1f);
    private static readonly Color SectionColor= new(0.62f, 0.74f, 0.88f, 1f);
    private static readonly Color TitleColor  = UIPalette.Gold;

    // ── Static ───────────────────────────────────────────────
    private static UI_Settings _instance;

    // ── Private ──────────────────────────────────────────────
    private GameObject _root;

    private Slider _masterSlider, _bgmSlider, _sfxSlider;
    private TMP_Text _masterValue, _bgmValue, _sfxValue;

    private TMP_Dropdown _resolutionDropdown;
    private TMP_Text     _windowModeLabel;

    private List<Vector2Int> _resolutions = new();
    private bool _fullscreen = true;
    private bool _suppressCallbacks;   // 값 복원 중 onValueChanged가 되먹임되는 것을 막는다
    private bool _volumeDirty;         // 드래그 중 미뤄 둔 볼륨 저장이 남아 있는가

    // ── Properties ───────────────────────────────────────────
    public bool IsOpen => _root != null && _root.activeSelf;

    // ── Lifecycle ────────────────────────────────────────────

    /// <summary>
    /// 미뤄 둔 볼륨 저장을 마우스를 뗀 프레임에 한 번만 커밋한다.
    /// 이 컴포넌트는 @UIRoot(DDOL)에 상주해 설정이 닫혀 있어도 Update가 돌므로,
    /// 플래그가 꺼져 있으면 즉시 반환한다(bool 검사 1회).
    /// </summary>
    private void Update()
    {
        if (!_volumeDirty) return;
        if (Input.GetMouseButton(0)) return;

        _volumeDirty = false;
        Managers.Sound?.SaveVolumePrefs();
    }

    // ── Public Methods ───────────────────────────────────────

    /// <summary>어디서든 설정 화면을 연다. 인스턴스가 없으면 DDOL 호스트에 붙여 만든다.</summary>
    public static void Open() => Resolve()?.OpenInternal();

    /// <summary>열려 있으면 닫고 true를 반환한다(ESC 처리용).</summary>
    public static bool CloseIfOpen()
    {
        if (_instance == null || !_instance.IsOpen) return false;
        _instance.CloseInternal();
        return true;
    }

    // ── Private Methods ──────────────────────────────────────

    /// <summary>
    /// 인스턴스 확보. ESC 입력의 유일한 진입점인 <see cref="EscKeyListener"/>와 같은 오브젝트에 얹는다 —
    /// 그 오브젝트는 @UIRoot(DDOL) 안에 있어 씬을 넘어가도 살아남는다.
    /// 테스트 씬 등 @UIRoot가 없는 환경에선 전용 DDOL 오브젝트를 만든다.
    /// </summary>
    private static UI_Settings Resolve()
    {
        if (_instance != null) return _instance;

        var listener = Object.FindFirstObjectByType<EscKeyListener>(FindObjectsInactive.Include);
        GameObject host;
        if (listener != null)
        {
            host = listener.gameObject;
        }
        else
        {
            host = new GameObject("@Settings");
            Object.DontDestroyOnLoad(host);
        }

        _instance = host.GetOrAddComponent<UI_Settings>();
        return _instance;
    }

    private void OpenInternal()
    {
        if (_root == null) Build();

        RefreshFromSystems();
        _root.SetActive(true);
        _root.transform.SetAsLastSibling();
    }

    private void CloseInternal()
    {
        if (_root != null) _root.SetActive(false);
    }

    /// <summary>표시값을 실제 시스템 상태에서 다시 읽어온다 — 화면이 곧 진실이 되지 않게.</summary>
    private void RefreshFromSystems()
    {
        _suppressCallbacks = true;

        var sound = Managers.Sound;
        if (sound != null)
        {
            // Init이 PlayerPrefs를 필드로 읽어들인다. 부트에서 이미 불렸겠지만 멱등하므로 한 번 더 부른다 —
            // 안 불린 상태로 읽으면 저장값 대신 초기값(1.0)이 보이고, 슬라이더를 건드리는 순간
            // 그 1.0이 저장된 볼륨을 덮어쓴다.
            sound.Init();

            SetSlider(_masterSlider, _masterValue, sound.MasterVolume);
            SetSlider(_bgmSlider,    _bgmValue,    sound.BgmVolume);
            SetSlider(_sfxSlider,    _sfxValue,    sound.EffectVolume);
        }

        _fullscreen = ScreenSettings.HasSaved ? ScreenSettings.SavedFullscreen : ScreenSettings.IsFullscreenNow;
        UpdateWindowModeLabel();

        _resolutions = ScreenSettings.GetAvailableResolutions();
        var options = new List<string>(_resolutions.Count);
        foreach (var r in _resolutions)
            options.Add($"{r.x} × {r.y}");

        _resolutionDropdown.ClearOptions();
        _resolutionDropdown.AddOptions(options);

        var current = ScreenSettings.HasSaved
            ? new Vector2Int(ScreenSettings.SavedWidth, ScreenSettings.SavedHeight)
            : new Vector2Int(Screen.width, Screen.height);

        int index = _resolutions.IndexOf(current);
        _resolutionDropdown.SetValueWithoutNotify(Mathf.Max(0, index));
        _resolutionDropdown.RefreshShownValue();

        _suppressCallbacks = false;
    }

    private static void SetSlider(Slider slider, TMP_Text valueText, float value)
    {
        if (slider != null) slider.SetValueWithoutNotify(value);
        SetValueText(valueText, value);
    }

    private static void SetValueText(TMP_Text valueText, float value)
    {
        if (valueText != null) valueText.text = Mathf.RoundToInt(value * 100f) + "%";
    }

    private void UpdateWindowModeLabel()
    {
        if (_windowModeLabel != null)
            _windowModeLabel.text = _fullscreen ? "전체 화면" : "창 모드";
    }

    // ── Build ────────────────────────────────────────────────

    private void Build()
    {
        _root = new GameObject("SettingsRoot", typeof(RectTransform), typeof(Canvas),
                               typeof(CanvasScaler), typeof(GraphicRaycaster));
        _root.transform.SetParent(transform, false);

        var canvas = _root.GetComponent<Canvas>();
        canvas.renderMode      = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder    = SortingOrder;

        // 프로젝트 공통 기준 — 1920x1080 / Scale With Screen Size / Match 0.5.
        // 패널이 고정 크기 중앙 배치라 이 조합이면 21:9·4:3·1280x720 전부 화면 안에 들어온다.
        var scaler = _root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode     = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight  = 0.5f;

        var dim = NewImage("Backdrop", _root.transform, Backdrop);
        Stretch(dim.rectTransform);

        var panel = NewImage("Panel", _root.transform, PanelBg);
        var prt = panel.rectTransform;
        prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
        prt.anchoredPosition = Vector2.zero;
        prt.sizeDelta = new Vector2(PanelW, PanelH);
        var outline = panel.gameObject.AddComponent<Outline>();
        outline.effectColor    = PanelEdge;
        outline.effectDistance = new Vector2(2f, -2f);

        // 위에서 아래로 내려가는 커서. 각 요소는 [y - h/2]에 중심을 둔다.
        float y = PanelH * 0.5f - TopPad;

        y = AddTitle(panel.transform, y);
        y = AddSectionLabel(panel.transform, y, "오디오");
        y = AddVolumeRow(panel.transform, y, "마스터", out _masterSlider, out _masterValue, OnMasterChanged);
        y = AddVolumeRow(panel.transform, y, "배경음", out _bgmSlider,    out _bgmValue,    OnBgmChanged);
        y = AddVolumeRow(panel.transform, y, "효과음", out _sfxSlider,    out _sfxValue,    OnSfxChanged);

        y -= 14f;
        y = AddSectionLabel(panel.transform, y, "화면");
        y = AddResolutionRow(panel.transform, y);
        y = AddWindowModeRow(panel.transform, y);

        y -= 18f;
        AddCloseButton(panel.transform, y);
    }

    private static float AddTitle(Transform panel, float y)
    {
        const float H = 54f;
        NewLabel("Title", panel, "설정", 36f, TitleColor, new Vector2(0f, y - H * 0.5f),
                 new Vector2(PanelW - SidePad * 2f, H), TextAlignmentOptions.Center);
        return y - H - 6f;
    }

    private static float AddSectionLabel(Transform panel, float y, string text)
    {
        const float H = 34f;
        NewLabel($"Section_{text}", panel, text, 24f, SectionColor,
                 new Vector2(LabelX, y - H * 0.5f), new Vector2(LabelW, H), TextAlignmentOptions.Left);
        return y - H - RowGap;
    }

    private float AddVolumeRow(Transform panel, float y, string label,
                               out Slider slider, out TMP_Text valueText,
                               UnityEngine.Events.UnityAction<float> onChanged)
    {
        float cy = y - RowH * 0.5f;

        NewLabel($"Label_{label}", panel, label, 24f, LabelColor,
                 new Vector2(LabelX, cy), new Vector2(LabelW, RowH), TextAlignmentOptions.Left);

        slider = NewSlider(panel, $"Slider_{label}", new Vector2(CtrlX, cy), new Vector2(CtrlW, 24f));
        slider.onValueChanged.AddListener(onChanged);

        valueText = NewLabel($"Value_{label}", panel, "100%", 22f, LabelColor,
                             new Vector2(ValueX, cy), new Vector2(ValueW, RowH), TextAlignmentOptions.Right);

        return y - RowH - RowGap;
    }

    private float AddResolutionRow(Transform panel, float y)
    {
        float cy = y - RowH * 0.5f;

        NewLabel("Label_Resolution", panel, "해상도", 24f, LabelColor,
                 new Vector2(LabelX, cy), new Vector2(LabelW, RowH), TextAlignmentOptions.Left);

        _resolutionDropdown = NewDropdown(panel, new Vector2(CtrlX, cy), new Vector2(CtrlW, 44f));
        _resolutionDropdown.onValueChanged.AddListener(OnResolutionChanged);

        return y - RowH - RowGap;
    }

    private float AddWindowModeRow(Transform panel, float y)
    {
        float cy = y - RowH * 0.5f;

        NewLabel("Label_WindowMode", panel, "창 모드", 24f, LabelColor,
                 new Vector2(LabelX, cy), new Vector2(LabelW, RowH), TextAlignmentOptions.Left);

        var img = NewImage("Btn_WindowMode", panel, CtrlBg);
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(CtrlX, cy);
        rt.sizeDelta = new Vector2(CtrlW, 44f);

        var btn = img.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(OnToggleWindowMode);
        img.gameObject.AddComponent<UIButtonFeedback>();

        _windowModeLabel = NewLabel("Label", img.transform, "전체 화면", 22f, LabelColor,
                                    Vector2.zero, new Vector2(CtrlW, 44f), TextAlignmentOptions.Center);

        return y - RowH - RowGap;
    }

    private void AddCloseButton(Transform panel, float y)
    {
        const float W = 240f, H = 60f;

        var img = NewImage("Btn_Close", panel, CtrlBg);
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(0f, y - H * 0.5f);
        rt.sizeDelta = new Vector2(W, H);

        var btn = img.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(CloseInternal);
        img.gameObject.AddComponent<UIButtonFeedback>();

        NewLabel("Label", img.transform, "닫기", 26f, LabelColor, Vector2.zero,
                 new Vector2(W, H), TextAlignmentOptions.Center);
    }

    // ── Widget Factory ───────────────────────────────────────

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    private static Image NewImage(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = color;
        return img;
    }

    private static TMP_Text NewLabel(string name, Transform parent, string text, float size, Color color,
                                     Vector2 pos, Vector2 sizeDelta, TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = sizeDelta;

        var tmp = go.AddComponent<TextMeshProUGUI>();
        var font = ResolveFont();
        if (font != null) tmp.font = font;

        tmp.text          = text;
        tmp.fontSize      = size;
        tmp.color         = color;
        tmp.alignment     = align;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static TMP_FontAsset ResolveFont()
    {
        var font = TMP_Settings.defaultFontAsset;
        if (font == null || font.material == null)
            font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        return font;
    }

    /// <summary>uGUI 기본 Slider 계층(Background / Fill Area·Fill / Handle Slide Area·Handle)을 그대로 만든다.</summary>
    private static Slider NewSlider(Transform parent, string name, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;

        var bg = NewImage("Background", go.transform, TrackBg);
        var bgRt = bg.rectTransform;
        bgRt.anchorMin = new Vector2(0f, 0.25f);
        bgRt.anchorMax = new Vector2(1f, 0.75f);
        bgRt.offsetMin = bgRt.offsetMax = Vector2.zero;
        bg.raycastTarget = false;

        var fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(go.transform, false);
        var faRt = fillArea.GetComponent<RectTransform>();
        faRt.anchorMin = new Vector2(0f, 0.25f);
        faRt.anchorMax = new Vector2(1f, 0.75f);
        faRt.offsetMin = new Vector2(5f, 0f);
        faRt.offsetMax = new Vector2(-15f, 0f);

        var fill = NewImage("Fill", fillArea.transform, TrackFill);
        var fRt = fill.rectTransform;
        fRt.anchorMin = new Vector2(0f, 0f);
        fRt.anchorMax = new Vector2(1f, 1f);
        fRt.offsetMin = fRt.offsetMax = Vector2.zero;
        fRt.sizeDelta = new Vector2(10f, 0f);
        fill.raycastTarget = false;

        var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
        handleArea.transform.SetParent(go.transform, false);
        var haRt = handleArea.GetComponent<RectTransform>();
        haRt.anchorMin = new Vector2(0f, 0f);
        haRt.anchorMax = new Vector2(1f, 1f);
        haRt.offsetMin = new Vector2(10f, 0f);
        haRt.offsetMax = new Vector2(-10f, 0f);

        var handle = NewImage("Handle", handleArea.transform, HandleColor);
        var hRt = handle.rectTransform;
        hRt.anchorMin = new Vector2(0f, 0f);
        hRt.anchorMax = new Vector2(0f, 1f);
        hRt.sizeDelta = new Vector2(22f, 0f);

        var slider = go.AddComponent<Slider>();
        slider.fillRect      = fRt;
        slider.handleRect    = hRt;
        slider.targetGraphic = handle;
        slider.direction     = Slider.Direction.LeftToRight;
        slider.minValue      = 0f;
        slider.maxValue      = 1f;
        slider.wholeNumbers  = false;
        slider.value         = 1f;
        return slider;
    }

    /// <summary>
    /// TMP_Dropdown은 캡션/화살표 외에 <b>비활성 Template</b>(Viewport + Content + Item Toggle)이 반드시
    /// 있어야 목록이 열린다. 프리팹 없이 쓰려면 그 계층을 손으로 만들어 줘야 한다.
    /// </summary>
    private static TMP_Dropdown NewDropdown(Transform parent, Vector2 pos, Vector2 size)
    {
        const float ItemH = 32f;
        const float ListH = 168f;

        var bg = NewImage("Dropdown_Resolution", parent, CtrlBg);
        var rt = bg.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;

        var caption = NewLabel("Label", bg.transform, string.Empty, 22f, LabelColor,
                               Vector2.zero, size, TextAlignmentOptions.Center);

        // ── Template (비활성) ──
        var template = new GameObject("Template", typeof(RectTransform), typeof(Image),
                                      typeof(ScrollRect), typeof(CanvasGroup));
        template.transform.SetParent(bg.transform, false);
        var tRt = template.GetComponent<RectTransform>();
        tRt.anchorMin = new Vector2(0f, 0f);
        tRt.anchorMax = new Vector2(1f, 0f);
        tRt.pivot     = new Vector2(0.5f, 1f);
        tRt.anchoredPosition = new Vector2(0f, -2f);
        tRt.sizeDelta = new Vector2(0f, ListH);
        template.GetComponent<Image>().color = PanelBg;

        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
        viewport.transform.SetParent(template.transform, false);
        var vRt = viewport.GetComponent<RectTransform>();
        vRt.anchorMin = new Vector2(0f, 0f);
        vRt.anchorMax = new Vector2(1f, 1f);
        vRt.pivot     = new Vector2(0f, 1f);
        vRt.offsetMin = Vector2.zero;
        vRt.offsetMax = Vector2.zero;
        viewport.GetComponent<Image>().color = PanelBg;
        viewport.GetComponent<Mask>().showMaskGraphic = false;

        var content = new GameObject("Content", typeof(RectTransform));
        content.transform.SetParent(viewport.transform, false);
        var cRt = content.GetComponent<RectTransform>();
        cRt.anchorMin = new Vector2(0f, 1f);
        cRt.anchorMax = new Vector2(1f, 1f);
        cRt.pivot     = new Vector2(0.5f, 1f);
        cRt.anchoredPosition = Vector2.zero;
        cRt.sizeDelta = new Vector2(0f, ItemH);

        var item = new GameObject("Item", typeof(RectTransform), typeof(Toggle));
        item.transform.SetParent(content.transform, false);
        var iRt = item.GetComponent<RectTransform>();
        iRt.anchorMin = new Vector2(0f, 0.5f);
        iRt.anchorMax = new Vector2(1f, 0.5f);
        iRt.pivot     = new Vector2(0.5f, 0.5f);
        iRt.sizeDelta = new Vector2(0f, ItemH);

        var itemBg = NewImage("Item Background", item.transform, new Color(0.12f, 0.14f, 0.2f, 1f));
        Stretch(itemBg.rectTransform);

        var check = NewImage("Item Checkmark", item.transform, TitleColor);
        var chRt = check.rectTransform;
        chRt.anchorMin = chRt.anchorMax = new Vector2(0f, 0.5f);
        chRt.pivot     = new Vector2(0.5f, 0.5f);
        chRt.anchoredPosition = new Vector2(16f, 0f);
        chRt.sizeDelta = new Vector2(12f, 12f);

        var itemLabel = NewLabel("Item Label", item.transform, string.Empty, 20f, LabelColor,
                                 Vector2.zero, Vector2.zero, TextAlignmentOptions.Left);
        var ilRt = itemLabel.rectTransform;
        ilRt.anchorMin = Vector2.zero;
        ilRt.anchorMax = Vector2.one;
        ilRt.offsetMin = new Vector2(34f, 0f);
        ilRt.offsetMax = new Vector2(-10f, 0f);

        var toggle = item.GetComponent<Toggle>();
        toggle.targetGraphic = itemBg;
        toggle.graphic       = check;
        toggle.isOn          = true;

        var scroll = template.GetComponent<ScrollRect>();
        scroll.content      = cRt;
        scroll.viewport     = vRt;
        scroll.horizontal   = false;
        scroll.vertical     = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 24f;

        template.SetActive(false);

        var dropdown = bg.gameObject.AddComponent<TMP_Dropdown>();
        dropdown.targetGraphic = bg;
        dropdown.template      = tRt;
        dropdown.captionText   = caption;
        dropdown.itemText      = itemLabel;
        return dropdown;
    }

    // ── Event Handlers ───────────────────────────────────────

    // 슬라이더는 드래그 중 초당 수십 번 호출된다 → 즉시 반영만 하고(save:false) 저장은 미룬다.
    // 실제 기록은 Update가 마우스를 뗀 프레임에 한 번만 수행한다.
    private void OnMasterChanged(float v)
    {
        if (_suppressCallbacks) return;
        Managers.Sound?.SetMasterVolume(v, save: false);
        SetValueText(_masterValue, v);
        _volumeDirty = true;
    }

    private void OnBgmChanged(float v)
    {
        if (_suppressCallbacks) return;
        Managers.Sound?.SetBgmVolume(v, save: false);
        SetValueText(_bgmValue, v);
        _volumeDirty = true;
    }

    private void OnSfxChanged(float v)
    {
        if (_suppressCallbacks) return;
        Managers.Sound?.SetEffectVolume(v, save: false);
        SetValueText(_sfxValue, v);
        _volumeDirty = true;
    }

    private void OnResolutionChanged(int index)
    {
        if (_suppressCallbacks) return;
        if (index < 0 || index >= _resolutions.Count) return;

        var res = _resolutions[index];
        ScreenSettings.Apply(res.x, res.y, _fullscreen);
    }

    private void OnToggleWindowMode()
    {
        _fullscreen = !_fullscreen;
        UpdateWindowModeLabel();

        int i = _resolutionDropdown != null ? _resolutionDropdown.value : -1;
        var res = (i >= 0 && i < _resolutions.Count)
            ? _resolutions[i]
            : new Vector2Int(Screen.width, Screen.height);

        ScreenSettings.Apply(res.x, res.y, _fullscreen);
    }
}
