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