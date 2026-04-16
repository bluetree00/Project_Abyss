using UnityEngine;
using Cysharp.Threading.Tasks;

public sealed class DebugStageRunPanel : MonoBehaviour
{
    [SerializeField] private ChapterId chapter = ChapterId.Chapter1;

    [Header("Optional")]
    [SerializeField] private GameRunBootstrapper bootstrapper;

    [Header("Debug Keys")]
    [SerializeField] private KeyCode clearRoomKey = KeyCode.F5;
    [SerializeField] private KeyCode returnToStageMapKey = KeyCode.F6;
    [SerializeField] private KeyCode spawnItemKey = KeyCode.F7;

    private bool _started;

    private void Awake()
    {
        if (bootstrapper == null)
            bootstrapper = FindObjectOfType<GameRunBootstrapper>(true);
    }

    private void Start()
    {
        var run = GetCurrentRun();
        if (run != null && run.IsRunning) return;

        if (_started) return;
        _started = true;
        StartRun().Forget();
    }

    private static readonly string[] DebugSpawnItems =
    {
        "item_frog_prince_ball",       // 근거리 공격력 +5 (Always)
        "item_cinderella_shoes",       // 이동속도 +0.2 (Always)
        "item_beauty_beast_rose",      // 체력+10, 흡혈2% (Always+OnHit)
        "item_wolf_claw",              // HP50%이하 공격력+20% (HPBelow50)
        "item_hansel_cookie",          // 방 클리어 시 체력3 회복 (OnRoomClear)
        "item_sleeping_beauty_spindle",// 피격 시 5% 무효화 (OnHit)
    };
    private int _spawnIndex;

    private void Update()
    {
        if (Input.GetKeyDown(clearRoomKey))
            HandleClearRoom();
        else if (Input.GetKeyDown(returnToStageMapKey))
            HandleReturnToStageMap();
        else if (Input.GetKeyDown(spawnItemKey))
            HandleSpawnItem();
    }

    private void HandleClearRoom()
    {
        var run = GetCurrentRun();
        if (run == null || !run.IsRunning) return;
        if (run.CurrentRunState == GameRunSession.RunState.Map) return;

        var spm = run.StagePointManager;
        if (spm != null && spm.CurrentPointId >= 0)
            spm.MarkCleared(spm.CurrentPointId);

        run.EnterMap();
        Debug.Log($"[DebugRunPanel] {clearRoomKey} → 방 클리어 스킵, Map 전환");
    }

    private void HandleSpawnItem()
    {
        var run = GetCurrentRun();
        if (run == null || !run.IsRunning || run.Player == null) return;

        string itemId = DebugSpawnItems[_spawnIndex % DebugSpawnItems.Length];
        _spawnIndex++;

        // ItemDataManager에서 데이터 조회
        var entries = Managers.ItemData?.GetItem(itemId);
        RuntimeItemData data;

        if (entries != null && entries.Count > 0)
        {
            data = RuntimeItemData.FromServer(entries);
            // 새 CSV에는 name이 없으므로 id를 표시명으로 사용
            if (string.IsNullOrEmpty(data.displayName))
                data.displayName = itemId.Replace("item_", "").Replace("_", " ");
        }
        else
        {
            // 데이터 없으면 수동 생성
            data = CreateFallbackItem(itemId);
        }

        if (data == null) return;

        // 플레이어 앞 5m에 스폰
        var playerT = run.Player.transform;
        Vector3 spawnPos = playerT.position + playerT.forward * 5f + Vector3.up * 0.5f;

        WorldItemDisplay.SpawnFromData(data, spawnPos);
        Debug.Log($"[DebugSpawn] F7 → {data.displayName} ({data.effects.Count}개 효과) at {spawnPos}");
    }

    private static RuntimeItemData CreateFallbackItem(string itemId)
    {
        return new RuntimeItemData
        {
            itemId = itemId,
            displayName = itemId.Replace("item_", "").Replace("_", " "),
            rarity = ItemRarity.Common,
            category = ItemCategory.Ring,
            effects = new System.Collections.Generic.List<ItemEffectSlot>
            {
                new ItemEffectSlot { effectType = "MeleeDamage", trigger = "Always", value = 5 }
            }
        };
    }

    private void HandleReturnToStageMap()
    {
        var run = GetCurrentRun();
        if (run == null || !run.IsRunning) return;

        // 현재 포인트 클리어 처리
        var spm = run.StagePointManager;
        if (spm != null && spm.CurrentPointId >= 0)
            spm.MarkCleared(spm.CurrentPointId);

        Debug.Log($"[DebugRunPanel] {returnToStageMapKey} → 전투 클리어, StageMap 씬 전환");
        AppBootstrapper.Instance.RequestLoad(Define.Scene.StageMap);
    }

    private async UniTaskVoid StartRun()
    {
        await UniTask.WaitUntil(() => AppBootstrapper.Instance != null && AppBootstrapper.Instance.IsReady);

        var app = AppBootstrapper.Instance;

        // StageMap 씬: StageMapBootstrapper가 비동기로 런을 초기화 중일 수 있으므로 대기
        if (StageMapBootstrapper.Instance != null)
        {
            await UniTask.WaitUntil(() => app.CurrentRun != null && app.CurrentRun.IsRunning);
            Debug.Log("[DebugRunPanel] StageMapBootstrapper 런 준비 완료.");
            _started = false;
            return;
        }

        // GameScene: GameRunBootstrapper를 통해 런 시작
        if (bootstrapper == null)
            bootstrapper = FindObjectOfType<GameRunBootstrapper>(true);

        if (bootstrapper == null)
        {
            Debug.LogError("[DebugRunPanel] GameRunBootstrapper not found.");
            _started = false;
            return;
        }

        await bootstrapper.StartRunAsync(chapter);

        // StageMap 선택 단계를 건너뛰므로 직접 전투 모드로 전환
        // (정상 플로우에서는 방 선택 시 NotifyCombatStarted()가 호출됨)
        bootstrapper.Run?.NotifyCombatStarted();
    }

    /// <summary>
    /// GameRunBootstrapper 또는 AppBootstrapper.CurrentRun에서 세션을 가져옵니다.
    /// </summary>
    private GameRunSession GetCurrentRun()
    {
        if (bootstrapper != null) return bootstrapper.Run;
        var inst = GameRunBootstrapper.Instance;
        if (inst != null) return inst.Run;
        var app = AppBootstrapper.Instance;
        return app != null ? app.CurrentRun : null;
    }
}
