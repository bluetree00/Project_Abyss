using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using Cysharp.Threading.Tasks;

public sealed class SceneTransitionManager
{
    private readonly MonoBehaviour _runner;

    public SceneTransitionManager(MonoBehaviour runner)
    {
        _runner = runner;
    }

    public void LoadScene(Define.Scene scene, Action onLoaded = null)
    {
        LoadSceneAsync(scene, onLoaded).Forget();
    }

    private async UniTaskVoid LoadSceneAsync(Define.Scene scene, Action onLoaded)
    {
        // 층계 회랑이 미리 불러 둔 씬 — 로딩 화면 없이 켠다(회랑 끝 문이 화면을 덮고 들어온다).
        var pre = ScenePreloader.Take(scene.ToString());
        if (pre != null)
        {
            await UniTask.WaitUntil(() => pre.progress >= 0.9f);
            pre.allowSceneActivation = true;
            await UniTask.WaitUntil(() => pre.isDone);
            onLoaded?.Invoke();
            return;
        }

        var loading = UI_SceneLoading.Instance;
        if (loading != null) await loading.ShowAsync();

        var op = SceneManager.LoadSceneAsync(scene.ToString());
        op.allowSceneActivation = false;

        float speed = loading != null ? loading.ProgressSpeed : 0.5f;
        float display = 0f;
        while (op.progress < 0.9f)
        {
            display = Mathf.MoveTowards(display, op.progress / 0.9f, Time.unscaledDeltaTime * speed);
            loading?.SetProgress(display);
            await UniTask.Yield(PlayerLoopTiming.Update);
        }
        while (display < 1f)
        {
            display = Mathf.MoveTowards(display, 1f, Time.unscaledDeltaTime * speed);
            loading?.SetProgress(display);
            await UniTask.Yield(PlayerLoopTiming.Update);
        }
        op.allowSceneActivation = true;
        await UniTask.WaitUntil(() => op.isDone);

        onLoaded?.Invoke();
        // 오버레이 숨김은 각 씬의 부트스트래퍼가 NotifySceneReady() 호출 시 처리
    }
}