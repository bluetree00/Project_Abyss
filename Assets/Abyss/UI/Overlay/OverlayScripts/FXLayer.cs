//============================================================
// FXLayer.cs
// - Canvas_Overlay 하위 FXLayer.prefab의 컨테이너
// - 3개의 View(Full-Screen Flash / Zoom-Line / Damage Vignette)에 위임
// - UIManager.GetOverlayUI<FXLayer>()로 접근 — UI_Base 상속 필수
//============================================================
using UnityEngine;

namespace Abyss.UI.Overlay
{
    public sealed class FXLayer : UI_Base
    {
        // ── SerializeField ────────────────────────────────────────────
        [Header("Views")]
        [SerializeField] private FullScreenFlashView flashView;
        [SerializeField] private ZoomLineView        zoomLineView;
        [SerializeField] private DamageVignetteView  vignetteView;

        // ── Properties ────────────────────────────────────────────────
        public FullScreenFlashView FlashView    => flashView;
        public ZoomLineView        ZoomLineView => zoomLineView;
        public DamageVignetteView  VignetteView => vignetteView;

        // ── Lifecycle ─────────────────────────────────────────────────
        public override void Init()
        {
            base.Init();

            if (flashView    == null) flashView    = GetComponentInChildren<FullScreenFlashView>(true);
            if (zoomLineView == null) zoomLineView = GetComponentInChildren<ZoomLineView>(true);
            if (vignetteView == null) vignetteView = GetComponentInChildren<DamageVignetteView>(true);
        }

        private void Awake()
        {
            // 프리팹에서 활성화되어 있으면 Init 생략돼도 안전하게 캐싱
            if (flashView    == null) flashView    = GetComponentInChildren<FullScreenFlashView>(true);
            if (zoomLineView == null) zoomLineView = GetComponentInChildren<ZoomLineView>(true);
            if (vignetteView == null) vignetteView = GetComponentInChildren<DamageVignetteView>(true);
        }

        // ── Public Methods (위임) ─────────────────────────────────────
        public void Flash(Color color, float duration, float peakAlpha = 0.3f)
        {
            if (flashView == null) return;
            flashView.Flash(color, duration, peakAlpha);
        }

        public void ZoomIn(float intensity, float duration)
        {
            if (zoomLineView == null) return;
            zoomLineView.ZoomIn(intensity, duration);
        }

        public void Vignette(Color color, float intensity, float duration)
        {
            if (vignetteView == null) return;
            vignetteView.Vignette(color, intensity, duration);
        }
    }
}
