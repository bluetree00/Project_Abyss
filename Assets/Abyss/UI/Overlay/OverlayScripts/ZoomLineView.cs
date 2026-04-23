//============================================================
// ZoomLineView.cs
// - 블로그 ④ Zoom-In 집중선 — 크리티컬/Heavy 타격에만 짧은 펄스
// - 런타임에 절차적 radial lines 텍스처 생성 (아트 에셋 불필요)
// - 중앙 투명, 외곽 방사형 빛살. ThunderGroggyVignetteView 패턴 응용
// - peak alpha 0.3~0.5 권장 (블로그 "강하게 하지 말라" 지침 준수)
//============================================================
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace Abyss.UI.Overlay
{
    public sealed class ZoomLineView : MonoBehaviour
    {
        // ── Constants ─────────────────────────────────────────────────
        private const int   TextureSize        = 512;
        private const int   LineCount          = 24;     // 방사형 line 수
        private const float InnerRadius        = 0.18f;  // 완전 투명 영역 반경 (0~1)
        private const float OuterRadius        = 0.98f;  // 페이드 끝 반경
        private const float LineHalfAngleDeg   = 3.5f;   // 각 line 폭 (+/- 각도)

        // ── SerializeField ────────────────────────────────────────────
        [Header("Refs")]
        [SerializeField] private Image       image;
        [SerializeField] private CanvasGroup canvasGroup;

        [Header("Shape")]
        [SerializeField] private AnimationCurve decayCurve = new AnimationCurve(
            new Keyframe(0f,   0f),
            new Keyframe(0.1f, 1f),
            new Keyframe(1f,   0f));

        // ── Private ───────────────────────────────────────────────────
        private static Sprite s_cachedSprite; // 런타임 재사용
        private CancellationTokenSource _cts;

        // ── Lifecycle ─────────────────────────────────────────────────
        private void Awake()
        {
            if (image == null) image = GetComponent<Image>();
            if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();

            if (image != null)
            {
                image.raycastTarget = false;
                if (image.sprite == null)
                    image.sprite = GetOrCreateSprite();
                image.color = Color.white;
            }

            if (canvasGroup != null)
            {
                canvasGroup.alpha          = 0f;
                canvasGroup.blocksRaycasts = false;
                canvasGroup.interactable   = false;
            }
        }

        private void OnDestroy()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }

        // ── Public Methods ────────────────────────────────────────────
        /// <summary>집중선 펄스. intensity는 0~1 peak alpha (권장 0.3~0.5).</summary>
        public void ZoomIn(float intensity, float duration)
        {
            if (image == null || canvasGroup == null) return;
            if (duration <= 0f) return;

            _cts?.Cancel();
            _cts?.Dispose();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(
                this.GetCancellationTokenOnDestroy());

            PlayAsync(Mathf.Clamp01(intensity), duration, _cts.Token).Forget();
        }

        // ── Private Methods ───────────────────────────────────────────
        private async UniTaskVoid PlayAsync(float peak, float duration, CancellationToken token)
        {
            try
            {
                float t = 0f;
                while (t < duration)
                {
                    token.ThrowIfCancellationRequested();

                    float n = Mathf.Clamp01(t / duration);
                    canvasGroup.alpha = decayCurve.Evaluate(n) * peak;

                    t += Time.unscaledDeltaTime;
                    await UniTask.Yield(PlayerLoopTiming.Update, token);
                }

                canvasGroup.alpha = 0f;
            }
            catch (OperationCanceledException)
            {
                if (canvasGroup != null) canvasGroup.alpha = 0f;
            }
        }

        //==========================================================
        // Procedural Texture — radial lines
        //==========================================================

        private static Sprite GetOrCreateSprite()
        {
            if (s_cachedSprite != null) return s_cachedSprite;

            var tex = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false);
            tex.wrapMode   = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            float   half         = TextureSize * 0.5f;
            var     pixels       = new Color32[TextureSize * TextureSize];
            float   angleStepRad = Mathf.PI * 2f / LineCount;
            float   halfAngleRad = LineHalfAngleDeg * Mathf.Deg2Rad;

            for (int y = 0; y < TextureSize; y++)
            {
                for (int x = 0; x < TextureSize; x++)
                {
                    float nx   = (x - half) / half;
                    float ny   = (y - half) / half;
                    float dist = Mathf.Sqrt(nx * nx + ny * ny);

                    // 중앙 투명
                    if (dist <= InnerRadius)
                    {
                        pixels[y * TextureSize + x] = new Color32(255, 255, 255, 0);
                        continue;
                    }

                    // 외곽 컷오프
                    if (dist > OuterRadius) dist = OuterRadius;

                    float angle = Mathf.Atan2(ny, nx); // -PI ~ PI

                    // 가장 가까운 line 중심과의 각도 차 (최소값)
                    float angleMod   = Mathf.Repeat(angle, angleStepRad);
                    float angleDelta = Mathf.Min(angleMod, angleStepRad - angleMod);

                    // line 내부 여부 — halfAngleRad 이내면 alpha on
                    float angleFactor = 1f - Mathf.SmoothStep(0f, halfAngleRad, angleDelta);

                    // 반경 페이드 — 중앙 근처부터 페이드 인, 외곽에서 페이드 아웃
                    float radialFactor =
                        Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(InnerRadius, InnerRadius + 0.15f, dist)) *
                        Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(OuterRadius - 0.15f, OuterRadius, dist));

                    float a = angleFactor * radialFactor;
                    pixels[y * TextureSize + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);

            s_cachedSprite = Sprite.Create(
                tex,
                new Rect(0, 0, TextureSize, TextureSize),
                new Vector2(0.5f, 0.5f));
            return s_cachedSprite;
        }
    }
}
