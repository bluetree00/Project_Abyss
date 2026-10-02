using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 다음 씬을 미리 불러 두고 켜기만 미룬다(S5 층계 회랑 — 회랑을 걷는 동안 다음 챕터를 불러 두어 검은 로딩 화면을 없앤다).
/// 씬 로드 두 갈래(<see cref="SceneTransitionManager"/> · <see cref="AppBootstrapper"/> 테스트 씬 경로)가 <see cref="Take"/>로 받아 켠다.
/// <para>⚠️ 켜기를 미룬 씬이 있으면 유니티는 그 뒤의 씬 로드를 줄 세운다 — 다른 씬을 요청하면 미룬 씬을 먼저 켜 보내고
/// (그 씬이 잠깐 켜졌다가 요청한 씬으로 바뀐다) 요청한 로드가 이어서 돈다. 회랑 중 거점 복귀 같은 드문 경우만 해당.</para>
/// </summary>
public static class ScenePreloader
{
    private static AsyncOperation s_op;
    private static string         s_scene;

    public static bool   IsPending    => s_op != null;
    public static string PendingScene => s_scene;

    /// <summary>미리 불러 두기 시작(이미 있으면 무시).</summary>
    public static void Begin(string scene)
    {
        if (s_op != null || string.IsNullOrEmpty(scene)) return;
        s_op = SceneManager.LoadSceneAsync(scene);
        if (s_op == null) return;
        s_op.allowSceneActivation = false;
        s_scene = scene;
        Debug.Log($"[ScenePreloader] 미리 불러 두기 — {scene}");
    }

    /// <summary>
    /// <paramref name="scene"/>을 미리 불러 두었으면 그 작업을 넘겨준다(켜기는 호출측이 allowSceneActivation = true).
    /// 다른 씬을 미뤄 두었으면 그 씬을 켜 보내 줄을 비우고 null — 호출측은 평소대로 로드한다.
    /// </summary>
    public static AsyncOperation Take(string scene)
    {
        if (s_op == null) return null;
        var op = s_op;
        bool same = s_scene == scene;
        s_op = null;
        s_scene = null;
        if (same) return op;
        Debug.LogWarning($"[ScenePreloader] 미리 불러 둔 씬이 요청과 다르다 — 먼저 켜 보낸다");
        op.allowSceneActivation = true;
        return null;
    }
}
