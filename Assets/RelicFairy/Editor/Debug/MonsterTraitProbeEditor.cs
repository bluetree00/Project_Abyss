#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-02 몬스터 시기 특성(레벨디자인 설계서 §4) 실측.
/// ① 「시기 → 해방기」: 테스트 시기 오버라이드를 해방기로(원래 값은 기억했다가 ③이 되돌린다). 악몽 분포는 ②의 굴림 표로 본다.
/// ② 「특성 실측 (런 중 · 전투방)」: 굴림 분포(시기 × 등급 2,000번) · 지금 방의 특성 몹 목록 · 분열(45%까지 깎기 → 분신 2) · 폭발 유해(처치 → 1.2초 뒤 폭발)
///    · 배지 화면(Temp/ui_shots/Trait_badges.png). 결과: Temp/monster_trait_probe.txt · 로그 「[TraitProbe] 끝」.
/// ③ 「시기 원래대로」.
/// </summary>
public static class MonsterTraitProbeEditor
{
    private const string SavedKey = "RelicFairy.Story.NightmareOverride.TraitProbeSaved";

    [MenuItem("RelicFairy/Debug/10-02 특성 ① 시기 → 해방기 (테스트)")]
    private static void EraLiberated()
    {
        if (!EditorPrefs.HasKey(SavedKey))
            EditorPrefs.SetInt(SavedKey, EditorPrefs.GetInt(StoryProgress.DebugOverridePrefsKey, -1));
        EditorPrefs.SetInt(StoryProgress.DebugOverridePrefsKey, 1);
        StoryProgress.RefreshDebugOverride();
        Debug.Log($"[TraitProbe] 시기 → {StoryProgress.Era}");
    }

    [MenuItem("RelicFairy/Debug/10-02 특성 ③ 시기 원래대로")]
    private static void EraRestore()
    {
        int saved = EditorPrefs.GetInt(SavedKey, -1);
        EditorPrefs.SetInt(StoryProgress.DebugOverridePrefsKey, saved);
        EditorPrefs.DeleteKey(SavedKey);
        StoryProgress.RefreshDebugOverride();
        Debug.Log($"[TraitProbe] 시기 원래대로({saved}) → {StoryProgress.Era}");
    }

    [MenuItem("RelicFairy/Debug/10-02 특성 ② 실측 (런 중 · 전투방)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) return;
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("몬스터 시기 특성 실측\n");
        try
        {
            // 굴림 분포 — 표 그대로 나오는지(방당 종류 2 제한은 빼고 셈)
            foreach (var era in new[] { StoryEra.Sealed, StoryEra.Liberated, StoryEra.NightmareMode })
            foreach (var g in new[] { MonsterGrade.Common, MonsterGrade.Elite, MonsterGrade.Boss })
            {
                int any = 0, total = 0;
                var byKind = new Dictionary<MonsterTraitKind, int>();
                for (int i = 0; i < 2000; i++)
                {
                    var r = MonsterTraitTable.Roll(g, era, null);
                    if (r.Count > 0) any++;
                    total += r.Count;
                    foreach (var k in r) byKind[k] = byKind.TryGetValue(k, out var n) ? n + 1 : 1;
                }
                var kinds = new List<string>();
                foreach (var kv in byKind) kinds.Add($"{kv.Key} {kv.Value}");
                sb.AppendLine($"{era}/{g}: 특성 있음 {any / 20f:0.#}% · 평균 {total / 2000f:0.00}개 · {string.Join(" ", kinds)}");
            }
            sb.AppendLine($"지금 시기 {StoryProgress.Era}");

            // 지금 방
            var list = new List<MonsterBase>();
            MonsterBase.CopyActive(list);
            var plain = new List<MonsterBase>();
            foreach (var m in list)
            {
                if (m == null || m.IsDead || m.Grade == MonsterGrade.Boss) continue;
                if (m.TryGetComponent<MonsterTraits>(out var t) && t.Kinds.Count > 0)
                    sb.AppendLine($"  특성 몹 {m.name}({m.Grade}): {string.Join(", ", t.Kinds)} · 배지 {t.Badges.Count} · 바 {(m.WorldHPBar != null ? "있음" : "없음")}");
                else plain.Add(m);
            }
            sb.AppendLine($"살아 있는 몹 {list.Count} · 특성 없는 몹 {plain.Count}");

            // 분열 — 특성 없는 몹 하나에 붙이고 체력을 45%로
            if (plain.Count > 0)
            {
                var victim = plain[0];
                var spawner = Object.FindFirstObjectByType<MonsterSpawner>();
                int before = list.Count;
                MonsterTraits.Attach(victim, new[] { MonsterTraitKind.Splitter },
                    pos => SpawnLikeAsync(victim, pos));
                int cut = Mathf.CeilToInt(victim.CurrentHp - victim.EffectiveMaxHp * 0.45f);
                victim.TakeSynergyDamage(Mathf.Max(1, cut), null, 1f);
                await UniTask.Delay(1500, ignoreTimeScale: true);
                MonsterBase.CopyActive(list);
                sb.AppendLine($"분열: {victim.name} 체력 {victim.CurrentHp}/{victim.EffectiveMaxHp} · 몹 {before} → {list.Count} (스포너 {(spawner != null ? "있음" : "없음")})");
            }

            // 폭발 유해 — 다른 몹에 붙이고 처치
            if (plain.Count > 1)
            {
                var bomb = plain[1];
                MonsterTraits.Attach(bomb, new[] { MonsterTraitKind.Volatile }, null);
                Vector3 at = bomb.transform.position;
                bomb.TakeSynergyDamage(bomb.CurrentHp + 10, null, 1f);
                await UniTask.Delay(400, ignoreTimeScale: true);
                bool warned = GameObject.Find("Guide_Disc") != null;
                await UniTask.Delay(1300, ignoreTimeScale: true);
                sb.AppendLine($"폭발 유해: {bomb.name} 처치 · 예고 원 {(warned ? "보임" : "없음")} @ {at}");
            }

            // 배지 화면 — 남은 몹 두셋에 보여 주기용 특성
            MonsterBase.CopyActive(list);
            var show = new[]
            {
                new[] { MonsterTraitKind.Swift, MonsterTraitKind.Ward },
                new[] { MonsterTraitKind.Commander, MonsterTraitKind.Resist },
                new[] { MonsterTraitKind.Trail },
            };
            int s = 0;
            foreach (var m in list)
            {
                if (s >= show.Length) break;
                if (m == null || m.IsDead || m.Grade == MonsterGrade.Boss || m.TryGetComponent<MonsterTraits>(out _)) continue;
                MonsterTraits.Attach(m, show[s++], null);
            }
            await UniTask.Delay(800, ignoreTimeScale: true);
            Directory.CreateDirectory("Temp/ui_shots");
            ScreenCapture.CaptureScreenshot("Temp/ui_shots/Trait_badges.png");
            await UniTask.Delay(500, ignoreTimeScale: true);
            sb.AppendLine($"배지 화면용 특성 {s}마리");
        }
        catch (System.Exception e)
        {
            sb.AppendLine("예외: " + e);
        }
        File.WriteAllText("Temp/monster_trait_probe.txt", sb.ToString());
        Debug.Log("[TraitProbe] 끝\n" + sb);
    }

    private static async UniTask SpawnLikeAsync(MonsterBase like, Vector3 pos)
    {
        // 실측용 — 스포너 길 대신 같은 프리팹을 풀에서 꺼낸다(방 생존 수엔 안 들어간다)
        var key = like.name.Replace("(Clone)", "").Trim();
        try
        {
            var child = await Managers.ObjectPooler.SpawnAsync<MonsterBase>(key, ObjectPoolerManager.PoolType.Monster, pos, Quaternion.identity);
            if (child != null) MonsterTraits.MakeSplitChild(child);
            Debug.Log($"[TraitProbe] 분신 {(child != null ? child.name : "실패")} ({key})");
        }
        catch (System.Exception e) { Debug.LogWarning($"[TraitProbe] 분신 실패 {key}: {e.Message}"); }
    }
}
#endif
