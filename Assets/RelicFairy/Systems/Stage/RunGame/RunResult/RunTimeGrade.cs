using UnityEngine;

/// <summary>
/// 챕터 전투 시계 등급(10-24 U5 · 기획/RelicFairy_런_시간_등급_권장안_20261023.md 안 A · D8 「기록만 한 빌드 먼저」).
/// 기준 시간 = 그 챕터에서 <b>실제로 싸운 방</b>마다 더한 값(방 수가 런마다 달라도 공정) · 등급 = 전투 시계 ÷ 기준 시간.
/// 지금은 기록만 — 보상(챕터 정수 %)은 사람 시간으로 기준을 맞춘 뒤 켠다. 처음 두 런 너그러움(D6)도 그때.
/// </summary>
public static class RunTimeGrade
{
    // ── Constants ──
    public const float S = 0.70f, A = 0.85f, B = 1.10f;   // 문턱(D4) — 그 위는 C
    public const float LiberatedMult = 1.15f;              // 해방기 — 보스 말고 방 기준 ×1.15
    public const float NightmareMult = 1.25f;              // 악몽 — ×1.25
    public const float DepthMult     = 1.15f;              // 심연 깊이마다 ×1.15

    // 방 기준(초) — Ch1 ~ Ch4(§3-1). 보스는 시기마다 따로(페이지 수가 다르다).
    private static readonly float[] NormalSec     = { 20f, 26f, 30f, 32f };
    private static readonly float[] EliteSec      = { 30f, 38f, 42f, 45f };
    private static readonly float[] MidBossSec    = { 50f, 55f, 60f, 65f };
    private static readonly float[] BossSec       = { 95f, 105f, 125f, 135f };
    private static readonly float[] BossLibSec    = { 115f, 125f, 150f, 165f };
    private static readonly float[] BossNmSec     = { 125f, 135f, 160f, 175f };

    // ── Public Methods ──
    /// <summary>시계가 세는 방인가 — 전투방 · 정예 · 정예 보스 · 보스(보스 직전 방에 적이 있으면 일반방으로). 재화방 · 쉼터는 안 센다.</summary>
    public static bool Counts(RoomPlanKind kind)
        => kind is RoomPlanKind.Normal or RoomPlanKind.Elite or RoomPlanKind.Event or RoomPlanKind.PreBoss
                or RoomPlanKind.MidBoss or RoomPlanKind.Boss;

    /// <summary>방 하나의 기준 시간(초).</summary>
    public static float RoomBase(RoomPlanKind kind, int chapter, StoryEra era, int depth)
    {
        int i = Mathf.Clamp(chapter, 1, 4) - 1;
        float sec;
        if (kind == RoomPlanKind.Boss)
            sec = era == StoryEra.NightmareMode ? BossNmSec[i] : era == StoryEra.Liberated ? BossLibSec[i] : BossSec[i];
        else
        {
            sec = kind switch
            {
                RoomPlanKind.Elite   => EliteSec[i],
                RoomPlanKind.MidBoss => MidBossSec[i],
                _                    => NormalSec[i],
            };
            sec *= era == StoryEra.NightmareMode ? NightmareMult : era == StoryEra.Liberated ? LiberatedMult : 1f;
        }
        return sec * Mathf.Pow(DepthMult, Mathf.Max(0, depth));
    }

    /// <summary>등급 글자(S · A · B · C) — 기준이 없으면(싸운 방이 없음 · 옛 세이브) 빈 글.</summary>
    public static string Letter(float combatSec, float baseSec)
    {
        if (baseSec <= 0f || combatSec <= 0f) return string.Empty;
        float r = combatSec / baseSec;
        return r <= S ? "S" : r <= A ? "A" : r <= B ? "B" : "C";
    }

    /// <summary>등급 색 — S 금 · A 은 · B 청동 · C 회색(「느림」이 아니라 「기록」으로 보이게 옅게, §6).</summary>
    public static string LetterHex(string letter) => letter switch
    {
        "S" => "#E8C46A",
        "A" => "#D3D8E2",
        "B" => "#C4926A",
        _   => "#8C8F98",
    };

    /// <summary>「등급 글자」를 색 입혀서 — 비었으면 빈 글.</summary>
    public static string LetterInk(float combatSec, float baseSec)
    {
        string l = Letter(combatSec, baseSec);
        return l.Length == 0 ? string.Empty : $"<color={LetterHex(l)}>{l}</color>";
    }
}
