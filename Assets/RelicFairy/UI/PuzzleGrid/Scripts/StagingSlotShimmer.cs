using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 보관함 아이템 카드에 대각선 shimmer 반짝임 효과를 추가한다.
/// Time.timeScale = 0 환경에서도 동작하도록 unscaledTime 기반.
/// StagingAreaView.RefreshSlotDisplay에서 AddComponent/Destroy로 관리한다.
/// 전설 룬은 <see cref="Configure"/>로 금빛·짧은 주기로 바꿔 판·보관함·획득 카드 어디서든 한눈에 띄게 한다.
/// </summary>
[DisallowMultipleComponent]
public sealed class StagingSlotShimmer : MonoBehaviour
{
    // ── Constants ──
    private const float PERIOD         = 2.8f;
    private const float SWEEP_DURATION = 0.50f;
    private const float CARD_WIDTH     = 110f;
    private const float PEAK_ALPHA     = 0.30f;

    /// <summary>전설 룬 광택 — 금빛, 더 진하고 더 자주 흐른다.</summary>
    public static readonly Color LegendaryTint = new(1f, 0.86f, 0.52f, 1f);

    // ── Private ──
    private RectTransform _shimmerRT;
    private Image         _shimmerImg;
    private float         _offset;
    private float         _period = PERIOD;
    private float         _travel = CARD_WIDTH;
    private float         _peak   = PEAK_ALPHA;
    private Color         _tint   = Color.white;

    // ── Lifecycle ──

    private void Awake() => Build();

    // ── Update ──

    private void Update()
    {
        if (_shimmerImg == null) return;

        float t = Mathf.Repeat(Time.unscaledTime + _offset, _period);

        if (t < SWEEP_DURATION)
        {
            float p = t / SWEEP_DURATION;
            // -60 → 폭 + 30
            _shimmerRT.anchoredPosition = new Vector2(
                Mathf.Lerp(-60f, _travel + 30f, p), 0f);

            // sin 커브로 부드럽게 등장·퇴장
            _shimmerImg.color = new Color(_tint.r, _tint.g, _tint.b, Mathf.Sin(p * Mathf.PI) * _peak);
        }
        else
        {
            _shimmerImg.color = new Color(_tint.r, _tint.g, _tint.b, 0f);
        }
    }

    // ── Public ──

    /// <summary>
    /// 광택의 색·세기·주기·쓸고 지나갈 폭을 바꾼다. <paramref name="phase"/>를 주면 무작위 위상 대신 그 값을 쓴다 —
    /// 여러 칸에 나눠 붙일 때 칸 위치만큼 늦추면 하나의 빛이 모양 전체를 가로질러 흐르는 것처럼 보인다.
    /// </summary>
    public void Configure(Color tint, float peakAlpha, float period, float travelWidth, float? phase = null)
    {
        _tint   = tint;
        _peak   = peakAlpha;
        _period = Mathf.Max(SWEEP_DURATION + 0.1f, period);
        _travel = travelWidth;
        if (phase.HasValue) _offset = phase.Value;
        if (_shimmerRT != null) _shimmerRT.sizeDelta = new Vector2(Mathf.Clamp(travelWidth * 0.36f, 24f, 90f), 0f);
    }

    // ── Build ──

    private void Build()
    {
        // 카드 경계 밖으로 stripe가 넘치지 않도록 RectMask2D 클리핑 컨테이너
        var maskGO = new GameObject("ShimmerMask", typeof(RectTransform));
        maskGO.transform.SetParent(transform, false);
        var maskRT = maskGO.GetComponent<RectTransform>();
        maskRT.anchorMin = Vector2.zero;
        maskRT.anchorMax = Vector2.one;
        maskRT.sizeDelta = Vector2.zero;
        maskGO.AddComponent<RectMask2D>();

        // 경사진 흰색 stripe
        var stripeGO = new GameObject("Stripe", typeof(RectTransform));
        stripeGO.transform.SetParent(maskGO.transform, false);
        _shimmerRT = stripeGO.GetComponent<RectTransform>();
        _shimmerRT.anchorMin        = new Vector2(0f, 0f);
        _shimmerRT.anchorMax        = new Vector2(0f, 1f);
        _shimmerRT.pivot            = new Vector2(0.5f, 0.5f);
        _shimmerRT.sizeDelta        = new Vector2(40f, 0f);
        _shimmerRT.localRotation    = Quaternion.Euler(0f, 0f, 12f);
        _shimmerRT.anchoredPosition = new Vector2(-60f, 0f);

        _shimmerImg              = stripeGO.AddComponent<Image>();
        _shimmerImg.color        = new Color(1f, 1f, 1f, 0f);
        _shimmerImg.raycastTarget = false;

        // 카드마다 랜덤 위상 → 여러 카드가 동시에 반짝이지 않음
        _offset = Random.Range(0f, PERIOD);
    }
}
