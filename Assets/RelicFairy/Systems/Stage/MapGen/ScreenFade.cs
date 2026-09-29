using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 화면 전환 유틸. 단순 알파 페이드(Out/In)와 방향성 와이프(Cover/Reveal)를 제공한다.
/// 와이프는 컬러 패널을 진행 방향으로 슬라이드시켜 '이동' 느낌을 준다(Congruence 원칙).
/// DontDestroyOnLoad 캔버스 1개를 재사용, timeScale 영향 없는 unscaled 시간으로 동작.
/// <para>09-26: 와이프 스킨(<see cref="ScreenWipeSkinSO"/>)이 있으면 단색 판 대신 잉크 와이프 — 찢긴 가장자리가
/// 같은 방향으로 번져 덮고, 몸통은 심연 구름에 방 종류색, 가운데 룬 마법진. 스킨이 없으면 예전 단색 판.</para>
/// </summary>
public static class ScreenFade
{
    private const int SortingOrder = UISortingOrder.ScreenFade;

    private static readonly Vector2 FullMin = Vector2.zero;
    private static readonly Vector2 FullMax = Vector2.one;

    private static CanvasGroup _cg;
    private static Image       _img;
    private static Material    _inkMat;   // UI/RoomWipe 재질 — 스킨을 처음 쓸 때 만든다

    private static readonly int ProgressId = Shader.PropertyToID("_Progress");
    private static readonly int RevealId   = Shader.PropertyToID("_Reveal");
    private static readonly int DirId      = Shader.PropertyToID("_Dir");
    private static readonly int AspectId   = Shader.PropertyToID("_Aspect");
    private static readonly int TintId     = Shader.PropertyToID("_Tint");
    private static readonly int RimId      = Shader.PropertyToID("_Rim");
    private static readonly int FadeId     = Shader.PropertyToID("_Fade");

    // 잉크 와이프 페이드 — 덮을 땐 진행의 앞 60% 동안 서서히 짙어지고, 걷을 땐 뒤 80% 동안 옅어진다(09-26 「갑자기 나오지 않게」).
    private const float InkFadeInPortion  = 0.6f;
    private const float InkFadeOutStart   = 0.2f;
    // 테두리·마법진 빛에 섞는 흰빛 — 방 종류색을 그대로 쓰면 원색이 눈을 찌른다.
    private const float InkRimWhite       = 0.40f;

    // ── 알파 페이드 ──
    public static UniTask Out(float duration, CancellationToken ct = default) => FadeTo(1f, duration, ct);
    public static UniTask In(float duration, CancellationToken ct = default)  => FadeTo(0f, duration, ct);

    // ── 방향성 와이프 ──
    /// <summary>패널이 -dir 쪽 화면 밖에서 들어와 화면을 덮는다. color = 다음 방 종류색.</summary>
    public static UniTask CoverAsync(Vector2 dir, Color color, float duration, AnimationCurve ease, CancellationToken ct = default)
        => TryInk(out var ink)
            ? InkWipe(ink, dir, color, duration, ease, ct, reveal: false)
            : Slide(FullMin - dir, FullMax - dir, FullMin, FullMax, color, duration, ease, ct, blockAfter: true);

    /// <summary>덮인 상태에서 패널이 dir 방향으로 빠져나가 새 화면을 드러낸다.</summary>
    public static UniTask RevealAsync(Vector2 dir, float duration, AnimationCurve ease, CancellationToken ct = default)
        => TryInk(out var ink) && _img != null && _img.material == ink
            ? InkWipe(ink, dir, null, duration, ease, ct, reveal: true)
            : Slide(FullMin, FullMax, FullMin + dir, FullMax + dir, null, duration, ease, ct, blockAfter: false);

    // ── 내부 ──

    private static void Ensure()
    {
        if (_cg != null) return;

        var go = new GameObject("@ScreenFade");
        Object.DontDestroyOnLoad(go);

        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortingOrder;

        _cg = go.AddComponent<CanvasGroup>();
        _cg.alpha          = 0f;
        _cg.blocksRaycasts = false;
        _cg.interactable   = false;

        var imgGO = new GameObject("Cover");
        imgGO.transform.SetParent(go.transform, false);
        _img = imgGO.AddComponent<Image>();
        _img.color         = Color.black;
        _img.raycastTarget = false;
        var rt = _img.rectTransform;
        rt.anchorMin = FullMin; rt.anchorMax = FullMax;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    private static async UniTask FadeTo(float target, float duration, CancellationToken ct)
    {
        Ensure();
        // 알파 페이드(Out/In)는 항상 검정 암전 — 직전 방향성 와이프(Slide)가 _img.color에 남긴
        // 방 종류색(예: 보스방 어두운 빨강)을 물려받아 빨갛게 페이드되는 누수를 차단.
        _img.color = Color.black;
        _img.material = null;   // 직전 잉크 와이프 재질을 물려받지 않게
        _img.enabled  = true;
        var rt = _img.rectTransform;
        rt.anchorMin = FullMin; rt.anchorMax = FullMax; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        _cg.blocksRaycasts = target > 0.5f;

        if (duration <= 0f) { _cg.alpha = target; if (target <= 0f) _cg.blocksRaycasts = false; return; }

        float start = _cg.alpha, t = 0f;
        while (t < duration)
        {
            ct.ThrowIfCancellationRequested();
            t += Time.unscaledDeltaTime;
            _cg.alpha = Mathf.Lerp(start, target, t / duration);
            await UniTask.Yield(PlayerLoopTiming.Update);
        }
        _cg.alpha = target;
        if (target <= 0f) _cg.blocksRaycasts = false;
    }

    private static async UniTask Slide(
        Vector2 fromMin, Vector2 fromMax, Vector2 toMin, Vector2 toMax,
        Color? color, float duration, AnimationCurve ease, CancellationToken ct, bool blockAfter)
    {
        Ensure();
        _cg.alpha = 1f; // 와이프는 알파 1, 위치(앵커)로만 가림
        _img.material = null;
        _img.enabled  = true;
        if (color.HasValue) _img.color = color.Value;
        _cg.blocksRaycasts = true;
        var rt = _img.rectTransform;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

        if (duration <= 0f) { rt.anchorMin = toMin; rt.anchorMax = toMax; _cg.blocksRaycasts = blockAfter; return; }

        float t = 0f;
        while (t < duration)
        {
            ct.ThrowIfCancellationRequested();
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / duration);
            if (ease != null) k = ease.Evaluate(k);
            rt.anchorMin = Vector2.LerpUnclamped(fromMin, toMin, k);
            rt.anchorMax = Vector2.LerpUnclamped(fromMax, toMax, k);
            await UniTask.Yield(PlayerLoopTiming.Update);
        }
        rt.anchorMin = toMin; rt.anchorMax = toMax;
        _cg.blocksRaycasts = blockAfter;
    }

    /// <summary>와이프 스킨이 로드돼 있으면 잉크 와이프 재질. 없거나 셰이더를 못 쓰면 false → 단색 판.</summary>
    private static bool TryInk(out Material mat)
    {
        if (_inkMat == null)
        {
            var skin = UISkin.ScreenWipe;
            if (skin == null || skin.Shader == null || !skin.Shader.isSupported) { mat = null; return false; }
            _inkMat = new Material(skin.Shader) { name = "RoomWipe (runtime)", hideFlags = HideFlags.HideAndDontSave };
            skin.ApplyTo(_inkMat);
        }
        mat = _inkMat;
        return true;
    }

    /// <summary>
    /// 잉크 와이프 — 화면 전체 판은 그대로 두고 셰이더의 진행값만 올린다. 덮기: 찢긴 가장자리가 dir 쪽으로 번져 덮는다 /
    /// 걷기: 뒷가장자리가 dir 쪽으로 빠진다. 다 걷히면 판을 꺼 투명한 화면 전체 셰이더를 매 프레임 그리지 않는다.
    /// </summary>
    private static async UniTask InkWipe(Material mat, Vector2 dir, Color? color, float duration, AnimationCurve ease,
                                         CancellationToken ct, bool reveal)
    {
        Ensure();
        var rt = _img.rectTransform;
        rt.anchorMin = FullMin; rt.anchorMax = FullMax; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        _img.material = mat;
        _img.color    = Color.white;
        _img.enabled  = true;
        if (color.HasValue)
        {
            var c = color.Value;
            mat.SetColor(TintId, c);
            // 가장자리·마법진 빛 = 방 종류색을 가장 밝은 채널이 1이 되게 편 뒤 흰빛을 조금 — 판 색(어두운 톤) 그대로면 안 보인다.
            float mx = Mathf.Max(0.001f, Mathf.Max(c.r, Mathf.Max(c.g, c.b)));
            float k0 = 1f - InkRimWhite;
            mat.SetColor(RimId, new Color(c.r / mx * k0 + InkRimWhite, c.g / mx * k0 + InkRimWhite, c.b / mx * k0 + InkRimWhite, 1f));
        }
        mat.SetVector(DirId, dir);
        mat.SetFloat(RevealId, reveal ? 1f : 0f);
        mat.SetFloat(AspectId, Screen.width / (float)Mathf.Max(1, Screen.height));
        mat.SetFloat(ProgressId, 0f);
        mat.SetFloat(FadeId, InkFade(0f, reveal));
        _cg.alpha          = 1f;
        _cg.blocksRaycasts = true;

        float t = 0f;
        while (t < duration)
        {
            ct.ThrowIfCancellationRequested();
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / duration);
            mat.SetFloat(ProgressId, ease != null ? ease.Evaluate(k) : k);
            mat.SetFloat(FadeId, InkFade(k, reveal));
            await UniTask.Yield(PlayerLoopTiming.Update);
        }
        mat.SetFloat(ProgressId, 1f);
        mat.SetFloat(FadeId, InkFade(1f, reveal));
        if (reveal)
        {
            _img.enabled = false;
            _cg.alpha    = 0f;
        }
        _cg.blocksRaycasts = !reveal;
    }

    /// <summary>진행 k(0~1)에서의 잉크 불투명도. 덮기는 0→1(앞부분에서), 걷기는 1→0(뒷부분에서).</summary>
    private static float InkFade(float k, bool reveal) => reveal
        ? 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((k - InkFadeOutStart) / (1f - InkFadeOutStart)))
        : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(k / InkFadeInPortion));
}
