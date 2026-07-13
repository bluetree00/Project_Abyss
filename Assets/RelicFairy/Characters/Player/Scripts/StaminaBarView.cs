using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 스태미너 바 — 원신·명조 문법 그대로.
///  · 화면 중앙(캐릭터) 살짝 아래에 가로 바
///  · 소모/회복 중에만 보이고 <b>가득 차면 자동으로 숨는다</b> ("안 쓰면 안 보인다")
///  · 완전 소진 시 <b>빨강</b>으로 전환(젤다의 빨간 게이지와 같은 신호)
///
/// HUD 프리팹/씬을 건드리지 않도록 런타임에 자체 Canvas를 만든다
/// (DodgePresentation과 동일한 '런타임 자동 부착' 패턴). 추후 HUD로 이관 가능.
/// </summary>
[DisallowMultipleComponent]
public class StaminaBarView : MonoBehaviour
{
    // ── Constants ─────────────────────────────────────────────────
    private const float BarWidth   = 220f;
    private const float BarHeight  = 10f;
    private const float BarOffsetY = -130f;   // 화면 중앙(캐릭터) 기준 아래로
    private const float FadeSpeed  = 8f;      // 표시/숨김 페이드 속도(초당)

    private static readonly Color FillNormal    = new Color(1f, 0.85f, 0.30f, 1f);   // 원신식 노랑
    private static readonly Color FillExhausted = new Color(1f, 0.35f, 0.30f, 1f);   // 소진 시 빨강
    private static readonly Color BgColor       = new Color(0f, 0f, 0f, 0.45f);

    // ── Private ───────────────────────────────────────────────────
    private PlayerController _controller;
    private CanvasGroup      _group;
    private Image            _fill;
    private RectTransform    _fillRect;

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        _controller = GetComponent<PlayerController>();
        Build();
    }

    // Canvas는 루트 오브젝트라 플레이어를 비활성화(풀 반환)해도 혼자 계속 그려진다 → 함께 껐다 켠다.
    private void OnEnable()
    {
        if (_group != null) _group.gameObject.SetActive(true);
    }

    private void OnDisable()
    {
        if (_group != null)
        {
            _group.alpha = 0f;
            _group.gameObject.SetActive(false);
        }
    }

    private void OnDestroy()
    {
        // 플레이어와 함께 만든 Canvas는 함께 정리 — 씬 전환/사망 후 잔류 방지.
        if (_group != null) Destroy(_group.gameObject);
    }

    private void LateUpdate()
    {
        if (_controller == null || _group == null) return;

        var stats   = _controller.RuntimeStats;
        var stamina = _controller.Stamina;
        var data    = _controller.CharacterData;
        if (stats == null || stamina == null || data == null) return;

        float max = stats.MaxStamina;

        // 지연 없이 계속 회복하므로 '0인 순간'은 스쳐 지나간다.
        // 그래서 빨강 기준을 "지금 대시할 수 없을 만큼 부족"으로 잡는다 — 플레이어가 알아야 할 건 그것.
        bool cantDash = !stamina.CanConsume(data.dodgeStaminaCost, max);

        _fillRect.sizeDelta = new Vector2(BarWidth * stamina.Normalized(max), BarHeight);
        _fill.color         = cantDash ? FillExhausted : FillNormal;

        // 가득 차면 숨긴다 — 젤다/원신/명조 공통 문법.
        // 슬로모(저스트 회피) 중에도 UI 페이드가 늘어지지 않도록 unscaled 시간으로.
        float target = stamina.IsFull(max) ? 0f : 1f;
        _group.alpha = Mathf.MoveTowards(_group.alpha, target, FadeSpeed * Time.unscaledDeltaTime);
    }

    // ── Private Methods ───────────────────────────────────────────
    private void Build()
    {
        var canvasGo = new GameObject("~StaminaBarCanvas");

        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;

        // 프로젝트 캔버스 기준 준수 — 1920×1080, Scale With Screen Size, Match 0.5
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight  = 0.5f;

        _group = canvasGo.AddComponent<CanvasGroup>();
        _group.alpha          = 0f;   // 가득 찬 상태로 시작 → 안 보임
        _group.interactable   = false;
        _group.blocksRaycasts = false;

        // 배경(빈 게이지)
        var bgGo   = new GameObject("Bg", typeof(RectTransform), typeof(Image));
        bgGo.transform.SetParent(canvasGo.transform, false);
        var bgRect = (RectTransform)bgGo.transform;
        bgRect.anchorMin        = new Vector2(0.5f, 0.5f);
        bgRect.anchorMax        = new Vector2(0.5f, 0.5f);
        bgRect.pivot            = new Vector2(0f, 0.5f);
        bgRect.anchoredPosition = new Vector2(-BarWidth * 0.5f, BarOffsetY);
        bgRect.sizeDelta        = new Vector2(BarWidth, BarHeight);
        bgGo.GetComponent<Image>().color = BgColor;

        // 채움 — 왼쪽 pivot으로 sizeDelta만 줄여 채움 표현(머티리얼/스프라이트 의존 없음)
        var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fillGo.transform.SetParent(bgGo.transform, false);
        _fillRect                  = (RectTransform)fillGo.transform;
        _fillRect.anchorMin        = new Vector2(0f, 0.5f);
        _fillRect.anchorMax        = new Vector2(0f, 0.5f);
        _fillRect.pivot            = new Vector2(0f, 0.5f);
        _fillRect.anchoredPosition = Vector2.zero;
        _fillRect.sizeDelta        = new Vector2(BarWidth, BarHeight);

        _fill       = fillGo.GetComponent<Image>();
        _fill.color = FillNormal;
    }
}
