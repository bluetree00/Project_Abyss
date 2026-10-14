#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// [편집 · 1회] 10-14 정예 애니 점검 — RPG 몬스터 팩 컨트롤러는 데모용이라 상태마다 조건 없는 「끝나면 다음」 전이가 걸려 있다
/// (달리기 4~8바퀴 → 감지 → 피격 → 어지러움 → 사망 · 전투 대기 4바퀴 → 공격 · 공격 → 다음 공격 → 도발 → 승리).
/// 게임은 상태를 코드로 직접 틀므로(CrossFade) 이 사슬이 몇 바퀴 뒤 동작을 저절로 바꿨다 — 추적 중 감지 동작 · 헛공격 · 대기 중 사망 동작.
/// 조건 없는 종료 시간 전이를 지운다. 시작 → 반복 짝(…ST → …RPT · …Start → …Maint)은 한 동작이라 둔다.
/// 결과: Logs/boss_fight_sim/anim_chain_strip_*.txt · 로그 「[AnimChainStrip]」.
/// </summary>
internal static class MonsterAnimChainStripEditor
{
    private const string PackRoot = "Assets/RelicFairy/_Imported/RPGMonsterBundlePolyart";
    private static readonly string[] Extra =
    {
        "Assets/RelicFairy/Shared/Item/Specter/Specter - Copy.controller",
        "Assets/RelicFairy/Shared/Item/Specter/Specter.controller",
    };

    [MenuItem("RelicFairy/Monster/Anim Chains - List (Edit)")]
    private static void List() => Run(apply: false);

    [MenuItem("RelicFairy/Monster/Anim Chains - Strip (Edit)")]
    private static void Strip() => Run(apply: true);

    private static void Run(bool apply)
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[AnimChainStrip] 편집 모드에서만"); return; }
        var paths = new List<string>();
        foreach (var g in AssetDatabase.FindAssets("t:AnimatorController", new[] { PackRoot })) paths.Add(AssetDatabase.GUIDToAssetPath(g));
        paths.AddRange(Extra);
        var sb = new StringBuilder($"몬스터 팩 컨트롤러 데모 전이 — {(apply ? "지움" : "목록만")}\n");
        int total = 0;
        foreach (var path in paths)
        {
            var ac = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(path);
            if (ac == null) continue;
            int n = 0;
            var lines = new StringBuilder();
            foreach (var layer in ac.layers) n += Walk(layer.stateMachine, apply, lines);
            if (n == 0) continue;
            total += n;
            sb.AppendLine($"{Path.GetFileName(path)} — {n}");
            sb.Append(lines);
            if (apply) EditorUtility.SetDirty(ac);
        }
        if (apply) AssetDatabase.SaveAssets();
        sb.AppendLine($"합계 {total}");
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "boss_fight_sim");
        Directory.CreateDirectory(dir);
        string outPath = Path.Combine(dir, $"anim_chain_strip_{System.DateTime.Now:MMdd_HHmmss}.txt");
        File.WriteAllText(outPath, sb.ToString(), Encoding.UTF8);
        Debug.Log($"[AnimChainStrip] {(apply ? "지움" : "목록")} {total}개 — {outPath}");
    }

    private static int Walk(UnityEditor.Animations.AnimatorStateMachine sm, bool apply, StringBuilder lines)
    {
        int n = 0;
        foreach (var cs in sm.states)
        {
            var st = cs.state;
            foreach (var t in st.transitions)
            {
                if (t.conditions.Length > 0 || !t.hasExitTime) continue;
                string dst = t.destinationState != null ? t.destinationState.name : "(출구)";
                if (StartToRepeat(st.name, dst)) continue;
                lines.AppendLine($"    {st.name} →({t.exitTime:0.##}) {dst}");
                if (apply) st.RemoveTransition(t);
                n++;
            }
        }
        foreach (var sub in sm.stateMachines) n += Walk(sub.stateMachine, apply, lines);
        return n;
    }

    private static bool StartToRepeat(string src, string dst)
        => (src.EndsWith("ST") && dst.EndsWith("RPT") && src.Substring(0, src.Length - 2) == dst.Substring(0, dst.Length - 3))
        || (src.EndsWith("Start") && dst.EndsWith("Maint") && src.Substring(0, src.Length - 5) == dst.Substring(0, dst.Length - 5));
}
#endif
