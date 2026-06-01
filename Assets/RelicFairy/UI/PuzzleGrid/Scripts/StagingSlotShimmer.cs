using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 보관함 아이템 카드에 대각선 shimmer 반짝임 효과를 추가한다.
/// Time.timeScale = 0 환경에서도 동작하도록 unscaledTime 기반.
/// StagingAreaView.RefreshSlotDisplay에서 AddComponent/Destroy로 관리한다.
/// </summary>
[DisallowMultipleComponent]
public sealed class StagingSlotShimmer : MonoBehaviour
{
    // ── Constants ──
    private const float PERIOD         = 2.8f;
    private const float SWEEP_DURATION = 0.50f;
    private const float CARD_WIDTH     = 110f;

    // ── Private ──
    private RectTransform _shimmerRT;
    private Image         _shimmerImg;
    private float         _offset;

    // ── Lifecycle ──

    private void Awake() => Build();

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

    // ── Update ──

    private void Update()
    {
        if (_shimmerImg == null) return;

        float t = (Time.unscaledTime + _offset) % PERIOD;

        if (t < SWEEP_DURATION)
        {
            float p = t / SWEEP_DURATION;
            // -60 → CARD_WIDTH + 30
            _shimmerRT.anchoredPosition = new Vector2(
                Mathf.Lerp(-60f, CARD_WIDTH + 30f, p), 0f);

            // sin 커브로 부드럽게 등장·퇴장
            _shimmerImg.color = new Color(1f, 1f, 1f, Mathf.Sin(p * Mathf.PI) * 0.30f);
        }
        else
        {
            _shimmerImg.color = new Color(1f, 1f, 1f, 0f);
        }
    }
}
