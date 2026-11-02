#if UNITY_EDITOR
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 11-02 Q7 사용자 「유물을 얻고 지나치더라도 그 위치의 가이드 위치 표기가 안 없어진다」 실측(거점, 플레이 중).
/// 「매 판」 길 걷기(BaseCampRouteProbeEditor)를 시작하고 70초 동안 1초마다 — 로드아웃(검 · 원거리 · 유물) · 길잡이 비트 · 주 목표 ·
/// 투명도가 남은 표식마다(목표 이름 · 투명도 · 거리 · 화면 위치) 적는다. 결과 Temp/guide_marker1102.txt · 로그 「[GM02] 끝」.
/// </summary>
public static class GuideMarker1102ProbeEditor
{
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static readonly string[] Names = { "검", "원거리", "유물", "파츠", "성문", "제단" };

    [MenuItem("RelicFairy/Debug/11-02 거점 길잡이 표식 관찰 (거점, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[GM02] 플레이 중에만"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("11-02 거점 길잡이 표식 관찰\n");
        try
        {
            await CloseDialoguesAsync(sb);
            EditorApplication.ExecuteMenuItem("RelicFairy/Debug/BaseCamp Sanctum/Route Walk - Return Run (Play)");
            for (int s = 0; s < 70; s++)
            {
                await UniTask.Delay(1000, ignoreTimeScale: true);
                var g = Object.FindFirstObjectByType<BaseCampObjectiveGuide>();
                var lo = AppBootstrapper.Instance?.Loadout;
                var p = Managers.Player?.PlayerTransform;
                if (g == null) { sb.AppendLine($"{s + 1}s 길잡이 없음"); continue; }
                var t = typeof(BaseCampObjectiveGuide);
                var vm = t.GetMethod("VisibleMask", Inst);
                object[] args = { -1 };
                int mask = vm != null ? (int)vm.Invoke(g, args) : -1;
                var markers = t.GetField("_markers", Inst)?.GetValue(g) as System.Array;
                var points = t.GetField("_points", Inst)?.GetValue(g) as Vector3[];
                var line = new StringBuilder();
                line.Append($"{s + 1}s 플레이어 {(p != null ? p.position.ToString("F0") : "-")} · 검 {lo?.WeaponSlot0 != null} 원거리 {lo?.WeaponSlot1 != null} 유물 {(lo?.Relic != null ? lo.Relic.name : "없음")} · 비트 {System.Convert.ToString(mask, 2)} 주 {args[0]} ·");
                if (markers != null)
                    for (int i = 0; i < markers.Length; i++)
                    {
                        var m = markers.GetValue(i);
                        if (m == null) continue;
                        float a = (float)(m.GetType().GetField("Alpha")?.GetValue(m) ?? 0f);
                        if (a < 0.05f) continue;
                        string dist = (m.GetType().GetField("Dist")?.GetValue(m) as TMPro.TMP_Text)?.text;
                        line.Append($" [{(i < Names.Length ? Names[i] : i.ToString())} 투명도 {a:0.00} · {dist} · 자리 {(points != null ? points[i].ToString("F0") : "-")}]");
                    }
                sb.AppendLine(line.ToString());
            }
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            File.WriteAllText("Temp/guide_marker1102.txt", sb.ToString());
            Debug.Log("[GM02] 끝 → Temp/guide_marker1102.txt");
        }
    }

    /// <summary>대사창이 떠 있으면(시간 정지 · 입력 막힘) 끝까지 넘긴다 — 11-02 첫 실측이 멀린 대사 중에 재어 무효였다.</summary>
    private static async UniTask CloseDialoguesAsync(StringBuilder sb)
    {
        int n = 0;
        float quiet = 0f;
        for (float t = 0f; t < 20f && quiet < 1.5f; t += 0.15f)
        {
            var popup = Object.FindFirstObjectByType<UI_DialoguePopup>();
            if (popup == null || !popup.isActiveAndEnabled) quiet += 0.15f;
            else
            {
                quiet = 0f;
                (new SerializedObject(popup).FindProperty("advanceButton").objectReferenceValue as UnityEngine.UI.Button)?.onClick.Invoke();
                n++;
            }
            await UniTask.Delay(150, ignoreTimeScale: true);
        }
        sb.AppendLine($"대사 넘김 {n} · 시간 배율 {Time.timeScale:0.00} · 입력 막힘 {UIInputGate.Blocked}");
    }
}
#endif
