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

    // ── Public ───────────────────────────────────────────────
    /// <summary>true면 이 팝업이 열려있는 동안 게임플레이를 차단한다(인게임 시간정지 + 플레이어 입력잠금).
    /// 또한 열려있는 동안 대사 이벤트가 대기한다. 선택/편집 UI·대사 팝업이 override한다.</summary>
    public virtual bool BlocksGameplay => false;

    /// <summary>true면 ESC로 이 팝업을 닫을 수 있다(스택 최상단일 때만, EscKeyListener 경유).
    /// 기본 false — 선택을 강제하는 팝업(보상/무기 모루 등)은 _tcs를 버튼으로만 완료시키므로
    /// ESC로 닫히면 대기가 끝나지 않는다. 취소 경로가 검증된 팝업만 override해서 opt-in한다.</summary>
    public virtual bool CloseOnEscape => false;

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

    /// <summary>
    /// 정상 경로(ClosePopupUI)를 거치지 않고 파괴되는 경우가 있다 — 부모 캔버스 파괴, 외부 Destroy 등.
    /// 그때 스택에는 항목이 남고 게임플레이 차단(시간정지·입력잠금)이 <b>영구히 걸린 채</b>로 남는다.
    /// 파괴 시점에 차단 상태를 재평가시켜 그 잠금이 새지 않게 한다
    /// (IsGameplayBlocked는 파괴된 항목을 세지 않으므로 재평가만으로 해제된다).
    /// </summary>
    protected virtual void OnDestroy()
    {
        if (BlocksGameplay) Managers.UI?.RefreshGameplayBlockExternally();
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
