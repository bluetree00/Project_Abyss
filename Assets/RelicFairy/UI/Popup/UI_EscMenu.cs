using TMPro;
using UnityEngine;
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
    // 버튼 5개 기준 높이(4개 422 + 「되찾은 기억」 82). 타이틀(중심 -52, 높이 48) 아래 17px, 마지막 버튼 아래 17px가 되도록 계산한 값이다.
    // 버튼을 늘리거나 줄이면 PanelH도 ±(BtnH + BtnGap) 해야 여백 대칭이 유지된다.
    private const float PanelW = 420f, PanelH = 504f;
    private const float BtnW   = 320f, BtnH   = 66f, BtnGap = 16f;

    private static readonly Color Backdrop   = new(0f, 0f, 0f, 0.72f);
    // 전 화면 공통 언어(UITheme) — 인디고 글래스 판 + 금 가는 선, 주 버튼 금 · 보조 먹빛 · 종료 가라앉은 진홍(09-28 UI 톤 통일).
    private static readonly Color LabelColor = UITheme.Ink;
    private static readonly Color TitleColor = UITheme.Gold;

    // ── Private ──────────────────────────────────────────────
    private GameObject    _root;
    private RectTransform _panel;
    private bool          _quitting;
    private UI_RelicMemoryPage _memoryPage;   // 「되찾은 기억」 — 보유 유물 조각 보기(유물 성장 v2 §6-5)

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
        if (_root == null) Build();

        UIFader.On(_root, _panel).Show();   // 순간 등장 → 공통 박자(09-28)
        TimeScaleArbiter.Acquire(this, 0f, TimeScaleArbiter.Priority.Pause);
    }

    public void Close()
    {
        CloseMemoryPage();
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

        UITheme.StyleText(NewLabel("Title", panel.transform, "일시정지", 34f, TitleColor,
                 new Vector2(0f, PanelH * 0.5f - 52f), new Vector2(PanelW - 40f, 48f)), TitleColor);

        // 버튼 묶음의 시작 y. 타이틀 아래 여백과 패널 하단 여백이 17px로 같아지는 값이다.
        float top  = PanelH * 0.5f - 126f;
        float step = BtnH + BtnGap;
        NewButton(panel.transform, "계속하기",   new Vector2(0f, top),            UITheme.CtaTint,       OnResume);
        NewButton(panel.transform, "되찾은 기억", new Vector2(0f, top - step),     UITheme.SecondaryTint, OnMemory);
        NewButton(panel.transform, "설정",       new Vector2(0f, top - step * 2f), UITheme.SecondaryTint, OnSettings);
        NewButton(panel.transform, "로비로 가기", new Vector2(0f, top - step * 3f), UITheme.SecondaryTint, OnLobby);
        NewButton(panel.transform, "게임 종료",   new Vector2(0f, top - step * 4f), UITheme.DangerTint,    OnQuit);
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

    private void NewButton(Transform parent, string label, Vector2 pos, Color tint,
                           UnityEngine.Events.UnityAction onClick)
    {
        var img = NewImage($"Btn_{label}", parent, tint);
        var rt  = img.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(BtnW, BtnH);
        UITheme.StyleButton(img, tint);   // 전 화면 같은 베벨 버튼(크기를 잡은 뒤 — 9-slice 배율이 칸 크기를 본다)

        var btn = img.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);

        // UI_Popup을 상속하지 않아 자동 부착 경로를 못 탄다 — 팝업과 같은 손맛을 여기서 직접 건다.
        img.gameObject.AddComponent<UIButtonFeedback>();

        UITheme.StyleText(NewLabel("Label", img.transform, label, 26f, LabelColor, Vector2.zero, new Vector2(BtnW, BtnH)), LabelColor);
    }

    private void CloseMemoryPage()
    {
        if (_memoryPage == null || !_memoryPage.gameObject.activeSelf) return;
        _memoryPage.Close();
        if (_panel != null) _panel.gameObject.SetActive(true);
    }

    // ── Event Handlers ───────────────────────────────────────
    private void OnResume() => Close();

    // 일시정지 패널 자리에 「되찾은 기억」 쪽을 연다(같은 캔버스 · 같은 정지). 돌아가기로 패널이 돌아온다.
    private void OnMemory()
    {
        if (_root == null) return;
        _memoryPage ??= UI_RelicMemoryPage.Create(_root.transform, CloseMemoryPage);
        _panel.gameObject.SetActive(false);
        _memoryPage.Open();
    }

    // 설정은 이 메뉴를 닫지 않고 위에 겹쳐 연다(sortingOrder SystemModalTop).
    // 닫으면 시간 정지가 풀려 설정을 만지는 동안 게임이 다시 돈다.
    private void OnSettings() => UI_Settings.Open();

    private void OnLobby()
    {
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
        _quitting = true;
        TimeScaleArbiter.Release(this);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
