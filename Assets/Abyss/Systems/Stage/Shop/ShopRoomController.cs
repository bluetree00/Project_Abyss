using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 상점 방 런타임 컨트롤러.
/// MapBuilder가 배치한 ShopStallInteraction들을 수집하고,
/// ShopCatalogSO에서 slotCount만큼 상품을 뽑아 각 진열대에 분배한다.
/// 플레이어의 구매 요청을 중계하여 골드 차감/인벤토리 추가를 수행한다.
/// ESC 키로 StageMap 씬으로 복귀.
/// </summary>
public class ShopRoomController : MonoBehaviour
{
    // ── 비공개 필드 ─────────────────────────────────────────
    private readonly List<ShopStallInteraction> _stalls = new();
    private GameRunSession _run;
    private ShopCatalogSO _catalog;
    private int _slotCount;
    private bool _initialized;
    private bool _exiting;

    // ── Lifecycle ───────────────────────────────────────────

    private void Update()
    {
        if (!_initialized || _exiting) return;

        if (Input.GetKeyDown(KeyCode.Escape))
            ExitToStageMap();
    }

    private void OnDestroy()
    {
        foreach (var stall in _stalls)
        {
            if (stall != null)
                stall.OnPurchaseRequested -= HandlePurchase;
        }
        _stalls.Clear();
    }

    // ── Public Methods ──────────────────────────────────────

    /// <summary>
    /// Bootstrapper에서 호출. 방 루트 하위의 모든 진열대를 수집하고
    /// 카탈로그에서 상품을 분배한다.
    /// </summary>
    public void Initialize(GameRunSession run, ShopCatalogSO catalog, int slotCount)
    {
        if (_initialized)
        {
            Debug.LogWarning("[ShopRoom] 이미 초기화됨");
            return;
        }

        _run = run;
        _catalog = catalog;
        _slotCount = Mathf.Max(0, slotCount);

        CollectStalls();
        DistributeItems();

        _initialized = true;
        Debug.Log($"[ShopRoom] 초기화 완료. 진열대 {_stalls.Count}개 / 요청 슬롯 {_slotCount}개");
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

    private bool HandlePurchase(ShopStallInteraction stall)
    {
        if (stall == null || stall.Item == null) return false;
        if (_run == null || !_run.IsRunning)
        {
            Debug.LogWarning("[ShopRoom] 활성 런 없음 — 구매 거부");
            return false;
        }

        var playerState = _run.PlayerState;
        if (playerState == null) return false;

        int price = stall.Item.Price;
        if (!playerState.TrySpendGold(price))
        {
            Debug.Log($"[ShopRoom] 골드 부족: 필요 {price}, 보유 {playerState.TempGold}");
            return false;
        }

        var itemSO = stall.Item.Item;
        var runtimeItem = RuntimeItemData.FromSO(itemSO);
        if (runtimeItem != null)
        {
            _run.ItemInventory?.AddItem(runtimeItem);
            Debug.Log($"[ShopRoom] 구매 성공: {itemSO.itemId} ({price}G)");
        }
        else
        {
            Debug.LogWarning($"[ShopRoom] RuntimeItemData 생성 실패: {itemSO?.itemId}");
        }

        return true;
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
