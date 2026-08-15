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

    // ── 심연의 정수 ────────────────────────────────────
    public int abyssEssence;           // 심연의 정수 (기억의 제단 재화)

    // ── 기억의 제단 — 해금 / 기록 ───────────────────────
    /// <summary>해금된 노드 id 목록(쉼표 구분). sealBrokenBossIds와 같은 규약 — 백엔드가 flat 컬럼만 저장한다.</summary>
    public string unlockedIds = "";

    /// <summary>누적 기록. <c>key:value</c> 쌍을 쉼표로 이은 문자열(<c>maxDepth:23,eliteKills:47</c>).
    /// 해금 <b>할인 조건</b>과 업적 <b>진척</b>이 같은 값을 본다 — 정본 §2·§6.</summary>
    public string records = "";

    // ── [레거시] 유물의 각성 6계열 ──────────────────────
    // 영구 스탯은 폐기됐다(정본 §1 — 항상 적용되어 숙련 가독성을 훼손). 스탯 적용은 이미 끊겨 있고,
    // 이 필드들은 <b>환급 마이그레이션 원장</b>으로만 남는다. MemoryAltarService가 정수로 되돌린 뒤 0으로 만든다.
    // 백엔드 flat 컬럼이라 필드 자체를 지우면 스키마가 어긋나므로 삭제하지 않는다.
    public int awakeningLevelSword;
    public int awakeningLevelShield;
    public int awakeningLevelHeart;
    public int awakeningLevelStep;
    public int awakeningLevelMana;
    public int awakeningLevelLuck;

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

        unlockedIds          = "";
        records              = "";

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

        ApplyRunRecords(result);
    }

    /// <summary>
    /// 런 결과를 영구 기록(<see cref="records"/>)에 반영한다. <b>런 종료 시 1회</b>만 — 정본 §8-1.
    /// <para>여기 쌓인 값이 기억의 제단의 <b>할인 조건</b>과 업적 <b>진척</b> 양쪽을 동시에 판정한다.
    /// 같은 사실을 두 곳에서 세지 않기 위해 기록은 이 한 곳에서만 쓴다.</para>
    /// </summary>
    private void ApplyRunRecords(EndRunResult result)
    {
        // 최고치 계열 — 갱신될 때만 오른다.
        SetRecordMax(MemoryAltarCatalog.Rec.MaxDepth,   result.AbyssDepth);
        SetRecordMax(MemoryAltarCatalog.Rec.MaxChapter, highestChapter);
        SetRecordMax(MemoryAltarCatalog.Rec.MaxEnhance, result.MaxEnhance);

        // 누적 계열 — 런마다 더한다.
        AddRecord(MemoryAltarCatalog.Rec.Kills,       result.Kills);
        AddRecord(MemoryAltarCatalog.Rec.EliteKills,  result.EliteKills);
        AddRecord(MemoryAltarCatalog.Rec.BossKills,   result.BossKills);
        AddRecord(MemoryAltarCatalog.Rec.RoomClears,  result.RoomClears);
        AddRecord(MemoryAltarCatalog.Rec.ShopUses,    result.ShopUses);
        AddRecord(MemoryAltarCatalog.Rec.RefineCount, result.RefineUses);

        // 완주 횟수는 totalClears가 정본이지만, 조건 판정이 records 한 곳만 보도록 같이 적어둔다.
        SetRecordMax(MemoryAltarCatalog.Rec.Clears, totalClears);

        // ── 기행 — <b>완주했을 때만</b> 확정한다. 도중에 죽었으면 "포션 안 썼다"가 성립하지 않는다.
        if (result.IsCleared)
        {
            SetRecordMax(MemoryAltarCatalog.Rec.NoPotionClear,  result.PotionUsed ? 0 : 1);
            SetRecordMax(MemoryAltarCatalog.Rec.NoSpecialClear, result.SpecialVisits == 0 ? 1 : 0);
        }
        // 무피격 챕터는 챕터 단위라 완주와 무관하게 누적된다.
        AddRecord(MemoryAltarCatalog.Rec.FlawlessChapter, result.FlawlessChapters);

        PushRecordsToAchievements();
    }

    /// <summary>
    /// 기록을 업적 진척에 흘린다. <b>업적은 기록의 표시 형태</b>이므로 따로 세지 않는다 —
    /// 처치·방·상점을 QuestEvents로 별도 집계하면 같은 사실을 두 곳이 기억하게 되고,
    /// 이어하기·저장 시점에 따라 두 값이 어긋난다.
    /// <para>절대값을 보내므로 업적 Task는 <c>SimpleSet</c>이어야 한다(SimpleCount면 매 런 누적돼 폭주).</para>
    /// </summary>
    private void PushRecordsToAchievements()
    {
        foreach (var key in MemoryAltarCatalog.Rec.All)
            QuestEvents.ReportRecord(key, GetRecord(key));
    }

    // ── 해금 (unlockedIds) ─────────────────────────────

    /// <summary>해당 노드가 해금됐는지.</summary>
    public bool IsUnlocked(string nodeId)
    {
        if (string.IsNullOrEmpty(nodeId) || string.IsNullOrEmpty(unlockedIds)) return false;
        foreach (var id in unlockedIds.Split(','))
            if (id == nodeId) return true;
        return false;
    }

    /// <summary>
    /// 정수를 지불하고 노드를 해금한다. 이미 해금됐거나 정수가 모자라면 false.
    /// <para>cost&lt;=0은 <b>거부</b>한다 — 노드 정의가 비면 0이 넘어오는데 그대로 통과시키면 전량 무료 해금이 된다.
    /// (구 TryUpgradeAwakening에서 실제로 겪었던 함정이라 규약을 그대로 옮긴다.)</para>
    /// </summary>
    public bool TryUnlock(string nodeId, int cost)
    {
        if (string.IsNullOrEmpty(nodeId)) return false;
        if (cost <= 0) return false;
        if (IsUnlocked(nodeId)) return false;
        if (abyssEssence < cost) return false;

        unlockedIds = string.IsNullOrEmpty(unlockedIds) ? nodeId : unlockedIds + "," + nodeId;
        abyssEssence -= cost;
        return true;
    }

    // ── 기록 (records) ─────────────────────────────────

    /// <summary>기록 값. 키가 없으면 0.</summary>
    public int GetRecord(string key)
    {
        if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(records)) return 0;

        foreach (var pair in records.Split(','))
        {
            int sep = pair.IndexOf(':');
            if (sep != key.Length) continue;
            if (string.CompareOrdinal(pair, 0, key, 0, key.Length) != 0) continue;
            return int.TryParse(pair.Substring(sep + 1), out var v) ? v : 0;
        }
        return 0;
    }

    /// <summary>최고 기록 갱신(더 큰 값일 때만). 실제로 올랐으면 true — 저장·연출 트리거용.</summary>
    public bool SetRecordMax(string key, int value)
    {
        if (string.IsNullOrEmpty(key)) return false;
        if (value <= GetRecord(key)) return false;
        WriteRecord(key, value);
        return true;
    }

    /// <summary>누적 기록에 더한다(처치 수 등). 실제로 변했으면 true.</summary>
    public bool AddRecord(string key, int delta)
    {
        if (string.IsNullOrEmpty(key) || delta == 0) return false;
        WriteRecord(key, GetRecord(key) + delta);
        return true;
    }

    private void WriteRecord(string key, int value)
    {
        var sb = new System.Text.StringBuilder(records.Length + key.Length + 8);
        bool replaced = false;

        if (!string.IsNullOrEmpty(records))
        {
            foreach (var pair in records.Split(','))
            {
                int sep = pair.IndexOf(':');
                if (sep <= 0) continue;

                bool isTarget = sep == key.Length && string.CompareOrdinal(pair, 0, key, 0, key.Length) == 0;
                if (sb.Length > 0) sb.Append(',');

                if (isTarget) { sb.Append(key).Append(':').Append(value); replaced = true; }
                else          { sb.Append(pair); }
            }
        }

        if (!replaced)
        {
            if (sb.Length > 0) sb.Append(',');
            sb.Append(key).Append(':').Append(value);
        }

        records = sb.ToString();
    }

    /// <summary>
    /// 각성 레벨을 1 올리고 비용을 차감한다. 비용이 부족하거나 최대 레벨이면 false 반환.
    /// <para>cost&lt;=0은 <b>거부</b>한다 — 차트 미로드/컬럼 누락이면 GetUpgradeCost가 0을 돌려주는데,
    /// 그대로 통과시키면 정수 0으로 각성이 무한히 올라간다(무료 각성).</para>
    /// </summary>
    [System.Obsolete("각성 6계열은 폐기됐다(정본 §1 영구 스탯 제거). 해금은 TryUnlock을 쓴다. " +
                     "환급 마이그레이션 경로만 남기기 위해 필드/조회는 유지한다.")]
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
