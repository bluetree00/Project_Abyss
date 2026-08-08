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

    // ── 보스 조우 기록 ─────────────────────────────────
    /// <summary>리치 보스방에 진입한 누적 횟수(대사 변주·통계용). 봉인 해금은 sealBrokenBossIds가 담당.</summary>
    public int lichEncounterCount;

    // ── 보스 봉인(2페이지) 해금 — 메타 영구 ──────────────
    /// <summary>봉인이 해제된 보스 id 목록(쉼표 구분 문자열). 초회 클리어 시 해당 보스 id 추가 → 이후 페이즈2 해금.
    /// 백엔드가 flat 컬럼(int/문자열)만 저장하므로 리스트 대신 CSV 문자열로 보관한다.</summary>
    public string sealBrokenBossIds = "";

    /// <summary>해당 보스의 봉인이 해제됐는지(페이즈2 해금 여부).</summary>
    public bool IsBossSealBroken(string bossId)
    {
        if (string.IsNullOrEmpty(bossId) || string.IsNullOrEmpty(sealBrokenBossIds)) return false;
        foreach (var id in sealBrokenBossIds.Split(','))
            if (id == bossId) return true;
        return false;
    }

    /// <summary>보스 봉인을 해제한다(초회 클리어). 이미 해제됐으면 false(중복 저장 방지).</summary>
    public bool BreakBossSeal(string bossId)
    {
        if (string.IsNullOrEmpty(bossId) || IsBossSealBroken(bossId)) return false;
        sealBrokenBossIds = string.IsNullOrEmpty(sealBrokenBossIds)
            ? bossId : sealBrokenBossIds + "," + bossId;
        return true;
    }

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

        lichEncounterCount   = 0;
        sealBrokenBossIds    = "";

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

        // 골드는 <b>런 재화</b>다(상점 소비 = PlayerRunState.TempGold). 런을 넘겨 쌓지 않는다.
        // 과거엔 gold 에도 누적했는데 소비처가 하나도 없어, 로비에 "쓸 수 없는 숫자"만 불어났다.
        // 영구 이월은 각성 정수(abyssEssence) 하나뿐 — 통계용 누적만 남긴다.
        if (result.GainedGold > 0)
            totalGoldEarned += result.GainedGold;

        if (result.GainedEssence > 0)
            abyssEssence += result.GainedEssence;
    }

    /// <summary>
    /// 각성 레벨을 1 올리고 비용을 차감한다. 비용이 부족하거나 최대 레벨이면 false 반환.
    /// <para>cost&lt;=0은 <b>거부</b>한다 — 차트 미로드/컬럼 누락이면 GetUpgradeCost가 0을 돌려주는데,
    /// 그대로 통과시키면 정수 0으로 각성이 무한히 올라간다(무료 각성).</para>
    /// </summary>
    public bool TryUpgradeAwakening(string categoryId, int cost)
    {
        if (cost <= 0) return false;
        if (abyssEssence < cost) return false;

        // 최대 레벨 도달 검사 — 데이터가 없으면(maxLevel<=0) 올릴 근거 자체가 없으므로 거부.
        int maxLevel = Managers.RelicAwakening?.GetMaxLevel(categoryId) ?? 0;
        if (maxLevel <= 0 || GetAwakeningLevel(categoryId) >= maxLevel) return false;

        // 차감은 카테고리 검증 뒤에 — 먼저 빼면 알 수 없는 categoryId에서 정수만 사라진다.
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

        abyssEssence -= cost;
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
