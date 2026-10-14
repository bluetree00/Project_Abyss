using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// ESC 메뉴 — 계속하기 / 설정 / 로비로 가기 / 게임 종료.
///
/// 프리팹·Addressable 없이 런타임에 자기 UI를 만든다. UIManager.ShowPopupUI 경로는
/// "UI/Popup/{타입명}" Addressable 프리팹을 요구하는데, 이 메뉴 하나 때문에 프리팹 저작과
/// 번들 재빌드를 묶어두면 손이 많이 가고 배선이 빠지면 조용히 안 뜬다.
/// 자체 Canvas(sortingOrder 500)를 들고 있어 상위 계층 구성에 의존하지 않는다.
///
/// 시간 정지는 기존 ESC 북과 동일하게 TimeScaleArbiter로 잡는다(Pause 우선순위).
/// </summary>
public sealed class UI_EscMenu : MonoBehaviour
{
    // ── Constants ────────────────────────────────────────────
    private const int   SortingOrder = UISortingOrder.SystemModal;
    // 10-11 위계 개편(리서치 C §6 「고칠 값」) — 66px 베벨 · 간격 16 → 48px 평판 · 간격 8, 고른 줄은 위아래 1px 금 띠(RoR2 일시정지).
    // 세로 = 위 여백 30 + 제목 44 + 24 + 버튼 5개(5×48 + 4×8 = 272) + 아래 여백 30 = 400.
    // 버튼을 늘리거나 줄이면 PanelH도 ±(BtnH + BtnGap) 해야 여백 대칭이 유지된다.
    private const float PanelW = 420f, PanelH = 512f;   // 10-13 「장비」 · 10-14 「업적」 버튼 하나씩 더(+BtnH + BtnGap)
    private const float BtnW   = 320f, BtnH   = 48f, BtnGap = 8f;
    private const float PadY   = 30f, TitleH = 44f, TitleGap = 24f;
    private const float TitleSize = 32f, LabelSize = 22f;

    private static readonly Color Backdrop   = new(0f, 0f, 0f, 0.72f);
    // 뜻은 셋만 — 주 버튼(계속하기) 금 · 보조 잉크 · 종료 진홍(09-28 UI 톤 통일의 세 갈래를 평판 위 글자색 · 옅은 바탕으로 옮긴다).
    private static readonly Color LabelColor  = UITheme.Thin.Ink;
    private static readonly Color TitleColor  = UITheme.Gold;
    private static readonly Color PlateIdle   = new(1f, 1f, 1f, 0.035f);
    private static readonly Color PlateCta    = UITheme.Thin.WithA(UITheme.Thin.Gold, 0.10f);
    private static readonly Color PlateDanger = UITheme.Thin.WithA(UITheme.Thin.Danger, 0.12f);

    // ── Private ──────────────────────────────────────────────
    private GameObject    _root;
    private RectTransform _panel;
    private bool          _quitting;
    private UI_RelicMemoryPage _memoryPage;   // 「되찾은 기억」 — 보유 유물 조각 보기(유물 성장 v2 §6-5)
    private UI_EquipmentPage   _equipPage;    // 「장비」 — 무기 두 칸 · 강화 · 형 · 스킬 단계(10-13 4b 요청 8)
    private TMP_Text      _lobbyLabel;
    private bool          _lobbyArmed;   // 런 중 「로비로 가기」 첫 누름 — 한 번 더 눌러야 포기한다
    private TMP_Text      _quitLabel;
    private bool          _quitArmed;    // 전투 중(피해를 받은 뒤) 「게임 종료」 첫 누름 — 지금 끄면 이어하기에서 사망 정산(10-10 Hades식)

    // ── Properties ───────────────────────────────────────────
    public bool IsOpen => _root != null && (_root.TryGetComponent<UIFader>(out var f) ? f.Visible : _root.activeSelf);

    // ── Lifecycle ────────────────────────────────────────────
    private void OnDestroy()
    {
        // 강제 파괴(씬 전환 등)에도 timeScale이 0으로 남지 않게. Release는 멱등이다.
        TimeScaleArbiter.Release(this);
    }

    // ── Public Methods ───────────────────────────────────────
    public void Open()
    {
        if (_quitting) return;
        if (!IsOpen) Managers.Sound?.PlayUiEvent(SoundEvent.UiOpen);
        if (_root == null) Build();

        UIFader.On(_root, _panel).Show();   // 순간 등장 → 공통 박자(09-28)
        TimeScaleArbiter.Acquire(this, 0f, TimeScaleArbiter.Priority.Pause);
    }

    public void Close()
    {
        if (IsOpen) Managers.Sound?.PlayUiEvent(SoundEvent.UiClose);
        DisarmLobby();
        CloseMemoryPage();
        CloseEquipPage();
        if (_root != null) UIFader.On(_root, _panel).Hide();
        TimeScaleArbiter.Release(this);
    }

    // ── Private Methods ──────────────────────────────────────
    private void Build()
    {
        _root = new GameObject("EscMenuRoot", typeof(RectTransform), typeof(Canvas),
                               typeof(CanvasScaler), typeof(GraphicRaycaster));
        _root.transform.SetParent(transform, false);
        // 루트를 화면 전체로 — 새 RectTransform 기본 100×100이라 막(Backdrop)이 패널 뒤 가운데에만 깔려
        // 화면이 한 번도 어두워지지 않았다(09-28 UI 전수, 「팝업 루트 100×100」 함정).
        Stretch((RectTransform)_root.transform);

        var canvas = _root.GetComponent<Canvas>();
        canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = SortingOrder;

        // 프로젝트 공통 기준 — 1920x1080 / Scale With Screen Size / Match 0.5
        var scaler = _root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight  = 0.5f;

        // 뒷배경 — 클릭이 게임으로 새지 않도록 화면 전체를 덮는다
        var dim = NewImage("Backdrop", _root.transform, Backdrop);
        Stretch(dim.rectTransform);

        // 패널 — 인디고 글래스 + 금 가는 선
        var panel = NewImage("Panel", _root.transform, UITheme.Window);
        var prt = panel.rectTransform;
        prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
        prt.anchoredPosition = Vector2.zero;
        prt.sizeDelta = new Vector2(PanelW, PanelH);
        UITheme.StylePanel(panel, UITheme.Window);
        _panel = prt;

        // 제목 — DNF는 이 한 줄에만(가짜 굵게 없음, 부드러운 그림자는 StyleText).
        UITheme.StyleText(NewLabel("Title", panel.transform, "일시정지", TitleSize, TitleColor,
                 new Vector2(0f, PanelH * 0.5f - PadY - TitleH * 0.5f), new Vector2(PanelW - 40f, TitleH)), TitleColor);

        // 첫 버튼 중심 y — 위 여백 · 제목 · 간격을 뺀 자리에서 버튼 반 높이만큼 내려온다(아래 여백도 PadY로 같다).
        float top  = PanelH * 0.5f - PadY - TitleH - TitleGap - BtnH * 0.5f;
        float step = BtnH + BtnGap;
        NewButton(panel.transform, "계속하기",   new Vector2(0f, top),            PlateCta,    UITheme.Thin.Gold,   OnResume);
        NewButton(panel.transform, "되찾은 기억", new Vector2(0f, top - step),     PlateIdle,   LabelColor,          OnMemory);
        NewButton(panel.transform, "장비",       new Vector2(0f, top - step * 2f), PlateIdle,   LabelColor,          OnEquipment);
        NewButton(panel.transform, "업적",       new Vector2(0f, top - step * 3f), PlateIdle,   LabelColor,          OnAchievements);
        NewButton(panel.transform, "설정",       new Vector2(0f, top - step * 4f), PlateIdle,   LabelColor,          OnSettings);
        NewButton(panel.transform, "로비로 가기", new Vector2(0f, top - step * 5f), PlateIdle,   LabelColor,          OnLobby);
        _lobbyLabel = panel.transform.Find("Btn_로비로 가기/Label")?.GetComponent<TMP_Text>();
        NewButton(panel.transform, "게임 종료",   new Vector2(0f, top - step * 6f), PlateDanger, UITheme.Thin.Danger, OnQuit);
        _quitLabel = panel.transform.Find("Btn_게임 종료/Label")?.GetComponent<TMP_Text>();
    }

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

    private static TMP_Text NewLabel(string name, Transform parent, string text, float size,
                                     Color color, Vector2 pos, Vector2 sizeDelta)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = sizeDelta;

        var tmp = go.AddComponent<TextMeshProUGUI>();
        var font = TMP_Settings.defaultFontAsset;
        if (font == null || font.material == null)
            font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        if (font != null) tmp.font = font;

        tmp.text          = text;
        tmp.fontSize      = size;
        tmp.color         = color;
        tmp.alignment     = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
        return tmp;
    }

    /// <summary>
    /// 평판 버튼 한 줄 — 옅은 바탕(뜻별) + 위아래 1px 금 띠(고르거나 마우스가 올라오면 0.14초에 걸쳐 85%로 켜진다) + Noto 500 글자.
    /// 66px 베벨은 메뉴 한 줄이 판보다 무거워 보였다(리서치 C §6 — RoR2 46px 평판 · 간격 8).
    /// </summary>
    private void NewButton(Transform parent, string label, Vector2 pos, Color plate, Color ink,
                           UnityEngine.Events.UnityAction onClick)
    {
        var img = NewImage($"Btn_{label}", parent, plate);
        var rt  = img.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(BtnW, BtnH);

        var btn = img.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.transition    = Selectable.Transition.None;   // 고른 표시는 금 띠가 맡는다 — 틴트까지 겹치면 표시가 둘이 된다
        btn.onClick.AddListener(onClick);

        // UI_Popup을 상속하지 않아 자동 부착 경로를 못 탄다 — 팝업과 같은 손맛을 여기서 직접 건다.
        img.gameObject.AddComponent<UIButtonFeedback>();
        img.gameObject.AddComponent<SelectBand>().Init(Band(rt, "BandTop", 1f), Band(rt, "BandBottom", 0f));

        var t = NewLabel("Label", img.transform, label, LabelSize, ink, Vector2.zero, new Vector2(BtnW, BtnH));
        UITheme.StyleThinText(t, UIWeight.Medium, LabelSize, ink);
    }

    /// <summary>버튼 위(y=1) · 아래(y=0) 가장자리에 붙는 1px 금 띠. 처음엔 투명.</summary>
    private static Image Band(RectTransform host, string name, float y)
    {
        var img = NewImage(name, host, UITheme.Thin.WithA(UITheme.Thin.Gold, 0f));
        img.raycastTarget = false;
        var rt = img.rectTransform;
        rt.anchorMin = new Vector2(0f, y);
        rt.anchorMax = new Vector2(1f, y);
        rt.pivot     = new Vector2(0.5f, y);
        rt.sizeDelta = new Vector2(0f, UITheme.Thin.Stroke);
        rt.anchoredPosition = Vector2.zero;
        return img;
    }

    private void DisarmLobby()
    {
        _lobbyArmed = false;
        if (_lobbyLabel != null) _lobbyLabel.text = "로비로 가기";
        _quitArmed = false;
        if (_quitLabel != null) _quitLabel.text = "게임 종료";
    }

    private void CloseEquipPage()
    {
        if (_equipPage == null || !_equipPage.gameObject.activeSelf) return;
        _equipPage.Close();
        if (_panel != null) _panel.gameObject.SetActive(true);
    }

    private void CloseMemoryPage()
    {
        if (_memoryPage == null || !_memoryPage.gameObject.activeSelf) return;
        _memoryPage.Close();
        if (_panel != null) _panel.gameObject.SetActive(true);
    }

    // ── Event Handlers ───────────────────────────────────────
    private void OnResume() => Close();

    /// <summary>
    /// 「업적」 — 어디서든 진행 · 받을 것을 본다(10-14 사용자 「업적과 해금을 잘 볼 수 있는 구조」). 이 메뉴는 시스템 정렬이라 팝업이 밑에 깔리므로
    /// 메뉴를 닫고 업적 화면을 연다. 정수는 기억의 제단에서 받는다(조회 전용 — 화면이 「제단에서 받기」라고 말한다).
    /// </summary>
    private void OnAchievements()
    {
        Managers.Sound?.PlayUiEvent(SoundEvent.UiClick);
        Close();
        UI_AwakeningPanel.OpenAchievementsAsync(readOnlyView: true).Forget();
    }

    // 일시정지 패널 자리에 「되찾은 기억」 쪽을 연다(같은 캔버스 · 같은 정지). 돌아가기로 패널이 돌아온다.
    private void OnMemory()
    {
        if (_root == null) return;
        Managers.Sound?.PlayUiEvent(SoundEvent.UiClick);
        _memoryPage ??= UI_RelicMemoryPage.Create(_root.transform, CloseMemoryPage);
        _panel.gameObject.SetActive(false);
        _memoryPage.Open();
    }

    // 설정은 이 메뉴를 닫지 않고 위에 겹쳐 연다(sortingOrder SystemModalTop).
    // 닫으면 시간 정지가 풀려 설정을 만지는 동안 게임이 다시 돈다.
    private void OnSettings() => UI_Settings.Open();

    // 「장비」 쪽 — 「되찾은 기억」과 같은 자리 · 같은 정지(10-13). 런 밖이면 빈 칸이 보인다.
    private void OnEquipment()
    {
        if (_root == null) return;
        Managers.Sound?.PlayUiEvent(SoundEvent.UiClick);
        _equipPage ??= UI_EquipmentPage.Create(_root.transform, CloseEquipPage);
        _panel.gameObject.SetActive(false);
        _equipPage.Open();
    }

    private void OnLobby()
    {
        // 런 중엔 한 번 더 눌러야 — 이 버튼은 런을 포기(정산 없음)한다. 확인 없이 바로 포기해 진행이 사라졌다(10-09 출시 점검).
        if (!_lobbyArmed && GameRunBootstrapper.Instance?.Run?.IsRunning == true)
        {
            _lobbyArmed = true;
            Managers.Sound?.PlayUiEvent(SoundEvent.UiClick);
            if (_lobbyLabel != null) _lobbyLabel.text = "런 포기 — 한 번 더";
            return;
        }

        // 시간부터 되돌린다 — 로딩 연출과 로비가 timeScale 0으로 멈춘 채 뜨면 안 된다.
        Close();

        // 진행 중 런이 있으면 <b>세션까지</b> 정식 종료한다.
        // AppBootstrapper.EndRun()만 부르면 세이브 폐기와 HUD 해제밖에 안 된다 —
        // GameRunSession.EndRun()을 안 태우면 룬판(MerlinRuneBridge.ClearBoard)이 DDOL로 살아남아
        // 다음 런에 이전 룬이 박힌 채 시작되고, EffectManager/CovenantHandler.Cleanup과
        // PlayerState.Deactivate도 건너뛴다.
        //
        // 순서 주의: AppBootstrapper.EndRun()이 <b>먼저</b>다. 그래야 OnRunEnded 구독이 끊긴 상태에서
        // 세션 종료가 돌아 <b>포기한 런에는 정산(정수·골드)을 주지 않는다</b>는 계약이 지켜진다.
        // 정리만 태우고 보상은 주지 않는 경로가 이것뿐이라 순서에 의미가 있다.
        var run = GameRunBootstrapper.Instance?.Run ?? AppBootstrapper.Instance?.CurrentRun;
        if (AppBootstrapper.Instance != null && AppBootstrapper.Instance.CurrentRun != null)
            AppBootstrapper.Instance.EndRun();
        run?.EndRun(isCleared: false, reason: "abandon");

        AppBootstrapper.Instance?.RequestLoad(Define.Scene.Lobby);
    }

    private void OnQuit()
    {
        // 피해를 받은 뒤 전투가 끝나기 전이면 한 번 더 — 지금 끄면 이 런은 이어하기에서 사망으로 정산된다(Hades의 「포기」 · 10-10).
        if (!_quitArmed && RunFlowController.Active != null && RunFlowController.Active.IsCombatLocked)
        {
            _quitArmed = true;
            Managers.Sound?.PlayUiEvent(SoundEvent.UiClick);
            if (_quitLabel != null) _quitLabel.text = "전투 중 — 끄면 런 사망";
            return;
        }

        _quitting = true;
        TimeScaleArbiter.Release(this);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ── Nested ───────────────────────────────────────────────

    /// <summary>
    /// 고른 줄 표시 — 마우스가 올라오거나(포인터) 패드 · 키보드로 골랐을 때(선택) 위아래 금 띠가 켜진다.
    /// 시간정지 중에도 돈다(<see cref="UIMotion"/> = unscaled). 메뉴가 꺼지면 띠를 바로 끈다.
    /// </summary>
    private sealed class SelectBand : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
    {
        private const float OnAlpha = 0.85f;

        private Image _top, _bottom;
        private bool  _hover, _selected;
        private float _a;
        private CancellationTokenSource _cts;

        private void OnDisable()
        {
            Cancel();
            _hover = _selected = false;
            SetAlpha(0f);
        }

        private void OnDestroy() => Cancel();

        public void Init(Image top, Image bottom) { _top = top; _bottom = bottom; }

        public void OnPointerEnter(PointerEventData e) { _hover = true;     Refresh(); }
        public void OnPointerExit(PointerEventData e)  { _hover = false;    Refresh(); }
        public void OnSelect(BaseEventData e)          { _selected = true;  Refresh(); }
        public void OnDeselect(BaseEventData e)        { _selected = false; Refresh(); }

        private void Refresh()
        {
            float to = _hover || _selected ? OnAlpha : 0f;
            Cancel();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
            FadeAsync(_a, to, _cts.Token).Forget();
        }

        private async UniTaskVoid FadeAsync(float from, float to, CancellationToken ct)
        {
            try { await UIMotion.Tween(UIMotion.Quick, k => SetAlpha(Mathf.Lerp(from, to, k)), ct); }
            catch (System.OperationCanceledException) { }
        }

        private void SetAlpha(float a)
        {
            _a = a;
            var c = UITheme.Thin.WithA(UITheme.Thin.Gold, a);
            if (_top != null)    _top.color = c;
            if (_bottom != null) _bottom.color = c;
        }

        private void Cancel()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }
    }
}
