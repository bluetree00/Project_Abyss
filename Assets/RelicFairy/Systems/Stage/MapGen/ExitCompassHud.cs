using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

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
    private const float EdgeMargin  = 88f;   // 화면 가장자리 여백(px)
    private const float OnScreenUp  = 58f;   // 화면 안일 때 출구 위로 띄우는 오프셋(px)
    private const int   SortOrder   = UISortingOrder.HudIndicator;  // 팝업 위에 뜨던 버그 수정(640→120)

    // 배지 치수(1920×1080 기준). 맨 텍스트가 아니라 판때기 배지로 띄워야 배경과 섞이지 않는다.
    private const float PlateW      = 264f;
    private const float PlateH      = 76f;
    private const float AccentW     = 6f;    // 좌측 방 종류 색 띠
    private const float PadX        = 18f;
    private const float ArrowGap    = 26f;   // 배지 왼쪽 바깥 화살표 간격
    private const float StackGapY   = 10f;   // 같은 변에 몰린 배지끼리 세로로 벌리는 간격

    private static readonly Color PlateColor = new(0.04f, 0.05f, 0.08f, 0.88f);
    private static readonly Color SubColor   = new(0.74f, 0.78f, 0.86f, 1f);

    private sealed class Entry
    {
        public Transform       Target;
        public RectTransform   Rect;      // 배지 루트
        public RectTransform   ArrowRect; // 화면 밖일 때만 켜지는 방향 지시자(회전)
        public Graphic         Arrow;
    }

    private static ExitCompassHud _instance;

    /// <summary>생성돼 있으면 반환(없으면 null) — 정리용. 생성이 필요하면 Create()를 쓴다.</summary>
    public static ExitCompassHud Instance => _instance;

    private readonly List<Entry> _entries = new();
    private readonly List<Vector2> _placed = new();   // 이번 프레임에 배치된 배지 중심(겹침 해소용, 재사용해 할당 없음)
    private RectTransform _root;
    private Camera        _cam;
    private Canvas        _canvas;   // 배지 폭을 픽셀로 환산해 가장자리 클램프에 쓴다

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
        _canvas   = GetComponent<Canvas>();
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    // ── Public Methods ────────────────────────────────────

    /// <summary>
    /// 표시할 출구 목록 교체. 각 출구를 <b>배지</b>(어두운 판 + 종류 색 띠 + 이름 + 보상 요약)로 만든다.
    /// title = "◆ 정예", subtitle = "희귀 이상 · 후보 4"처럼 조립된 문자열(조립은 RunFlowController 담당).
    /// </summary>
    public void SetExits(IReadOnlyList<(Transform target, string title, string subtitle, Color color)> exits)
    {
        Clear();
        if (exits == null) return;

        for (int i = 0; i < exits.Count; i++)
        {
            var (target, title, subtitle, color) = exits[i];
            if (target == null) continue;

            // ── 배지 루트 ──
            var go = new GameObject($"ExitBadge_{i}", typeof(RectTransform));
            go.transform.SetParent(_root, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);  // 스트레치 방지 — 자식 배치 기준을 중앙으로 고정
            rt.sizeDelta = new Vector2(PlateW, PlateH);

            // 판때기 — 밝은 배경에서도 글자가 묻히지 않게 어두운 반투명 판을 깐다.
            var plate = NewGraphic<Image>(rt, "Plate", new Vector2(PlateW, PlateH), Vector2.zero);
            plate.color = PlateColor;

            // 좌측 색 띠 — 방 종류를 색으로 먼저 읽히게(글리프보다 빠름).
            var accent = NewGraphic<Image>(rt, "Accent", new Vector2(AccentW, PlateH),
                                           new Vector2(-(PlateW - AccentW) * 0.5f, 0f));
            accent.color = color;

            // 1행: 종류
            var t = NewGraphic<TextMeshProUGUI>(rt, "Title", new Vector2(PlateW - PadX * 2f, 38f),
                                                new Vector2(PadX * 0.5f, 15f));
            t.text             = title;
            t.fontSize         = 30f;
            t.fontStyle        = FontStyles.Bold;
            t.alignment        = TextAlignmentOptions.Left;
            t.color            = color;
            t.textWrappingMode = TextWrappingModes.NoWrap;

            // 2행: 그 방에서 얻는 것(정보량이 결정을 만든다)
            var s = NewGraphic<TextMeshProUGUI>(rt, "Sub", new Vector2(PlateW - PadX * 2f, 30f),
                                                new Vector2(PadX * 0.5f, -17f));
            s.text             = subtitle;
            s.fontSize         = 20f;
            s.alignment        = TextAlignmentOptions.Left;
            s.color            = SubColor;
            s.textWrappingMode = TextWrappingModes.NoWrap;

            // 방향 화살표 — 글리프 8종 대신 ▶ 하나를 회전시킨다(폰트에 없는 ↖↙가 □로 깨지던 문제 해소).
            var arrow = NewGraphic<TextMeshProUGUI>(rt, "Arrow", new Vector2(40f, 40f),
                                                    new Vector2(-(PlateW * 0.5f + ArrowGap), 0f));
            arrow.text      = "▶";
            arrow.fontSize  = 34f;
            arrow.fontStyle = FontStyles.Bold;
            arrow.alignment = TextAlignmentOptions.Center;
            arrow.color     = color;

            _entries.Add(new Entry
            {
                Target    = target,
                Rect      = rt,
                ArrowRect = arrow.rectTransform,
                Arrow     = arrow,
            });
        }
    }

    /// <summary>배지 자식 그래픽 생성 헬퍼 — 중앙 기준 앵커 + 레이캐스트 차단 해제.</summary>
    private static T NewGraphic<T>(RectTransform parent, string name, Vector2 size, Vector2 pos)
        where T : Graphic
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin        = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot            = new Vector2(0.5f, 0.5f);
        rt.sizeDelta        = size;
        rt.anchoredPosition = pos;

        var g = go.AddComponent<T>();
        g.raycastTarget = false;
        return g;
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
        _placed.Clear();

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
            if (onScreen)
            {
                pos = new Vector2(sp.x, sp.y + OnScreenUp);       // 화면 안 — 방향 지시 불필요
            }
            else
            {
                Vector2 dir = new Vector2(sp.x, sp.y) - center;
                if (dir.sqrMagnitude < 0.0001f) dir = Vector2.up;
                pos = ClampToEdge(center, dir, w, h);

                // ▶(0°=오른쪽) 하나를 목표 방향으로 회전 — 대각선 글리프(↖↙)가 폰트에 없어 □로 깨지던 것을 대체.
                float ang = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                e.ArrowRect.localRotation = Quaternion.Euler(0f, 0f, ang);
            }

            if (e.Arrow != null && e.Arrow.gameObject.activeSelf == onScreen)
                e.Arrow.gameObject.SetActive(!onScreen);

            // 배지는 맨 텍스트보다 넓어 가장자리에서 잘린다 — 실제 배지 폭(캔버스 스케일 반영)으로 최종 클램프.
            float scale = _canvas != null ? _canvas.scaleFactor : 1f;
            float halfW = PlateW * 0.5f * scale;
            float halfH = PlateH * 0.5f * scale;
            float leftPad = onScreen ? halfW : halfW + (ArrowGap + 20f) * scale;
            pos.x = Mathf.Clamp(pos.x, leftPad + 8f, w - halfW - 8f);
            pos.y = Mathf.Clamp(pos.y, halfH + 8f,  h - halfH - 8f);

            // 출구가 같은 변으로 몰리면 배지가 같은 자리에 겹쳐 한 장만 읽힌다 — 세로로 쌓아 벌린다.
            pos = StackAwayFromPlaced(pos, halfW, halfH, halfH * 2f + StackGapY * scale, h);
            _placed.Add(pos);

            e.Rect.gameObject.SetActive(true);
            e.Rect.position = pos;                                // Overlay 캔버스 → 스크린 좌표 그대로
        }
    }

    /// <summary>
    /// 이미 배치된 배지와 겹치면 아래로 밀어 쌓는다(바닥에 닿으면 위로). 순회 횟수를 배치 수로 묶어
    /// 밀어낸 자리가 또 겹치는 연쇄도 유한하게 끝난다.
    /// </summary>
    private Vector2 StackAwayFromPlaced(Vector2 pos, float halfW, float halfH, float step, float screenH)
    {
        float minY = halfH + 8f;
        float maxY = screenH - halfH - 8f;

        for (int guard = 0; guard < _placed.Count; guard++)
        {
            int hit = -1;
            for (int j = 0; j < _placed.Count; j++)
            {
                if (Mathf.Abs(_placed[j].x - pos.x) < halfW * 2f &&
                    Mathf.Abs(_placed[j].y - pos.y) < halfH * 2f) { hit = j; break; }
            }
            if (hit < 0) break;

            float down = _placed[hit].y - step;
            pos.y = down >= minY ? down : _placed[hit].y + step;
            pos.y = Mathf.Clamp(pos.y, minY, maxY);
        }
        return pos;
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

}
