using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 대시(스태미너) 게이지 — 원신·명조 문법 + 「한 칸 = 대시 1회」.
///  · 화면 중앙(캐릭터) 살짝 아래 가로 게이지. 스킨은 HUD 체력바에서 파생한 스프라이트(CharacterData.dashGauge*)
///  · 대시 1회 비용마다 칸 구분선 — 저스트 회피 환급(+25)이 '한 칸이 돌아왔다'로 읽힌다
///  · 게이지를 돌려받으면 돌아온 구간이 청록(저스트 회피 색)으로 번쩍이고, 가득 차도 잠깐 보인다
///    (보상이 설명 없이 차오르면 결함으로 읽힌다 — 2026-09-19 연출 설계 E2)
///  · 소모/회복 중에만 보이고 <b>가득 차면 자동으로 숨는다</b> · 대시할 수 없으면 <b>빨강</b>
/// 스프라이트가 비어 있으면 예전 기본 막대로 그린다.
///
/// HUD 프리팹/씬을 건드리지 않도록 런타임에 자체 Canvas를 만든다
/// (DodgePresentation과 동일한 '런타임 자동 부착' 패턴). 추후 HUD로 이관 가능.
/// </summary>
[DisallowMultipleComponent]
public class StaminaBarView : MonoBehaviour
{
    // ── Constants ─────────────────────────────────────────────────
    private const float BarWidth         = 240f;
    private const float PlainBarHeight   = 10f;     // 스프라이트가 없을 때의 막대 높이
    private const float BarOffsetY       = -248f;   // 화면 중앙(캐릭터) 기준 아래로 — 캐릭터 몸에 안 가리게 발밑 쪽으로 내림
    private const float FadeSpeed        = 8f;      // 표시/숨김 페이드 속도(초당)
    private const float GainFlashSeconds = 0.5f;    // 돌아온 구간이 번쩍이는 시간
    private const float GainCatchUp      = 0.25f;   // 본 채움이 돌아온 구간을 따라 차오르는 시간
    private const float HoldAfterGain    = 1.0f;    // 환급 뒤엔 가득 차도 이만큼 보인다
    private const int   MaxDividers      = 8;
    private const float DividerWidth     = 2f;

    private static readonly Color FillNormal    = new Color(1f, 0.78f, 0.30f, 1f);    // 호박색
    private static readonly Color FillExhausted = new Color(0.95f, 0.30f, 0.26f, 1f); // 대시 불가 — 빨강
    private static readonly Color GainColor     = new Color(0.40f, 0.95f, 0.92f, 1f); // 저스트 회피 청록
    private static readonly Color FrameColor    = new Color(0.86f, 0.82f, 0.94f, 1f);
    private static readonly Color DividerColor  = new Color(0.08f, 0.06f, 0.11f, 0.95f);
    private static readonly Color PlainBgColor  = new Color(0f, 0f, 0f, 0.45f);

    // ── Private ───────────────────────────────────────────────────
    private PlayerController  _controller;
    private StaminaController _subscribed;
    private CanvasGroup       _group;
    private Image             _fill;
    private Image             _gain;
    private readonly Image[]  _dividers = new Image[MaxDividers];
    private bool  _useSprites;
    private float _barHeight;
    private float _dividerStep = -1f;   // 마지막으로 배치한 칸 간격(비율) — 바뀔 때만 다시 놓는다

    private float _gainStart = -1f;     // 환급 시각(실제시간). 0 미만이면 번쩍임 없음
    private float _gainFrom;            // 환급 직전 비율
    private float _holdUntil;           // 이 시각까지는 가득 차도 숨기지 않는다

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
        Subscribe(_controller != null ? _controller.Stamina : null);
    }

    private void LateUpdate()
    {
        if (_controller == null || _group == null) return;

        var stats   = _controller.RuntimeStats;
        var stamina = _controller.Stamina;
        var data    = _controller.CharacterData;
        if (stats == null || stamina == null || data == null) return;
        if (stamina != _subscribed) Subscribe(stamina);   // 게이지 객체가 늦게 생기거나 바뀐 경우

        float max = stats.MaxStamina;
        float now = Time.unscaledTime;   // 슬로모(저스트 회피) 중에도 UI가 늘어지지 않도록 실제시간

        // 지연 없이 계속 회복하므로 '0인 순간'은 스쳐 지나간다.
        // 그래서 빨강 기준을 "지금 대시할 수 없을 만큼 부족"으로 잡는다 — 플레이어가 알아야 할 건 그것.
        // 실제 소모(PlayerController.TryConsumeDodgeStamina)와 같은 배율 — 악몽 규칙 「뿌리의 속박」.
        float dashCost = data.dodgeStaminaCost * NightmareRules.DodgeStaminaMultiplier;
        bool  cantDash = !stamina.CanConsume(dashCost, max);
        LayoutDividers(max > 0f ? dashCost / max : 0f);

        float value = stamina.Normalized(max);
        float shown = value;
        if (_gainStart >= 0f)
        {
            float e = now - _gainStart;
            if (e >= GainFlashSeconds)
            {
                _gainStart = -1f;
                SetColorAlpha(_gain, 0f);
            }
            else
            {
                // 돌아온 구간은 청록으로 먼저 보이고, 본 채움이 그 위로 따라 차오른다.
                float k = Mathf.Clamp01(e / GainCatchUp);
                shown = Mathf.Lerp(_gainFrom, value, 1f - (1f - k) * (1f - k));
                SetFill(_gain, value);
                SetColorAlpha(_gain, 1f - e / GainFlashSeconds);
            }
        }

        SetFill(_fill, shown);
        _fill.color = cantDash ? FillExhausted : FillNormal;

        // 가득 차면 숨긴다 — 젤다/원신/명조 공통 문법. 단 돌려받은 직후엔 보여 준다.
        float target = stamina.IsFull(max) && now >= _holdUntil ? 0f : 1f;
        _group.alpha = Mathf.MoveTowards(_group.alpha, target, FadeSpeed * Time.unscaledDeltaTime);
    }

    private void OnDisable()
    {
        Subscribe(null);
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

    // ── Private Methods ───────────────────────────────────────────
    private void Subscribe(StaminaController stamina)
    {
        if (_subscribed == stamina) return;
        if (_subscribed != null) _subscribed.Refunded -= HandleRefunded;
        _subscribed = stamina;
        if (_subscribed != null) _subscribed.Refunded += HandleRefunded;
    }

    private void Build()
    {
        var data = _controller != null ? _controller.CharacterData : null;
        _useSprites = data != null && data.dashGaugeFill != null;
        _barHeight  = _useSprites
            ? BarWidth * data.dashGaugeFill.rect.height / Mathf.Max(1f, data.dashGaugeFill.rect.width)
            : PlainBarHeight;

        var canvasGo = new GameObject("~StaminaBarCanvas");

        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = UISortingOrder.WorldGauge;

        // 프로젝트 캔버스 기준 준수 — 1920×1080, Scale With Screen Size, Match 0.5
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight  = 0.5f;

        _group = canvasGo.AddComponent<CanvasGroup>();
        _group.alpha          = 0f;   // 가득 찬 상태로 시작 → 안 보임
        _group.interactable   = false;
        _group.blocksRaycasts = false;

        // 게이지 영역 — 바탕·채움·구분선이 같은 사각형을 공유한다.
        var bar = CreateImage("Bar", canvasGo.transform, _useSprites ? data.dashGaugeBase : null,
                              _useSprites && data.dashGaugeBase != null ? Color.white : PlainBgColor);
        var barRect = (RectTransform)bar.transform;
        barRect.anchorMin        = new Vector2(0.5f, 0.5f);
        barRect.anchorMax        = new Vector2(0.5f, 0.5f);
        barRect.pivot            = new Vector2(0.5f, 0.5f);
        barRect.anchoredPosition = new Vector2(0f, BarOffsetY);
        barRect.sizeDelta        = new Vector2(BarWidth, _barHeight);

        // 돌아온 구간(청록) — 본 채움 아래에 깔려, 본 채움이 따라 차오르기 전까지 보인다.
        _gain = CreateFillImage("Gain", bar.transform, data);
        _gain.color = new Color(GainColor.r, GainColor.g, GainColor.b, 0f);

        _fill = CreateFillImage("Fill", bar.transform, data);
        _fill.color = FillNormal;

        for (int i = 0; i < MaxDividers; i++)
        {
            var d  = CreateImage("Divider", bar.transform, null, DividerColor);
            var rt = (RectTransform)d.transform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(DividerWidth, 0f);
            d.gameObject.SetActive(false);
            _dividers[i] = d;
        }

        // 테두리 — 채움보다 크다(위아래 장식). 원본 비율 그대로 게이지 중심에 맞춘다.
        if (_useSprites && data.dashGaugeFrame != null)
        {
            var fr   = data.dashGaugeFrame.rect;
            var fill = data.dashGaugeFill.rect;
            float scale = BarWidth / Mathf.Max(1f, fill.width);
            var frame   = CreateImage("Frame", bar.transform, data.dashGaugeFrame, FrameColor);
            var frt     = (RectTransform)frame.transform;
            frt.anchorMin        = new Vector2(0.5f, 0.5f);
            frt.anchorMax        = new Vector2(0.5f, 0.5f);
            frt.pivot            = new Vector2(0.5f, 0.5f);
            frt.anchoredPosition = Vector2.zero;
            frt.sizeDelta        = new Vector2(fr.width * scale, fr.height * scale);
        }
    }

    // 채움 이미지 — 스프라이트가 있으면 Filled(가로·왼쪽부터), 없으면 왼쪽 pivot 사각형을 폭으로 줄인다.
    private Image CreateFillImage(string name, Transform parent, CharacterData data)
    {
        var img = CreateImage(name, parent, _useSprites ? data.dashGaugeFill : null, Color.white);
        var rt  = (RectTransform)img.transform;
        if (_useSprites)
        {
            rt.anchorMin  = Vector2.zero;
            rt.anchorMax  = Vector2.one;
            rt.offsetMin  = Vector2.zero;
            rt.offsetMax  = Vector2.zero;
            img.type       = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Horizontal;
            img.fillOrigin = (int)Image.OriginHorizontal.Left;
            img.fillAmount = 1f;
        }
        else
        {
            rt.anchorMin        = new Vector2(0f, 0.5f);
            rt.anchorMax        = new Vector2(0f, 0.5f);
            rt.pivot            = new Vector2(0f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta        = new Vector2(BarWidth, _barHeight);
        }
        return img;
    }

    private static Image CreateImage(string name, Transform parent, Sprite sprite, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.sprite        = sprite;
        img.color         = color;
        img.raycastTarget = false;
        return img;
    }

    private void SetFill(Image img, float value)
    {
        value = Mathf.Clamp01(value);
        if (_useSprites) img.fillAmount = value;
        else ((RectTransform)img.transform).sizeDelta = new Vector2(BarWidth * value, _barHeight);
    }

    private static void SetColorAlpha(Image img, float a)
    {
        var c = img.color;
        c.a = Mathf.Clamp01(a);
        img.color = c;
    }

    // 칸 구분선 — 대시 1회 비용마다. 비용·최대치가 바뀔 때만 다시 놓는다(악몽 배율·최대 스태미너 보너스).
    private void LayoutDividers(float step)
    {
        if (Mathf.Approximately(step, _dividerStep)) return;
        _dividerStep = step;

        int count = step > 0.001f ? Mathf.Min(MaxDividers, Mathf.FloorToInt((1f - 0.0001f) / step)) : 0;
        for (int i = 0; i < MaxDividers; i++)
        {
            bool on = i < count;
            _dividers[i].gameObject.SetActive(on);
            if (on)
                ((RectTransform)_dividers[i].transform).anchoredPosition = new Vector2(BarWidth * step * (i + 1), 0f);
        }
    }

    // ── Event Handlers ────────────────────────────────────────────
    private void HandleRefunded(float amount)
    {
        if (_controller == null || _subscribed == null) return;
        var stats = _controller.RuntimeStats;
        float max = stats != null ? stats.MaxStamina : 0f;
        if (max <= 0f) return;

        float now   = Time.unscaledTime;
        float after = _subscribed.Normalized(max);
        // 이전 번쩍임이 진행 중이면 그 시작점을 이어 쓴다(연속 적중 +5가 겹쳐도 돌아온 구간 전체가 보이게).
        if (_gainStart < 0f) _gainFrom = Mathf.Clamp01(after - amount / max);
        _gainStart = now;
        _holdUntil = now + HoldAfterGain;
    }
}
