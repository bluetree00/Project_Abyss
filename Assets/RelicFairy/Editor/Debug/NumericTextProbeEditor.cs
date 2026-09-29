using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// [실측 · 런 안에서] 09-29 수치 문구 · 상점 가호 버그 — 가호 카드 글(실제 수치)과 가호가 스탯에 들어가는지(이속 ×100 · 체력/치명/치피/쿨감/행운 무효 수정).
/// 가호는 버프 층에 넣어 재고 곧바로 걷는다(BuffHandler.ClearAll 뒤 방 버프 원복 없음 — 테스트 런 전용).
/// 결과: Temp/numeric_text_probe.txt
/// </summary>
public static class NumericTextProbeEditor
{
    private const BindingFlags StaticNonPub = BindingFlags.Static | BindingFlags.NonPublic;

    [MenuItem("RelicFairy/Debug/수치 문구 실측 — 상점 가호 (런 안에서)")]
    private static void Run()
    {
        var run = GameRunBootstrapper.Instance?.Run;
        var stats = run?.Player?.RuntimeStats;
        if (!Application.isPlaying || stats == null || run.BuffHandler == null) { Debug.LogWarning("[NumericTextProbe] 런 안에서"); return; }

        var sb = new StringBuilder();
        // ① 카드 글 — 가호를 여러 번 뽑아 종류별 첫 글을 모은다
        var build = typeof(AbyssPeddlerCatalog).GetMethod("BuildBuff", StaticNonPub);
        var seen = new HashSet<string>();
        for (int seed = 0; seed < 400 && build != null; seed++)
        {
            var p = build.Invoke(null, new object[] { new System.Random(seed) }) as ShopProduct;
            if (p == null || !seen.Add(p.DisplayName + p.EffectText)) continue;
            sb.AppendLine($"카드 {p.DisplayName} · 「{p.EffectText}」 · {p.DetailText}");
        }

        // ② 스탯 반영 — 가호 하나씩 넣고 바뀐 값을 잰다
        var rows = ShopBuffTable.Rows;
        if (rows != null)
            foreach (var row in rows)
            {
                if (row.tier != 1 && row.statType != StatType.Projectile) continue;
                run.BuffHandler.ClearAll();
                stats.RefreshRoomBuffs(run.BuffHandler);
                var before = Snap(stats);
                float v = row.isPercent && row.value > 1f ? row.value / 100f : row.value;
                run.BuffHandler.AddBuff(new StatModifier(row.statType, v), 99, row.isPercent, false, row.buffId, row.tier);
                stats.RefreshRoomBuffs(run.BuffHandler);
                var after = Snap(stats);
                sb.AppendLine($"가호 {row.buffId} ({row.statType} {row.value}{(row.isPercent ? "%" : "")}) → {Diff(before, after)}");
            }
        run.BuffHandler.ClearAll();
        stats.RefreshRoomBuffs(run.BuffHandler);

        File.WriteAllText(Path.Combine("Temp", "numeric_text_probe.txt"), sb.ToString());
        Debug.Log("[NumericTextProbe] 끝 — Temp/numeric_text_probe.txt\n" + sb);
    }

    private static float[] Snap(PlayerRuntimeStats s) => new[]
    {
        s.MeleeAttack, s.Defense, s.MaxHp, s.MoveSpeedMultiplier, s.AttackSpeedMultiplier,
        s.CritChanceBonus, s.CritDamageBonus, s.SkillCooldownReduction, s.Luck, s.BonusProjectile,
    };

    private static readonly string[] Names = { "근접", "방어", "최대HP", "이속배율", "공속배율", "치명%p", "치피", "쿨감", "행운", "투사체" };

    private static string Diff(float[] a, float[] b)
    {
        var parts = new List<string>();
        for (int i = 0; i < a.Length; i++)
            if (!Mathf.Approximately(a[i], b[i])) parts.Add($"{Names[i]} {a[i]:0.###}→{b[i]:0.###}");
        return parts.Count > 0 ? string.Join(" · ", parts) : "변화 없음 ✗";
    }
}
