using UnityEngine;

/// <summary>
/// 이벤트방 놀이의 챕터 개성(10-01 설계 §4) — 빛깔 · 난이도 단계 · 사물 이름.
/// 놀이 이름과 한 줄 규칙은 챕터와 무관하게 같다(기능 표기는 테마화하지 않는다 — 07-21 원칙).
/// </summary>
public static class MinigameTheme
{
    // 인덱스 0 = Ch1 숲 · 1 = Ch2 불꽃 동굴 · 2 = Ch3 성채 · 3 = Ch4 대제단
    private static readonly Color[] Colors =
    {
        new Color(0.55f, 0.85f, 0.35f),   // 연두
        new Color(1.00f, 0.55f, 0.20f),   // 주황
        new Color(0.95f, 0.90f, 0.70f),   // 흰 금
        new Color(0.62f, 0.35f, 0.95f),   // 보라
    };

    private static readonly string[] PlateNames = { "이끼 발판", "달군 발판", "문장 발판", "성흔 발판" };
    private static readonly string[] ChestNames = { "이끼 낀 보물상자", "그을린 보물상자", "봉헌 보물상자", "노획품 상자" };

    /// <summary>챕터 → 0~3(설계 수치표의 몇 번째 값). 세션이 없거나 범위를 벗어나면 0.</summary>
    public static int Tier(GameRunSession run)
    {
        int i = (run != null ? (int)run.CurrentChapter : 1) - 1;
        return Mathf.Clamp(i, 0, 3);
    }

    public static Color Of(GameRunSession run) => Colors[Tier(run)];

    public static string PlateName(GameRunSession run) => PlateNames[Tier(run)];
    public static string ChestName(GameRunSession run) => ChestNames[Tier(run)];

    /// <summary>난이도 단계별 값 고르기 — 설계표의 「2.4 / 2.2 / 2.0 / 1.8」 같은 네 값.</summary>
    public static float Pick(int tier, float t0, float t1, float t2, float t3) => tier switch
    {
        0 => t0, 1 => t1, 2 => t2, _ => t3,
    };
}
