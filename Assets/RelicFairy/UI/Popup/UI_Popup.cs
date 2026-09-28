using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

public class UI_Popup : UI_Base
{
    // ── Constants ────────────────────────────────────────────
    // 공통 박자(UIFader) — 의뢰서 §3 「페이드+스케일 0.15초 · 과함 지양」. 예전 0.20초 · 0.85→1 · 튕김(EaseOutBack)은
    // 장난감처럼 튀었고, 루트째 배율을 줘 화면 전체 막까지 움츠러들었다(09-28 UI 톤 진단).
    private const float OpenDuration  = UIFader.OpenSec;
    private const float CloseDuration = UIFader.CloseSec;

    private static readonly Vector3 OpenStartScale = Vector3.one * UIFader.PanelFromScale;
    private static readonly Vector3 CloseEndScale  = Vector3.one * UIFader.PanelToScale;

    // ── Private ──────────────────────────────────────────────
    private CanvasGroup   _cg;
    private RectTransform _rt;
    private bool          _isClosing;
    private readonly List<RectTransform> _fullScreen = new();   // 막처럼 화면을 가득 덮는 자식 — 배율을 되돌려 고정
    private bool          _fullScreenCollected;

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
        {
            // 자기 배율을 직접 굴리는 UI에는 붙이지 않는다 — 같은 localScale을 두고 다투면
            // 마우스를 뗄 때 기준 크기로 되돌아가 선택 강조가 지워진다.
            if (btn.GetComponent<IOwnsButtonScale>() != null) continue;
            if (btn.GetComponent<UIButtonFeedback>() == null)
                btn.gameObject.AddComponent<UIButtonFeedback>();
        }
    }

    // ── Open ─────────────────────────────────────────────────

    /// <summary>UIManager가 팝업 표시 직후 호출. fire-and-forget.</summary>
    internal void PlayOpenAnimation()
    {
        _isClosing = false;
        CollectFullScreen();
        _cg.alpha  = 0f;
        SetScale(OpenStartScale.x);
        OpenAsync().Forget();
    }

    private async UniTaskVoid OpenAsync()
    {
        float t = 0f;
        while (t < 1f && !_isClosing)
        {
            t = Mathf.Min(t + Time.unscaledDeltaTime / OpenDuration, 1f);
            float e = EaseOutCubic(t);
            SetScale(Mathf.Lerp(OpenStartScale.x, 1f, e));
            _cg.alpha = e;
            await UniTask.Yield(PlayerLoopTiming.Update);
            if (this == null) return;
        }
        if (!_isClosing)
        {
            SetScale(1f);
            _cg.alpha = 1f;
        }
    }

    /// <summary>
    /// 루트 배율을 주되, 화면을 가득 덮는 자식(막 · 전체 바탕)은 되돌려 제자리에 둔다 — 판만 살짝 커지는 것처럼 보인다.
    /// 되돌림이 정확하려면 자식의 기준점이 루트와 같아야 한다(가득 늘인 자식은 대개 가운데 기준).
    /// </summary>
    private void SetScale(float s)
    {
        _rt.localScale = Vector3.one * s;
        var inv = Vector3.one * (s > 0.0001f ? 1f / s : 1f);
        for (int i = 0; i < _fullScreen.Count; i++)
            if (_fullScreen[i] != null) _fullScreen[i].localScale = inv;
    }

    private void CollectFullScreen()
    {
        if (_fullScreenCollected || _rt == null) return;
        _fullScreenCollected = true;
        for (int i = 0; i < _rt.childCount; i++)
        {
            if (!(_rt.GetChild(i) is RectTransform c)) continue;
            bool full = c.anchorMin == Vector2.zero && c.anchorMax == Vector2.one
                     && c.offsetMin.sqrMagnitude < 1f && c.offsetMax.sqrMagnitude < 1f
                     && (c.pivot - _rt.pivot).sqrMagnitude < 0.0001f;
            if (full) _fullScreen.Add(c);
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
            CollectFullScreen();
            float startAlpha = _cg.alpha;
            float startScale = _rt.localScale.x;
            float t          = 0f;

            while (t < 1f)
            {
                t = Mathf.Min(t + Time.unscaledDeltaTime / CloseDuration, 1f);
                float e    = EaseInQuad(t);
                SetScale(Mathf.Lerp(startScale, CloseEndScale.x, e));
                _cg.alpha  = Mathf.Lerp(startAlpha, 0f, e);
                await UniTask.Yield(PlayerLoopTiming.Update);
                if (this == null) return;
            }
        }

        if (this != null && gameObject != null)
            Object.Destroy(gameObject);
    }

    // ── Easing ───────────────────────────────────────────────

    private static float EaseOutCubic(float t) => 1f - (1f - t) * (1f - t) * (1f - t);

    private static float EaseInQuad(float t) => t * t;
}
