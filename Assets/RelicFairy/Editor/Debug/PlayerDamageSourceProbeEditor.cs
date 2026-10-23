#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-23 실측 — 플레이어가 받는 피해의 출처를 남긴다(ee 「Ch4 리치 등장 칭호 직후 한 방 사망 · 출처 로그 없음」).
/// <see cref="PlayerController.OnDamageResolved"/>마다 가해자 경로 · 들어온/최종 피해 · 결과 · 호출 스택(앞 14줄),
/// 그와 별도로 체력이 피해 이벤트 없이 떨어지면(직접 차감 · 즉사) 그 프레임을 남긴다. 플레이가 끝나면 스스로 멈춘다.
/// </summary>
public static class PlayerDamageSourceProbeEditor
{
    private const int StackLines = 14;

    private static PlayerController s_player;
    private static int  s_lastHp = -1;
    private static bool s_resolvedThisFrame;

    [MenuItem("RelicFairy/Debug/10-23 플레이어 피해 출처 기록 (Play)")]
    private static void Begin()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[DmgSrc] 플레이 모드에서"); return; }
        Stop();
        s_player = GameRunBootstrapper.Instance?.Run?.Player;
        if (s_player == null) { Debug.LogWarning("[DmgSrc] 플레이어 없음"); return; }
        s_lastHp = s_player.RuntimeStats.Hp;
        s_player.OnDamageResolved += OnResolved;
        EditorApplication.update += Tick;
        Debug.Log($"[DmgSrc] 시작 — 체력 {s_lastHp}/{s_player.RuntimeStats.MaxHp}");
    }

    private static void Stop()
    {
        EditorApplication.update -= Tick;
        if (s_player != null) s_player.OnDamageResolved -= OnResolved;
        s_player = null;
    }

    private static void OnResolved(GameObject attacker, int raw, int final, PlayerController.DamageOutcome outcome)
    {
        s_resolvedThisFrame = true;
        string stack = string.Join("\n    ", Environment.StackTrace.Split('\n').Skip(2).Take(StackLines).Select(l => l.Trim()));
        Debug.Log($"[DmgSrc] 피해 {raw} → {final} · {outcome} · 가해자 {PathOf(attacker)} · 체력 {s_player?.RuntimeStats.Hp} · t {Time.time:0.00}\n    {stack}");
    }

    private static void Tick()
    {
        if (!Application.isPlaying || s_player == null) { Stop(); return; }
        int hp = s_player.RuntimeStats.Hp;
        if (hp < s_lastHp && !s_resolvedThisFrame)
            Debug.Log($"[DmgSrc] 피해 이벤트 없이 체력 {s_lastHp} → {hp} · t {Time.time:0.00} · 자리 {s_player.transform.position}");
        s_lastHp = hp;
        s_resolvedThisFrame = false;
    }

    private static string PathOf(GameObject go)
    {
        if (go == null) return "(없음)";
        var t = go.transform;
        string path = t.name;
        for (int i = 0; i < 3 && t.parent != null; i++) { t = t.parent; path = t.name + "/" + path; }
        return path;
    }
}
#endif
