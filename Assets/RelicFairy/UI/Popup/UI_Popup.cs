using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

public class UI_Popup : UI_Base
{
    // ── Constants ────────────────────────────────────────────
    private const float OpenDuration  = 0.20f;
    private const float CloseDuration = 0.14f;

    private static readonly Vector3 OpenStartScale = Vector3.one * 0.85f;
    private static readonly Vector3 CloseEndScale  = Vector3.one * 0.92f;

    // ── Private ──────────────────────────────────────────────
    private CanvasGroup   _cg;
    private RectTransform _rt;
    private bool          _isClosing;

    // ── Init ─────────────────────────────────────────────────

    public override void Init()
    {
        Managers.UI.SetCanvas(gameObject, true);

        _rt = GetComponent<RectTransform>();
        _cg = gameObject.GetOrAddComponent<CanvasGroup>();

        foreach (var btn in GetComponentsInChildren<Button>(true))
            if (btn.GetComponent<UIButtonFeedback>() == null)
                btn.gameObject.AddComponent<UIButtonFeedback>();
    }

    // ── Open ─────────────────────────────────────────────────

    /// <summary>UIManager가 팝업 표시 직후 호출. fire-and-forget.</summary>
    internal void PlayOpenAnimation()
    {
        _isClosing     = false;
        _cg.alpha      = 0f;
        _rt.localScale = OpenStartScale;
        OpenAsync().Forget();
    }

    private async UniTaskVoid OpenAsync()
    {
        float t = 0f;
        while (t < 1f && !_isClosing)
        {
            t = Mathf.Min(t + Time.unscaledDeltaTime / OpenDuration, 1f);
            _rt.localScale = Vector3.LerpUnclamped(OpenStartScale, Vector3.one, EaseOutBack(t));
            _cg.alpha      = Mathf.Clamp01(t * 2f);
            await UniTask.Yield(PlayerLoopTiming.Update);
            if (this == null) return;
        }
        if (!_isClosing)
        {
            _rt.localScale = Vector3.one;
            _cg.alpha      = 1f;
        }
    }

    // ── Close ─────────────────────────────────────────────────

    public virtual void ClosePopupUI()
    {
        Managers.UI.ClosePopupUI(this);
    }

    /// <summary>UIManager가 스택에서 팝업을 제거한 뒤 호출. 애니메이션 후 자신을 파괴한다.</summary>
    internal void StartCloseAndDestroy()
    {
        _isClosing = true;
        CloseAsync().Forget();
    }

    private async UniTaskVoid CloseAsync()
    {
        if (_cg != null && _rt != null)
        {
            float   startAlpha = _cg.alpha;
            Vector3 startScale = _rt.localScale;
            float   t          = 0f;

            while (t < 1f)
            {
                t = Mathf.Min(t + Time.unscaledDeltaTime / CloseDuration, 1f);
                float e    = EaseInQuad(t);
                _rt.localScale = Vector3.Lerp(startScale, CloseEndScale, e);
                _cg.alpha      = Mathf.Lerp(startAlpha, 0f, e);
                await UniTask.Yield(PlayerLoopTiming.Update);
                if (this == null) return;
            }
        }

        if (this != null && gameObject != null)
            Object.Destroy(gameObject);
    }

    // ── Easing ───────────────────────────────────────────────

    private static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
    }

    private static float EaseInQuad(float t) => t * t;
}
