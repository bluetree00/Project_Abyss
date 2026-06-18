using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 로비 콜드 부팅 커버 연출.
/// 검은 전체화면 + 타이틀 페이드인 → 로비 준비(AppBootstrapper.IsReady) 시 커버 페이드아웃 → 로비 노출.
/// Lobby 씬에 직접 배치(프레임0부터 렌더), 최상단 캔버스. 부팅 1회용.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public sealed class LobbyBootCover : MonoBehaviour
{
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private Graphic title;
    [SerializeField] private float titleFadeInDuration = 0.7f;
    [SerializeField] private float coverFadeOutDuration = 0.5f;
    [SerializeField] private float maxWaitSeconds = 15f;

    private void Awake()
    {
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        canvasGroup.alpha = 1f;
        canvasGroup.blocksRaycasts = true;
        if (title != null) SetGraphicAlpha(title, 0f);
    }

    private void Start()
    {
        RunAsync(this.GetCancellationTokenOnDestroy()).Forget();
    }

    private async UniTaskVoid RunAsync(CancellationToken ct)
    {
        try
        {
            await FadeTitleInAsync(ct);
            await WaitForLobbyVisibleAsync(ct);
            await FadeCoverOutAsync(ct);
            canvasGroup.blocksRaycasts = false;
            gameObject.SetActive(false);
        }
        catch (OperationCanceledException) { }
    }

    private async UniTask FadeTitleInAsync(CancellationToken ct)
    {
        if (title == null) return;

        Transform tr = title.transform;
        Vector3 from = Vector3.one * 0.96f;
        Vector3 to = Vector3.one;
        float t = 0f;
        while (t < titleFadeInDuration)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / titleFadeInDuration);
            SetGraphicAlpha(title, k);
            tr.localScale = Vector3.Lerp(from, to, k);
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }
        SetGraphicAlpha(title, 1f);
        tr.localScale = to;
    }

    // 로비 인트로 영상이 실제 재생되기 시작(=로비가 화면에 나오기 시작)할 때까지 대기.
    // IsReady 만으로는 LobbyRoot 활성화 직후라 인트로 영상/페이드인 전이므로 부족하다.
    private async UniTask WaitForLobbyVisibleAsync(CancellationToken ct)
    {
        float t = 0f;
        LobbyIntroPlayer intro = null;
        while (t < maxWaitSeconds)
        {
            if (intro == null)
                intro = FindAnyObjectByType<LobbyIntroPlayer>(FindObjectsInactive.Exclude);
            if (intro != null && intro.IsPlaying)
                return;

            t += Time.unscaledDeltaTime;
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }
    }

    private async UniTask FadeCoverOutAsync(CancellationToken ct)
    {
        float start = canvasGroup.alpha;
        float t = 0f;
        while (t < coverFadeOutDuration)
        {
            t += Time.unscaledDeltaTime;
            canvasGroup.alpha = Mathf.Lerp(start, 0f, Mathf.Clamp01(t / coverFadeOutDuration));
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }
        canvasGroup.alpha = 0f;
    }

    private static void SetGraphicAlpha(Graphic g, float a)
    {
        var c = g.color;
        c.a = a;
        g.color = c;
    }
}
