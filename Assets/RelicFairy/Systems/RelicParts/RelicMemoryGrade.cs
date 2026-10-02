/// <summary>
/// 유물 기억 조각의 등급 = <b>드러난 줄 수</b>(유물 성장 v2, 10-02). 숫자가 커지는 게 아니라 규칙이 한 줄씩 열린다.
/// 흐릿 = ①핵심만 · 선명 = ①② · 찬란 = ①②③.
/// </summary>
public enum RelicMemoryGrade
{
    None    = 0,
    Faint   = 1,   // 흐릿
    Clear   = 2,   // 선명
    Radiant = 3,   // 찬란
}

/// <summary>
/// 등급 확률 — 시기(봉인 · 해방 · 악몽) × 제단 등급 띠(0 · 1 · 2). 카드마다 따로 굴린다.
/// <b>보스 · 챕터는 등급에 손대지 않는다</b>(사용자 10-02 「모드레드라서 더 좋은 걸 주는 건 정정」).
/// 띠 한 단마다 흐릿 −10%p를 선명 · 찬란으로 옮긴다(1단 +6/+4, 2단 +5/+5) — 설계 v2 §1-2-1 · d6 제단 v1.
/// </summary>
public static class RelicMemoryOdds
{
    /// <summary>찬란 없이 이만큼 드래프트가 지나면 다음 드래프트 한 장을 찬란으로 올린다(천장).</summary>
    public const int PityDrafts = 4;

    // [시기, 띠] → (흐릿, 선명, 찬란) %. 합은 늘 100.
    private static readonly int[,,] s_table =
    {
        { { 65, 28,  7 }, { 55, 34, 11 }, { 45, 39, 16 } },   // 봉인기
        { { 50, 38, 12 }, { 40, 44, 16 }, { 30, 49, 21 } },   // 해방기
        { { 35, 45, 20 }, { 25, 51, 24 }, { 15, 56, 29 } },   // 악몽
    };

    /// <summary>시기 → 표 행(봉인 0 · 해방 1 · 악몽 2).</summary>
    public static int EraIndex(StoryEra era) => era switch
    {
        StoryEra.Liberated     => 1,
        StoryEra.NightmareMode => 2,
        _                      => 0,
    };

    /// <summary>(흐릿, 선명, 찬란) 퍼센트.</summary>
    public static (int faint, int clear, int radiant) Table(int era, int band)
    {
        era  = era  < 0 ? 0 : era  > 2 ? 2 : era;
        band = band < 0 ? 0 : band > 2 ? 2 : band;
        return (s_table[era, band, 0], s_table[era, band, 1], s_table[era, band, 2]);
    }

    /// <summary>등급 한 번 굴리기.</summary>
    public static RelicMemoryGrade Roll(int era, int band, System.Random rng)
    {
        var (faint, clear, _) = Table(era, band);
        int r = rng.Next(100);
        if (r < faint)         return RelicMemoryGrade.Faint;
        if (r < faint + clear) return RelicMemoryGrade.Clear;
        return RelicMemoryGrade.Radiant;
    }

    /// <summary>등급 이름(카드 · 보유 보기).</summary>
    public static string Label(RelicMemoryGrade g) => g switch
    {
        RelicMemoryGrade.Faint   => "흐릿",
        RelicMemoryGrade.Clear   => "선명",
        RelicMemoryGrade.Radiant => "찬란",
        _                        => string.Empty,
    };
}
