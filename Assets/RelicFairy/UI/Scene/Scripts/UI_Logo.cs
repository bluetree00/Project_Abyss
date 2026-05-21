using UnityEngine;
using Cysharp.Threading.Tasks;

public class UI_Logo : UI_Scene
{
    [SerializeField] private CanvasGroup logoGroup;
    [SerializeField] private float fadeInDuration = 0.8f;
    [SerializeField] private float holdDuration   = 1.5f;

    public override void Init()
    {
        base.Init();
        PlayAsync().Forget();
    }

    private async UniTaskVoid PlayAsync()
    {
        if (logoGroup != null) logoGroup.alpha = 0f;

        await FadeAsync(1f, fadeInDuration);
        await UniTask.Delay(System.TimeSpan.FromSeconds(holdDuration), ignoreTimeScale: true);

        AppBootstrapper.Instance.RequestLoad(Define.Scene.Lobby);
    }

    private async UniTask FadeAsync(float target, float duration)
    {
        if (logoGroup == null) return;

        float start = logoGroup.alpha;
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            logoGroup.alpha = Mathf.Lerp(start, target, Mathf.Clamp01(t / duration));
            await UniTask.Yield(PlayerLoopTiming.Update);
        }
        logoGroup.alpha = target;
    }
}
