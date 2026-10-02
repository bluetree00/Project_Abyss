using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 베이스캠프 목표 길잡이 — 아직 안 끝낸 곳마다 <b>목표 표식</b>(◆ + 남은 거리, 화면 밖이면 가장자리에서 방향)을 띄우고,
/// 화면 왼쪽 위에 할 일 줄을 적는다. 09-30 사용자: 「미완료 구간은 화살표나 명조·원신 같은 구조로」.
///   · 목표: 미완의 검(처음만) → 장비 공방 · 유물 성소 → 파츠 공방(원거리를 얻은 뒤) → 심연의 문(전부 끝나면).
///   · 첫 판만이 아니라 <b>매 판</b> — 복귀 판에도 안 고른 곳이 남아 있으면 표식이 선다. 고르면 그 표식이 사라지고 다음이 밝아진다.
///   · 가장 앞 순서의 목표가 「주」(밝게), 나머지는 옅게. 가까이(5 m) 가면 옅어진다 — 거기선 스테이션 이름표·[F] 안내가 이어받는다.
///   · 연출·대사·팝업 중에는 숨는다. 조작은 막지 않는다.
/// 첫 판의 노란 ▼(<see cref="OnboardingGuideArrow"/>)와 목표 이름판(<see cref="ZoneSign"/>)은 이 표식이 대신한다(<see cref="Active"/>).
/// <see cref="BaseCampFxDirector"/>가 시작할 때 붙인다 — 씬에 따로 놓지 않는다.
/// </summary>
public sealed class BaseCampObjectiveGuide : MonoBehaviour
{
    // ── Constants ─────────────────────────────────────────────
    private const int   Count          = 5;
    private const int   Sword = 0, Ranged = 1, Relic = 2, Parts = 3, Gate = 4;   // 표시·주 목표 순서
    private const float MarkerHeight   = 2.6f;    // 목표 위(m)
    private const float NearFadeStart  = 7f;      // 이 거리부터 옅어져
    private const float NearFadeEnd    = 4.5f;    // 여기서 사라진다(스테이션 이름표·[F]가 이어받음)
    private const float EdgePadding    = 90f;     // 화면 가장자리 여백(px, 1080 기준 배율 전)
    private const float BottomPadding  = 270f;    // 아래쪽은 HUD(무기 칸·체력·스킬) 위까지만 — 표식이 무기 칸에 겹쳤다(09-30 캡처)
    private const float FadeSpeed      = 5f;
    private const float PrimaryAlpha   = 1f;
    private const float SecondaryAlpha = 0.55f;
    private const float IconSize       = 30f;
    private const float DistSize       = 20f;
    private const float TrackerSize    = 22f;

    private static readonly Color Gold = new(1f, 0.82f, 0.45f, 1f);
    private static readonly Color Ink  = new(0.95f, 0.93f, 0.88f, 1f);
    private static readonly string[] Lines =
    {
        "미완의 검을 쥔다",
        "장비 공방 — 원거리 무기를 고른다",
        "유물 성소 — 기사의 유물을 고른다",
        "파츠 공방 — 파츠 하나를 고른다",
        "심연의 문 — 포탈로 내려간다",
    };

    // ── Static ────────────────────────────────────────────────
    /// <summary>목표 표식이 돌고 있는가 — 온보딩 ▼·목표 이름판은 이때 숨는다(같은 곳을 두 벌로 가리키지 않게).</summary>
    public static bool Active { get; private set; }

    // 도메인 리로드가 꺼져 있으면 정적 값이 이전 플레이에서 남는다
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Active = false;

    // ── Private ───────────────────────────────────────────────
    private sealed class Marker
    {
        public RectTransform Root;
        public CanvasGroup   Group;
        public RectTransform Arrow;
        public TMP_Text      Dist;
        public float         Alpha;
        public int           Meters = -1;
    }

    private readonly Vector3[] _points = new Vector3[Count];
    private readonly bool[]    _has    = new bool[Count];
    private readonly Marker[]  _markers = new Marker[Count];
    private BaseCampFxDirector _fx;
    private Camera       _cam;
    private CanvasGroup  _trackerGroup;
    private TMP_Text     _tracker;
    private int          _trackerMask = -1;
    private float        _trackerAlpha;
    private readonly System.Text.StringBuilder _sb = new(160);

    // ── Lifecycle ─────────────────────────────────────────────
    private void Start()
    {
        _cam = Camera.main;
        var sword = FindFirstObjectByType<WorldSwordAwakening>();
        if (sword != null) Set(Sword, sword.transform.position);

        var relics = FindObjectsByType<RelicAltar>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (relics.Length > 0)
        {
            Vector3 sum = Vector3.zero;
            foreach (var r in relics) sum += r.transform.position;
            Set(Relic, sum / relics.Length);   // 성소 가운데(제단 사이 통로)
        }
        Build();
        Active = true;
    }

    private void LateUpdate()
    {
        if (_cam == null) { _cam = Camera.main; if (_cam == null) return; }

        int mask = VisibleMask(out int primary);
        Vector3 playerPos = Managers.Player?.PlayerTransform != null ? Managers.Player.PlayerTransform.position : Vector3.zero;
        float dt = Time.unscaledDeltaTime;
        float scale = Screen.height / 1080f;

        for (int i = 0; i < Count; i++)
        {
            var m = _markers[i];
            float target = 0f;
            if ((mask & (1 << i)) != 0)
            {
                Vector3 flat = _points[i] - playerPos;
                flat.y = 0f;
                float dist = flat.magnitude;
                float near = Mathf.Clamp01((dist - NearFadeEnd) / (NearFadeStart - NearFadeEnd));
                target = (i == primary ? PrimaryAlpha : SecondaryAlpha) * near;
                if (target > 0.01f || m.Alpha > 0.01f) Place(m, _points[i] + Vector3.up * MarkerHeight, scale);
                int meters = Mathf.RoundToInt(dist);
                if (meters != m.Meters)
                {
                    m.Meters = meters;
                    m.Dist.SetText("{0}m", meters);   // 서식 SetText — 문자열을 새로 만들지 않는다
                }
            }
            m.Alpha = Mathf.MoveTowards(m.Alpha, target, FadeSpeed * dt);
            m.Group.alpha = m.Alpha;
        }

        if (mask != _trackerMask)
        {
            _trackerMask = mask;
            if (mask != 0) ComposeTracker(mask, primary);
        }
        _trackerAlpha = Mathf.MoveTowards(_trackerAlpha, mask != 0 ? 1f : 0f, FadeSpeed * dt);
        _trackerGroup.alpha = _trackerAlpha;
    }

    private void OnDestroy() => Active = false;

    // ── Public Methods ────────────────────────────────────────
    /// <summary>연출 담당이 시작할 때 한 번 — 공방·파츠 공방·성문 앞 자리.</summary>
    public void Setup(BaseCampFxDirector fx, Transform forge, Transform parts, Transform gateFront)
    {
        _fx = fx;
        if (forge != null) Set(Ranged, forge.position);
        if (parts != null) Set(Parts, parts.position);
        if (gateFront != null) Set(Gate, gateFront.position);
    }

    // ── Private Methods ───────────────────────────────────────
    private void Set(int i, Vector3 p)
    {
        _points[i] = p;
        _has[i] = true;
    }

    /// <summary>지금 표식을 세울 목표들(비트) + 그중 주 목표. 연출·대사·팝업 중이거나 로드아웃이 없으면 0.</summary>
    private int VisibleMask(out int primary)
    {
        primary = -1;
        var lo = AppBootstrapper.Instance?.Loadout;
        if (lo == null) return 0;
        if (BaseCampBootstrapper.Instance != null && !BaseCampBootstrapper.Instance.IsReady) return 0;
        if (ZoneSign.LabelsHidden || OnboardingGuideArrow.Suppressed || UIInputGate.Blocked) return 0;

        int mask = 0;
        bool sword  = lo.WeaponSlot0 != null;
        bool ranged = lo.WeaponSlot1 != null;
        bool part   = !string.IsNullOrEmpty(lo.StartPartId);
        bool relic  = lo.Relic != null;

        if (!sword)
        {
            if (_has[Sword]) mask |= 1 << Sword;   // 검을 쥐기 전엔 이것 하나만(소환의 방)
        }
        else if (_fx == null || !_fx.ConjurePending)   // 처음 판: 공방·성소가 생겨난 뒤부터
        {
            if (!ranged && _has[Ranged]) mask |= 1 << Ranged;
            if (!relic && _has[Relic]) mask |= 1 << Relic;
            if (ranged && !part && _has[Parts]) mask |= 1 << Parts;   // 파츠는 원거리 무기에 끼우는 것 — 원거리 뒤에
            if (ranged && part && relic && _has[Gate]) mask |= 1 << Gate;
        }
        // 주 목표 = 매 판 걷는 순서(장비 → 파츠 → 유물 → 성문)에서 가장 앞
        if ((mask & (1 << Sword)) != 0) primary = Sword;
        else if ((mask & (1 << Ranged)) != 0) primary = Ranged;
        else if ((mask & (1 << Parts)) != 0) primary = Parts;
        else if ((mask & (1 << Relic)) != 0) primary = Relic;
        else if ((mask & (1 << Gate)) != 0) primary = Gate;
        return mask;
    }

    /// <summary>표식을 화면에 놓는다 — 화면 안이면 목표 위, 밖이면 가장자리에 붙여 방향 화살표를 돌린다.</summary>
    private void Place(Marker m, Vector3 world, float scale)
    {
        Vector3 sp = _cam.WorldToScreenPoint(world);
        float pad = EdgePadding * scale;
        float padBottom = BottomPadding * scale;
        bool inFront = sp.z > 0f;
        bool onScreen = inFront && sp.x >= pad && sp.x <= Screen.width - pad && sp.y >= padBottom && sp.y <= Screen.height - pad;
        if (onScreen)
        {
            m.Root.position = new Vector3(sp.x, sp.y, 0f);
            if (m.Arrow.gameObject.activeSelf) m.Arrow.gameObject.SetActive(false);
            return;
        }

        Vector2 center = new(Screen.width * 0.5f, Screen.height * 0.5f);
        Vector2 dir = new Vector2(sp.x, sp.y) - center;
        if (!inFront) dir = -dir;                     // 뒤쪽이면 반전
        if (dir.sqrMagnitude < 0.001f) dir = Vector2.up;
        dir.Normalize();
        float maxX = center.x - pad, maxY = dir.y < 0f ? center.y - padBottom : center.y - pad;
        float k = Mathf.Min(maxX / Mathf.Max(Mathf.Abs(dir.x), 0.0001f), maxY / Mathf.Max(Mathf.Abs(dir.y), 0.0001f));
        Vector2 p = center + dir * k;
        m.Root.position = new Vector3(p.x, p.y, 0f);
        if (!m.Arrow.gameObject.activeSelf) m.Arrow.gameObject.SetActive(true);
        m.Arrow.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
        m.Arrow.anchoredPosition = dir * 34f;
    }

    /// <summary>왼쪽 위 할 일 줄 — 목표가 바뀔 때만 다시 쓴다. 주 목표는 금빛, 나머지는 옅게.</summary>
    private void ComposeTracker(int mask, int primary)
    {
        _sb.Clear();
        AppendLine(primary, true);
        for (int i = 0; i < Count; i++)
            if (i != primary && (mask & (1 << i)) != 0) AppendLine(i, false);
        _tracker.SetText(_sb);
    }

    private void AppendLine(int i, bool primary)
    {
        if (i < 0) return;
        if (_sb.Length > 0) _sb.Append('\n');
        if (primary) _sb.Append("<color=#FFD173>◆</color> ").Append(Lines[i]);
        else _sb.Append("<alpha=#99><size=85%>◇ ").Append(Lines[i]).Append("</size><alpha=#FF>");
    }

    /// <summary>자기 캔버스(1920×1080 · Match 0.5) — HUD 표식 층. 레이캐스터가 없어 클릭을 받지 않는다.</summary>
    private void Build()
    {
        var canvasGo = new GameObject("ObjectiveGuideCanvas", typeof(Canvas), typeof(CanvasScaler));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = UISortingOrder.HudIndicator;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode     = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight  = 0.5f;

        for (int i = 0; i < Count; i++) _markers[i] = MakeMarker(canvasGo.transform, i);

        var trackerGo = new GameObject("Tracker", typeof(RectTransform), typeof(CanvasGroup));
        trackerGo.transform.SetParent(canvasGo.transform, false);
        _trackerGroup = trackerGo.GetComponent<CanvasGroup>();
        _trackerGroup.alpha = 0f;
        _trackerGroup.blocksRaycasts = false;
        _trackerGroup.interactable = false;
        var trt = (RectTransform)trackerGo.transform;
        trt.anchorMin = trt.anchorMax = new Vector2(0f, 1f);
        trt.pivot = new Vector2(0f, 1f);
        trt.anchoredPosition = new Vector2(48f, -150f);
        trt.sizeDelta = new Vector2(620f, 120f);
        _tracker = MakeText(trt, "Text", TrackerSize, Ink, TextAlignmentOptions.TopLeft);
        _tracker.lineSpacing = 12f;
        var tr = _tracker.rectTransform;
        tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
        tr.offsetMin = tr.offsetMax = Vector2.zero;
    }

    private static Marker MakeMarker(Transform parent, int index)
    {
        var go = new GameObject($"Marker_{index}", typeof(RectTransform), typeof(CanvasGroup));
        go.transform.SetParent(parent, false);
        var m = new Marker { Root = (RectTransform)go.transform, Group = go.GetComponent<CanvasGroup>() };
        m.Root.sizeDelta = new Vector2(120f, 70f);
        m.Group.alpha = 0f;
        m.Group.blocksRaycasts = false;
        m.Group.interactable = false;

        var icon = MakeText(m.Root, "Icon", IconSize, Gold, TextAlignmentOptions.Center);
        icon.text = "◆";
        icon.rectTransform.anchoredPosition = new Vector2(0f, 10f);
        icon.rectTransform.sizeDelta = new Vector2(60f, 40f);

        m.Dist = MakeText(m.Root, "Dist", DistSize, Ink, TextAlignmentOptions.Center);
        m.Dist.rectTransform.anchoredPosition = new Vector2(0f, -20f);
        m.Dist.rectTransform.sizeDelta = new Vector2(120f, 28f);

        var arrow = MakeText(m.Root, "Arrow", 22f, Gold, TextAlignmentOptions.Center);
        arrow.text = "▶";
        m.Arrow = arrow.rectTransform;
        m.Arrow.sizeDelta = new Vector2(30f, 30f);
        m.Arrow.gameObject.SetActive(false);
        return m;
    }

    private static TMP_Text MakeText(RectTransform parent, string name, float size, Color color, TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.fontSize         = size;
        t.fontStyle        = FontStyles.Normal;   // 기본 폰트가 이미 굵다 — 가짜 굵게 금지(글자 정본)
        t.color            = color;
        t.alignment        = align;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.raycastTarget    = false;
        TMPOutlineHelper.ApplySoftShadow(t);
        return t;
    }
}
