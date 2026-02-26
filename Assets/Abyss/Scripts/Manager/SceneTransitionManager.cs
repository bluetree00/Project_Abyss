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
        string sceneName = scene.ToString();

        // 필요하면 여기서 페이드아웃
        await SceneManager.LoadSceneAsync(sceneName);

        // 필요하면 여기서 페이드인

        onLoaded?.Invoke();
    }
}