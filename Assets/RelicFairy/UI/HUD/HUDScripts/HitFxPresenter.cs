//============================================================
// HitFxPresenter.cs
// - HitFeedbackService.OnHit 구독 → FXLayer로 위임 (블로그 UI 화면 연출 3종)
// - 보완(Flash): 모든 히트
// - ④ Zoom-In: 크리티컬만
// - ⑦ Damage Vignette: 플레이어가 피격자일 때만
//
// 호스트: FXLayer.prefab 루트에 부착 — 수명이 Layer와 동일
//============================================================
using RelicFairy.UI.Overlay;
using UnityEngine;

public sealed class HitFxPresenter : MonoBehaviour
{
    // ── Constants ─────────────────────────────────────────────────
    // Flash
    private const float FlashPeakNormal   = 0.3f;
    private const float FlashPeakCritical = 0.55f;
    private const float FlashDuration     = 0.06f;

    // Zoom-In (크리티컬만)
    private const float ZoomIntensity     = 0.4f;
    private const float ZoomDuration      = 0.14f;

    // Damage Vignette (플레이어 피격)
    private const float VignetteIntensity = 0.6f;
    private const float VignetteDuration  = 0.32f;

    private static readonly Color DefaultFlashColor   = new Color(1f, 1f, 1f, 1f);
    private static readonly Color CritFlashColor      = new Color(1f, 0.95f, 0.35f, 1f);
    private static readonly Color DamageVignetteColor = new Color(1f, 0.1f, 0.1f, 1f);

    // ── Private ───────────────────────────────────────────────────
    private FXLayer _layer;

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        // 같은 프리팹 루트에 FXLayer가 있음 (FXLayer.prefab)
        _layer = GetComponent<FXLayer>();
        if (_layer == null) _layer = GetComponentInParent<FXLayer>(true);
    }

    private void OnEnable()
    {
        HitFeedbackService.OnHit += OnHit;
    }

    private void OnDisable()
    {
        HitFeedbackService.OnHit -= OnHit;
    }

    // ── Event Handler ─────────────────────────────────────────────
    private void OnHit(HitInfo info)
    {
        var fx = _layer;
        if (fx == null) fx = Managers.UI?.GetOverlayUI<FXLayer>();
        if (fx == null) return;

        // 1) Flash — 모든 히트
        var   flashColor = info.IsCritical ? CritFlashColor : DefaultFlashColor;
        float flashPeak  = info.IsCritical ? FlashPeakCritical : FlashPeakNormal;
        fx.Flash(flashColor, FlashDuration, flashPeak);

        // 2) Zoom-In 집중선 — 크리티컬만
        if (info.IsCritical)
            fx.ZoomIn(ZoomIntensity, ZoomDuration);

        // 3) Damage Vignette — 피격자가 플레이어일 때만
        if (info.Target != null && info.Target.GetComponent<PlayerController>() != null)
            fx.Vignette(DamageVignetteColor, VignetteIntensity, VignetteDuration);
    }

}
