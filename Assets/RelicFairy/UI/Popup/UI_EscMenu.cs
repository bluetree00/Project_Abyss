using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ESC 메뉴 — 계속하기 / 로비로 가기 / 게임 종료.
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
    private const float PanelW = 420f, PanelH = 340f;
    private const float BtnW   = 320f, BtnH   = 66f, BtnGap = 16f;

    private static readonly Color Backdrop   = new(0f, 0f, 0f, 0.72f);
    private static readonly Color PanelBg    = new(0.08f, 0.09f, 0.13f, 0.97f);
    private static readonly Color PanelEdge  = new(0.52f, 0.72f, 0.86f, 0.85f);
    private static readonly Color BtnBg      = new(0.16f, 0.19f, 0.27f, 1f);
    private static readonly Color BtnQuitBg  = new(0.30f, 0.13f, 0.15f, 1f);
    private static readonly Color LabelColor = new(0.94f, 0.96f, 1f, 1f);
    private static readonly Color TitleColor = new(1f, 0.88f, 0.55f, 1f);

    // ── Private ──────────────────────────────────────────────
    private GameObject _root;
    private bool       _quitting;

    // ── Properties ───────────────────────────────────────────
    public bool IsOpen => _root != null && _root.activeSelf;

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

        _root.SetActive(true);
        TimeScaleArbiter.Acquire(this, 0f, TimeScaleArbiter.Priority.Pause);
    }

    public void Close()
    {
        if (_root != null) _root.SetActive(false);
        TimeScaleArbiter.Release(this);
    }

    // ── Private Methods ──────────────────────────────────────
    private void Build()
    {
        _root = new GameObject("EscMenuRoot", typeof(RectTransform), typeof(Canvas),
                               typeof(CanvasScaler), typeof(GraphicRaycaster));
        _root.transform.SetParent(transform, false);

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

        // 패널
        var panel = NewImage("Panel", _root.transform, PanelBg);
        var prt = panel.rectTransform;
        prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
        prt.anchoredPosition = Vector2.zero;
        prt.sizeDelta = new Vector2(PanelW, PanelH);
        var outline = panel.gameObject.AddComponent<Outline>();
        outline.effectColor    = PanelEdge;
        outline.effectDistance = new Vector2(2f, -2f);

        NewLabel("Title", panel.transform, "일시정지", 34f, TitleColor,
                 new Vector2(0f, PanelH * 0.5f - 52f), new Vector2(PanelW - 40f, 48f));

        // 버튼 3개 묶음의 시작 y. 타이틀 아래 여백과 패널 하단 여백이 17px로 같아지는 값이다.
        float top = 44f;
        NewButton(panel.transform, "계속하기",   new Vector2(0f, top),                        BtnBg,     OnResume);
        NewButton(panel.transform, "로비로 가기", new Vector2(0f, top - (BtnH + BtnGap)),      BtnBg,     OnLobby);
        NewButton(panel.transform, "게임 종료",   new Vector2(0f, top - (BtnH + BtnGap) * 2f), BtnQuitBg, OnQuit);
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

    private void NewButton(Transform parent, string label, Vector2 pos, Color bg,
                           UnityEngine.Events.UnityAction onClick)
    {
        var img = NewImage($"Btn_{label}", parent, bg);
        var rt  = img.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(BtnW, BtnH);

        var btn = img.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);

        NewLabel("Label", img.transform, label, 26f, LabelColor, Vector2.zero, new Vector2(BtnW, BtnH));
    }

    // ── Event Handlers ───────────────────────────────────────
    private void OnResume() => Close();

    private void OnLobby()
    {
        // 시간부터 되돌린다 — 로딩 연출과 로비가 timeScale 0으로 멈춘 채 뜨면 안 된다.
        Close();

        // 진행 중 런이 있으면 정식 종료 경로를 태운다(로컬 런 세이브 폐기 + HUD 정리).
        // 이걸 건너뛰면 이전 런의 보스 체력바·서약·골드가 로비까지 따라온다.
        if (AppBootstrapper.Instance != null && AppBootstrapper.Instance.CurrentRun != null)
            AppBootstrapper.Instance.EndRun();

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
