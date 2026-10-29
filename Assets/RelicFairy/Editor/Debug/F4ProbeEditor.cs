#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-29 F4 위험한 문 확인 — 켜면 해방기 · 악몽 갈림길의 두 문을 모두 위험한 정예 문으로(<see cref="RunSequencer.DebugForceDanger"/>).
/// 자동 실측과 같이 써서 「[RoomWave] … 정예 2」 · 표시(4c) · 보상 하한(4c)을 본다. 플레이를 멈춰도 값이 남으니 끝나면 끈다.
/// </summary>
public static class F4ProbeEditor
{
    [MenuItem("RelicFairy/Debug/10-29 F4 위험한 문 강제 켜기·끄기")]
    private static void Toggle()
    {
        RunSequencer.DebugForceDanger = !RunSequencer.DebugForceDanger;
        Debug.Log($"[F4Probe] 위험한 문 강제 {(RunSequencer.DebugForceDanger ? "켬" : "끔")}");
    }
}
#endif
