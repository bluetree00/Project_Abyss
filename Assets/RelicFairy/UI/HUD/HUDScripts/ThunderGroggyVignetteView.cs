using UnityEngine;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;
using System.Threading;

namespace RelicFairy.Monster
{
/// <summary>
/// 번개 그로기 비네트 오버레이.
/// - 서서히 시야가 좁아지고(비네트), duration 후 서서히 회복되는 HUD 효과.
/// - 이미 그로기 상태일 경우 fade-in을 다시 시작하지 않고 유지 시간만 연장한다.
/// - ThunderGroggyVignetteView.Trigger(duration)으로 어디서든 호출.
/// </summary>
public sealed class ThunderGroggyVignetteView : MonoBehaviour
{
    private const int VignetteTextureSize = 256;

    private static ThunderGroggyVignetteView s_instance;

    private Image  _image;
    private CancellationTokenSource _cts;

    // 그로기 종료 예정 시각 (Time.unscaledTime 기준)
    private float _groggyEndTime = -1f;
    private bool  _isGroggied;

    //============================================================
    // Static API
    //============================================================

    /// <summary>번개 그로기 비네트를 재생한다. 이미 그로기 상태라면 종료 시각만 연장한다.</summary>
    public static void Trigger(float duration)
        => EnsureInstance().RequestGroggy(duration);

    //============================================================
    // Lifecycle
    //============================================================

    private void OnDestroy()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        if (s_instance == this) s_instance = null;
    }

    //============================================================
    // Instance Setup
    //============================================================

    private static ThunderGroggyVignetteView EnsureInstance()
    {
        if (s_instance != null) return s_instance;

        var root = new GameObject("[ThunderGroggyVignette]");
        DontDestroyOnLoad(root);

        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = UISortingOrder.MetaVignette;

        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight  = 0.5f;

        var imgGo = new GameObject("VignetteImage");
        imgGo.transform.SetParent(root.transform, false);

        var img = imgGo.AddComponent<Image>();
        var rt  = img.rectTransform;
        rt.anchorMin  = Vector2.zero;
        rt.anchorMax  = Vector2.one;
        rt.offsetMin  = Vector2.zero;
        rt.offsetMax  = Vector2.zero;
        img.raycastTarget = false;
        img.color         = new Color(0f, 0f, 0.08f, 0f);
        img.sprite        = CreateVignetteSprite(VignetteTextureSize);

        var view    = root.AddComponent<ThunderGroggyVignetteView>();
        view._image = img;
        s_instance  = view;
        return view;
    }

    //============================================================
    // Groggy 요청
    //============================================================

    private void RequestGroggy(float duration)
    {
        float newEnd = Time.unscaledTime + duration;

        if (_isGroggied)
        {
            // 이미 그로기 → 종료 시각만 연장 (깜빡임 없음)
            _groggyEndTime = Mathf.Max(_groggyEndTime, newEnd);
            return;
        }

        // 새로 그로기 진입
        _groggyEndTime = newEnd;
        _isGroggied    = true;
        Play().Forget();
    }

    //============================================================
    // Animation
    //============================================================

    private async UniTaskVoid Play()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        try
        {
            const float fadeIn  = 0.5f;
            const float fadeOut = 0.6f;

            // ── Fade In ───────────────────────────────────────────
            await Fade(GetAlpha(), 1f, fadeIn, token);

            // ── Hold: 종료 시각까지 대기 (_groggyEndTime은 외부에서 연장 가능) ──
            while (Time.unscaledTime < _groggyEndTime - fadeOut)
            {
                token.ThrowIfCancellationRequested();
                await UniTask.Yield(PlayerLoopTiming.Update, token);
            }

            // ── Fade Out ──────────────────────────────────────────
            await Fade(GetAlpha(), 0f, fadeOut, token);
            SetAlpha(0f);
        }
        catch (System.OperationCanceledException)
        {
            SetAlpha(0f);
        }
        finally
        {
            _isGroggied = false;
        }
    }

    private async UniTask Fade(float from, float to, float dur, CancellationToken token)
    {
        float elapsed = 0f;
        dur = Mathf.Max(0.001f, dur);

        while (elapsed < dur)
        {
            token.ThrowIfCancellationRequested();
            elapsed += Time.unscaledDeltaTime;
            SetAlpha(Mathf.Lerp(from, to, elapsed / dur));
            await UniTask.Yield(PlayerLoopTiming.Update, token);
        }
        SetAlpha(to);
    }

    private float GetAlpha() => _image != null ? _image.color.a : 0f;

    private void SetAlpha(float a)
    {
        if (_image == null) return;
        var c = _image.color;
        c.a = a;
        _image.color = c;
    }

    //============================================================
    // Vignette Texture — 중심부 투명 영역을 좁게 설정 (시야 많이 가림)
    //============================================================

    private static Sprite CreateVignetteSprite(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;

        float half = size * 0.5f;
        var pixels = new Color32[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float nx   = (x - half) / half;
                float ny   = (y - half) / half;
                float dist = Mathf.Sqrt(nx * nx + ny * ny);

                // 중심 0.18 이내만 투명, 0.18~0.75 사이 빠르게 어두워짐
                // → 플레이어 주변 좁은 원형만 보이는 터널 비전
                float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.18f, 0.75f, dist));
                pixels[y * size + x] = new Color32(0, 0, 15, (byte)(a * 255));
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply();

        return Sprite.Create(tex,
            new Rect(0, 0, size, size),
            new Vector2(0.5f, 0.5f));
    }
}
}
