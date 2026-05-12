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
    public async UniTask SaveAsync(GameRunSession session, int slotIndex, bool isNewRun = false)
    {
        if (session == null || !session.IsRunning) return;
        if (!IsValidSlot(slotIndex)) return;

        if (isNewRun)
            Saves[slotIndex].retryCount++;

        Saves[slotIndex] = BuildSaveData(session, slotIndex, Saves[slotIndex].retryCount, Saves[slotIndex]);

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
    // Private Methods — Build
    // ─────────────────────────────────────────────────────────

    private static RunSaveData BuildSaveData(GameRunSession session, int slotIndex, int retryCount, RunSaveData prevSave = null)
    {
        var ps = session.PlayerState;

        var itemWrapper = new ItemListWrapper();
        itemWrapper.items.AddRange(session.ItemInventory.Items);

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

        return new RunSaveData
        {
            slotIndex        = slotIndex,
            hasActiveRun     = true,
            chapter          = (int)session.CurrentChapter,
            currentPointId   = session.StagePointManager?.CurrentPointId ?? -1,
            currentHp        = ps?.Hp       ?? 0,
            maxHp            = ps?.MaxHp    ?? 100,
            runGold          = ps?.TempGold ?? 0,
            retryCount       = retryCount,
            progressPercent  = progress,
            itemCount        = session.ItemInventory.Items.Count,
            synergyCount     = session.AppliedSynergies.Count,
            roomClearCount   = session.RoomClearRecords.Count,
            characterKey     = charKey,
            characterName    = charName,
            weapon0PrefabKey = ExtractWeaponKey(session, 0),
            weapon1PrefabKey = ExtractWeaponKey(session, 1),
            graphJson        = BuildGraphJson(session),
            itemsJson        = JsonUtility.ToJson(itemWrapper),
            synergiesJson    = JsonUtility.ToJson(synWrapper),
            roomLogsJson     = JsonUtility.ToJson(logWrapper),
            savedAt          = DateTime.UtcNow.ToString("o"),
        };
    }

    private static int CalcProgressPercent(ChapterId chapter)
    {
        // 챕터당 20% (Chapter1=0%, Chapter2=20%, ..., Chapter5=80%)
        // 런 클리어(챕터5 보스 격파)=100%는 런 종료 시점에 별도 기록 가능
        return Mathf.Clamp(((int)chapter - 1) * 20, 0, 100);
    }

    private static string ExtractWeaponKey(GameRunSession session, int slot)
    {
        if (session.SavedWeaponSlots == null || session.SavedWeaponSlots.Length <= slot)
            return string.Empty;
        return session.SavedWeaponSlots[slot]?.weaponPrefabKey ?? string.Empty;
    }

    private static string BuildGraphJson(GameRunSession session)
    {
        var graph = session.CachedStageGraph;
        var spm   = session.StagePointManager;
        if (graph?.Nodes == null || spm == null) return string.Empty;

        var saved = new SavedStageGraph
        {
            fullPattern   = graph.FullPattern,
            middlePattern = graph.MiddlePattern,
            nodes         = new SavedStageNode[graph.Nodes.Count],
        };

        for (int i = 0; i < graph.Nodes.Count; i++)
        {
            var node = graph.Nodes[i];
            var ctx  = spm.GetContext(node.PointId);

            saved.nodes[i] = new SavedStageNode
            {
                pointId            = node.PointId,
                stageCategory      = (int)node.Stage,
                normalRoomCategory = (int)node.Normal,
                layerIndex         = node.LayerIndex,
                indexInLayer       = node.IndexInLayer,
                nextPointIds       = node.NextPointIds?.ToArray() ?? Array.Empty<int>(),
                state              = ctx != null ? (int)ctx.State : 0,
                resolvedRoomId     = ctx?.ResolvedRoomId ?? string.Empty,
                isResolved         = ctx?.IsResolved ?? false,
                minDifficulty      = ctx?.MinDifficulty ?? -1,
                maxDifficulty      = ctx?.MaxDifficulty ?? -1,
            };
        }

        return JsonUtility.ToJson(saved);
    }

    private static Param ToParam(RunSaveData d) => new Param
    {
        { "slotIndex",        d.slotIndex },
        { "hasActiveRun",     d.hasActiveRun },
        { "chapter",          d.chapter },
        { "currentPointId",   d.currentPointId },
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
        { "graphJson",        d.graphJson },
        { "itemsJson",        d.itemsJson },
        { "synergiesJson",    d.synergiesJson },
        { "roomLogsJson",     d.roomLogsJson },
        { "savedAt",          d.savedAt },
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
            currentPointId   = ParseInt(row,    "currentPointId",  -1),
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
            graphJson        = ParseString(row, "graphJson"),
            itemsJson        = ParseString(row, "itemsJson"),
            synergiesJson    = ParseString(row, "synergiesJson"),
            roomLogsJson     = ParseString(row, "roomLogsJson"),
            savedAt          = ParseString(row, "savedAt"),
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
