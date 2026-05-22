//============================================================
// TransitionOverlay.cs
// - Canvas_Overlay 위의 전체 화면 페이드 연출
// - Sort Order 최상위로 모든 레이어를 덮음
// - StagePointUI 등에서 방 이동 시 호출
//============================================================
using System;
using UnityEngine;
using Cysharp.Threading.Tasks;

public sealed class TransitionOverlay : MonoBehaviour
{
    public static TransitionOverlay Instance { get; private set; }

    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private float       fadeDuration = 0.3f;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();

        // 시작 시 완전 투명
        if (canvasGroup != null)
        {
            canvasGroup.alpha          = 0f;
            canvasGroup.blocksRaycasts = false;
        }
    }

    private void OnDestroy()
    {
        if (ReferenceEquals(Instance, this)) Instance = null;
    }

    // ─────────────────────────────────────────────────────────
    // Public API
    // ─────────────────────────────────────────────────────────

    /// <summary>페이드인 → 동기 콜백 실행 → 페이드아웃</summary>
    public async UniTask PlayAsync(Action midAction)
    {
        await FadeTo(1f);
        midAction?.Invoke();
        await FadeTo(0f);
    }

    /// <summary>페이드인 → 비동기 콜백 실행 → 페이드아웃</summary>
    public async UniTask PlayAsync(Func<UniTask> midAction)
    {
        await FadeTo(1f);
        if (midAction != null) await midAction();
        await FadeTo(0f);
    }

    // ─────────────────────────────────────────────────────────
    // Internal
    // ─────────────────────────────────────────────────────────

    private async UniTask FadeTo(float target)
    {
        if (canvasGroup == null) return;

        canvasGroup.blocksRaycasts = true;   // 페이드 중 입력 차단

        float start = canvasGroup.alpha;
        float t     = 0f;
        float dur   = Mathf.Max(0.0001f, fadeDuration);

        while (t < 1f)
        {
            t                  += Time.unscaledDeltaTime / dur;
            canvasGroup.alpha   = Mathf.Lerp(start, target, t);
            await UniTask.Yield();
        }

        canvasGroup.alpha = target;
        canvasGroup.blocksRaycasts = target > 0.5f;  // 투명해지면 입력 해제
    }
}
