#if UNITY_EDITOR
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-02 실측 보조 — 전투방에 산 적이 나올 때까지 기다린다(대사는 넘기며). 최대 60초.
/// 서약 실측이 웨이브 시작 전에 돌아 「대상 없음」 149칸이 나온 일(10-02)의 뒤처리.
/// 로그 「[CombatWait] 적 N」(N = 산 적 수, 0이면 시간 초과).
/// </summary>
public static class CombatWaitProbeEditor
{
    private const int   Want    = 3;
    private const float Timeout = 60f;

    [MenuItem("RelicFairy/Debug/10-02 전투방 적 기다리기 (플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) return;
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        float end = Time.realtimeSinceStartup + Timeout;
        int alive = 0;
        while (Time.realtimeSinceStartup < end)
        {
            if (Object.FindFirstObjectByType<UI_DialoguePopup>() != null) TestHubDebugMenu.AdvanceDialogue();
            alive = 0;
            foreach (var m in Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
                if (!m.IsDead && m.CurrentHp > 0 && m.gameObject.activeInHierarchy) alive++;
            if (alive >= Want) break;
            await UniTask.Delay(500, ignoreTimeScale: true);
        }
        if (alive >= Want) await UniTask.Delay(1500, ignoreTimeScale: true);   // 등장 연출이 끝나게
        Debug.Log($"[CombatWait] 적 {(alive >= Want ? alive : 0)}");
    }
}
#endif
