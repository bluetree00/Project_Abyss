#if UNITY_EDITOR
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-10 룬 등급 확률 표 — 사용자 「좋은 룬의 등장 확률이 실제로 진행될수록 적절히 올라가는지 체크」.
/// 챕터(Ch1~4) × 시기(봉인기 · 해방기 · 악몽) × 방(일반 · 정예)마다 RoomRewardTable.For(방, 챕터 단계, 시기)의
/// 분포(기대)와 그 가중치로 1만 번 굴린 비율(실제)을 뽑는다. 제단 잠금으로 접기 전의 원래 등급이다.
/// 챕터가 오를 때 Epic + Legend 몫이 줄지 않는지(단조 증가)도 판정한다. 편집 중에 돈다(플레이 불필요).
/// 결과 = 콘솔 「[RuneOdds]」 + Temp/rune_odds_table.txt.
/// </summary>
public static class RuneOddsTableEditor
{
    private const int Rolls = 10000;

    [MenuItem("RelicFairy/Debug/10-10 룬 등급 확률 표 (편집 중)")]
    private static void Run()
    {
        var sb  = new StringBuilder("[RuneOdds] 룬 등급 확률 — 기대(가중치) / 실제(1만 회) · %\n");
        var rng = new System.Random(1010);
        int fail = 0;

        foreach (var kind in new[] { RoomPlanKind.Normal, RoomPlanKind.Elite })
        foreach (var era in new[] { StoryEra.Sealed, StoryEra.Liberated, StoryEra.NightmareMode })
        {
            float prevHigh = -1f;
            for (int ch = 1; ch <= 4; ch++)
            {
                var w = RoomRewardTable.For(kind, ch - 1, EraBalance.For(era)).Weights;
                float s = w.Sum;
                var n = new int[4];
                for (int i = 0; i < Rolls; i++)
                {
                    double r = rng.NextDouble() * s;
                    int t = r < w.Common ? 0
                          : r < w.Common + w.Rare ? 1
                          : r < w.Common + w.Rare + w.Epic ? 2 : 3;
                    n[t]++;
                }
                float high = (w.Epic + w.Legendary) / s;
                bool up = high >= prevHigh - 0.0001f;
                if (!up) fail++;
                prevHigh = high;
                sb.AppendLine($"{kind,-6} {era,-13} Ch{ch} | 기대 C{P(w.Common, s)} R{P(w.Rare, s)} E{P(w.Epic, s)} L{P(w.Legendary, s)}" +
                              $" | 실제 C{Q(n[0])} R{Q(n[1])} E{Q(n[2])} L{Q(n[3])} | E+L {high * 100f:0.0}{(up ? "" : "  ✗ 줄었다")}");
            }
        }
        sb.AppendLine($"단조 증가(챕터마다 Epic+Legend가 줄지 않음): {(fail == 0 ? "통과" : $"실패 {fail}")}");

        Directory.CreateDirectory("Temp");
        File.WriteAllText("Temp/rune_odds_table.txt", sb.ToString());
        Debug.Log(sb.ToString());
    }

    private static string P(float v, float s) => s > 0f ? (v / s * 100f).ToString("0.0") : "0";
    private static string Q(int c) => (c * 100f / Rolls).ToString("0.0");
}
#endif
