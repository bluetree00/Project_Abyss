using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 설정 화면 — 오디오(마스터/배경음/효과음/UI) + 화면(해상도/창모드) + 그래픽(품질 7항목).
///
/// <para><b>기존 시스템에 붙기만 한다.</b> 볼륨은 <see cref="SoundManager"/>의 채널 API(믹서 있으면 믹서,
/// 없으면 폴백 곱셈)로, 화면은 <see cref="ScreenSettings"/>, 그래픽은 <see cref="GraphicsQualitySettings"/>로
/// 나간다. 이 클래스는 값을 들고 있지 않다 — 표시와 입력 전달만 한다.</para>
///
/// <para><b>프리팹 대신 런타임 생성</b>인 이유는 <see cref="UI_EscMenu"/>와 같다. UIManager.ShowPopupUI는
/// "UI/Popup/{타입명}" Addressable 프리팹을 요구하는데, 항목을 추가하려면 Addressables 그룹 에셋을
/// 함께 건드려야 하고 번들 재빌드가 빠지면 조용히 안 뜬다. 앵커를 직접 잡으므로 21:9·4:3·저해상도에서
/// 레이아웃이 깨질 여지도 없다.</para>
///
/// <para><b>2단 구성이고 ScrollRect를 쓰지 않는다.</b> 항목이 13개로 늘어 한 열로는 세로가 남지 않는데,
/// 스크롤을 넣으면 마스크가 TMP_Dropdown의 펼친 목록까지 잘라 먹는다(목록은 드롭다운 자신의 자식으로
/// 생성된다). 왼쪽 열 6행 + 오른쪽 열 7행으로 나누면 패널이 1480×620에 들어가고, 이 크기는 검증 대상
/// 최소치인 5:4(논리 1611×1289)·21:9(2217×935)·1280×720(1920×1080) 어디서도 화면 밖으로 안 나간다.</para>
///
/// <para>시간 정지는 <b>잡지 않는다</b>. 인게임에선 항상 ESC 메뉴(TimeScaleArbiter 보유) 위에 겹쳐 열리고,
/// 로비에선 멈출 게임플레이가 없다. 여기서 또 잡으면 로비의 연출까지 같이 멈춘다.</para>
/// </summary>
public sealed class UI_Settings : MonoBehaviour
{
    // ── Constants ────────────────────────────────────────────
    private const int SortingOrder = UISortingOrder.SystemModalTop;

    private const float PanelW = 1480f, PanelH = 620f;
    private const float SidePad = 40f, TopPad = 30f;
    private const float RowH = 48f, RowGap = 6f;
    private const float TitleH = 54f, SectionH = 34f, SectionGap = 14f;

    private const float LabelW = 200f;
    private const float CtrlW  = 340f;
    private const float ValueW = 110f;

    // 한 열 = 라벨 200 + 20 + 컨트롤 340 + 10 + 수치 110.
    private const float ColW   = LabelW + 20f + CtrlW + 10f + ValueW; // 680
    private const float ColGap = 40f;

    // 열 중심의 패널 로컬 x. 왼쪽에 오디오+화면(6행), 오른쪽에 그래픽(7행)이 앉아 높이가 맞는다.
    private const float ColLeftX  = -PanelW * 0.5f + SidePad + ColW * 0.5f;                 // -360
    private const float ColRightX = -PanelW * 0.5f + SidePad + ColW + ColGap + ColW * 0.5f; // +360

    // 열 중심 기준 오프셋 — 라벨 열 / 컨트롤 열 / 수치 열
    private const float LabelDx = -ColW * 0.5f + LabelW * 0.5f;               // -240
    private const float CtrlDx  = -ColW * 0.5f + LabelW + 20f + CtrlW * 0.5f; // +50
    private const float ValueDx =  ColW * 0.5f - ValueW * 0.5f;               // +285

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
    private static readonly Color ConfirmColor = new(0.20f, 0.34f, 0.50f, 1f);   // 확정 = 강조
    private static readonly Color CancelColor  = new(0.16f, 0.17f, 0.22f, 1f);   // 되돌림 = 보조

    // ── Static ───────────────────────────────────────────────
    private static UI_Settings _instance;

    // ── Private ──────────────────────────────────────────────
    private GameObject _root;

    private Slider _masterSlider, _bgmSlider, _sfxSlider, _uiSlider;
    private TMP_Text _masterValue, _bgmValue, _sfxValue, _uiValue;

    private TMP_Dropdown _resolutionDropdown;
    private TMP_Text     _windowModeLabel;

    private TMP_Dropdown _presetDropdown, _aaDropdown, _shadowDropdown, _postDropdown, _fpsDropdown;
    private Slider       _renderScaleSlider;
    private TMP_Text     _renderScaleValue, _vsyncLabel;

    private List<Vector2Int> _resolutions = new();

    // 화면 설정은 <b>즉시 적용하지 않는다</b> — 드롭다운을 고를 때마다 창이 다시 잡히면
    // 펼쳐 둔 목록이 사라지고 되돌릴 방법도 없다. 여기 담아 두었다가 「완료」에서 한 번에 나간다.
    private Vector2Int _pendingRes;
    private bool _fullscreen = true;

    // 열 때의 값. 「취소」로 여기까지 되돌린다 — 소리·그래픽은 조절하는 동안 미리보기로 살아 있어서
    // 되돌릴 기준이 있어야 한다.
    private GraphicsQualitySettings.Snapshot _entryGraphics;
    private float _entryMaster, _entryBgm, _entrySfx, _entryUi;
    private bool  _entryCaptured;
    private bool _suppressCallbacks;   // 값 복원 중 onValueChanged가 되먹임되는 것을 막는다
    private bool _volumeDirty;         // 드래그 중 미뤄 둔 볼륨 저장이 남아 있는가
    private bool _graphicsDirty;       // 렌더 스케일 드래그 중 미뤄 둔 저장이 남아 있는가

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
        if (!_volumeDirty && !_graphicsDirty) return;
        if (Input.GetMouseButton(0)) return;

        if (_volumeDirty)
        {
            _volumeDirty = false;
            Managers.Sound?.SaveVolumePrefs();
        }

        if (_graphicsDirty)
        {
            _graphicsDirty = false;
            GraphicsQualitySettings.SavePrefs();
        }
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

        _entryGraphics = GraphicsQualitySettings.Capture();
        var sound0 = Managers.Sound;
        if (sound0 != null)
        {
            sound0.Init();
            _entryMaster = sound0.MasterVolume;
            _entryBgm    = sound0.BgmVolume;
            _entrySfx    = sound0.EffectVolume;
            _entryUi     = sound0.UiVolume;
        }
        _entryCaptured = true;

        RefreshFromSystems();
        _root.SetActive(true);
        _root.transform.SetAsLastSibling();
    }

    /// <summary>
    /// 「취소」와 ESC가 같이 쓰는 경로. 미리보기로 바뀐 소리·그래픽을 <b>열 때 값으로 되돌리고</b> 닫는다.
    /// 화면(해상도·창모드)은 애초에 적용하지 않았으므로 되돌릴 것이 없다.
    /// </summary>
    private void CloseInternal()
    {
        if (_entryCaptured)
        {
            // 개별 노브(AA·그림자·프레임 상한 등)는 각자 SavePrefs를 이미 해 버렸다 —
            // 되돌림도 저장까지 해야 다음 부팅에 만져 본 값이 살아 돌아오지 않는다.
            GraphicsQualitySettings.RestoreSnapshot(_entryGraphics, save: true);

            var sound = Managers.Sound;
            if (sound != null)
            {
                sound.SetMasterVolume(_entryMaster, save: false);
                sound.SetBgmVolume   (_entryBgm,    save: false);
                sound.SetEffectVolume(_entrySfx,    save: false);
                sound.SetUiVolume    (_entryUi,     save: false);
                sound.SaveVolumePrefs();
            }

            _volumeDirty = _graphicsDirty = false;
            _entryCaptured = false;
        }

        if (_root != null) _root.SetActive(false);
    }

    /// <summary>
    /// 「완료」 — 지금 화면에 보이는 값을 확정한다.
    /// 미리보기로 이미 걸려 있는 소리·그래픽은 저장만 하면 되고,
    /// 화면(해상도·창모드)은 <b>여기서 처음 적용된다</b>.
    /// </summary>
    private void ConfirmInternal()
    {
        _volumeDirty = _graphicsDirty = false;
        _entryCaptured = false;   // 아래 닫기가 되돌리지 않도록 먼저 끈다

        Managers.Sound?.SaveVolumePrefs();
        GraphicsQualitySettings.SavePrefs();

        // 값이 그대로면 건드리지 않는다 — 같은 해상도로 SetResolution을 부르면 창만 한 번 깜빡인다.
        bool changed = !ScreenSettings.HasSaved
                       || ScreenSettings.SavedWidth  != _pendingRes.x
                       || ScreenSettings.SavedHeight != _pendingRes.y
                       || ScreenSettings.SavedFullscreen != _fullscreen;
        if (changed && _pendingRes.x > 0 && _pendingRes.y > 0)
            ScreenSettings.Apply(_pendingRes.x, _pendingRes.y, _fullscreen);

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
            SetSlider(_uiSlider,     _uiValue,     sound.UiVolume);
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

        // 열 때의 선택이 곧 초기 대기값이다. 아무것도 안 고치고 「완료」를 눌러도 화면이 안 흔들린다.
        _pendingRes = (index >= 0 && index < _resolutions.Count) ? _resolutions[index] : current;

        RefreshGraphicsFromSystems();

        _suppressCallbacks = false;
    }

    /// <summary>
    /// 그래픽 표시값을 <see cref="GraphicsQualitySettings"/>에서 다시 읽는다.
    /// <b>호출 측이 <see cref="_suppressCallbacks"/>를 잡아 준다</b> — 프리셋 전환처럼 다른 노브까지
    /// 한꺼번에 되돌리는 경로에서도 재진입 없이 쓰기 위해서다.
    /// </summary>
    private void RefreshGraphicsFromSystems()
    {
        SetDropdown(_presetDropdown, GraphicsQualitySettings.PresetIndex);
        SetDropdown(_aaDropdown,     GraphicsQualitySettings.Antialiasing);
        SetDropdown(_shadowDropdown, GraphicsQualitySettings.Shadows);
        SetDropdown(_postDropdown,   GraphicsQualitySettings.PostProcessing);
        SetDropdown(_fpsDropdown,    GraphicsQualitySettings.FrameRateIndex);

        SetSlider(_renderScaleSlider, _renderScaleValue, GraphicsQualitySettings.RenderScale);
        UpdateVSyncRow();
    }

    private static void SetDropdown(TMP_Dropdown dropdown, int value)
    {
        if (dropdown == null) return;
        dropdown.SetValueWithoutNotify(Mathf.Clamp(value, 0, Mathf.Max(0, dropdown.options.Count - 1)));
        dropdown.RefreshShownValue();
    }

    /// <summary>
    /// vSync가 켜져 있으면 <c>Application.targetFrameRate</c>는 무시된다 — 죽은 노브를 남기지 않도록
    /// 프레임 상한 드롭다운을 함께 잠근다(상호배타).
    ///
    /// <para>잠근 다음엔 <b>글자도 바꾼다</b>. 옵션 인덱스가 가리키는 이름을 그대로 두면 잠긴 행에
    /// 「무제한」이 남아 실제 상한과 어긋나 보인다 — 표시는
    /// <see cref="GraphicsQualitySettings.FrameCapDisplay"/>가 정한다. 이 함수는 표시값을 다시 읽는
    /// 모든 경로의 끝에서 불리므로(SetDropdown의 RefreshShownValue 뒤) 캡션 덮어쓰기가 살아남는다.</para>
    /// </summary>
    private void UpdateVSyncRow()
    {
        bool on = GraphicsQualitySettings.VSync;

        if (_vsyncLabel != null)
            _vsyncLabel.text = on ? "켜기" : "끄기";

        if (_fpsDropdown == null) return;

        _fpsDropdown.interactable = !on;
        if (_fpsDropdown.captionText != null)
            _fpsDropdown.captionText.text = GraphicsQualitySettings.FrameCapDisplay;
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
        float top = PanelH * 0.5f - TopPad;
        top = AddTitle(panel.transform, top) - 8f;

        // ── 왼쪽 열 — 오디오 4 + 화면 2 (총 418) ──
        float y = top;
        y = AddSectionLabel(panel.transform, ColLeftX, y, "오디오");
        y = AddVolumeRow(panel.transform, ColLeftX, y, "마스터", out _masterSlider, out _masterValue, OnMasterChanged);
        y = AddVolumeRow(panel.transform, ColLeftX, y, "배경음", out _bgmSlider,    out _bgmValue,    OnBgmChanged);
        y = AddVolumeRow(panel.transform, ColLeftX, y, "효과음", out _sfxSlider,    out _sfxValue,    OnSfxChanged);
        y = AddVolumeRow(panel.transform, ColLeftX, y, "UI",     out _uiSlider,     out _uiValue,     OnUiChanged);

        y -= SectionGap;
        y = AddSectionLabel(panel.transform, ColLeftX, y, "화면  (완료 시 적용)");
        y = AddResolutionRow(panel.transform, ColLeftX, y);
        y = AddWindowModeRow(panel.transform, ColLeftX, y);

        // ── 오른쪽 열 — 그래픽 7 (총 418) ──
        y = top;
        y = AddSectionLabel(panel.transform, ColRightX, y, "그래픽");
        y = AddDropdownRow(panel.transform, ColRightX, y, "품질 프리셋", "Preset",
                           GraphicsQualitySettings.PresetNames, OnPresetChanged, out _presetDropdown);
        y = AddSliderRow(panel.transform, ColRightX, y, "렌더 스케일",
                         GraphicsQualitySettings.MinRenderScale, GraphicsQualitySettings.MaxRenderScale,
                         out _renderScaleSlider, out _renderScaleValue, OnRenderScaleChanged);
        y = AddDropdownRow(panel.transform, ColRightX, y, "안티앨리어싱", "Antialiasing",
                           GraphicsQualitySettings.AaNames, OnAntialiasingChanged, out _aaDropdown);
        y = AddDropdownRow(panel.transform, ColRightX, y, "그림자 품질", "Shadow",
                           GraphicsQualitySettings.ShadowNames, OnShadowChanged, out _shadowDropdown);
        y = AddDropdownRow(panel.transform, ColRightX, y, "포스트프로세싱", "Post",
                           GraphicsQualitySettings.PostNames, OnPostProcessingChanged, out _postDropdown);
        y = AddToggleRow(panel.transform, ColRightX, y, "수직 동기화", "VSync",
                         OnToggleVSync, out _vsyncLabel);
        y = AddDropdownRow(panel.transform, ColRightX, y, "프레임 상한", "FrameCap",
                           GraphicsQualitySettings.FpsNames, OnFrameRateChanged, out _fpsDropdown);

        AddBottomButtons(panel.transform);
    }

    private static float AddTitle(Transform panel, float y)
    {
        NewLabel("Title", panel, "설정", 36f, TitleColor, new Vector2(0f, y - TitleH * 0.5f),
                 new Vector2(PanelW - SidePad * 2f, TitleH), TextAlignmentOptions.Center);
        return y - TitleH - 6f;
    }

    private static float AddSectionLabel(Transform panel, float cx, float y, string text)
    {
        NewLabel($"Section_{text}", panel, text, 24f, SectionColor,
                 new Vector2(cx + LabelDx, y - SectionH * 0.5f), new Vector2(LabelW, SectionH),
                 TextAlignmentOptions.Left);
        return y - SectionH - RowGap;
    }

    /// <summary>행의 왼쪽 라벨을 놓고, 컨트롤이 앉을 중심 y를 돌려준다.</summary>
    private static float AddRowLabel(Transform panel, float cx, float y, string name, string label)
    {
        float cy = y - RowH * 0.5f;
        NewLabel($"Label_{name}", panel, label, 24f, LabelColor,
                 new Vector2(cx + LabelDx, cy), new Vector2(LabelW, RowH), TextAlignmentOptions.Left);
        return cy;
    }

    private float AddVolumeRow(Transform panel, float cx, float y, string label,
                               out Slider slider, out TMP_Text valueText,
                               UnityEngine.Events.UnityAction<float> onChanged)
        => AddSliderRow(panel, cx, y, label, 0f, 1f, out slider, out valueText, onChanged);

    private float AddSliderRow(Transform panel, float cx, float y, string label,
                               float min, float max,
                               out Slider slider, out TMP_Text valueText,
                               UnityEngine.Events.UnityAction<float> onChanged)
    {
        float cy = AddRowLabel(panel, cx, y, label, label);

        slider = NewSlider(panel, $"Slider_{label}", new Vector2(cx + CtrlDx, cy), new Vector2(CtrlW, 24f), min, max);
        slider.onValueChanged.AddListener(onChanged);

        valueText = NewLabel($"Value_{label}", panel, "100%", 22f, LabelColor,
                             new Vector2(cx + ValueDx, cy), new Vector2(ValueW, RowH), TextAlignmentOptions.Right);

        return y - RowH - RowGap;
    }

    private float AddDropdownRow(Transform panel, float cx, float y, string label, string name,
                                 IReadOnlyList<string> options,
                                 UnityEngine.Events.UnityAction<int> onChanged,
                                 out TMP_Dropdown dropdown)
    {
        float cy = AddRowLabel(panel, cx, y, name, label);

        dropdown = NewDropdown(panel, $"Dropdown_{name}", new Vector2(cx + CtrlDx, cy), new Vector2(CtrlW, 44f));

        var list = new List<string>(options.Count);
        for (int i = 0; i < options.Count; i++)
            list.Add(options[i]);
        dropdown.AddOptions(list);

        dropdown.onValueChanged.AddListener(onChanged);
        return y - RowH - RowGap;
    }

    /// <summary>가운데 글자만 바뀌는 토글 버튼 행(창 모드·수직 동기화). 상태 글자는 out으로 넘긴다.</summary>
    private float AddToggleRow(Transform panel, float cx, float y, string label, string name,
                               UnityEngine.Events.UnityAction onClick, out TMP_Text stateLabel)
    {
        float cy = AddRowLabel(panel, cx, y, name, label);

        var img = NewImage($"Btn_{name}", panel, CtrlBg);
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(cx + CtrlDx, cy);
        rt.sizeDelta = new Vector2(CtrlW, 44f);

        var btn = img.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);
        img.gameObject.AddComponent<UIButtonFeedback>();

        stateLabel = NewLabel("Label", img.transform, string.Empty, 22f, LabelColor,
                              Vector2.zero, new Vector2(CtrlW, 44f), TextAlignmentOptions.Center);

        return y - RowH - RowGap;
    }

    private float AddResolutionRow(Transform panel, float cx, float y)
    {
        float cy = AddRowLabel(panel, cx, y, "Resolution", "해상도");

        // 항목은 모니터가 보고하는 목록이라 RefreshFromSystems가 채운다.
        _resolutionDropdown = NewDropdown(panel, "Dropdown_Resolution", new Vector2(cx + CtrlDx, cy), new Vector2(CtrlW, 44f));
        _resolutionDropdown.onValueChanged.AddListener(OnResolutionChanged);

        return y - RowH - RowGap;
    }

    private float AddWindowModeRow(Transform panel, float cx, float y)
        => AddToggleRow(panel, cx, y, "창 모드", "WindowMode", OnToggleWindowMode, out _windowModeLabel);

    /// <summary>
    /// 패널 아래쪽에 고정. 두 열의 길이가 달라져도 버튼 위치는 흔들리지 않는다.
    /// 「완료」가 확정, 「취소」(=ESC)가 되돌림 — 닫는 길이 둘로 갈려야 미리보기가 성립한다.
    /// </summary>
    private void AddBottomButtons(Transform panel)
    {
        const float W = 240f, H = 60f, BottomPad = 28f, Gap = 24f;

        float cy = -PanelH * 0.5f + BottomPad + H * 0.5f;
        MakeBottomButton(panel, "Btn_Cancel",  "취소", new Vector2(-(W + Gap) * 0.5f, cy), W, H, CancelColor,  CloseInternal);
        MakeBottomButton(panel, "Btn_Confirm", "완료", new Vector2( (W + Gap) * 0.5f, cy), W, H, ConfirmColor, ConfirmInternal);
    }

    private static void MakeBottomButton(Transform panel, string name, string label, Vector2 pos,
                                         float w, float h, Color fill, UnityEngine.Events.UnityAction onClick)
    {
        var img = NewImage(name, panel, fill);
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(w, h);

        var btn = img.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);
        img.gameObject.AddComponent<UIButtonFeedback>();

        NewLabel("Label", img.transform, label, 26f, LabelColor, Vector2.zero,
                 new Vector2(w, h), TextAlignmentOptions.Center);
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
    private static Slider NewSlider(Transform parent, string name, Vector2 pos, Vector2 size, float min, float max)
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
        slider.minValue      = min;
        slider.maxValue      = max;
        slider.wholeNumbers  = false;
        slider.value         = max;
        return slider;
    }

    /// <summary>
    /// TMP_Dropdown은 캡션/화살표 외에 <b>비활성 Template</b>(Viewport + Content + Item Toggle)이 반드시
    /// 있어야 목록이 열린다. 프리팹 없이 쓰려면 그 계층을 손으로 만들어 줘야 한다.
    /// </summary>
    private static TMP_Dropdown NewDropdown(Transform parent, string name, Vector2 pos, Vector2 size)
    {
        const float ItemH = 32f;
        const float ListH = 168f;
        const float Gap   = 2f;

        // 목록은 컨트롤 아래로 펼쳐지는 게 기본인데, 아래쪽 행에선 그러면 패널 밖으로 흘러나온다.
        // TMP_Dropdown이 스스로 하는 반전은 <b>루트 캔버스</b> 밖으로 나갈 때만 걸리므로(Show의
        // FlipLayoutOnAxis는 rootCanvasRect 기준) 화면 안이기만 하면 패널을 넘든 말든 그냥 둔다.
        // 목록 높이를 줄이면 여섯 항목짜리(프레임 상한)가 더 잘게 스크롤될 뿐이고, ScrollRect는
        // 클래스 주석대로 마스크가 목록을 잘라 먹는다 → 방향만 뒤집는다.
        bool dropUp = pos.y - size.y * 0.5f - Gap - ListH < -PanelH * 0.5f;

        var bg = NewImage(name, parent, CtrlBg);
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
        // 펼친 목록은 이 템플릿의 앵커·피벗을 그대로 복제해 쓴다(TMP_Dropdown.Show가 SetParent(.., false)로
        // 붙인다) → 위로 펼치려면 여기서 위 모서리에 걸어 두면 된다. 항목이 적어 목록이 줄어들 때도
        // 피벗 쪽 모서리가 고정되므로 어느 방향이든 컨트롤에 붙어 있다.
        var tRt = template.GetComponent<RectTransform>();
        tRt.anchorMin = new Vector2(0f, dropUp ? 1f : 0f);
        tRt.anchorMax = new Vector2(1f, dropUp ? 1f : 0f);
        tRt.pivot     = new Vector2(0.5f, dropUp ? 0f : 1f);
        tRt.anchoredPosition = new Vector2(0f, dropUp ? Gap : -Gap);
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

    private void OnUiChanged(float v)
    {
        if (_suppressCallbacks) return;
        Managers.Sound?.SetUiVolume(v, save: false);
        SetValueText(_uiValue, v);
        _volumeDirty = true;
    }

    // 화면은 여기서 적용하지 않는다 — 고른 값만 담고, 실제 전환은 ConfirmInternal(「완료」)이 한다.
    private void OnResolutionChanged(int index)
    {
        if (_suppressCallbacks) return;
        if (index < 0 || index >= _resolutions.Count) return;

        _pendingRes = _resolutions[index];
    }

    private void OnToggleWindowMode()
    {
        _fullscreen = !_fullscreen;
        UpdateWindowModeLabel();
    }

    // ── Event Handlers · 그래픽 ───────────────────────────────

    // 프리셋은 티어 에셋을 갈아끼우면서 개별 노브도 그 티어 기본값으로 되돌린다 → 표시를 통째로 다시 읽는다.
    private void OnPresetChanged(int index)
    {
        if (_suppressCallbacks) return;

        GraphicsQualitySettings.SetPreset(index);

        _suppressCallbacks = true;
        RefreshGraphicsFromSystems();
        _suppressCallbacks = false;
    }

    // 렌더 스케일은 볼륨과 같은 드래그 경로다 — 즉시 반영만 하고 저장은 손을 뗄 때 Update가 한 번 한다.
    private void OnRenderScaleChanged(float v)
    {
        if (_suppressCallbacks) return;

        GraphicsQualitySettings.SetRenderScale(v, save: false);
        SetValueText(_renderScaleValue, v);
        _graphicsDirty = true;
        SyncPresetLabel();
    }

    private void OnAntialiasingChanged(int index)
    {
        if (_suppressCallbacks) return;
        GraphicsQualitySettings.SetAntialiasing(index);
        SyncPresetLabel();
    }

    private void OnShadowChanged(int index)
    {
        if (_suppressCallbacks) return;
        GraphicsQualitySettings.SetShadows(index);
        SyncPresetLabel();
    }

    private void OnPostProcessingChanged(int index)
    {
        if (_suppressCallbacks) return;
        GraphicsQualitySettings.SetPostProcessing(index);
        SyncPresetLabel();
    }

    private void OnToggleVSync()
    {
        GraphicsQualitySettings.SetVSync(!GraphicsQualitySettings.VSync);
        UpdateVSyncRow();
        SyncPresetLabel();
    }

    private void OnFrameRateChanged(int index)
    {
        if (_suppressCallbacks) return;
        GraphicsQualitySettings.SetFrameRateIndex(index);
        SyncPresetLabel();
    }

    /// <summary>개별 노브를 만졌으면 프리셋 표시를 「사용자 지정」으로 옮긴다.</summary>
    private void SyncPresetLabel()
    {
        _suppressCallbacks = true;
        SetDropdown(_presetDropdown, GraphicsQualitySettings.PresetIndex);
        _suppressCallbacks = false;
    }
}
