using UnityEngine;

/// <summary>
/// 역직렬화된 세이브 데이터를 <b>반환 직전</b> 중앙에서 정규화(클램프)한다.
/// 두 스토어(LocalFileRunSaveStore / LocalFileMetaStore)의 읽기 경로에서만 호출되어
/// 모든 호출자가 안전한 값을 수령한다.
///
/// 목적: ① 손상/변조 세이브가 게임을 크래시시키지 않게 방어 ② 캐주얼 변조 억제.
/// 원칙(보수): <b>값은 항상 로드 성공</b>(거부·삭제 절대 금지). 정상 플레이 값이
/// 클램프에 걸리지 않도록 경계를 넉넉히 둔다(예: currentHp==maxHp, gold==0 허용).
/// 시드(masterSeed)·외형/내용 JSON은 강제 수정하지 않는다(과도 개입 금지).
/// </summary>
public static class SaveSanitizer
{
    // ChapterId enum 범위(1~4). StageEnums.ChapterId와 동기 유지.
    private const int MinChapter = 1;
    private const int MaxChapter = 4;

    // 활성 런의 최소 최대체력 하한.
    private const int MinMaxHp = 1;

    // 무기 슬롯: -1(없음), 0, 1.
    private const int MinWeaponSlot = -1;
    private const int MaxWeaponSlot = 1;

    // 최고 도달 챕터 표시 상한(0=없음 ~ 5=엔딩).
    private const int MaxHighestChapter = 5;

    // ── RunSaveData ──────────────────────────────────────────

    /// <summary>진행 중 런 세이브를 정규화한다(in-place). 클램프 발생 시 경고 1줄.</summary>
    public static void Sanitize(RunSaveData d)
    {
        if (d == null) return;
        bool changed = false;

        d.runGold         = ClampMin(d.runGold,         0, ref changed);
        d.runEssence      = ClampMin(d.runEssence,      0, ref changed);
        d.fuelEnhanceMaterial = ClampMin(d.fuelEnhanceMaterial, 0, ref changed);
        d.fuelRuneOre         = ClampMin(d.fuelRuneOre,         0, ref changed);
        d.potionCapacity      = ClampMin(d.potionCapacity, PlayerRunState.DefaultPotionCapacity, ref changed);
        d.potionCount         = Mathf.Clamp(d.potionCount, 0, d.potionCapacity);
        d.maxHp           = ClampMin(d.maxHp,           MinMaxHp, ref changed);
        // currentHp: [0, maxHp] — 0(사망 상태)도 허용해 정상값을 끌어올리지 않는다.
        d.currentHp       = ClampRange(d.currentHp,     0, d.maxHp, ref changed);
        d.chapter         = ClampRange(d.chapter,       MinChapter, MaxChapter, ref changed);
        d.progressPercent = ClampRange(d.progressPercent, 0, 100, ref changed);

        // 카운트류(음수 불가). 상한은 동적이라 두지 않는다.
        d.retryCount       = ClampMin(d.retryCount,       0, ref changed);
        d.itemCount        = ClampMin(d.itemCount,        0, ref changed);
        d.synergyCount     = ClampMin(d.synergyCount,     0, ref changed);
        d.roomClearCount   = ClampMin(d.roomClearCount,   0, ref changed);
        d.currentZoneIndex = ClampMin(d.currentZoneIndex, 0, ref changed);
        d.visitCount       = ClampMin(d.visitCount,       0, ref changed);
        d.shopUsed         = ClampMin(d.shopUsed,         0, ref changed);
        d.eventUsed        = ClampMin(d.eventUsed,        0, ref changed);

        d.weaponCurrentSlot = ClampRange(d.weaponCurrentSlot, MinWeaponSlot, MaxWeaponSlot, ref changed);

        // 강화 레벨: ≥0만. 상한은 재련소 차트(EnhanceTableSO) 구동이라 여기서 강제하지 않는다.
        d.weapon0EnhanceLevel = ClampMin(d.weapon0EnhanceLevel, 0, ref changed);
        d.weapon1EnhanceLevel = ClampMin(d.weapon1EnhanceLevel, 0, ref changed);
        d.weapon0EvolutionStage = ClampMin(d.weapon0EvolutionStage, 0, ref changed);
        d.weapon1EvolutionStage = ClampMin(d.weapon1EvolutionStage, 0, ref changed);

        // 재련소 RNG 소비 수: ≥0. 음수면 스트림 진행이 깨져 save-scum이 뚫린다.
        d.crucibleRollIndex = ClampMin(d.crucibleRollIndex, 0, ref changed);

        // masterSeed·heading·anchorToggle·currentRoomKind/Mirror·seqPhase 등 절차생성
        // 내부 상태는 클램프하지 않는다(시드 정합성/복원 무결성 보존, 과도 개입 금지).

        // JSON 문자열 필드: 파싱 실패 시에만 빈 값 폴백(크래시 방지). 내용은 강제 수정 안 함.
        d.itemsJson              = SafeJson<ItemListWrapper>(d.itemsJson, ref changed);
        d.stagingItemsJson       = SafeJson<ItemListWrapper>(d.stagingItemsJson, ref changed);
        d.roomLogsJson           = SafeJson<RoomClearLogWrapper>(d.roomLogsJson, ref changed);
        d.clearedZoneIndicesJson = SafeJson<IntListWrapper>(d.clearedZoneIndicesJson, ref changed);
        d.covenantsJson          = SafeJson<CovenantListWrapper>(d.covenantsJson, ref changed);
        d.runeCellsJson          = SafeJson<Vector2IntListWrapper>(d.runeCellsJson, ref changed);
        d.runePlacementsJson     = SafeJson<RunePlacementListWrapper>(d.runePlacementsJson, ref changed);
        d.cooldownsJson          = SafeJson<CooldownListWrapper>(d.cooldownsJson, ref changed);

        if (changed)
            Debug.LogWarning($"[SaveIntegrity] 런 세이브 비정상값 클램프 적용(슬롯{d.slotIndex}) — 로드는 계속");
    }

    // ── UserGameData(meta_save) ──────────────────────────────

    /// <summary>영구 메타를 정규화한다(in-place). 클램프 발생 시 경고 1줄.</summary>
    public static void Sanitize(UserGameData d)
    {
        if (d == null) return;
        bool changed = false;

        d.level = ClampMin(d.level, 1, ref changed);

        // 유일한 float 필드 — NaN/Infinity 가드 후 음수 방지.
        d.experience = SafeFloat(d.experience, ref changed);
        d.experience = ClampMinF(d.experience, 0f, ref changed);

        d.gold  = ClampMin(d.gold,  0, ref changed);
        d.jewel = ClampMin(d.jewel, 0, ref changed);
        d.heart = ClampMin(d.heart, 0, ref changed);

        d.totalRuns          = ClampMin(d.totalRuns,          0, ref changed);
        d.totalClears        = ClampMin(d.totalClears,        0, ref changed);
        d.highestChapter     = ClampRange(d.highestChapter,   0, MaxHighestChapter, ref changed);
        d.totalGoldEarned    = ClampMin(d.totalGoldEarned,    0, ref changed);
        d.lichEncounterCount = ClampMin(d.lichEncounterCount, 0, ref changed);

        d.abyssEssence = ClampMin(d.abyssEssence, 0, ref changed);

        // 각성 레벨: ≥0만. 상한은 차트(GetMaxLevel) 구동이라 여기서 강제하지 않는다(과도 개입 방지).
        d.awakeningLevelSword  = ClampMin(d.awakeningLevelSword,  0, ref changed);
        d.awakeningLevelShield = ClampMin(d.awakeningLevelShield, 0, ref changed);
        d.awakeningLevelHeart  = ClampMin(d.awakeningLevelHeart,  0, ref changed);
        d.awakeningLevelStep   = ClampMin(d.awakeningLevelStep,   0, ref changed);
        d.awakeningLevelMana   = ClampMin(d.awakeningLevelMana,   0, ref changed);
        d.awakeningLevelLuck   = ClampMin(d.awakeningLevelLuck,   0, ref changed);

        if (changed)
            Debug.LogWarning("[SaveIntegrity] 메타 세이브 비정상값 클램프 적용 — 로드는 계속");
    }

    // ── Helpers ──────────────────────────────────────────────

    private static int ClampMin(int v, int min, ref bool changed)
    {
        if (v < min) { changed = true; return min; }
        return v;
    }

    private static int ClampRange(int v, int min, int max, ref bool changed)
    {
        if (v < min) { changed = true; return min; }
        if (v > max) { changed = true; return max; }
        return v;
    }

    private static float ClampMinF(float v, float min, ref bool changed)
    {
        if (v < min) { changed = true; return min; }
        return v;
    }

    private static float SafeFloat(float v, ref bool changed)
    {
        if (float.IsNaN(v) || float.IsInfinity(v)) { changed = true; return 0f; }
        return v;
    }

    /// <summary>
    /// JSON 문자열이 해당 래퍼로 파싱되는지만 검증한다. 빈 문자열은 그대로 통과(정상).
    /// 파싱 실패(손상)면 빈 문자열로 폴백해 이후 역직렬화 크래시를 막는다. 내용은 수정하지 않는다.
    /// </summary>
    private static string SafeJson<T>(string json, ref bool changed) where T : class
    {
        if (string.IsNullOrEmpty(json)) return json;
        try
        {
            JsonUtility.FromJson<T>(json);
            return json;
        }
        catch
        {
            changed = true;
            return string.Empty;
        }
    }
}
