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

    // ── 유물의 각성 ────────────────────────────────────
    public int abyssEssence;           // 심연의 정수 (각성 재화)
    public int awakeningLevelSword;    // 검의 각성 — 공격력
    public int awakeningLevelShield;   // 방패의 각성 — 방어력
    public int awakeningLevelHeart;    // 심장의 각성 — 최대 체력
    public int awakeningLevelStep;     // 발걸음의 각성 — 이동속도
    public int awakeningLevelMana;     // 마력의 각성 — 스킬 쿨타임
    public int awakeningLevelLuck;     // 행운의 각성 — 행운

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

        abyssEssence         = 0;
        awakeningLevelSword  = 0;
        awakeningLevelShield = 0;
        awakeningLevelHeart  = 0;
        awakeningLevelStep   = 0;
        awakeningLevelMana   = 0;
        awakeningLevelLuck   = 0;
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
            gold            += result.GainedGold;
            totalGoldEarned += result.GainedGold;
        }

        if (result.GainedEssence > 0)
            abyssEssence += result.GainedEssence;
    }

    /// <summary>각성 레벨을 1 올리고 비용을 차감한다. 비용이 부족하면 false 반환.</summary>
    public bool TryUpgradeAwakening(string categoryId, int cost)
    {
        if (abyssEssence < cost) return false;

        abyssEssence -= cost;
        switch (categoryId)
        {
            case AwakeningCategory.Sword:  awakeningLevelSword++;  break;
            case AwakeningCategory.Shield: awakeningLevelShield++; break;
            case AwakeningCategory.Heart:  awakeningLevelHeart++;  break;
            case AwakeningCategory.Step:   awakeningLevelStep++;   break;
            case AwakeningCategory.Mana:   awakeningLevelMana++;   break;
            case AwakeningCategory.Luck:   awakeningLevelLuck++;   break;
            default: return false;
        }
        return true;
    }

    /// <summary>카테고리 ID에 해당하는 현재 각성 레벨 반환.</summary>
    public int GetAwakeningLevel(string categoryId) => categoryId switch
    {
        AwakeningCategory.Sword  => awakeningLevelSword,
        AwakeningCategory.Shield => awakeningLevelShield,
        AwakeningCategory.Heart  => awakeningLevelHeart,
        AwakeningCategory.Step   => awakeningLevelStep,
        AwakeningCategory.Mana   => awakeningLevelMana,
        AwakeningCategory.Luck   => awakeningLevelLuck,
        _                        => 0,
    };
}
