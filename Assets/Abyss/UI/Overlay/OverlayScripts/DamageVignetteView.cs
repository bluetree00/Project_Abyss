//============================================================
// DamageVignetteView.cs
// - 블로그 ⑦ Damage Vignette — 플레이어가 피격자일 때만 화면 테두리 붉게
// - ThunderGroggyVignetteView 구조 복제 + Canvas 자체 생성 로직 제거 (FXLayer 자식으로 배치)
// - Fade-In 후 Hold 없이 즉시 Fade-Out (히트 플래시형)
//============================================================
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace Abyss.UI.Overlay
{
    public sealed class DamageVignetteView : MonoBehaviour
    {
        // ── Constants ─────────────────────────────────────────────────
        private const int   VignetteTextureSize = 256;
        private const float DefaultInnerRadius  = 0.35f;   // 중심 투명 영역 — Groggy보다 넓게 (가독성 보존)
        private const float DefaultOuterRadius  = 0.85f;

        // 피격 기본 색상 — 진한 빨강
        private static readonly Color DefaultColor = new Color(1f, 0.1f, 0.1f, 1f);

        // ── SerializeField ────────────────────────────────────────────
        [Header("Refs")]
        [SerializeField] private Image image;

        [Header("Shape")]
        [SerializeField] private AnimationCurve decayCurve = new AnimationCurve(
            new Keyframe(0f,   0f),
            new Keyframe(0.2f, 1f),
            new Keyframe(1f,   0f));

        // ── Private ───────────────────────────────────────────────────
        private static Sprite s_cachedSprite;   // 런타임 재사용
        private CancellationTokenSource _cts;

        // ── Lifecycle ─────────────────────────────────────────────────
        private void Awake()
        {
            if (image == null) image = GetComponent<Image>();

            if (image != null)
            {
                image.raycastTarget = false;
                if (image.sprite == null)
                    image.sprite = GetOrCreateSprite();
                SetAlpha(0f);
            }
        }

        private void OnDestroy()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }

        // ── Public Methods ────────────────────────────────────────────
        /// <summary>피격 비네트 펄스. color의 RGB만 반영, alpha는 intensity로 제어.</summary>
        public void Vignette(Color color, float intensity, float duration)
        {
            if (image == null) return;
            if (duration <= 0f) return;

            var rgb = color; rgb.a = 0f;
            image.color = rgb;

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
                    SetAlpha(decayCurve.Evaluate(n) * peak);

                    t += Time.unscaledDeltaTime;
                    await UniTask.Yield(PlayerLoopTiming.Update, token);
                }

                SetAlpha(0f);
            }
            catch (OperationCanceledException)
            {
                SetAlpha(0f);
            }
        }

        private void SetAlpha(float a)
        {
            if (image == null) return;
            var c = image.color;
            c.a = a;
            image.color = c;
        }

        //==========================================================
        // Procedural Vignette Texture
        //==========================================================

        private static Sprite GetOrCreateSprite()
        {
            if (s_cachedSprite != null) return s_cachedSprite;

            var tex = new Texture2D(VignetteTextureSize, VignetteTextureSize, TextureFormat.RGBA32, false);
            tex.wrapMode   = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            float half = VignetteTextureSize * 0.5f;
            var pixels = new Color32[VignetteTextureSize * VignetteTextureSize];

            for (int y = 0; y < VignetteTextureSize; y++)
            {
                for (int x = 0; x < VignetteTextureSize; x++)
                {
                    float nx   = (x - half) / half;
                    float ny   = (y - half) / half;
                    float dist = Mathf.Sqrt(nx * nx + ny * ny);

                    // inner 이내 투명, outer 이후 full alpha — 가독성 보존 위해 inner 넓게
                    float a = Mathf.SmoothStep(0f, 1f,
                        Mathf.InverseLerp(DefaultInnerRadius, DefaultOuterRadius, dist));

                    pixels[y * VignetteTextureSize + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);

            s_cachedSprite = Sprite.Create(
                tex,
                new Rect(0, 0, VignetteTextureSize, VignetteTextureSize),
                new Vector2(0.5f, 0.5f));
            _ = DefaultColor;   // 사용처 명시 (Presenter 기본값)
            return s_cachedSprite;
        }
    }
}
