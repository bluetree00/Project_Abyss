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
    public void ClearLocalRun(int slot) => _localStore.Delete(slot);

    /// <summary>
    /// 방 경계에서 현재 런 전체 상태를 로컬에 저장한다.
    /// BuildSaveData로 공통 필드를 채운 뒤 절차생성/로드아웃/룬 확장 필드를 덧붙인다.
    /// </summary>
    public void SaveRunLocal(GameRunSession session, in RunMetaSnapshot meta)
    {
        if (session == null || !session.IsRunning) return;

        int slot  = ActiveSlotIndex;
        var prev  = _localStore.Load(slot);
        int retry = prev?.retryCount ?? 0;

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
        d.heading           = m.heading;
        d.anchorToggle      = m.anchorToggle;
        d.currentRoomPoolKey = m.currentRoomPoolKey;
        d.currentRoomKind   = m.currentRoomKind;
        d.currentRoomMirror = m.currentRoomMirror;

        var cdw = new CooldownListWrapper();
        if (m.cooldowns != null) cdw.items.AddRange(m.cooldowns);
        d.cooldownsJson = JsonUtility.ToJson(cdw);

        // 런 상태 확장
        d.runEssence       = s.RunDelta?.GainedEssence ?? 0;
        d.fuelEnhanceMaterial = s.FuelBank?.EnhanceMaterial ?? 0;   // 이벤트방 연료 은행
        d.fuelRuneOre         = s.FuelBank?.RuneOre ?? 0;
        // 현재 슬롯: 라이브 WeaponManager 우선(첫 방 -1 케이스 해결), 없으면 씬 전환 시 저장값
        int liveSlot       = s.Player?.WeaponManager?.CurrentSlotIndex ?? -1;
        d.weaponCurrentSlot = liveSlot >= 0 ? liveSlot : s.SavedCurrentSlotIndex;

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

    // ─────────────────────────────────────────────────────────
    // Private Methods — Build
    // ─────────────────────────────────────────────────────────

    private static RunSaveData BuildSaveData(GameRunSession session, int slotIndex, int retryCount, bool isInStartRoom = false, RunSaveData prevSave = null)
    {
        var ps = session.PlayerState;

        var itemWrapper = new ItemListWrapper();
        itemWrapper.items.AddRange(session.ItemInventory.PlacedItems);

        var synWrapper = new SynergyListWrapper();
        synWrapper.items.AddRange(session.AppliedSynergies);

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
            progressPercent        = progress,
            itemCount              = session.ItemInventory.PlacedCount + session.ItemInventory.StagingCount,
            synergyCount           = session.AppliedSynergies.Count,
            roomClearCount         = session.RoomClearRecords.Count,
            characterKey           = charKey,
            characterName          = charName,
            weapon0PrefabKey       = weapon0Key,
            weapon1PrefabKey       = weapon1Key,
            itemsJson              = JsonUtility.ToJson(itemWrapper),
            synergiesJson          = JsonUtility.ToJson(synWrapper),
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
