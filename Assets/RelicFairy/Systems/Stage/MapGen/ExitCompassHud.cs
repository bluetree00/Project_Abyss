using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// 출구 나침반 HUD — 공개된 출구가 <b>어느 방향에 어떤 방</b>인지 화면에 상시 표시한다.
///
/// 카메라가 정면 고정(회전 없음)이라 옆쪽 출구가 화면 밖으로 나가면 존재 자체를 알 수 없다.
/// 그래서 출구마다 인디케이터를 띄우고:
///   · 화면 안이면  → 출구 위에 "종류 글리프 + 이름"
///   · 화면 밖이면  → 화면 가장자리에 클램프 + 8방위 화살표로 방향 지시
///
/// 런타임 절차 생성(프리팹/Addressable 불필요) — UI_ChallengeHud·OnboardingGuideArrow와 동일 패턴.
/// RunFlowController가 RevealGates에서 SetExits, ClearGates에서 Clear를 호출한다.
/// </summary>
public sealed class ExitCompassHud : MonoBehaviour
{
    private const float EdgeMargin  = 72f;   // 화면 가장자리 여백(px)
    private const float OnScreenUp  = 46f;   // 화면 안일 때 출구 위로 띄우는 오프셋(px)
    private const int   SortOrder   = 640;

    private sealed class Entry
    {
        public Transform       Target;
        public string          Label;
        public Color           Color;
        public RectTransform   Rect;
        public TextMeshProUGUI Tmp;
    }

    private static ExitCompassHud _instance;

    /// <summary>생성돼 있으면 반환(없으면 null) — 정리용. 생성이 필요하면 Create()를 쓴다.</summary>
    public static ExitCompassHud Instance => _instance;

    private readonly List<Entry> _entries = new();
    private RectTransform _root;
    private Camera        _cam;

    /// <summary>없으면 만들고 반환. 씬 전환에도 살아남지 않아도 되는 런 스코프 HUD.</summary>
    public static ExitCompassHud Create()
    {
        if (_instance != null) return _instance;

        var go = new GameObject("ExitCompassHud", typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler));
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortOrder;

        var scaler = go.GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode         = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight  = 0.5f;

        _instance = go.AddComponent<ExitCompassHud>();
        _instance._root = go.GetComponent<RectTransform>();
        return _instance;
    }

    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    // ── Public Methods ────────────────────────────────────

    /// <summary>표시할 출구 목록 교체. label은 "■ 상점"처럼 글리프+이름, color는 방 종류 밝은색.</summary>
    public void SetExits(IReadOnlyList<(Transform target, string label, Color color)> exits)
    {
        Clear();
        if (exits == null) return;

        for (int i = 0; i < exits.Count; i++)
        {
            var (target, label, color) = exits[i];
            if (target == null) continue;

            var go = new GameObject($"ExitMark_{i}");
            go.transform.SetParent(_root, false);

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text             = label;
            tmp.fontSize         = 34f;
            tmp.fontStyle        = FontStyles.Bold;
            tmp.alignment        = TextAlignmentOptions.Center;
            tmp.color            = color;
            tmp.outlineColor     = new Color(0f, 0f, 0f, 1f);
            tmp.outlineWidth     = 0.24f;
            tmp.raycastTarget    = false;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;

            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(300f, 60f);

            _entries.Add(new Entry { Target = target, Label = label, Color = color, Rect = rt, Tmp = tmp });
        }
    }

    /// <summary>표시 제거(방 전환 시).</summary>
    public void Clear()
    {
        for (int i = 0; i < _entries.Count; i++)
            if (_entries[i].Rect != null) Destroy(_entries[i].Rect.gameObject);
        _entries.Clear();
    }

    public void Close()
    {
        Clear();
        if (this != null) Destroy(gameObject);
    }

    // ── Private Methods ───────────────────────────────────

    private void LateUpdate()
    {
        if (_entries.Count == 0) return;
        if (_cam == null) _cam = Camera.main;
        if (_cam == null) return;

        float w = Screen.width, h = Screen.height;
        var   center = new Vector2(w * 0.5f, h * 0.5f);

        for (int i = 0; i < _entries.Count; i++)
        {
            var e = _entries[i];
            if (e.Target == null || e.Rect == null) { if (e.Rect != null) e.Rect.gameObject.SetActive(false); continue; }

            Vector3 sp = _cam.WorldToScreenPoint(e.Target.position);
            bool behind = sp.z < 0f;
            if (behind) { sp.x = w - sp.x; sp.y = h - sp.y; }   // 뒤쪽은 반대편으로 투영

            bool onScreen = !behind
                            && sp.x >= EdgeMargin && sp.x <= w - EdgeMargin
                            && sp.y >= EdgeMargin && sp.y <= h - EdgeMargin;

            Vector2 pos;
            string  text;
            if (onScreen)
            {
                pos  = new Vector2(sp.x, sp.y + OnScreenUp);
                text = e.Label;                                   // 화면 안 — 방향 화살표 불필요
            }
            else
            {
                Vector2 dir = new Vector2(sp.x, sp.y) - center;
                if (dir.sqrMagnitude < 0.0001f) dir = Vector2.up;
                pos  = ClampToEdge(center, dir, w, h);
                text = DirArrow(dir) + " " + e.Label;             // 화면 밖 — 8방위 화살표로 방향 지시
            }

            e.Rect.gameObject.SetActive(true);
            e.Rect.position = pos;                                // Overlay 캔버스 → 스크린 좌표 그대로
            if (e.Tmp.text != text) e.Tmp.text = text;
        }
    }

    /// <summary>중심에서 dir 방향으로 쏜 반직선이 화면(여백 적용) 경계와 만나는 점.</summary>
    private static Vector2 ClampToEdge(Vector2 center, Vector2 dir, float w, float h)
    {
        float halfW = w * 0.5f - EdgeMargin;
        float halfH = h * 0.5f - EdgeMargin;
        dir.Normalize();

        // 각 축까지의 스케일 중 작은 쪽이 실제 교차점
        float sx = Mathf.Abs(dir.x) > 0.0001f ? halfW / Mathf.Abs(dir.x) : float.MaxValue;
        float sy = Mathf.Abs(dir.y) > 0.0001f ? halfH / Mathf.Abs(dir.y) : float.MaxValue;
        return center + dir * Mathf.Min(sx, sy);
    }

    /// <summary>방향 벡터 → 8방위 화살표 글리프(회전 없이 항상 읽히게).</summary>
    private static string DirArrow(Vector2 dir)
    {
        float ang = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;   // -180..180, +x=0
        if (ang < 0f) ang += 360f;
        int oct = Mathf.RoundToInt(ang / 45f) % 8;
        return oct switch
        {
            0 => "→", 1 => "↗", 2 => "↑", 3 => "↖",
            4 => "←", 5 => "↙", 6 => "↓", _ => "↘",
        };
    }
}
