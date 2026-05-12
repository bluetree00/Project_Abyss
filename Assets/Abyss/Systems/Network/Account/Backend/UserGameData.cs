[System.Serializable]
public class UserGameData
{
    // ── 로비 재화 ──────────────────────────────────────
    public int   level;
    public float experience;
    public int   gold;
    public int   jewel;
    public int   heart;

    // ── 인게임 누적 통계 ───────────────────────────────
    public int totalRuns;         // 총 런 시도 횟수
    public int totalClears;       // 런 클리어 횟수
    public int highestChapter;    // 최고 도달 챕터 (1~5)
    public int totalGoldEarned;   // 누적 골드 획득량 (통계용)

    public void Reset()
    {
        level            = 1;
        experience       = 0f;
        gold             = 0;
        jewel            = 0;
        heart            = 30;
        totalRuns        = 0;
        totalClears      = 0;
        highestChapter   = 0;
        totalGoldEarned  = 0;
    }

    /// <summary>런 종료 결과를 영구 데이터에 반영한다.</summary>
    public void ApplyRunResult(EndRunResult result)
    {
        totalRuns++;

        if (result.IsCleared)
            totalClears++;

        int chapterNum = (int)result.Chapter;
        if (chapterNum > highestChapter)
            highestChapter = chapterNum;

        if (result.GainedGold > 0)
        {
            gold           += result.GainedGold;
            totalGoldEarned += result.GainedGold;
        }
    }
}
