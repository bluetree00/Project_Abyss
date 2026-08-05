using System;
using UnityEngine;

/// <summary>
/// 진행 중 런 세이브(이어하기). 로컬 파일(persistentDataPath)이 단독 권위 — 슬롯 3개.
///
/// 저장 시점 : 방 경계(RunFlowController.SaveRunState → SaveRunLocal).
/// 삭제 시점 : 사망/런 클리어/슬롯 삭제(ClearLocalRun).
/// </summary>
public class RunProgressManager : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────
    // Constants
    // ─────────────────────────────────────────────────────────

    public const int SlotCount = 3;

    // ─────────────────────────────────────────────────────────
    // Static
    // ─────────────────────────────────────────────────────────

    public static RunProgressManager Instance { get; private set; }

    // ─────────────────────────────────────────────────────────
    // Private
    // ─────────────────────────────────────────────────────────

    // 진행 중 런 세이브는 로컬 파일(persistentDataPath)이 단독 권위.
    private readonly IRunSaveStore _localStore = new LocalFileRunSaveStore();

    // ─────────────────────────────────────────────────────────
    // Properties
    // ─────────────────────────────────────────────────────────

    /// <summary>현재 플레이 중인 슬롯 인덱스. UI에서 슬롯 선택 시 설정. 유효 범위(0~2)로 클램프.</summary>
    private int _activeSlotIndex = 0;
    public int ActiveSlotIndex
    {
        get => _activeSlotIndex;
        set => _activeSlotIndex = Mathf.Clamp(value, 0, SlotCount - 1);
    }

    // ─────────────────────────────────────────────────────────
    // Lifecycle
    // ─────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // 레거시 단일 run_save.json → 슬롯 파일 1회 이전(무손실).
        _localStore.MigrateIfNeeded();
    }

    // ─────────────────────────────────────────────────────────
    // Public Methods — 로컬 런 세이브
    // ─────────────────────────────────────────────────────────

    /// <summary>지정 슬롯에 유효한 진행 중 런 세이브가 있는지.</summary>
    public bool HasLocalRun(int slot) => _localStore.HasSave(slot);

    /// <summary>지정 슬롯의 로컬 런 세이브를 로드한다(없으면 null).</summary>
    public RunSaveData LoadLocalRun(int slot) => _localStore.Load(slot);

    /// <summary>지정 슬롯의 로컬 런 세이브를 삭제한다(사망/클리어/새 런 시작 시). 다른 슬롯 무영향.</summary>
    /// <summary>슬롯 삭제 — 런 세이브와 함께 <b>온보딩 완료 기록</b>도 초기화한다.
    /// (안 지우면 슬롯을 지우고 새로 시작해도 초회 온보딩이 스킵된다)</summary>
    /// <summary>
    /// 진행 중이던 런 세이브만 버린다. <b>초회 여부(온보딩)는 건드리지 않는다.</b>
    /// 런 종료(사망/클리어)와 "스타트룸 미퇴장 세이브 폐기"가 이 경로다 —
    /// 튜토리얼을 이미 마친 플레이어를 매번 초회로 되돌리면 안 된다.
    /// </summary>
    public void ClearLocalRun(int slot)
    {
        _localStore.Delete(slot);
    }

    /// <summary>
    /// 슬롯을 <b>처음 상태</b>로 되돌린다 — 세이브 + 초회 진행도(온보딩) + 시도 횟수.
    /// "새 게임"과 "슬롯 삭제"만 이 경로다. 이 슬롯으로 다시 시작하면 초회 경험이 그대로 재현된다.
    /// </summary>
    public void ResetSlot(int slot)
    {
        _localStore.Delete(slot);
        BaseCampOnboardingDirector.ClearForSlot(slot);
        ClearRetryCount(slot);
    }

    // ── 시도 횟수 (슬롯별) ─────────────────────────────────────
    // 세이브 파일에 두면 런 종료 시 파일과 함께 지워져 항상 0이 된다. 슬롯 스코프 PlayerPrefs가 정본.

    private static string RetryKey(int slot) => "run_retry_count_slot" + slot;

    /// <summary>이 슬롯에서 런을 시작한 누적 횟수.</summary>
    public static int GetRetryCount(int slot) => Mathf.Max(0, PlayerPrefs.GetInt(RetryKey(slot), 0));

    /// <summary>새 런 진입 시 +1. 세이브 표시(로비 카드)의 "시도 N회" 근거.</summary>
    public static void BumpRetryCount(int slot)
    {
        PlayerPrefs.SetInt(RetryKey(slot), GetRetryCount(slot) + 1);
        PlayerPrefs.Save();
    }

    public static void ClearRetryCount(int slot)
    {
        PlayerPrefs.DeleteKey(RetryKey(slot));
        PlayerPrefs.Save();
    }

    /// <summary>
    /// 허브(베이스캠프) 체류를 저장한다. 런 세이브와 달리 <b>진행 중인 세션이 없어도</b> 쓴다.
    ///
    /// 예전엔 세이브가 런 안에서만 생겨서, 인트로를 깨고 게임을 끄면 슬롯이 빈 채로 남았다.
    /// 그러면 다음 실행에서 "새 게임"밖에 못 누르고 → ResetSlot이 인트로 완료 플래그까지 지워
    /// <b>프롤로그를 처음부터 다시 봐야 했다.</b> 허브에서 보낸 시간이 통째로 휘발된 것.
    ///
    /// 담는 건 "이 슬롯은 허브까지 왔다"는 사실뿐이다(isInStartRoom=true).
    /// 무기·유물은 매 런 시작 의식(무형검 각성)에서 다시 갖추므로 저장하지 않는다.
    /// </summary>
    public void SaveHubProgress()
    {
        int slot = ActiveSlotIndex;
        var prev = _localStore.Load(slot);

        // 진행 중인 런 세이브를 허브 세이브로 덮지 않는다 — 런 중엔 허브에 올 일이 없지만,
        // 순서가 꼬여도 실제 진행이 날아가지 않게 막는다.
        if (prev != null && prev.hasActiveRun && !prev.isInStartRoom) return;

        var d = new RunSaveData
        {
            slotIndex         = slot,
            hasActiveRun      = true,
            isInStartRoom     = true,                    // 이어하기 → 베이스캠프로 복귀
            chapter           = prev?.chapter ?? (int)ChapterId.Chapter1,
            retryCount        = GetRetryCount(slot),
            playSeconds       = prev?.playSeconds ?? 0,  // 허브는 플레이 시계가 없어 직전 누적을 잇는다
            progressPercent   = prev?.progressPercent ?? 0,
            characterKey      = AppBootstrapper.Instance?.Loadout?.CharacterPrefabKey ?? string.Empty,
            savedAt           = DateTime.UtcNow.ToString("o"),
            saveVersion       = 1,
            weaponCurrentSlot = -1,
        };

        _localStore.Save(slot, d);
    }

    /// <summary>
    /// 방 경계에서 현재 런 전체 상태를 로컬에 저장한다.
    /// BuildSaveData로 공통 필드를 채운 뒤 절차생성/로드아웃/룬 확장 필드를 덧붙인다.
    /// </summary>
    public void SaveRunLocal(GameRunSession session, in RunMetaSnapshot meta)
    {
        if (session == null || !session.IsRunning) return;

        // 저장 직전에 무기 상태를 캡처한다 — 강화/진화/승급은 런 도중 바뀌는데,
        // 예전엔 씬 이탈에서만 캡처해 방 경계 저장·재련소 즉시저장이 낡은 값을 썼다.
        session.CaptureWeaponSlotsFromPlayer();

        int slot  = ActiveSlotIndex;
        var prev  = _localStore.Load(slot);
        // 시도 횟수는 세이브 파일이 아니라 슬롯 카운터가 정본 — 런이 끝나면 세이브가 지워지므로
        // 파일에서 이어받으면 영원히 0에 머문다.
        int retry = GetRetryCount(slot);

        var data = BuildSaveData(session, slot, retry, false, prev);
        ApplyExtendedFields(data, session, meta);

        _localStore.Save(slot, data);
    }

    /// <summary>로컬 세이브 전용 확장 필드 채움. BuildSaveData 공통 필드와 별개.</summary>
    private static void ApplyExtendedFields(RunSaveData d, GameRunSession s, in RunMetaSnapshot m)
    {
        d.saveVersion       = 1;

        // 절차생성 진행
        d.masterSeed        = m.masterSeed;
        d.visitCount        = m.visitCount;
        d.seqPhase          = m.seqPhase;
        d.shopUsed          = m.shopUsed;
        d.eventUsed         = m.eventUsed;
        d.crucibleUsed      = m.crucibleUsed;
        d.refineryUsed      = m.refineryUsed;
        d.shopMiss          = m.shopMiss;
        d.eventMiss         = m.eventMiss;
        d.crucibleMiss      = m.crucibleMiss;
        d.refineryMiss      = m.refineryMiss;
        d.heading           = m.heading;
        d.anchorToggle      = m.anchorToggle;
        d.currentRoomPoolKey = m.currentRoomPoolKey;
        d.currentRoomKind   = m.currentRoomKind;
        d.currentRoomMirror = m.currentRoomMirror;
        d.currentRoomCleared = m.currentRoomCleared;   // 클리어 후 저장 → 복원 시 몹 재스폰 방지
        d.crucibleRollIndex  = m.crucibleRollIndex;    // 재련소 RNG 스트림 위치(save-scum 방지)

        var cdw = new CooldownListWrapper();
        if (m.cooldowns != null) cdw.items.AddRange(m.cooldowns);
        d.cooldownsJson = JsonUtility.ToJson(cdw);

        // 런 상태 확장
        d.runEssence       = s.RunDelta?.GainedEssence ?? 0;
        d.fuelEnhanceMaterial = s.FuelBank?.EnhanceMaterial ?? 0;   // 이벤트방 연료 은행
        d.fuelRuneOre         = s.FuelBank?.RuneOre ?? 0;
        d.potionCount         = s.PlayerState?.PotionCount ?? 0;
        d.potionCapacity      = s.PlayerState?.PotionCapacity ?? PlayerRunState.DefaultPotionCapacity;
        // 현재 슬롯: 라이브 WeaponManager 우선(첫 방 -1 케이스 해결), 없으면 씬 전환 시 저장값
        int liveSlot       = s.Player?.WeaponManager?.CurrentSlotIndex ?? -1;
        d.weaponCurrentSlot = liveSlot >= 0 ? liveSlot : s.SavedCurrentSlotIndex;

        // 무기 강화/승급 상태 — 라이브 WeaponManager 우선, 없으면 씬 전환 저장 슬롯
        CaptureWeaponEnhance(d, s);

        var loadout = AppBootstrapper.Instance?.Loadout;
        d.relicKey = loadout?.Relic != null ? loadout.Relic.name : string.Empty;

        // 서약
        var cov = new CovenantListWrapper();
        if (s.CovenantHandler != null)
            foreach (var c in s.CovenantHandler.Covenants)
                cov.items.Add(new CovenantSaveEntry { id = c.CovenantId, stage = (int)c.Stage });
        d.covenantsJson = JsonUtility.ToJson(cov);

        // 보관함(staging) 아이템
        var stg = new ItemListWrapper();
        stg.items.AddRange(s.ItemInventory.StagingItems);
        d.stagingItemsJson = JsonUtility.ToJson(stg);

        // 룬 보드 점유 셀 (시너지 권위)
        var cw    = new Vector2IntListWrapper();
        var cells = MerlinRuneBridge.Instance?.CaptureRuneCells();
        if (cells != null) cw.items.AddRange(cells);
        d.runeCellsJson = JsonUtility.ToJson(cw);

        // 룬 보드 Shape 배치 (재편집용)
        var pw         = new RunePlacementListWrapper();
        var placements = MerlinRuneBridge.Instance?.CaptureRunePlacements();
        if (placements != null) pw.items.AddRange(placements);
        d.runePlacementsJson = JsonUtility.ToJson(pw);
    }

    /// <summary>무기 슬롯 강화/승급 상태를 세이브에 캡처. 라이브 WeaponManager → 씬 전환 저장 슬롯 순.</summary>
    private static void CaptureWeaponEnhance(RunSaveData d, GameRunSession s)
    {
        var wm = s.Player?.WeaponManager;
        WeaponData w0 = wm?.Weapon0Data ?? SlotFromSaved(s, 0);
        WeaponData w1 = wm?.Weapon1Data ?? SlotFromSaved(s, 1);
        d.weapon0EnhanceLevel = w0?.enhanceLevel ?? 0;
        d.weapon1EnhanceLevel = w1?.enhanceLevel ?? 0;
        d.weapon0EvolutionStage = w0?.evolutionStage ?? 0;
        d.weapon1EvolutionStage = w1?.evolutionStage ?? 0;
        d.weapon0LegendId     = w0?.legendId ?? string.Empty;
        d.weapon1LegendId     = w1?.legendId ?? string.Empty;
    }

    private static WeaponData SlotFromSaved(GameRunSession s, int slot)
        => (s.SavedWeaponSlots != null && s.SavedWeaponSlots.Length > slot) ? s.SavedWeaponSlots[slot] : null;

    // ─────────────────────────────────────────────────────────
    // Private Methods — Build
    // ─────────────────────────────────────────────────────────

    private static RunSaveData BuildSaveData(GameRunSession session, int slotIndex, int retryCount, bool isInStartRoom = false, RunSaveData prevSave = null)
    {
        var ps = session.PlayerState;

        var itemWrapper = new ItemListWrapper();
        itemWrapper.items.AddRange(session.ItemInventory.PlacedItems);

        var logWrapper = new RoomClearLogWrapper();
        logWrapper.records.AddRange(session.RoomClearRecords);

        var loadout       = AppBootstrapper.Instance?.Loadout;
        var charData      = loadout?.CharacterData;
        int progress      = CalcProgressPercent(session.CurrentChapter);

        // 이어하기 복원 시 CharacterSO가 null일 수 있으므로 이전 저장값을 폴백으로 사용
        string charKey  = !string.IsNullOrEmpty(loadout?.CharacterPrefabKey)
                            ? loadout.CharacterPrefabKey
                            : (prevSave?.characterKey  ?? string.Empty);
        string charName = charData?.characterName
                            ?? prevSave?.characterName
                            ?? string.Empty;

        // 무기 키: WeaponSO 이름(= Addressable 주소)을 저장해 이어하기 복원 시 WeaponSO 재로드에 사용한다.
        // 우선순위: session.SavedWeaponSlots.weaponSOKey → Loadout SO.name → prevSave → 빈 문자열
        string weapon0Key = ExtractWeaponSOKey(session, 0);
        if (string.IsNullOrEmpty(weapon0Key))
            weapon0Key = loadout?.WeaponSlot0?.name ?? prevSave?.weapon0PrefabKey ?? string.Empty;

        string weapon1Key = ExtractWeaponSOKey(session, 1);
        if (string.IsNullOrEmpty(weapon1Key))
            weapon1Key = loadout?.WeaponSlot1?.name ?? prevSave?.weapon1PrefabKey ?? string.Empty;

        // 존 레이아웃 모드 이어하기를 위해 클리어된 존 인덱스 목록 저장
        var zoneProgression = session.ZoneProgression;
        string clearedZoneIndicesJson = string.Empty;
        if (zoneProgression != null)
        {
            var clearedWrapper = new IntListWrapper();
            clearedWrapper.items.AddRange(zoneProgression.ClearedZones);
            clearedZoneIndicesJson = JsonUtility.ToJson(clearedWrapper);
        }

        return new RunSaveData
        {
            slotIndex              = slotIndex,
            hasActiveRun           = true,
            chapter                = (int)session.CurrentChapter,
            currentHp              = ps?.Hp       ?? 0,
            maxHp                  = ps?.MaxHp    ?? 100,
            runGold                = ps?.TempGold ?? 0,
            retryCount             = retryCount,
            playSeconds            = session.PlaySeconds,
            progressPercent        = progress,
            itemCount              = session.ItemInventory.PlacedCount + session.ItemInventory.StagingCount,
            synergyCount           = MerlinRuneBridge.Instance != null ? MerlinRuneBridge.Instance.ActiveSynergyCount : 0,
            roomClearCount         = session.RoomClearRecords.Count,
            characterKey           = charKey,
            characterName          = charName,
            weapon0PrefabKey       = weapon0Key,
            weapon1PrefabKey       = weapon1Key,
            itemsJson              = JsonUtility.ToJson(itemWrapper),
            roomLogsJson           = JsonUtility.ToJson(logWrapper),
            savedAt                = DateTime.UtcNow.ToString("o"),
            isInStartRoom          = isInStartRoom,
            currentZoneIndex       = zoneProgression?.CurrentZoneIndex ?? 0,
            clearedZoneIndicesJson = clearedZoneIndicesJson,
        };
    }

    private static int CalcProgressPercent(ChapterId chapter)
    {
        // 챕터당 25% (Chapter1=0%, Chapter2=25%, Chapter3=50%, Chapter4=75%)
        // 런 클리어(챕터4 보스 격파)=100%는 런 종료 시점에 별도 기록 가능
        return Mathf.Clamp(((int)chapter - 1) * 25, 0, 100);
    }

    private static string ExtractWeaponSOKey(GameRunSession session, int slot)
    {
        if (session.SavedWeaponSlots == null || session.SavedWeaponSlots.Length <= slot)
            return string.Empty;
        var data = session.SavedWeaponSlots[slot];
        // weaponSOKey 우선, 없으면 weaponPrefabKey 폴백 (이전 세이브 호환)
        return !string.IsNullOrEmpty(data?.weaponSOKey) ? data.weaponSOKey
             : data?.weaponPrefabKey ?? string.Empty;
    }
}
