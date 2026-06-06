using System;
using BackEnd;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 이어하기 저장/불러오기. 뒤끝 RUN_PROGRESS 테이블 CRUD — 슬롯 3개 지원.
///
/// 저장 시점 : StageMap 진입 (방 사이). StageMapBootstrapper가 SaveAsync 호출.
/// 삭제 시점 : 런 종료. AppBootstrapper.EndRun → ClearAsync 호출.
/// </summary>
public class RunProgressManager : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────
    // Constants
    // ─────────────────────────────────────────────────────────

    private const string TABLE     = "USER_RUN_PROGRESS";
    public  const int    SlotCount = 3;

    // ─────────────────────────────────────────────────────────
    // Static
    // ─────────────────────────────────────────────────────────

    public static RunProgressManager Instance { get; private set; }

    // ─────────────────────────────────────────────────────────
    // Private
    // ─────────────────────────────────────────────────────────

    private readonly string[] _rowInDates = new string[SlotCount];

    // PR1: 진행 중 런 세이브는 로컬 파일(persistentDataPath)이 권위.
    // 뒤끝 USER_RUN_PROGRESS(SaveAsync/ClearAsync)는 텔레메트리 분리 전까지 무변경으로 둔다.
    private readonly IRunSaveStore _localStore = new LocalFileRunSaveStore();

    // ─────────────────────────────────────────────────────────
    // Properties
    // ─────────────────────────────────────────────────────────

    public RunSaveData[] Saves           { get; private set; } = new RunSaveData[SlotCount];

    /// <summary>현재 플레이 중인 슬롯 인덱스. UI에서 슬롯 선택 시 설정.</summary>
    public int           ActiveSlotIndex { get; set; } = 0;

    public bool HasActiveSaveAt(int slot) => IsValidSlot(slot) && Saves[slot]?.hasActiveRun == true;

    // ─────────────────────────────────────────────────────────
    // Events
    // ─────────────────────────────────────────────────────────

    public event Action OnSaveLoaded;

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

        for (int i = 0; i < SlotCount; i++)
            Saves[i] = MakeEmpty(i);
    }

    // ─────────────────────────────────────────────────────────
    // Public Methods
    // ─────────────────────────────────────────────────────────

    /// <summary>로그인 직후 호출. RUN_PROGRESS 전체 행을 불러온다.</summary>
    public async UniTask LoadAsync()
    {
        var tcs = new UniTaskCompletionSource();

        Backend.GameData.GetMyData(TABLE, new Where(), callback =>
        {
            if (callback.IsSuccess())
            {
                try
                {
                    var rows = callback.FlattenRows();
                    for (int i = 0; i < rows.Count; i++)
                    {
                        var row  = rows[i];
                        int slot = ParseInt(row, "slotIndex", 0);
                        if (!IsValidSlot(slot)) continue;

                        _rowInDates[slot] = row["inDate"].ToString();
                        Saves[slot]       = ParseRow(row);
                    }

                    // 서버에 없는 슬롯은 빈 행 삽입 (비동기 fire-and-forget)
                    for (int i = 0; i < SlotCount; i++)
                    {
                        if (string.IsNullOrEmpty(_rowInDates[i]))
                            InsertEmptyRow(i);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[RunProgress] Load parse error: {e}");
                }
            }
            else
            {
                Debug.LogWarning($"[RunProgress] Load failed: {callback.GetMessage()}");
            }

            OnSaveLoaded?.Invoke();
            tcs.TrySetResult();
        });

        await tcs.Task;
    }

    /// <summary>
    /// StageMap 진입 시 호출. 현재 런 상태를 직렬화해 지정 슬롯에 저장한다.
    /// isNewRun=true이면 retryCount를 먼저 1 증가시킨다.
    /// </summary>
    public async UniTask SaveAsync(GameRunSession session, int slotIndex, bool isNewRun = false, bool isInStartRoom = false)
    {
        if (session == null || !session.IsRunning) return;
        if (!IsValidSlot(slotIndex)) return;

        if (isNewRun)
            Saves[slotIndex].retryCount++;

        Saves[slotIndex] = BuildSaveData(session, slotIndex, Saves[slotIndex].retryCount, isInStartRoom, Saves[slotIndex]);

        var param = ToParam(Saves[slotIndex]);
        var tcs   = new UniTaskCompletionSource();

        if (string.IsNullOrEmpty(_rowInDates[slotIndex]))
        {
            Backend.GameData.Insert(TABLE, param, cb =>
            {
                if (cb.IsSuccess())
                    _rowInDates[slotIndex] = cb.GetInDate();
                else
                    Debug.LogWarning($"[RunProgress] Save[{slotIndex}](Insert) failed: {cb.GetMessage()}");
                tcs.TrySetResult();
            });
        }
        else
        {
            Backend.GameData.UpdateV2(TABLE, _rowInDates[slotIndex], Backend.UserInDate, param, cb =>
            {
                if (!cb.IsSuccess())
                    Debug.LogWarning($"[RunProgress] Save[{slotIndex}](Update) failed: {cb.GetMessage()}");
                tcs.TrySetResult();
            });
        }

        await tcs.Task;
    }

    /// <summary>런 종료 또는 슬롯 삭제 시 호출. 모든 필드를 기본값으로 초기화한다.</summary>
    public async UniTask ClearAsync(int slotIndex)
    {
        if (!IsValidSlot(slotIndex)) return;

        Saves[slotIndex] = MakeEmpty(slotIndex);

        if (string.IsNullOrEmpty(_rowInDates[slotIndex])) return;

        var param = ToParam(MakeEmpty(slotIndex));
        var tcs   = new UniTaskCompletionSource();

        Backend.GameData.UpdateV2(TABLE, _rowInDates[slotIndex], Backend.UserInDate, param, cb =>
        {
            if (!cb.IsSuccess())
                Debug.LogWarning($"[RunProgress] Clear[{slotIndex}] failed: {cb.GetMessage()}");
            tcs.TrySetResult();
        });

        await tcs.Task;
    }

    // ─────────────────────────────────────────────────────────
    // Public Methods — 로컬 런 세이브 (PR1)
    // ─────────────────────────────────────────────────────────

    /// <summary>로컬에 유효한 진행 중 런 세이브가 있는지.</summary>
    public bool HasLocalRun => _localStore.HasSave();

    /// <summary>로컬 런 세이브를 로드한다(없으면 null).</summary>
    public RunSaveData LoadLocalRun() => _localStore.Load();

    /// <summary>로컬 런 세이브를 삭제한다(사망/클리어/새 런 시작 시).</summary>
    public void ClearLocalRun() => _localStore.Delete();

    /// <summary>
    /// 방 경계에서 현재 런 전체 상태를 로컬에 저장한다.
    /// 기존 BuildSaveData(서버 경로 무변경)로 공통 필드를 채운 뒤 절차생성/로드아웃/룬 확장 필드를 덧붙인다.
    /// </summary>
    public void SaveRunLocal(GameRunSession session, in RunMetaSnapshot meta)
    {
        if (session == null || !session.IsRunning) return;

        var prev  = _localStore.Load();
        int retry = prev?.retryCount ?? 0;

        var data = BuildSaveData(session, ActiveSlotIndex, retry, false, prev);
        ApplyExtendedFields(data, session, meta);

        _localStore.Save(data);
    }

    /// <summary>로컬 세이브 전용 확장 필드 채움. 서버 ToParam/BuildSaveData에는 영향 없음.</summary>
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

    private static Param ToParam(RunSaveData d) => new Param
    {
        { "slotIndex",        d.slotIndex },
        { "hasActiveRun",     d.hasActiveRun },
        { "chapter",          d.chapter },
        { "currentHp",        d.currentHp },
        { "maxHp",            d.maxHp },
        { "runGold",          d.runGold },
        { "retryCount",       d.retryCount },
        { "progressPercent",  d.progressPercent },
        { "itemCount",        d.itemCount },
        { "synergyCount",     d.synergyCount },
        { "roomClearCount",   d.roomClearCount },
        { "characterKey",     d.characterKey },
        { "characterName",    d.characterName },
        { "weapon0PrefabKey", d.weapon0PrefabKey },
        { "weapon1PrefabKey", d.weapon1PrefabKey },
        { "itemsJson",        d.itemsJson },
        { "synergiesJson",    d.synergiesJson },
        { "roomLogsJson",     d.roomLogsJson },
        { "savedAt",                d.savedAt },
        { "isInStartRoom",          d.isInStartRoom },
        { "currentZoneIndex",       d.currentZoneIndex },
        { "clearedZoneIndicesJson", d.clearedZoneIndicesJson },
    };

    // ─────────────────────────────────────────────────────────
    // Private Methods — Parse
    // ─────────────────────────────────────────────────────────

    private static RunSaveData ParseRow(LitJson.JsonData row)
    {
        return new RunSaveData
        {
            slotIndex        = ParseInt(row,    "slotIndex",        0),
            hasActiveRun     = ParseBool(row,   "hasActiveRun"),
            chapter          = ParseInt(row,    "chapter",          1),
            currentHp        = ParseInt(row,    "currentHp",       100),
            maxHp            = ParseInt(row,    "maxHp",           100),
            runGold          = ParseInt(row,    "runGold",           0),
            retryCount       = ParseInt(row,    "retryCount",        0),
            progressPercent  = ParseInt(row,    "progressPercent",   0),
            itemCount        = ParseInt(row,    "itemCount",          0),
            synergyCount     = ParseInt(row,    "synergyCount",       0),
            roomClearCount   = ParseInt(row,    "roomClearCount",     0),
            characterKey     = ParseString(row, "characterKey"),
            characterName    = ParseString(row, "characterName"),
            weapon0PrefabKey = ParseString(row, "weapon0PrefabKey"),
            weapon1PrefabKey = ParseString(row, "weapon1PrefabKey"),
            itemsJson        = ParseString(row, "itemsJson"),
            synergiesJson    = ParseString(row, "synergiesJson"),
            roomLogsJson           = ParseString(row, "roomLogsJson"),
            savedAt                = ParseString(row, "savedAt"),
            isInStartRoom          = ParseBool(row,   "isInStartRoom"),
            currentZoneIndex       = ParseInt(row,    "currentZoneIndex",    0),
            clearedZoneIndicesJson = ParseString(row, "clearedZoneIndicesJson"),
        };
    }

    private static bool   ParseBool(LitJson.JsonData row, string key)
        => row.ContainsKey(key) && bool.TryParse(row[key].ToString(), out var v) && v;
    private static int    ParseInt(LitJson.JsonData row, string key, int fallback)
        => row.ContainsKey(key) && int.TryParse(row[key].ToString(), out var v) ? v : fallback;
    private static string ParseString(LitJson.JsonData row, string key)
        => row.ContainsKey(key) ? row[key].ToString() : string.Empty;

    private static RunSaveData MakeEmpty(int slotIndex)
        => new RunSaveData { slotIndex = slotIndex, hasActiveRun = false };

    private void InsertEmptyRow(int slotIndex)
    {
        var param = ToParam(MakeEmpty(slotIndex));
        Backend.GameData.Insert(TABLE, param, cb =>
        {
            if (cb.IsSuccess())
                _rowInDates[slotIndex] = cb.GetInDate();
            else
                Debug.LogWarning($"[RunProgress] Empty insert[{slotIndex}] failed: {cb.GetMessage()}");
        });
    }

    private static bool IsValidSlot(int slot) => slot >= 0 && slot < SlotCount;
}
