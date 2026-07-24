using UnityEngine;
using Cysharp.Threading.Tasks;

public sealed class DebugStageRunPanel : MonoBehaviour
{
    [SerializeField] private ChapterId chapter = ChapterId.Chapter1;

    [Header("Optional")]
    [SerializeField] private GameRunBootstrapper bootstrapper;

    [Header("Debug Keys")]
    [SerializeField] private KeyCode clearRoomKey = KeyCode.F5;
    [SerializeField] private KeyCode spawnItemKey = KeyCode.F7;
    private bool _started;

    [Header("테스트 버튼 (재련소/정제소 즉시 오픈)")]
    [Tooltip("개발 편의용 좌상단 버튼. 두 방 모두 실제 진입 경로가 생겨 평시엔 꺼 둔다 — 필요할 때만 켤 것.")]
    [SerializeField] private bool showTestButtons = false;
    private CrucibleRoomController _testCrucible;

    private void Awake()
    {
        if (bootstrapper == null)
            bootstrapper = FindFirstObjectByType<GameRunBootstrapper>(FindObjectsInactive.Include);
    }

    private void Start()
    {
        var run = GetCurrentRun();
        if (run != null && run.IsRunning) return;

        // 시작방/허브 진입 흐름이 있는 씬에서는 GameRunBootstrapper가 스폰·런 시작을 담당하므로 자동 실행하지 않음.
        // (허브(BaseCamp)에서 로드아웃을 갖춘 채 진입하면 IsInStartRoom=false가 되므로 IsStartRoomScene으로 판별)
        var bootstrapperInstance = bootstrapper != null
            ? bootstrapper
            : FindFirstObjectByType<GameRunBootstrapper>(FindObjectsInactive.Include);
        if (bootstrapperInstance != null && bootstrapperInstance.IsStartRoomScene) return;

        if (_started) return;
        _started = true;
        StartRun().Forget();
    }

    private static readonly string[] DebugSpawnItems =
    {
        "item_t1_dull_blade",          // 힘의 룬 — 전체 공격력 +4 (Always)
        "item_t1_swift_charm",         // 쾌속의 룬 — 이동속도 +0.11 (Always)
        "item_t1_old_deck",            // 방벽의 룬 — 방어력 +5 (Always)
        "item_t1_crisis_blade",        // 위기의 룬 — 공격력 +18% (HPBelow50)
        "item_t1_preempt_blade",       // 선제의 룬 — 이동속도 +25% (AfterRoomEnter)
        "item_t1_threat_armor",        // 위협의 룬 — 방어력 +15% (EnemiesNearby)
        "item_t1_calm_blade",          // 냉정의 룬 — 치명타 확률 +6% (NoHit)
        "item_t2_first_strike",        // 선공의 룬 — 첫 타격 +40% (FirstAttackInRoom)
        "item_t2_forged_hammer",       // 망치의 룬 — 전체 공격력 +10 (Always)
        "item_t2_travel_bag",          // 여정의 룬 — 최대 체력 +50 (Always)
    };
    private int _spawnIndex;
    private readonly System.Collections.Generic.HashSet<string> _spawnedIds = new();

    private void Update()
    {
        if (Input.GetKeyDown(clearRoomKey))
            HandleClearRoom();
        else if (Input.GetKeyDown(spawnItemKey))
            HandleSpawnItem();
    }

    private void HandleClearRoom()
    {
        var run = GetCurrentRun();
        if (run == null || !run.IsRunning) return;
        if (run.CurrentRunState == GameRunSession.RunState.Map) return;

        var zp = run.ZoneProgression;
        if (zp == null) return;

        zp.EnableExitGateForZone(zp.CurrentZoneIndex);
        Debug.Log($"[DebugRunPanel] {clearRoomKey} → 방 클리어 스킵, 출구 게이트 활성화 (zone {zp.CurrentZoneIndex})");
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

    private async UniTaskVoid StartRun()
    {
        await UniTask.WaitUntil(() => AppBootstrapper.Instance != null && AppBootstrapper.Instance.IsReady);

        if (bootstrapper == null)
            bootstrapper = FindFirstObjectByType<GameRunBootstrapper>(FindObjectsInactive.Include);

        if (bootstrapper == null)
        {
            Debug.LogError("[DebugRunPanel] GameRunBootstrapper not found.");
            _started = false;
            return;
        }

        await bootstrapper.StartRunAsync(chapter);
        bootstrapper.Run?.NotifyCombatStarted();
    }

    // ── 디버그 키 가이드 UI ────────────────────────────────

    [SerializeField, Tooltip("레거시 디버그 키 힌트 오버레이 표시(기본 off — F5/F6/F7 키 자체는 유지)")]
    private bool showGuiHints = false;

    private void OnGUI()
    {
        if (showTestButtons) DrawTestButtons();
        if (!showGuiHints) return;   // 레거시 정리: 화면 힌트 비활성(키 동작은 Update에서 유지)

        var style = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            richText = true,
        };
        style.normal.textColor = Color.white;

        float x = 10f, y = Screen.height - 120f;

        var bootstrapperInst = bootstrapper != null ? bootstrapper : GameRunBootstrapper.Instance;
        bool inStartRoom = bootstrapperInst != null && bootstrapperInst.IsStartRoomScene;

        if (inStartRoom)
        {
            GUI.Label(new Rect(x, y, 300, 20), "<b>[F6]</b> StageMap으로 스킵", style);
            return;
        }

        var run = GetCurrentRun();
        if (run == null || !run.IsRunning) return;

        GUI.Label(new Rect(x, y,      300, 20), $"<b>[F5/F6]</b> 방 클리어 (출구 게이트 활성화)", style);
        GUI.Label(new Rect(x, y + 20, 300, 20), $"<b>[F7]</b> 아이템 스폰", style);
    }

    // ── 테스트 버튼 (재련소/정제소 즉시 오픈) ────────────────

    private void DrawTestButtons()
    {
        const float bw = 168f, bh = 34f, bx = 10f;
        float by = 10f;
        var prev = GUI.color;
        GUI.color = new Color(1f, 0.85f, 0.4f, 1f);
        if (GUI.Button(new Rect(bx, by, bw, bh), "재련소 테스트"))
            OpenCrucibleTestAsync().Forget();
        if (GUI.Button(new Rect(bx, by + bh + 6f, bw, bh), "정제소 테스트"))
            OpenRefineryTestAsync().Forget();
        GUI.color = prev;
    }

    /// <summary>테스트용 재련소 오픈 — NPC 없이 임시 컨트롤러를 만들어 UI만 직접 띄운다(컨트롤러 캐시·재사용).</summary>
    private async UniTaskVoid OpenCrucibleTestAsync()
    {
        var run = GetCurrentRun();
        if (run == null || !run.IsRunning)
        {
            Debug.LogWarning("[DebugTest] 재련소 — 진행 중인 런이 없습니다(먼저 런 시작).");
            return;
        }

        if (_testCrucible == null)
        {
            var go = new GameObject("~TestCrucible");
            go.transform.SetParent(transform, false);
            _testCrucible = go.AddComponent<CrucibleRoomController>();
            var table = await WeaponEnhanceService.EnsureLoadedAsync();
            _testCrucible.Initialize(run, table, null, null);   // npcPrefab=null → NPC 없이, UI만 직접 오픈
        }

        var panel = await Managers.UI.ShowPopupUIAndGetAsync<UI_CruciblePanel>();
        if (panel == null) { Debug.LogWarning("[DebugTest] UI_CruciblePanel 로드 실패"); return; }
        panel.Bind(_testCrucible);
    }

    /// <summary>테스트용 정제소 — 실제 정제소 패널(UI_RefineryPanel)을 연다. 원석이 부족하면 테스트용으로 20 지급.</summary>
    private async UniTaskVoid OpenRefineryTestAsync()
    {
        var run = GetCurrentRun();
        if (run == null || !run.IsRunning)
        {
            Debug.LogWarning("[DebugTest] 정제소 — 진행 중인 런이 없습니다(먼저 런 시작).");
            return;
        }

        // 테스트 편의: 돌릴 원석이 없으면 조금 지급
        if (run.FuelBank != null && run.FuelBank.RuneOre < 8)
            run.FuelBank.Add(FuelKind.RuneOre, 20);

        var panel = await Managers.UI.ShowPopupUIAndGetAsync<UI_RefineryPanel>();
        if (panel == null) Debug.LogWarning("[DebugTest] UI_RefineryPanel 로드 실패");
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
