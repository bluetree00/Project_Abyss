using System;
using System.Collections.Generic;
using RelicFairy.Monster;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 적 나침반 — 남은 적이 <b>어디 있는지</b> 화면에 알려 준다(10-01 사용자: 「가장 가까운 몬스터를 작게나마 화살표로,
/// 보스 때는 보스 아이콘 — 멀리 있어도 어디 있는지 알 수 있게」).
///   · 가장 가까운 몬스터: 캐릭터 둘레의 <b>작은 화살표</b>가 그쪽을 가리킨다. 적이 화면 안 가까이 있으면 숨는다(이미 보인다).
///   · 보스: 화면 밖이면 가장자리에 <b>보스 아이콘 + 방향 화살표 + 거리</b>. 화면 안이면 숨는다.
/// 카메라가 내려다보는 고정 시점이라 「화면 위 = 카메라 정면」이다 — 방향은 카메라 기준 수평 벡터로 구한다(투영 뒤집힘 없음).
/// 막는 팝업이 열리면 걷는다. 조작은 막지 않는다. 런타임 절차 생성(자기 캔버스) — <see cref="ExitCompassHud"/>와 같은 방식.
/// 런 씬의 @GameRun에 붙어 산다(보스 아이콘은 여기 직렬화 — 보스 프리팹은 커서 건드리지 않는다).
/// </summary>
public sealed class EnemyCompassHud : MonoBehaviour
{
    // ── Constants ─────────────────────────────────────────────
    private const float ArrowRadius     = 118f;   // 캐릭터에서 화살표까지(px, 1080 기준)
    private const float ArrowSize       = 26f;
    private const float NearHideDist    = 7f;     // 화면 안이고 이보다 가까우면 화살표를 숨긴다(m)
    private const float ViewportInset   = 0.05f;  // 화면 「안」 판정 여백
    private const float FadeSpeed       = 6f;
    private const float BossIconSize    = 64f;
    private const float EdgeMargin      = 96f;    // 보스 배지 — 화면 가장자리 여백(px, 1080 기준)
    private const float TopBand         = 170f;   // 위 가운데 = 보스 체력바 · 패턴 예고 자리
    private const float BottomBand      = 300f;   // 아래 = 무기 칸 · 체력 · 스킬 칸 자리
    private const float BossArrowGap    = 46f;
    private const float PlayerClearX    = 210f;   // 보스가 카메라 뒤(아래)일 때 배지를 캐릭터 옆으로 비키는 거리

    private static readonly Color EnemyInk = new(1f, 0.45f, 0.32f, 1f);
    private static readonly Color BossInk  = new(1f, 0.30f, 0.26f, 1f);
    private static readonly Color DistInk  = new(0.95f, 0.93f, 0.88f, 1f);
    private static readonly Color GoalInk  = new(0.35f, 0.92f, 0.98f, 1f);   // 목표(봉인석 등) — 청록 = 칠 수 있음(리치 색 규약)

    // 목표 — 걸려 있는 동안 작은 화살표가 가장 가까운 몬스터 대신 가장 가까운 목표를 가리킨다(10-03 리치 봉인석: 화면 밖 돌을 못 찾았다).
    private static readonly List<Transform> s_goals = new();

    [Serializable]
    private struct BossIcon
    {
        [Tooltip("StoryProgress 보스 id(forestguardian · dragon · deathknight · lich)")]
        public string bossId;
        public Texture2D icon;
    }

    // ── Serialized ────────────────────────────────────────────
    [Header("보스 아이콘(보스 모델 렌더 — UI/HUD/Sprites/BossIcons)")]
    [SerializeField] private BossIcon[] bossIcons;

    // ── Private ───────────────────────────────────────────────
    private Camera        _cam;
    private CanvasGroup   _group;
    private RectTransform _arrow;
    private CanvasGroup   _arrowGroup;
    private TMP_Text      _arrowText;
    private bool          _arrowGoal;      // 화살표가 목표를 가리키는 중(청록)
    private RectTransform _boss;
    private CanvasGroup   _bossGroup;
    private RawImage      _bossImage;
    private TMP_Text      _bossGlyph;      // 아이콘이 없을 때의 대체 표식
    private RectTransform _bossArrow;
    private TMP_Text      _bossDist;
    private MonsterBase   _bossShown;
    private float         _arrowAlpha, _bossAlpha;
    private int           _bossMeters = -1;

    // ── Lifecycle ─────────────────────────────────────────────
    private void Awake() => Build();

    private void LateUpdate()
    {
        var player = Managers.Player?.PlayerTransform;
        if (_cam == null) _cam = Camera.main;
        bool live = player != null && _cam != null && !UIInputGate.Blocked;
        float dt = Time.unscaledDeltaTime;
        float arrowTarget = 0f, bossTarget = 0f;

        if (live)
        {
            MonsterBase.FindCompassTargets(player.position, out var nearest, out float nearestDist, out var boss);
            Vector3 camFwd = _cam.transform.forward; camFwd.y = 0f;
            Vector3 camRight = _cam.transform.right; camRight.y = 0f;
            float scale = Screen.height / 1080f;

            // 목표가 걸려 있으면 몬스터보다 목표 — 칠 대상이 먼저다(10-03)
            Transform goal = NearestGoal(player.position, out float goalDist);
            Transform aim  = goal != null ? goal : nearest != null ? nearest.transform : null;
            float aimDist  = goal != null ? goalDist : nearestDist;
            SetArrowInk(goal != null);

            if (aim != null && camFwd.sqrMagnitude > 0.0001f)
            {
                // 화면 안 가까이 있는 적은 이미 보인다 — 멀거나 화면 밖일 때만 가리킨다
                if (aimDist > NearHideDist || !OnScreen(aim.position))
                {
                    arrowTarget = 1f;
                    Vector2 dir = ScreenDir(aim.position - player.position, camRight, camFwd);
                    Vector3 sp = _cam.WorldToScreenPoint(player.position + Vector3.up * 1.0f);
                    _arrow.position = new Vector3(sp.x + dir.x * ArrowRadius * scale, sp.y + dir.y * ArrowRadius * scale, 0f);
                    _arrow.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
                }
            }

            if (boss != null && camFwd.sqrMagnitude > 0.0001f && !OnScreen(boss.transform.position + Vector3.up * 1.5f))
            {
                bossTarget = 1f;
                if (boss != _bossShown) BindBoss(boss);
                Vector2 dir = ScreenDir(boss.transform.position - player.position, camRight, camFwd);
                PlaceAtEdge(dir, scale);
                Vector3 flat = boss.transform.position - player.position; flat.y = 0f;
                int meters = Mathf.RoundToInt(flat.magnitude);
                if (meters != _bossMeters)
                {
                    _bossMeters = meters;
                    _bossDist.SetText("{0}m", meters);   // 서식 SetText — 문자열을 새로 만들지 않는다
                }
            }
        }

        _arrowAlpha = Mathf.MoveTowards(_arrowAlpha, arrowTarget, FadeSpeed * dt);
        _bossAlpha  = Mathf.MoveTowards(_bossAlpha, bossTarget, FadeSpeed * dt);
        _arrowGroup.alpha = _arrowAlpha;
        _bossGroup.alpha  = _bossAlpha;
    }

    // ── Public Methods ────────────────────────────────────────
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => s_goals.Clear();

    /// <summary>목표를 건다 — 칠 대상(봉인석 등)이 멀거나 화면 밖이면 작은 화살표가 청록으로 그쪽을 가리킨다. 끝나면 <see cref="RemoveGoal"/>.</summary>
    public static void AddGoal(Transform target)
    {
        if (target != null && !s_goals.Contains(target)) s_goals.Add(target);
    }

    public static void RemoveGoal(Transform target) => s_goals.Remove(target);

    // ── Private Methods ───────────────────────────────────────

    /// <summary>월드 수평 벡터 → 화면 방향(오른쪽 +x · 위 +y). 내려다보는 카메라라 카메라 정면이 화면 위다.</summary>
    private static Vector2 ScreenDir(Vector3 world, Vector3 camRight, Vector3 camFwd)
    {
        world.y = 0f;
        var d = new Vector2(Vector3.Dot(world, camRight.normalized), Vector3.Dot(world, camFwd.normalized));
        return d.sqrMagnitude > 0.0001f ? d.normalized : Vector2.up;
    }

    private bool OnScreen(Vector3 world)
    {
        Vector3 v = _cam.WorldToViewportPoint(world);
        return v.z > 0f && v.x > ViewportInset && v.x < 1f - ViewportInset && v.y > ViewportInset && v.y < 1f - ViewportInset;
    }

    /// <summary>가장 가까운 목표(없으면 null). 파괴된 항목은 여기서 걷는다 — 할당 없음.</summary>
    private static Transform NearestGoal(Vector3 from, out float dist)
    {
        Transform best = null;
        float bestSqr = float.MaxValue;
        for (int i = s_goals.Count - 1; i >= 0; i--)
        {
            var t = s_goals[i];
            if (t == null) { s_goals.RemoveAt(i); continue; }
            Vector3 d = t.position - from;
            d.y = 0f;
            float sqr = d.sqrMagnitude;
            if (sqr < bestSqr) { bestSqr = sqr; best = t; }
        }
        dist = best != null ? Mathf.Sqrt(bestSqr) : 0f;
        return best;
    }

    /// <summary>화살표 색 — 목표면 청록, 몬스터면 주황(바뀔 때만 쓴다).</summary>
    private void SetArrowInk(bool goal)
    {
        if (_arrowGoal == goal) return;
        _arrowGoal = goal;
        _arrowText.color = goal ? GoalInk : EnemyInk;
    }

    /// <summary>보스 배지를 화면 가운데에서 dir 쪽 가장자리에 붙인다 — 위(보스 체력바)·아래(HUD) 띠는 비킨다.</summary>
    private void PlaceAtEdge(Vector2 dir, float scale)
    {
        Vector2 center = new(Screen.width * 0.5f, Screen.height * 0.5f);
        float maxX = center.x - EdgeMargin * scale;
        float maxY = dir.y >= 0f ? center.y - TopBand * scale : center.y - BottomBand * scale;
        float k = Mathf.Min(maxX / Mathf.Max(Mathf.Abs(dir.x), 0.0001f), maxY / Mathf.Max(Mathf.Abs(dir.y), 0.0001f));
        Vector2 p = center + dir * k;
        // 아래 띠 끝은 캐릭터 발밑이다(카메라가 캐릭터를 화면 아래쪽에 둔다) — 거기 붙으면 캐릭터와 겹쳐 옆으로 비킨다(10-01 실측)
        float clear = PlayerClearX * scale;
        if (dir.y < 0f && Mathf.Abs(p.x - center.x) < clear)
            p.x = center.x + (dir.x >= 0f ? clear : -clear);
        _boss.position = new Vector3(p.x, p.y, 0f);
        _bossArrow.anchoredPosition = dir * BossArrowGap;
        // 거리 글자는 화살표 반대쪽 — 아래를 가리키면 화살표가 글자를 덮는다
        float distY = BossIconSize * 0.5f + 14f;
        _bossDist.rectTransform.anchoredPosition = new Vector2(0f, dir.y < -0.3f ? distY : -distY);
        _bossArrow.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
    }

    private void BindBoss(MonsterBase boss)
    {
        _bossShown = boss;
        _bossMeters = -1;
        string id = boss is IPagedBoss paged ? paged.StoryBossId : boss is LichMonster ? StoryProgress.Lich : null;
        Texture2D icon = null;
        if (id != null && bossIcons != null)
            foreach (var b in bossIcons)
                if (b.bossId == id) { icon = b.icon; break; }
        _bossImage.texture = icon;
        _bossImage.enabled = icon != null;
        _bossGlyph.enabled = icon == null;   // 아이콘이 없는 보스는 붉은 ◆로
    }

    /// <summary>자기 캔버스(1920×1080 · Match 0.5) — HUD 표식 층. 레이캐스터가 없어 클릭을 받지 않는다.</summary>
    private void Build()
    {
        var canvasGo = new GameObject("EnemyCompassCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = UISortingOrder.HudIndicator;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode     = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight  = 0.5f;
        _group = canvasGo.GetComponent<CanvasGroup>();
        _group.blocksRaycasts = false;
        _group.interactable   = false;

        // 가장 가까운 몬스터 화살표
        var arrowText = MakeText(canvasGo.transform, "NearestArrow", ArrowSize, EnemyInk, out _arrowGroup);
        arrowText.text = "▶";
        _arrow = arrowText.rectTransform;
        _arrowText = arrowText;
        _arrow.sizeDelta = new Vector2(40f, 40f);

        // 보스 배지 — 아이콘(또는 ◆) + 거리 + 바깥 화살표
        var bossGo = new GameObject("BossBadge", typeof(RectTransform), typeof(CanvasGroup));
        bossGo.transform.SetParent(canvasGo.transform, false);
        _boss = (RectTransform)bossGo.transform;
        _boss.sizeDelta = new Vector2(BossIconSize, BossIconSize);
        _bossGroup = bossGo.GetComponent<CanvasGroup>();
        _bossGroup.alpha = 0f;
        _bossGroup.blocksRaycasts = false;

        var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer));
        iconGo.transform.SetParent(_boss, false);
        _bossImage = iconGo.AddComponent<RawImage>();
        _bossImage.raycastTarget = false;
        _bossImage.rectTransform.sizeDelta = new Vector2(BossIconSize, BossIconSize);

        _bossGlyph = MakeText(_boss, "Glyph", 44f, BossInk, out _);
        _bossGlyph.text = "◆";
        _bossGlyph.rectTransform.sizeDelta = new Vector2(BossIconSize, BossIconSize);
        _bossGlyph.enabled = false;

        _bossDist = MakeText(_boss, "Dist", 20f, DistInk, out _);
        _bossDist.rectTransform.anchoredPosition = new Vector2(0f, -(BossIconSize * 0.5f + 14f));
        _bossDist.rectTransform.sizeDelta = new Vector2(120f, 26f);

        var bossArrow = MakeText(_boss, "Arrow", 24f, BossInk, out _);
        bossArrow.text = "▶";
        _bossArrow = bossArrow.rectTransform;
        _bossArrow.sizeDelta = new Vector2(34f, 34f);
    }

    private static TMP_Text MakeText(Transform parent, string name, float size, Color color, out CanvasGroup group)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(CanvasGroup));
        go.transform.SetParent(parent, false);
        group = go.GetComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        var t = go.AddComponent<TextMeshProUGUI>();
        t.fontSize         = size;
        t.fontStyle        = FontStyles.Normal;   // 기본 폰트가 이미 굵다 — 가짜 굵게 금지(글자 정본)
        t.color            = color;
        t.alignment        = TextAlignmentOptions.Center;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.raycastTarget    = false;
        TMPOutlineHelper.ApplySoftShadow(t);
        return t;
    }
}
