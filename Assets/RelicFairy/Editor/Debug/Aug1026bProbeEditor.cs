#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-26 사용자 「개선한 카타나의 빌드가 실제 재련소에 안 나오는 것 같다 — 대검도 같이」 — 재련소 증강 뽑기를 런 단위로 흉내 낸다.
/// 실제 뽑기 함수(MeleeAugmentDraft.RollTier · Draw)를 그대로 부르고 화면마다 한 장을 무작위로 골라 무기에 붙인다(창 · 이벤트 없음 — 지금 런은 안 건드림).
/// 경우: 카타나 · 대검을 처음부터(+3 ~ +30 · 화면 10개) · 무형검에서 진화한 뒤(+12 ~ +30 · 화면 7개).
/// 증강마다 「한 런에 한 번이라도 제시될 확률」 · 「화면당 제시율」을 적는다. 결과 Temp/aug1026b.txt · 로그 「[Aug26b] 끝」.
/// </summary>
public static class Aug1026bProbeEditor
{
    private const string Tag = "[Aug26b]";
    private const int Runs = 600;
    private const BindingFlags Any = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

    [MenuItem("RelicFairy/Debug/10-26 재련소 증강 제시율 — 카타나 · 대검 새 증강 (플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning($"{Tag} 플레이 중에"); return; }
        var sb = new StringBuilder($"10-26 재련소 증강 제시율 — 런 {Runs}번씩\n");
        try
        {
            var draw = typeof(MeleeAugmentDraft).GetMethod("Draw", Any);
            foreach (var (label, path, from) in new[]
            {
                ("카타나 처음부터", "Assets/RelicFairy/Weapon/Katana/Data/T1_Katana.asset", 3),
                ("카타나 진화 뒤", "Assets/RelicFairy/Weapon/Katana/Data/T1_Katana.asset", 12),
                ("대검 처음부터", "Assets/RelicFairy/Weapon/Greatsword/Data/T1_Greatsword.asset", 3),
                ("대검 진화 뒤", "Assets/RelicFairy/Weapon/Greatsword/Data/T1_Greatsword.asset", 12),
            })
            {
                var so = AssetDatabase.LoadAssetAtPath<WeaponSO>(path);
                if (so == null) { sb.AppendLine($"■ {label} — SO 없음 {path}"); continue; }
                var perRun = new Dictionary<string, int>();
                var perScreen = new Dictionary<string, int>();
                int screens = 0;
                for (int r = 0; r < Runs; r++)
                {
                    var rng = new System.Random(1000 + r);
                    var w = WeaponData.FromSO(so);
                    var seen = new HashSet<string>();
                    int streak = 0;
                    for (int level = from; level <= 30; level += MeleeAugmentDraft.LevelStep)
                    {
                        w.enhanceLevel = level;
                        bool justEvolved = from == 12 && level == 12;
                        var tier = MeleeAugmentDraft.RollTier(level, justEvolved, streak, rng);
                        streak = tier == AugmentTier.Silver ? streak + 1 : 0;
                        var cards = draw.Invoke(null, new object[] { w, tier, rng, 2, null }) as List<MeleeAugmentCard>;
                        if (cards == null || cards.Count == 0) continue;
                        screens++;
                        foreach (var c in cards) { perScreen[c.Id] = perScreen.TryGetValue(c.Id, out var n) ? n + 1 : 1; seen.Add(c.Id); }
                        w.AddAugment(cards[rng.Next(cards.Count)].Id);   // 무작위로 하나 고른다
                    }
                    foreach (var id in seen) perRun[id] = perRun.TryGetValue(id, out var n) ? n + 1 : 1;
                }
                var kind = MeleeAugmentCatalog.KindOf(WeaponData.FromSO(so));
                sb.AppendLine($"\n■ {label} — 갈래 {kind} · 화면 {screens / (float)Runs:0.0}개/런");
                foreach (var d in MeleeAugmentCatalog.All.Where(d => d.Weapon == kind).Concat(MeleeAugmentCatalog.All.Where(d => d.Weapon == MeleeWeaponKind.None)))
                {
                    perRun.TryGetValue(d.Id, out int pr); perScreen.TryGetValue(d.Id, out int ps);
                    sb.AppendLine($"  {(d.Weapon == kind ? "전용" : "공용")} {MeleeAugmentDef.TierName(d.Tier)} 「{d.Name}」{(d.IsGrowth ? "(재련 자람)" : "")} — 한 런에 제시 {pr * 100f / Runs:0}% · 화면당 {ps * 100f / Mathf.Max(1, screens):0.0}%");
                }
            }
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        File.WriteAllText("Temp/aug1026b.txt", sb.ToString());
        Debug.Log($"{Tag} 끝\n" + sb);
    }
}
#endif
