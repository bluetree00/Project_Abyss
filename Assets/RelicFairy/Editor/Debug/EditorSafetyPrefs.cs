using UnityEditor;
using UnityEngine;

/// <summary>
/// [에디터 설정] 플레이 도중 도메인 리로드가 나지 않게 막는다.
///
/// 09-22에 세 세션이 같은 사고를 겪었다 — 플레이 중 에셋 임포트가 도메인 리로드를 부르면
/// 직렬화 안 된 일반 C# 필드가 전부 null이 되는데 <c>Awake</c>는 다시 돌지 않는다.
/// 그 결과 <c>PlayerController.Update()</c>가 NRE로 죽고(<c>Combo</c>가 null), 런이 반쪽이 된 채 굴러간다.
/// 실측 기록 유실·아레나 진입 실패도 같은 뿌리였다. 빌드에서는 일어나지 않는 에디터 전용 사건이라
/// 런타임 코드에 방어를 넣지 않고 <b>설정으로 막는다</b>(2026-09-22 사용자 결정).
///
/// EditorPrefs는 <b>머신 단위</b>라 한 번 적용하면 계속 간다. 에디터를 새로 설치하거나
/// 설정이 되돌아갔을 때 이 메뉴를 다시 누르면 된다.
///
/// ⚠️ Auto Refresh가 꺼지면 <b>.cs를 저장해도 자동으로 컴파일되지 않는다.</b>
/// 저장 뒤 Unity 창에서 Ctrl+R을 누르거나 MCP <c>refresh_unity</c>를 호출해야 한다.
/// </summary>
public static class EditorSafetyPrefs
{
    // ── Constants ─────────────────────────────────────────────────
    // Unity가 공개 API로 열어 두지 않은 환경설정 키들(Preferences 창이 쓰는 것과 같은 키).
    private const string AutoRefreshKey     = "kAutoRefresh";                 // 0=끔 · 1=켬
    private const string AutoRefreshModeKey = "kAutoRefreshMode";             // 0=Disabled · 1=Enabled · 2=EnabledOutsidePlaymode
    private const string CompileDuringPlay  = "ScriptCompilationDuringPlay";  // 0=계속 재생 · 1=재생 끝난 뒤 · 2=멈추고 컴파일

    private const int AutoRefreshOff            = 0;
    private const int RecompileAfterFinishedPlay = 1;

    // ── Public Methods ────────────────────────────────────────────
    [MenuItem("RelicFairy/Debug/에디터 안전 설정 적용 (플레이 중 리로드 차단)")]
    private static void Apply()
    {
        string before = Describe();

        EditorPrefs.SetInt(AutoRefreshKey,     AutoRefreshOff);
        EditorPrefs.SetInt(AutoRefreshModeKey, AutoRefreshOff);
        EditorPrefs.SetInt(CompileDuringPlay,  RecompileAfterFinishedPlay);

        Debug.Log($"[에디터안전설정] 적용 완료\n  이전: {before}\n  이후: {Describe()}\n" +
                  "  ⚠️ 이제 .cs를 저장해도 자동 컴파일되지 않는다 — Ctrl+R 또는 refresh_unity를 직접 호출할 것.");
    }

    [MenuItem("RelicFairy/Debug/에디터 안전 설정 확인")]
    private static void Check() => Debug.Log($"[에디터안전설정] 현재: {Describe()}");

    // ── Private Methods ───────────────────────────────────────────
    private static string Describe()
    {
        int auto = EditorPrefs.GetInt(AutoRefreshKey, -1);
        int mode = EditorPrefs.GetInt(AutoRefreshModeKey, -1);
        int play = EditorPrefs.GetInt(CompileDuringPlay, -1);
        return $"Auto Refresh={auto} · Auto Refresh Mode={mode}({ModeName(mode)}) · Script Changes While Playing={play}({PlayName(play)})";
    }

    private static string ModeName(int v) => v switch
    {
        0 => "끔",
        1 => "켬",
        2 => "플레이 밖에서만",
        _ => "미설정",
    };

    private static string PlayName(int v) => v switch
    {
        0 => "계속 재생하며 컴파일",
        1 => "재생 끝난 뒤 컴파일",
        2 => "재생 멈추고 컴파일",
        _ => "미설정",
    };
}
