#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// [도구] 콘솔 「Error Pause」를 끈다 · 켠다(10-30). 켜져 있으면 플레이 시작 때 Steam 클라이언트 없음 오류 한 줄에 게임이 멈춰
/// 자동 로그인 대기 시간 초과가 흐르지 않고(IsReady에 못 감) 자동 실측이 전부 「시작 실패」로 끝났다.
/// </summary>
internal static class ConsoleErrorPauseEditor
{
    [MenuItem("RelicFairy/Tools/콘솔 Error Pause 끄기")] private static void Off() => Set(false);
    [MenuItem("RelicFairy/Tools/콘솔 Error Pause 켜기")] private static void On()  => Set(true);

    private static void Set(bool on)
    {
        var asm   = typeof(EditorWindow).Assembly;
        var log   = asm.GetType("UnityEditor.LogEntries");
        var flags = asm.GetType("UnityEditor.ConsoleWindow+ConsoleFlags");
        var set   = log?.GetMethod("SetConsoleFlag", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (set == null || flags == null) { Debug.LogWarning("[ConsoleErrorPause] 콘솔 API를 찾지 못했다"); return; }
        int bit = (int)Enum.Parse(flags, "ErrorPause");
        set.Invoke(null, new object[] { bit, on });
        if (!on && EditorApplication.isPaused) EditorApplication.isPaused = false;
        Debug.Log($"[ConsoleErrorPause] Error Pause = {on}");
    }
}
#endif
