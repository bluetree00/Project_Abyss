using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 상점 방 런타임 컨트롤러.
/// 룸 프리팹의 매대(ShopStallInteraction) 수가 곧 슬롯 수.
///
/// 매대마다:
///   1. 매대의 카테고리(Item/Weapon)를 사용
///   2. LuckRollService.RollRarity(player.Luck) 로 등급 추첨
///   3. ShopDataManager.GetPool(category, rarity) 풀 조회
///   4. 인벤토리/무기 매니저로 보유 항목 필터
///   5. 풀 비면 등급 강등 fallback (Legendary→Epic→Rare→Common, 다 비면 SOLD OUT)
///   6. 매대 간 같은 target_id 중복 차단 (HashSet)
///   7. weight 기반 가중 추첨
///
/// 인벤토리/무기 변동 시 매대를 재평가하여 "보유 중" 라벨을 토글한다.
/// LuckRollTable이 비어있거나 ShopDataManager가 비초기화면 ShopCatalogSO 폴백.
/// </summary>
public class ShopRoomController : MonoBehaviour
{
    // ── Constants ───────────────────────────────────────────
    private const int MaxRarityFallbackAttempts = 4;

    // ── 비공개 필드 ─────────────────────────────────────────
    private readonly List<ShopStallInteraction> _stalls = new();
    private GameRunSession _run;
    private ShopCatalogSO _catalog;
    private LuckRollTableSO _luckTable;
    private int _slotCount;
    private bool _initialized;
    private bool _exiting;
    private bool _eventsHooked;
    private bool _weaponEventsHooked;
    private PlayerWeaponManager _hookedWeaponManager;

    // ── Lifecycle ───────────────────────────────────────────

    private void Update()
    {
        if (!_initialized || _exiting) return;

        if (Input.GetKeyDown(KeyCode.Escape))
            ExitToStageMap();
    }

    private void OnDestroy()
    {
        UnhookRunEvents();

        foreach (var stall in _stalls)
        {
            if (stall != null)
                stall.OnPurchaseRequested -= HandlePurchase;
        }
        _stalls.Clear();
    }

    // ── Public Methods ──────────────────────────────────────

    /// <summary>
    /// Bootstrapper에서 호출.
    /// catalog는 ShopDataManager 미초기화/LuckTable 비어있을 때의 폴백용 (deprecated, 신규 흐름은 chart 기반).
    /// </summary>
    public void Initialize(GameRunSession run, ShopCatalogSO catalog, LuckRollTableSO luckTable, int slotCount)
    {
        if (_initialized)
        {
            Debug.LogWarning("[ShopRoom] 이미 초기화됨");
            return;
        }

        _run = run;
        _catalog = catalog;
        _luckTable = luckTable;
        _slotCount = Mathf.Max(0, slotCount);

        CollectStalls();
        DistributeItems();
        HookRunEvents();
        RefreshAllStallOwnership();

        _initialized = true;
        Debug.Log($"[ShopRoom] 초기화 완료. 진열대 {_stalls.Count}개 / 요청 슬롯 {_slotCount}개 / luckTable={(luckTable != null ? "OK" : "null")} / catalog={(catalog != null ? "OK" : "null")}");
    }

    // ── Private Methods ─────────────────────────────────────

    private void CollectStalls()
    {
        _stalls.Clear();
        GetComponentsInChildren<ShopStallInteraction>(true, _stalls);

        foreach (var stall in _stalls)
            stall.OnPurchaseRequested += HandlePurchase;
    }

    private void DistributeItems()
    {
        if (_stalls.Count == 0) return;

        bool useChartFlow = Managers.ShopData != null
                            && Managers.ShopData.IsInitialized
                            && _luckTable != null;

        if (useChartFlow)
        {
            DistributeItemsFromChart();
            return;
        }

        // ─ 폴백: 레거시 ShopCatalogSO 흐름 ─
        Debug.LogWarning("[ShopRoom] Chart 기반 추첨 불가 — ShopCatalogSO 폴백 사용");
        DistributeItemsFromCatalog();
    }

    private void DistributeItemsFromChart()
    {
        var usedTargetIds = new HashSet<string>();
        int luck = ResolvePlayerLuck();
        int filled = 0;

        for (int i = 0; i < _stalls.Count; i++)
        {
            var stall = _stalls[i];
            var entry = TryRollEntryForStall(stall.Category, luck, usedTargetIds);
            if (entry != null)
            {
                usedTargetIds.Add(entry.target_id);
                stall.Bind(entry);
                filled++;
            }
            else
            {
                stall.BindEmpty();
                stall.MarkSold(); // 빈 매대는 SOLD OUT으로 처리하여 입력 차단
            }
        }

        Debug.Log($"[ShopRoom] Chart 추첨 완료: 채워진 매대 {filled}/{_stalls.Count} (luck={luck})");
    }

    private void DistributeItemsFromCatalog()
    {
        int distributeCount = Mathf.Min(_slotCount, _stalls.Count);
        List<ShopItemSO> picks = _catalog != null
            ? _catalog.PickRandom(distributeCount)
            : new List<ShopItemSO>();

        for (int i = 0; i < _stalls.Count; i++)
        {
            if (i < picks.Count)
                _stalls[i].Bind(picks[i]);
            else
                _stalls[i].MarkSold(); // 풀이 부족하면 남은 진열대는 SOLD OUT
        }

        if (picks.Count < distributeCount)
            Debug.LogWarning($"[ShopRoom] 카탈로그가 부족해 {distributeCount - picks.Count}개 진열대 비어있음");
    }

    /// <summary>
    /// 매대 1개에 들어갈 entry 추첨.
    /// 등급 추첨 → 풀 조회 + 인벤토리/중복 필터 → 비면 강등 fallback.
    /// 모두 실패하면 null.
    /// </summary>
    private ShopEntry TryRollEntryForStall(ShopCategory cat, int luck, HashSet<string> used)
    {
        var rarity = LuckRollService.RollRarity(luck, _luckTable);
        string catStr = cat.ToChartString();

        for (int attempt = 0; attempt < MaxRarityFallbackAttempts; attempt++)
        {
            var pool = Managers.ShopData.GetPool(catStr, rarity);
            var filtered = FilterPool(pool, used);

            if (filtered.Count > 0)
                return WeightedPick(filtered);

            // 강등 fallback: Legendary→Epic→Rare→Common
            if (rarity == ItemRarity.Common) return null;
            rarity = (ItemRarity)((int)rarity - 1);
        }
        return null;
    }

    private List<ShopEntry> FilterPool(List<ShopEntry> pool, HashSet<string> used)
    {
        var result = new List<ShopEntry>(pool.Count);
        for (int i = 0; i < pool.Count; i++)
        {
            var e = pool[i];
            if (e == null || string.IsNullOrEmpty(e.target_id)) continue;
            if (used.Contains(e.target_id)) continue;
            if (IsAlreadyOwned(e)) continue;
            if (e.weight <= 0) continue;
            result.Add(e);
        }
        return result;
    }

    /// <summary>weight 기반 가중 추첨. 빈 리스트면 null.</summary>
    private static ShopEntry WeightedPick(List<ShopEntry> pool)
    {
        int total = 0;
        for (int i = 0; i < pool.Count; i++) total += Mathf.Max(0, pool[i].weight);
        if (total <= 0) return pool.Count > 0 ? pool[0] : null;

        int roll = UnityEngine.Random.Range(0, total);
        int acc = 0;
        for (int i = 0; i < pool.Count; i++)
        {
            acc += Mathf.Max(0, pool[i].weight);
            if (roll < acc) return pool[i];
        }
        return pool[pool.Count - 1];
    }

    /// <summary>
    /// entry가 이미 인벤토리/무기 매니저에 보유 중인지.
    /// 아이템: CountItem &gt;= MaxStack
    /// 무기: HasWeaponId
    /// </summary>
    private bool IsAlreadyOwned(ShopEntry entry)
    {
        if (entry == null || string.IsNullOrEmpty(entry.target_id)) return false;
        if (_run == null) return false;

        var cat = ShopCategoryExtensions.FromChartString(entry.category);
        if (cat == ShopCategory.Weapon)
        {
            var wm = _run.Player?.WeaponManager;
            if (wm == null) return false;
            return wm.HasWeaponId(entry.target_id);
        }

        // Item
        var inv = _run.ItemInventory;
        if (inv == null) return false;

        var itemSO = ItemSORegistry.Find(entry.target_id);
        int maxStack = itemSO != null ? itemSO.MaxStack : 1;
        return inv.CountItem(entry.target_id) >= maxStack;
    }

    private int ResolvePlayerLuck()
    {
        var stats = _run?.Player?.RuntimeStats;
        return stats != null ? stats.Luck : 0;
    }

    // ── 인벤토리/무기 변동 이벤트 ───────────────────────────

    private void HookRunEvents()
    {
        if (_eventsHooked || _run == null) return;

        if (_run.ItemInventory != null)
            _run.ItemInventory.OnStagingChanged += OnInventoryChanged;

        // 무기 매니저는 플레이어 스폰 시점에 따라 늦게 붙을 수 있음 → OnPlayerBound로 후크
        TryHookWeaponManager(_run.Player);
        _run.OnPlayerBound += OnPlayerBound;

        _eventsHooked = true;
    }

    private void UnhookRunEvents()
    {
        if (!_eventsHooked) return;
        _eventsHooked = false;

        if (_run?.ItemInventory != null)
            _run.ItemInventory.OnStagingChanged -= OnInventoryChanged;

        if (_run != null)
            _run.OnPlayerBound -= OnPlayerBound;

        UnhookWeaponManager();
    }

    private void TryHookWeaponManager(PlayerController player)
    {
        if (_weaponEventsHooked) return;
        var wm = player?.WeaponManager;
        if (wm == null) return;

        wm.OnWeaponChanged += OnWeaponChanged;
        _hookedWeaponManager = wm;
        _weaponEventsHooked = true;
    }

    private void UnhookWeaponManager()
    {
        if (!_weaponEventsHooked) return;
        _weaponEventsHooked = false;

        if (_hookedWeaponManager != null)
            _hookedWeaponManager.OnWeaponChanged -= OnWeaponChanged;
        _hookedWeaponManager = null;
    }

    private void OnPlayerBound(PlayerController player)
    {
        TryHookWeaponManager(player);
        RefreshAllStallOwnership();
    }

    private void OnInventoryChanged() => RefreshAllStallOwnership();
    private void OnWeaponChanged(WeaponData _, GameObject __) => RefreshAllStallOwnership();

    private void RefreshAllStallOwnership()
    {
        for (int i = 0; i < _stalls.Count; i++)
        {
            var stall = _stalls[i];
            if (stall == null) continue;
            if (stall.IsSold) continue; // 이미 구매한 매대는 그대로

            bool owned = stall.Entry != null && IsAlreadyOwned(stall.Entry);
            stall.SetOwnedState(owned);
        }
    }

    // ── 구매 ────────────────────────────────────────────────

    private bool HandlePurchase(ShopStallInteraction stall)
    {
        if (stall == null) return false;
        if (_run == null || !_run.IsRunning)
        {
            Debug.LogWarning("[ShopRoom] 활성 런 없음 — 구매 거부");
            return false;
        }

        var playerState = _run.PlayerState;
        if (playerState == null) return false;

        // 신규 경로 (ShopEntry)
        if (stall.Entry != null)
            return HandlePurchaseFromEntry(stall, stall.Entry, playerState);

        // 레거시 경로 (ShopItemSO)
        if (stall.Item != null)
            return HandlePurchaseFromLegacyItem(stall, stall.Item, playerState);

        return false;
    }

    private bool HandlePurchaseFromEntry(ShopStallInteraction stall, ShopEntry entry, PlayerRunState playerState)
    {
        // 보유 중 가드 (인벤토리/무기 변동이 매대 갱신보다 늦을 수 있어 재검사)
        if (IsAlreadyOwned(entry))
        {
            Debug.Log($"[ShopRoom] 이미 보유 중 — 구매 거부: {entry.target_id}");
            // 매대 라벨 동기화
            stall.SetOwnedState(true);
            return false;
        }

        int price = Managers.ShopData != null
            ? Managers.ShopData.ResolvePrice(entry)
            : Mathf.Max(0, entry.price_override);

        if (!playerState.TrySpendGold(price))
        {
            Debug.Log($"[ShopRoom] 골드 부족: 필요 {price}, 보유 {playerState.TempGold}");
            return false;
        }

        var cat = ShopCategoryExtensions.FromChartString(entry.category);
        if (cat == ShopCategory.Weapon)
        {
            var wm = _run.Player?.WeaponManager;
            if (wm == null)
            {
                Debug.LogWarning("[ShopRoom] WeaponManager 없음 — 무기 구매 실패");
                playerState.AddTempGold(price); // 차감 환불
                return false;
            }
            // weapon_id 기반 prefabKey 조회
            string addressableKey = ResolveWeaponAddressableKey(entry.target_id);
            if (string.IsNullOrEmpty(addressableKey))
            {
                Debug.LogWarning($"[ShopRoom] weapon prefab key 조회 실패: {entry.target_id}");
                playerState.AddTempGold(price); // 차감 환불
                return false;
            }

            // 비동기로 무기 획득 + 교체 팝업. 실패/취소 시 환불.
            ProcessWeaponAcquisitionAsync(wm, addressableKey, price, entry.target_id).Forget();
            return true;
        }

        // Item
        var itemSO = ItemSORegistry.Find(entry.target_id);
        if (itemSO == null)
        {
            Debug.LogWarning($"[ShopRoom] ItemSO 미등록: {entry.target_id}");
            playerState.AddTempGold(price); // 차감 환불
            return false;
        }
        var runtimeItem = RuntimeItemData.FromSO(itemSO);
        if (runtimeItem == null)
        {
            Debug.LogWarning($"[ShopRoom] RuntimeItemData 생성 실패: {entry.target_id}");
            playerState.AddTempGold(price); // 차감 환불
            return false;
        }
        bool added = _run.ItemInventory != null && _run.ItemInventory.AddToStaging(runtimeItem);
        if (!added)
        {
            Debug.Log($"[ShopRoom] 인벤토리 가득 참 — 환불: {entry.target_id}");
            playerState.AddTempGold(price);
            return false;
        }
        Debug.Log($"[ShopRoom] 아이템 구매 성공: {entry.target_id} ({price}G)");
        return true;
    }

    /// <summary>
    /// 무기 비동기 획득 흐름. 빈 슬롯 자동 장착 또는 교체 팝업 → 사용자 선택 후 결과 처리.
    /// 실패/취소 시 차감된 골드 환불.
    /// </summary>
    private async UniTaskVoid ProcessWeaponAcquisitionAsync(PlayerWeaponManager wm, string addressableKey, int price, string targetIdForLog)
    {
        var ct = this.GetCancellationTokenOnDestroy();
        try
        {
            bool acquired = await wm.TryAcquireWeaponWithReplaceAsync(addressableKey, ct);
            if (!acquired)
            {
                Debug.Log($"[ShopRoom] 무기 구매 취소/실패 — 환불: {targetIdForLog} ({price}G)");
                _run?.PlayerState?.AddTempGold(price);
                return;
            }
            Debug.Log($"[ShopRoom] 무기 구매 성공: {targetIdForLog} ({price}G)");

            // 다음 방에서 장비가 유지되도록 세션에 즉시 저장
            var slotData = new WeaponData[wm.SlotCount];
            for (int i = 0; i < wm.SlotCount; i++)
                slotData[i] = wm.slots[i]?.runtimeData;
            _run.SaveWeaponSlots(slotData, wm.CurrentSlotIndex);
        }
        catch (OperationCanceledException)
        {
            // 룸/플레이어 파괴 시 환불 시도 (PlayerState가 살아있으면 적용)
            _run?.PlayerState?.AddTempGold(price);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[ShopRoom] 무기 구매 처리 중 예외 — 환불: {ex.Message}");
            _run?.PlayerState?.AddTempGold(price);
        }
    }

    private bool HandlePurchaseFromLegacyItem(ShopStallInteraction stall, ShopItemSO shopItem, PlayerRunState playerState)
    {
        if (shopItem == null || shopItem.Item == null) return false;

        int price = shopItem.Price;
        if (!playerState.TrySpendGold(price))
        {
            Debug.Log($"[ShopRoom] 골드 부족: 필요 {price}, 보유 {playerState.TempGold}");
            return false;
        }

        var itemSO = shopItem.Item;
        var runtimeItem = RuntimeItemData.FromSO(itemSO);
        if (runtimeItem == null)
        {
            Debug.LogWarning($"[ShopRoom] (레거시) RuntimeItemData 생성 실패: {itemSO?.itemId}");
            playerState.AddTempGold(price); // 차감 환불
            return false;
        }

        bool added = _run.ItemInventory != null && _run.ItemInventory.AddToStaging(runtimeItem);
        if (!added)
        {
            Debug.Log($"[ShopRoom] (레거시) 인벤토리 가득 참 — 환불: {itemSO.itemId}");
            playerState.AddTempGold(price);
            return false;
        }
        Debug.Log($"[ShopRoom] (레거시) 구매 성공: {itemSO.itemId} ({price}G)");
        return true;
    }

    /// <summary>weapon_id → Addressables WeaponSO 로드 키. WeaponSO Addressable 키 = weapon_id 컨벤션.</summary>
    private static string ResolveWeaponAddressableKey(string weaponId)
    {
        if (string.IsNullOrEmpty(weaponId)) return null;
        return weaponId;
    }

    private void ExitToStageMap()
    {
        if (_exiting) return;
        _exiting = true;

        if (_run != null && _run.IsRunning)
            _run.EnterStandby();

        var app = AppBootstrapper.Instance;
        if (app == null)
        {
            Debug.LogError("[ShopRoom] AppBootstrapper 없음 — 퇴장 실패");
            return;
        }

        app.RequestLoad(Define.Scene.StageMap);
    }
}
