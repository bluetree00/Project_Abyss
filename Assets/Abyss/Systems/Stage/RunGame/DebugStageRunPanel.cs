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
    [SerializeField] private KeyCode cycleElementKey = KeyCode.F8;

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
        "item_snow_white_mirror",      // 피해 10% 반사 (OnHit)
        "item_aladdin_carpet",         // 점프 착지 범위 피해 (OnJumpLand)
        "item_black_wings",            // 사망무효 + 10초 무적 (OnNearDeath)
        "item_excalibur_fragment",     // 10% 확률 추가 타격 (OnHit)
        "item_ifrit_ring",             // 불 무기+스킬 → 화염 폭발 (WithFireWeapon)
        "item_thor_hammer_fragment",   // 번개 무기+스킬 → 번개 강타 (WithLightningWeapon)
        "item_three_witches_thread",   // 시너지 완성 → 다음 공격 원소 (OnRecipeComplete)
    };
    private int _spawnIndex;
    private readonly System.Collections.Generic.HashSet<string> _spawnedIds = new();

    private void Update()
    {
        if (Input.GetKeyDown(clearRoomKey))
            HandleClearRoom();
        else if (Input.GetKeyDown(returnToStageMapKey))
            HandleReturnToStageMap();
        else if (Input.GetKeyDown(spawnItemKey))
            HandleSpawnItem();
        else if (Input.GetKeyDown(cycleElementKey))
            HandleCycleElement();
    }

    private static readonly WeaponElement[] ElementCycle =
    {
        WeaponElement.None,
        WeaponElement.Fire,
        WeaponElement.Water,
        WeaponElement.Grass,
        WeaponElement.Earth,
        WeaponElement.Lightning,
    };
    private int _elementIndex;

    private void HandleCycleElement()
    {
        var run = GetCurrentRun();
        if (run?.Player?.WeaponManager == null) return;

        var wd = run.Player.WeaponManager.CurrentWeaponData;
        if (wd == null) return;

        _elementIndex = (_elementIndex + 1) % ElementCycle.Length;
        wd.element = ElementCycle[_elementIndex];

        // 아이템 효과 컨텍스트 갱신
        run.EffectManager?.RefreshContext(run.Player, run);

        string name = ElementCycle[_elementIndex] switch
        {
            WeaponElement.Fire      => "불",
            WeaponElement.Water     => "물",
            WeaponElement.Grass     => "풀",
            WeaponElement.Earth     => "땅",
            WeaponElement.Lightning => "번개",
            _                       => "무속성",
        };
        Debug.Log($"[DebugPanel] F8 → 무기 속성: {name}");
    }

    private void HandleClearRoom()
    {
        var run = GetCurrentRun();
        if (run == null || !run.IsRunning) return;
        if (run.CurrentRunState == GameRunSession.RunState.Map) return;

        var spm = run.StagePointManager;
        if (spm == null || spm.CurrentPointId < 0) return;

        var ctx = spm.GetContext(spm.CurrentPointId);
        spm.MarkCleared(spm.CurrentPointId);

        // 보스 방이면 다음 챕터로 전환
        if (ctx != null && ctx.StageCategory == StageCategory.Boss)
        {
            HandleBossClearAsync(run).Forget();
            return;
        }

        run.EnterMap();
        Debug.Log($"[DebugRunPanel] {clearRoomKey} → 방 클리어 스킵, Map 전환");
    }

    private async Cysharp.Threading.Tasks.UniTaskVoid HandleBossClearAsync(GameRunSession run)
    {
        run.EnterChapterClear();

        Debug.Log($"[DebugRunPanel] 보스 클리어! 챕터 {run.CurrentChapter} → 다음 챕터 전환");

        // 페이드 → 다음 챕터 StageMap으로 전환
        if (run.AdvanceToNextChapter())
        {
            var app = AppBootstrapper.Instance;
            if (app != null)
            {
                if (TransitionOverlay.Instance != null)
                    await TransitionOverlay.Instance.PlayAsync(() => app.RequestLoad(Define.Scene.StageMap));
                else
                    app.RequestLoad(Define.Scene.StageMap);
            }
        }
        else
        {
            Debug.Log("[DebugRunPanel] 마지막 챕터 클리어! 런 종료.");
            // TODO: 런 클리어 결과 화면으로 전환
            var app = AppBootstrapper.Instance;
            if (app != null)
                app.RequestLoad(Define.Scene.Result);
        }
    }

    private void HandleSpawnItem()
    {
        var run = GetCurrentRun();
        if (run == null || !run.IsRunning || run.Player == null) return;

        // 인벤토리 보유 또는 이미 스폰된 아이템을 건너뛰고 순환
        var inventory = run.ItemInventory;
        string itemId = null;
        for (int i = 0; i < DebugSpawnItems.Length; i++)
        {
            string candidate = DebugSpawnItems[_spawnIndex % DebugSpawnItems.Length];
            _spawnIndex++;

            if (_spawnedIds.Contains(candidate)) continue;
            if (inventory != null && inventory.HasItem(candidate)) continue;

            itemId = candidate;
            break;
        }

        if (itemId == null)
        {
            Debug.Log("[DebugSpawn] 모든 디버그 아이템을 이미 보유/스폰 중");
            return;
        }

        _spawnedIds.Add(itemId);

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

        // SO 조회
        var so = ItemSORegistry.Find(itemId);

        // 플레이어 앞 5m에 스폰
        var playerT = run.Player.transform;
        Vector3 spawnPos = playerT.position + playerT.forward * 5f + Vector3.up * 0.5f;

        WorldItemDisplay.SpawnFromData(data, spawnPos, so: so);
        Debug.Log($"[DebugSpawn] F7 → {data.displayName} ({data.effects.Count}개 효과) SO={so != null} at {spawnPos}");
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

        var spm = run.StagePointManager;
        if (spm == null || spm.CurrentPointId < 0) return;

        var ctx = spm.GetContext(spm.CurrentPointId);
        spm.MarkCleared(spm.CurrentPointId);

        // 보스 방이면 다음 챕터로 전환
        if (ctx != null && ctx.StageCategory == StageCategory.Boss)
        {
            HandleBossClearAsync(run).Forget();
            return;
        }

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

    // ── 디버그 키 가이드 UI ────────────────────────────────

    private void OnGUI()
    {
        var run = GetCurrentRun();
        if (run == null || !run.IsRunning) return;

        string element = "무속성";
        var wd = run.Player?.WeaponManager?.CurrentWeaponData;
        if (wd != null)
        {
            element = wd.element switch
            {
                WeaponElement.Fire      => "<color=#FF6622>불</color>",
                WeaponElement.Water     => "<color=#4488FF>물</color>",
                WeaponElement.Grass     => "<color=#44CC44>풀</color>",
                WeaponElement.Earth     => "<color=#CC8844>땅</color>",
                WeaponElement.Lightning => "<color=#44CCFF>번개</color>",
                _                       => "무속성",
            };
        }

        var style = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            richText = true,
        };
        style.normal.textColor = Color.white;

        float x = 10f, y = Screen.height - 120f;
        GUI.Label(new Rect(x, y,      300, 20), $"<b>[F5]</b> 방 클리어  <b>[F6]</b> StageMap", style);
        GUI.Label(new Rect(x, y + 20, 300, 20), $"<b>[F7]</b> 아이템 스폰", style);
        GUI.Label(new Rect(x, y + 40, 300, 20), $"<b>[F8]</b> 무기 속성 변경: {element}", style);
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
