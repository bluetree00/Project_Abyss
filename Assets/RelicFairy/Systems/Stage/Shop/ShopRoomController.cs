using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>TryPurchaseSlot 결과. UI가 피드백 분기에 사용.</summary>
public enum ShopPurchaseResult
{
    Success,          // 즉시 구매 완료(아이템)
    PendingAsync,     // 비동기 확정 대기(무기 교체 팝업) — 결과는 OnShopChanged로 통지
    InsufficientGold, // 골드 부족
    Unavailable,      // 빈/품절/보유/대기 슬롯
    Failed,           // 인벤토리 가득 등 — 환불됨
}

/// <summary>
/// 상점 방 런타임 컨트롤러 (NPC + UI 방식).
///
/// 표현은 월드 매대(ShopStallInteraction/WorldItemDisplay) → 상점 NPC + UI 패널로 교체했다.
/// 데이터/계산 로직(등급 롤·가격표·환불·roomRng 결정성)은 그대로 재사용한다.
///
/// 흐름:
///   1. 방의 기존 매대(ShopStallInteraction)들을 비활성(되돌리기 쉬움)하고, 그 개수·카테고리를
///      슬롯 레이아웃 소스로 재사용한다. 매대가 없으면 slotCount 폴백.
///   2. NPC 1개를 매대 중심점에 스폰하고 OnInteract에 UI 열기를 연결.
///   3. 슬롯 N개를 인메모리(List&lt;ShopSlot&gt;)로 롤한다(roomRng 결정적).
///   4. UI(UI_ShopPanel)가 Slots를 읽어 그리고, TryPurchaseSlot/TryReroll로 구매·리롤한다.
///
/// 인벤토리/무기 변동 시 슬롯의 "보유 중" 상태를 재평가하고 OnShopChanged로 UI에 통지한다.
/// </summary>
public class ShopRoomController : MonoBehaviour
{
    /// <summary>상인 잡담 — 월드스페이스 말풍선으로 주기 출력.</summary>
    private static readonly string[] ShopChatterLines =
    {
        "천천히 둘러보게. 급할 것 없어.",
        "심연 아래선 골드보다 목숨이 비싸지.",
        "오늘 물건은 특별하다네.",
        "값은 정직하게 받는다네.",
        "살아서 돌아오면 또 오게나.",
    };

    // ── Constants ───────────────────────────────────────────
    private const int MaxRarityFallbackAttempts = 4;
    private const string PotionEntryId = "shop_potion";   // SHOP_DATA의 포션 엔트리 id(가격 원본)
    private const float NpcStandHeight = 1f; // 앵커 없는 폴백 스폰 시 캡슐 바닥이 지면에 닿도록(캡슐 height=2의 절반).

    /// <summary>NPC 앞 판매대까지의 거리(m) — 상인이 카운터 뒤에 선 구도.</summary>
    private const float CounterDistance = 2.5f;
    private static readonly string[] DeadStallChildren = { "SoldOutLabel", "DisplayVfxRoot" }; // 과거 월드 구매 상태연출 — NPC+UI로 대체됨.

    // ── 비공개 필드 ─────────────────────────────────────────
    private readonly List<ShopSlot> _slots = new();
    private readonly List<ShopCategory> _stallCategories = new();
    private GameRunSession _run;
    private ShopCatalogSO _catalog;
    private LuckRollTableSO _luckTable;
    private System.Random _roomRng;     // 최초 진열용 결정적 RNG. null이면 전역 Random.
    private System.Random _rerollRng;   // 리롤용 비결정 RNG(시간 기반). lazy init.
    private int _slotCount;
    private int _weaponSlotFallback;    // 매대 없는 방에서 무기 슬롯 수 폴백
    private bool _rerollEnabled;
    private int _rerollCost;

    private GameObject _npcInstance;
    private GameObject[] _decorPrefabs;   // [0]=판매대(NPC 정면), 나머지=뒤쪽 소품
    private ShopNpcInteraction _npc;

    private bool _initialized;
    private bool _uiOpen;
    private bool _eventsHooked;
    private bool _weaponEventsHooked;
    private bool _goldHooked;
    private PlayerWeaponManager _hookedWeaponManager;

    // 심연의 행상(신규 진열) — 버프/룬/재료/포션 정규 상품 + 오늘의 특가.
    private AbyssPeddlerCatalog.Result _peddler;

    // ── Properties (UI가 읽음) ──────────────────────────────
    public IReadOnlyList<ShopSlot> Slots => _slots;
    public int PlayerGold => _run?.PlayerState?.TempGold ?? 0;
    public bool RerollEnabled => _rerollEnabled;
    public int RerollCost => _rerollCost;

    /// <summary>심연의 행상 정규 상품(6칸).</summary>
    public IReadOnlyList<ShopProduct> Products => (IReadOnlyList<ShopProduct>)_peddler?.Products ?? Array.Empty<ShopProduct>();
    /// <summary>오늘의 특가(전 상품 중 무작위 할인). 없으면 null.</summary>
    public ShopProduct SpecialDeal => _peddler?.Special;
    /// <summary>특가 취소선 표시용 원가.</summary>
    public int SpecialOriginalPrice => _peddler?.SpecialOriginalPrice ?? 0;

    /// <summary>슬롯/골드 상태가 바뀌어 UI 재렌더가 필요할 때 발생.</summary>
    public event Action OnShopChanged;

    // ── Lifecycle ───────────────────────────────────────────

    private void OnDestroy()
    {
        UnhookRunEvents();

        if (_npc != null)
            _npc.OnInteract -= HandleNpcInteract;
    }

    // ── Public Methods ──────────────────────────────────────

    /// <summary>
    /// Bootstrapper에서 호출.
    /// catalog: ShopDataManager 미초기화/LuckTable 비어있을 때의 폴백.
    /// npcPrefab: 상점 NPC 프리팹(Addressable로 로드해 전달). null이면 NPC 없이 매대만 비활성.
    /// roomRng: 최초 진열 롤 결정성(이어하기 재현)용. null이면 전역 Random.
    /// rerollEnabled/rerollCost: 리롤 피처 플래그. 기본 off.
    /// </summary>
    public void Initialize(GameRunSession run, ShopCatalogSO catalog, LuckRollTableSO luckTable,
                           int slotCount, System.Random roomRng = null,
                           GameObject npcPrefab = null, int weaponSlotFallback = 1,
                           bool rerollEnabled = false, int rerollCost = 50)
    {
        if (_initialized)
        {
            Debug.LogWarning("[ShopRoom] 이미 초기화됨");
            return;
        }

        _run = run;
        _catalog = catalog;
        _luckTable = luckTable;
        _roomRng = roomRng;
        _slotCount = Mathf.Max(0, slotCount);
        _weaponSlotFallback = Mathf.Max(0, weaponSlotFallback);
        _rerollEnabled = rerollEnabled;
        _rerollCost = Mathf.Max(0, rerollCost);

        var (npcPos, npcRot) = ResolveNpcPlacement();
        BuildSlots(_roomRng);
        _peddler = AbyssPeddlerCatalog.Build(_roomRng);   // 심연의 행상 진열(방 시드 = 결정성 유지)
        SpawnNpc(npcPrefab, npcPos, npcRot);
        SpawnDecor(npcPos, npcRot);                       // 판매대(정면) + 뒤쪽 소품
        HookRunEvents();
        RefreshOwnership();

        _initialized = true;
        Debug.Log($"[ShopRoom] 초기화 완료(NPC+UI). 슬롯 {_slots.Count}개 / 매대(카운터) {_stallCategories.Count}개 / " +
                  $"luckTable={(luckTable != null ? "OK" : "null")} / rng={(roomRng != null ? "seeded" : "global")} / reroll={(_rerollEnabled ? $"on({_rerollCost}G)" : "off")}");
    }

    // ── 구매 (UI가 호출) ────────────────────────────────────

    public ShopPurchaseResult TryPurchaseSlot(int index)
    {
        if (index < 0 || index >= _slots.Count) return ShopPurchaseResult.Unavailable;
        if (_run == null || !_run.IsRunning) return ShopPurchaseResult.Unavailable;

        var slot = _slots[index];
        if (slot == null || !slot.Purchasable) return ShopPurchaseResult.Unavailable;

        var playerState = _run.PlayerState;
        if (playerState == null) return ShopPurchaseResult.Unavailable;

        if (slot.Service.HasValue)
            return PurchaseService(slot, slot.Service.Value);

        return slot.Entry != null
            ? PurchaseFromEntry(slot, slot.Entry, playerState)
            : PurchaseFromLegacy(slot, slot.LegacyItem, playerState);
    }

    /// <summary>심연의 행상 정규 상품 구매(인덱스). 골드 차감 → 지급 → 품절 처리.</summary>
    public ShopPurchaseResult TryBuyProduct(int index)
    {
        var list = _peddler?.Products;
        if (list == null || index < 0 || index >= list.Count) return ShopPurchaseResult.Unavailable;
        return BuyProduct(list[index]);
    }

    /// <summary>오늘의 특가 구매. 성공 시 특가는 사라진다(1회성).</summary>
    public ShopPurchaseResult TryBuySpecial()
    {
        var deal = _peddler?.Special;
        if (deal == null) return ShopPurchaseResult.Unavailable;

        var r = BuyProduct(deal);
        if (r == ShopPurchaseResult.Success) _peddler.Special = null;
        return r;
    }

    /// <summary>상품 1건 구매 공통 — 구매 가능/골드/지급/품절/저장/통지.</summary>
    private ShopPurchaseResult BuyProduct(ShopProduct p)
    {
        if (p == null || !p.Purchasable) return ShopPurchaseResult.Unavailable;
        if (_run == null || !_run.IsRunning) return ShopPurchaseResult.Unavailable;

        var playerState = _run.PlayerState;
        if (playerState == null) return ShopPurchaseResult.Unavailable;
        if (playerState.TempGold < p.Price) return ShopPurchaseResult.InsufficientGold;
        if (!playerState.TrySpendGold(p.Price)) return ShopPurchaseResult.InsufficientGold;

        bool granted = false;
        try { granted = p.Grant != null && p.Grant(_run); }
        catch (Exception e) { Debug.LogWarning($"[ShopRoom] 상품 지급 예외: {e.Message}"); }

        if (!granted)
        {
            playerState.AddTempGold(p.Price);   // 지급 실패 → 환불
            return ShopPurchaseResult.Failed;
        }

        p.Sold = true;
        RunFlowController.Active?.SaveNow("shop-product");
        OnShopChanged?.Invoke();
        return ShopPurchaseResult.Success;
    }

    /// <summary>정비소 서비스 구매 — ShopServiceRunner에 위임하고 결과를 상점 결과로 매핑한다.</summary>
    private ShopPurchaseResult PurchaseService(ShopSlot slot, ShopServiceKind kind)
    {
        var result = ShopServiceRunner.Execute(kind, _run);
        switch (result)
        {
            case ShopServiceResult.Success:
                // 서비스는 반복 구매 가능(품절 없음). 누진 가격 반영 위해 서비스 슬롯 가격만 갱신.
                RebuildServiceSlots();
                RunFlowController.Active?.SaveNow("shop-service");
                OnShopChanged?.Invoke();
                return ShopPurchaseResult.Success;
            case ShopServiceResult.InsufficientGold:
                return ShopPurchaseResult.InsufficientGold;
            case ShopServiceResult.NotImplemented:
            case ShopServiceResult.Unavailable:
            default:
                return ShopPurchaseResult.Unavailable;
        }
    }

    /// <summary>리롤. 비결정 RNG로 진열을 새로 롤. 피처 off거나 골드 부족이면 false.</summary>
    public bool TryReroll()
    {
        if (!_rerollEnabled) return false;
        var playerState = _run?.PlayerState;
        if (playerState == null) return false;

        if (_rerollCost > 0 && !playerState.TrySpendGold(_rerollCost))
        {
            Debug.Log($"[ShopRoom] 리롤 골드 부족: 필요 {_rerollCost}, 보유 {playerState.TempGold}");
            return false;
        }

        _rerollRng ??= new System.Random();
        BuildSlots(_rerollRng); // 의도적 비결정 — 이어하기 복원 대상 아님
        _peddler = AbyssPeddlerCatalog.Build(_rerollRng);   // 상품 돌리기 = 행상 진열도 새로 롤
        RefreshOwnership();
        OnShopChanged?.Invoke();
        Debug.Log($"[ShopRoom] 리롤 완료 ({_rerollCost}G 차감) — 슬롯 {_slots.Count}개 재생성");
        return true;
    }

    // ── NPC / UI ────────────────────────────────────────────

    private void SpawnNpc(GameObject npcPrefab, Vector3 pos, Quaternion rot)
    {
        if (npcPrefab == null)
        {
            Debug.LogWarning("[ShopRoom] NPC 프리팹 없음 — 상점 UI를 열 수 없습니다.");
            return;
        }

        _npcInstance = Instantiate(npcPrefab, pos, rot, transform);
        _npc = _npcInstance.GetComponent<ShopNpcInteraction>();
        if (_npc == null) _npc = _npcInstance.GetComponentInChildren<ShopNpcInteraction>(true);

        if (_npc != null)
            _npc.OnInteract += HandleNpcInteract;
        else
            Debug.LogWarning("[ShopRoom] NPC 프리팹에 ShopNpcInteraction 없음");

        // 주기적 월드스페이스 잡담 — 상인 컨셉.
        _npcInstance.AddComponent<NpcAmbientChatter>()
                    .Initialize(ShopChatterLines, 9f, new Color(1f, 0.9f, 0.6f));
    }

    /// <summary>Initialize 전에 호출 — [0]=NPC 앞 판매대, 나머지=뒤쪽 소품.</summary>
    public void SetDecorPrefabs(GameObject[] prefabs) => _decorPrefabs = prefabs;

    /// <summary>
    /// 상인 무대 구성 — 첫 소품을 NPC 정면 판매대로, 나머지는 뒤쪽 반원에 배치.
    /// 재련소·정제소와 동일 규약(카운터 뒤에 선 상인 구도).
    /// </summary>
    private void SpawnDecor(Vector3 npcPos, Quaternion npcRot)
    {
        if (_decorPrefabs == null || _decorPrefabs.Length == 0) return;

        ServiceRoomDecorPlacer.SyncPhysics();   // 갓 생성된 벽 콜라이더를 쿼리에 반영

        // grid_csv가 무대를 지정했으면(NC/NP 토큰) 그대로 쓴다. 없으면 아래 탐색 배치로 폴백.
        float anchorGroundY = npcPos.y - NpcStandHeight;
        if (ServiceRoomDecorPlacer.TryPlaceFromAnchors(transform, _decorPrefabs, npcPos, anchorGroundY, "ShopCounter"))
            return;

        var rng = _roomRng ?? new System.Random();
        Vector3 fwd   = npcRot * Vector3.forward;   // 플레이어 쪽
        Vector3 back  = -fwd;
        Vector3 right = npcRot * Vector3.right;
        float groundY = npcPos.y - NpcStandHeight;

        // 판매대 — NPC 정면. 벽이면 각도/거리를 조정해 빈 자리를 찾는다(못 찾으면 배치 생략).
        if (_decorPrefabs[0] != null &&
            ServiceRoomDecorPlacer.TryFindSpot(npcPos, fwd, CounterDistance, groundY, out var tablePos))
        {
            Vector3 faceBack = npcPos - tablePos;   // 카운터는 NPC를 마주본다
            float yaw = Mathf.Atan2(-faceBack.x, -faceBack.z) * Mathf.Rad2Deg;
            ServiceRoomDecorPlacer.Place(_decorPrefabs[0], tablePos, yaw, groundY, transform, "ShopCounter");
        }

        int n = _decorPrefabs.Length;
        for (int i = 1; i < n; i++)
        {
            var prefab = _decorPrefabs[i];
            if (prefab == null) continue;

            float t     = n > 2 ? (float)(i - 1) / (n - 2) : 0.5f;
            float ang   = Mathf.Lerp(-70f, 70f, t) * Mathf.Deg2Rad;
            float rad   = 3.5f + (float)rng.NextDouble() * 1.2f;
            Vector3 dir = back * Mathf.Cos(ang) + right * Mathf.Sin(ang);

            if (!ServiceRoomDecorPlacer.TryFindSpot(npcPos, dir, rad, groundY, out var pos)) continue;

            Vector3 toNpc = npcPos - pos;
            float yaw = Mathf.Atan2(-toNpc.x, -toNpc.z) * Mathf.Rad2Deg;
            ServiceRoomDecorPlacer.Place(prefab, pos, yaw, groundY, transform);
        }
    }

    private void HandleNpcInteract()
    {
        if (_uiOpen) return;
        OpenShopUIAsync().Forget();
    }

    private async UniTaskVoid OpenShopUIAsync()
    {
        _uiOpen = true;
        if (_npc != null) _npc.SetInteractable(false);

        var panel = await Managers.UI.ShowPopupUIAndGetAsync<UI_ShopPanel>();
        if (panel == null)
        {
            _uiOpen = false;
            if (_npc != null) _npc.SetInteractable(true);
            Debug.LogWarning("[ShopRoom] UI_ShopPanel 로드 실패");
            return;
        }
        panel.Bind(this);
    }

    /// <summary>UI_ShopPanel이 닫힐 때 호출.</summary>
    public void NotifyPanelClosed()
    {
        _uiOpen = false;
        if (_npc != null) _npc.SetInteractable(true);
    }

    // ── 슬롯 생성 ───────────────────────────────────────────

    /// <summary>NPC 스폰 위치/방향 결정 + 매대 마커 수집.
    /// 1순위: 커스텀 손맵 프리팹의 <see cref="ShopNpcAnchor"/>(디자이너 지정 위치·방향).
    /// 2순위: 매대 중심점. 3순위: 방 중앙. (절차 CSV 방엔 앵커가 없어 2/3으로 폴백)</summary>
    private (Vector3 pos, Quaternion rot) ResolveNpcPlacement()
    {
        CollectStalls(out Vector3 stallCenter, out bool hasStalls);

        var anchor = GetComponentInChildren<ShopNpcAnchor>(true);
        if (anchor != null)
            return (anchor.transform.position, anchor.transform.rotation);

        Vector3 pos = hasStalls ? stallCenter : transform.position;
        pos.y += NpcStandHeight; // 앵커가 정확한 높이를 주므로 폴백에서만 보정.

        // 매대 중심은 방 가장자리(벽)에 붙는 경우가 많다 → 고정 +Z가 아니라 가장 트인 쪽을 보게 한다.
        // (NPC가 벽을 보고 서거나, 카운터가 벽 안에 박히는 것을 방지)
        return (pos, ServiceRoomDecorPlacer.ResolveFacing(pos, Quaternion.identity));
    }

    /// <summary>매대(ShopStallInteraction) 마커를 수집해 슬롯 카테고리 소스로 쓰고, 중심점을 산출한다.
    /// 매대는 이제 보이는 카운터로 유지한다(숨기지 않음). 과거 월드 구매 상태연출(SOLD/VFX)만 끄고,
    /// 트리거 콜라이더는 솔리드 카운터로 전환한다(플레이어 통과 방지) — 레거시 정리.</summary>
    private void CollectStalls(out Vector3 center, out bool hasStalls)
    {
        _stallCategories.Clear();
        var stalls = new List<ShopStallInteraction>();
        GetComponentsInChildren<ShopStallInteraction>(true, stalls);

        Vector3 sum = Vector3.zero;
        int count = 0;
        foreach (var stall in stalls)
        {
            if (stall == null) continue;
            _stallCategories.Add(stall.Category);
            sum += stall.transform.position;
            count++;

            DisableDeadStallVisuals(stall.gameObject);
            if (stall.TryGetComponent<Collider>(out var col)) col.isTrigger = false;
        }

        hasStalls = count > 0;
        center = hasStalls ? sum / count : transform.position;
    }

    /// <summary>매대 프리팹(Block_ShopStall)에 남은 과거 상태연출 자식만 비활성화. 카운터 본체는 유지.</summary>
    private static void DisableDeadStallVisuals(GameObject stall)
    {
        var all = stall.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
            if (System.Array.IndexOf(DeadStallChildren, all[i].name) >= 0)
                all[i].gameObject.SetActive(false);
    }

    /// <summary>슬롯 카테고리 레이아웃. 매대가 있으면 그 카테고리를, 없으면 slotCount+무기폴백.</summary>
    private List<ShopCategory> BuildCategoryLayout()
    {
        // 상점 판매 = 포션만(BuildSlots가 고정 슬롯으로 추가). 무기·아이템 판매는 폐기.
        //   · 무기: 무형검 기본 지급 + 재련소 강화/진화로 진행(완제품 무기 구매 안 함)
        //   · 아이템: 드롭 + 정제로 획득
        // 매대 카테고리(_stallCategories)·슬롯수(_slotCount)는 더 이상 진열에 쓰지 않는다.
        // 남은 무기 판매 코드(ProcessWeaponAcquisitionAsync 등)는 데이터가 없어 도달 불가(정리 대상).
        return new List<ShopCategory>();
    }

    private void BuildSlots(System.Random rng)
    {
        _slots.Clear();

        bool useChartFlow = Managers.ShopData != null
                            && Managers.ShopData.IsInitialized
                            && _luckTable != null;

        if (useChartFlow)
            BuildSlotsFromChart(rng);
        else
            BuildSlotsFromCatalog();

        AppendPotionSlot();     // 포션은 랜덤 롤이 아니라 항상 있는 고정 슬롯
        AppendServiceSlots();   // 정비소 서비스(룬 제거·정수·서약·HP…) 고정 진열
    }

    /// <summary>정비소 서비스 슬롯 진열(항상 노출). 미구현 서비스는 '준비 중'(Locked)으로 표시만.</summary>
    private void AppendServiceSlots()
    {
        foreach (var def in ShopServiceCatalog.All)
        {
            int price   = ShopServiceRunner.GetPrice(def.Kind, _run);
            // 준비 중(미구현) 또는 조건 미충족(뺄 룬 없음 등)이면 잠금 표시.
            bool locked = !def.Implemented || !ShopServiceRunner.IsAvailable(def.Kind, _run);
            _slots.Add(ShopSlot.ForService(def.Kind, price, def.DisplayName, def.Description, locked));
        }
    }

    /// <summary>서비스 슬롯만 제거 후 재진열(누진 가격·조건 변화 반영). 아이템/무기/포션 슬롯은 유지.</summary>
    private void RebuildServiceSlots()
    {
        _slots.RemoveAll(s => s != null && s.Service.HasValue);
        AppendServiceSlots();
    }

    /// <summary>
    /// 체력 포션 슬롯을 진열 맨 뒤에 고정으로 붙인다(구매 재고 없음 개념 — 골드만 있으면 반복 구매 가능).
    /// SHOP_DATA의 category=potion 엔트리(price_override로 가격)를 GetById로 찾는다. 없으면 붙이지 않음.
    /// </summary>
    private void AppendPotionSlot()
    {
        var shopData = Managers.ShopData;
        if (shopData == null || !shopData.IsInitialized) return;

        var entry = shopData.GetById(PotionEntryId);
        if (entry == null) return;   // CSV에 포션 엔트리 없으면 스킵

        int price = shopData.ResolvePrice(entry);   // price_override 우선
        _slots.Add(new ShopSlot(entry, null, ShopCategory.Potion, price,
                                "체력 포션", ItemRarity.Common, null,
                                "즉시 최대 체력의 40%를 회복한다. 퀵슬롯(H)에 충전."));
    }

    private void BuildSlotsFromChart(System.Random rng)
    {
        var layout = BuildCategoryLayout();
        var usedTargetIds = new HashSet<string>();
        int luck = ResolvePlayerLuck();
        int filled = 0;

        foreach (var cat in layout)
        {
            var entry = TryRollEntry(cat, luck, usedTargetIds, rng);
            if (entry != null)
            {
                usedTargetIds.Add(entry.target_id);
                _slots.Add(BuildSlotFromEntry(entry, cat));
                filled++;
            }
            else
            {
                _slots.Add(ShopSlot.Empty(cat));
            }
        }

        Debug.Log($"[ShopRoom] Chart 진열 완료: 채워진 슬롯 {filled}/{_slots.Count} (luck={luck})");
    }

    private void BuildSlotsFromCatalog()
    {
        Debug.LogWarning("[ShopRoom] Chart 기반 추첨 불가 — ShopCatalogSO 폴백 사용");

        var layout = BuildCategoryLayout();
        int distributeCount = layout.Count > 0 ? layout.Count : Mathf.Max(0, _slotCount);
        List<ShopItemSO> picks = _catalog != null
            ? _catalog.PickRandom(distributeCount)
            : new List<ShopItemSO>();

        for (int i = 0; i < distributeCount; i++)
        {
            if (i < picks.Count && picks[i] != null && picks[i].Item != null)
                _slots.Add(BuildSlotFromLegacy(picks[i]));
            else
                _slots.Add(ShopSlot.Empty(ShopCategory.Item));
        }
    }

    private ShopSlot BuildSlotFromEntry(ShopEntry entry, ShopCategory cat)
    {
        int price = Managers.ShopData != null
            ? Managers.ShopData.ResolvePrice(entry)
            : Mathf.Max(0, entry.price_override);

        string name = entry.target_id;
        ItemRarity rarity = ItemRarity.Common;
        Sprite icon = null;

        if (cat == ShopCategory.Weapon)
        {
            var equip = Managers.ServerEquipment?.GetById(entry.target_id);
            if (equip != null)
            {
                if (!string.IsNullOrEmpty(equip.weapon_name)) name = equip.weapon_name;
                Enum.TryParse(equip.rarity, ignoreCase: true, out rarity);
            }
        }
        else
        {
            var itemSO = ItemSORegistry.Find(entry.target_id);
            if (itemSO != null)
            {
                if (!string.IsNullOrEmpty(itemSO.displayName)) name = itemSO.displayName;
                rarity = itemSO.rarity;
                icon = itemSO.icon;
            }
        }

        return new ShopSlot(entry, null, cat, price, name, rarity, icon, BuildDescription(cat, rarity));
    }

    private ShopSlot BuildSlotFromLegacy(ShopItemSO shopItem)
    {
        var so = shopItem.Item;
        string name = string.IsNullOrEmpty(so.displayName) ? so.itemId : so.displayName;
        return new ShopSlot(null, shopItem, ShopCategory.Item, shopItem.Price,
                            name, so.rarity, so.icon, BuildDescription(ShopCategory.Item, so.rarity));
    }

    private static string BuildDescription(ShopCategory cat, ItemRarity rarity)
    {
        string r = rarity switch
        {
            ItemRarity.Rare => "레어",
            ItemRarity.Epic => "에픽",
            ItemRarity.Legendary => "전설",
            _ => "일반",
        };
        return cat == ShopCategory.Weapon ? $"{r} 무기" : $"{r} 아이템";
    }

    // ── 등급 롤 (기존 로직 재사용) ──────────────────────────

    private ShopEntry TryRollEntry(ShopCategory cat, int luck, HashSet<string> used, System.Random rng)
    {
        var rarity = LuckRollService.RollRarity(luck, _luckTable, rng);
        string catStr = cat.ToChartString();

        for (int attempt = 0; attempt < MaxRarityFallbackAttempts; attempt++)
        {
            var pool = Managers.ShopData.GetPool(catStr, rarity);
            var filtered = FilterPool(pool, used);

            if (filtered.Count > 0)
                return WeightedPick(filtered, rng);

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

    /// <summary>weight 기반 가중 추첨. rng가 있으면 그 소스로 추첨(없으면 전역 Random).</summary>
    private static ShopEntry WeightedPick(List<ShopEntry> pool, System.Random rng)
    {
        int total = 0;
        for (int i = 0; i < pool.Count; i++) total += Mathf.Max(0, pool[i].weight);
        if (total <= 0) return pool.Count > 0 ? pool[0] : null;

        int roll = rng != null ? rng.Next(0, total) : UnityEngine.Random.Range(0, total);
        int acc = 0;
        for (int i = 0; i < pool.Count; i++)
        {
            acc += Mathf.Max(0, pool[i].weight);
            if (roll < acc) return pool[i];
        }
        return pool[pool.Count - 1];
    }

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

    // ── 구매 처리 (기존 로직 재사용) ────────────────────────

    private ShopPurchaseResult PurchaseFromEntry(ShopSlot slot, ShopEntry entry, PlayerRunState playerState)
    {
        if (IsAlreadyOwned(entry))
        {
            slot.Owned = true;
            OnShopChanged?.Invoke();
            return ShopPurchaseResult.Unavailable;
        }

        var cat = ShopCategoryExtensions.FromChartString(entry.category);

        // 포션 만재면 구매 자체를 막는다(골드 차감 전 — 낭비 방지). 재고 없음으로 표시.
        if (cat == ShopCategory.Potion && playerState.PotionCount >= playerState.PotionCapacity)
            return ShopPurchaseResult.Unavailable;

        int price = slot.Price;
        if (!playerState.TrySpendGold(price))
        {
            Debug.Log($"[ShopRoom] 골드 부족: 필요 {price}, 보유 {playerState.TempGold}");
            return ShopPurchaseResult.InsufficientGold;
        }

        if (cat == ShopCategory.Potion)
        {
            playerState.AddPotion(1);
            slot.Sold = true;   // 한 번 진열당 1개(반복 구매하려면 재진열/리롤)
            RunFlowController.Active?.SaveNow("shop-purchase");
            OnShopChanged?.Invoke();
            return ShopPurchaseResult.Success;
        }

        if (cat == ShopCategory.Weapon)
        {
            var wm = _run.Player?.WeaponManager;
            if (wm == null)
            {
                Debug.LogWarning("[ShopRoom] WeaponManager 없음 — 무기 구매 실패");
                playerState.AddTempGold(price); // 차감 환불(GoldGainRate 미적용 우회)
                return ShopPurchaseResult.Failed;
            }
            string addressableKey = ResolveWeaponAddressableKey(entry.target_id);
            if (string.IsNullOrEmpty(addressableKey))
            {
                Debug.LogWarning($"[ShopRoom] weapon prefab key 조회 실패: {entry.target_id}");
                playerState.AddTempGold(price);
                return ShopPurchaseResult.Failed;
            }

            // 비동기 무기 획득 + 교체 팝업. SOLD 확정은 획득 성공 이후로 미룬다.
            slot.Pending = true;
            OnShopChanged?.Invoke();
            ProcessWeaponAcquisitionAsync(slot, wm, addressableKey, price, entry.target_id).Forget();
            return ShopPurchaseResult.PendingAsync;
        }

        // Item
        var itemSO = ItemSORegistry.Find(entry.target_id);
        if (itemSO == null)
        {
            Debug.LogWarning($"[ShopRoom] ItemSO 미등록: {entry.target_id}");
            playerState.AddTempGold(price);
            return ShopPurchaseResult.Failed;
        }
        var runtimeItem = RuntimeItemData.FromSO(itemSO);
        if (runtimeItem == null)
        {
            Debug.LogWarning($"[ShopRoom] RuntimeItemData 생성 실패: {entry.target_id}");
            playerState.AddTempGold(price);
            return ShopPurchaseResult.Failed;
        }
        bool added = _run.ItemInventory != null && _run.ItemInventory.AddToStaging(runtimeItem);
        if (!added)
        {
            Debug.Log($"[ShopRoom] 인벤토리 가득 참 — 환불: {entry.target_id}");
            playerState.AddTempGold(price);
            return ShopPurchaseResult.Failed;
        }
        Debug.Log($"[ShopRoom] 아이템 구매 성공: {entry.target_id} ({price}G)");
        slot.Sold = true;
        RunFlowController.Active?.SaveNow("shop-purchase");   // S3: 구매 확정 → 즉시 저장
        OnShopChanged?.Invoke();
        return ShopPurchaseResult.Success;
    }

    private async UniTaskVoid ProcessWeaponAcquisitionAsync(ShopSlot slot, PlayerWeaponManager wm, string addressableKey, int price, string targetIdForLog)
    {
        var ct = this.GetCancellationTokenOnDestroy();
        try
        {
            bool acquired = await wm.TryAcquireWeaponWithReplaceAsync(addressableKey, ct);
            if (!acquired)
            {
                Debug.Log($"[ShopRoom] 무기 구매 취소/실패 — 환불: {targetIdForLog} ({price}G)");
                _run?.PlayerState?.AddTempGold(price);
                slot.Pending = false;
                OnShopChanged?.Invoke();
                return;
            }
            Debug.Log($"[ShopRoom] 무기 구매 성공: {targetIdForLog} ({price}G)");

            slot.Pending = false;
            slot.Sold = true;
            RunFlowController.Active?.SaveNow("shop-purchase");   // S3: 구매 확정 → 즉시 저장
            OnShopChanged?.Invoke();

            // 다음 방에서 장비 유지되도록 세션에 즉시 저장
            var slotData = new WeaponData[wm.SlotCount];
            for (int i = 0; i < wm.SlotCount; i++)
                slotData[i] = wm.slots[i]?.runtimeData;
            _run.SaveWeaponSlots(slotData, wm.CurrentSlotIndex);
        }
        catch (OperationCanceledException)
        {
            _run?.PlayerState?.AddTempGold(price);
            slot.Pending = false;
            // 룸 파괴 중일 수 있으므로 이벤트는 안전 호출
            OnShopChanged?.Invoke();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ShopRoom] 무기 구매 처리 중 예외 — 환불: {ex.Message}");
            _run?.PlayerState?.AddTempGold(price);
            slot.Pending = false;
            OnShopChanged?.Invoke();
        }
    }

    private ShopPurchaseResult PurchaseFromLegacy(ShopSlot slot, ShopItemSO shopItem, PlayerRunState playerState)
    {
        if (shopItem == null || shopItem.Item == null) return ShopPurchaseResult.Unavailable;

        int price = slot.Price;
        if (!playerState.TrySpendGold(price))
        {
            Debug.Log($"[ShopRoom] 골드 부족: 필요 {price}, 보유 {playerState.TempGold}");
            return ShopPurchaseResult.InsufficientGold;
        }

        var runtimeItem = RuntimeItemData.FromSO(shopItem.Item);
        if (runtimeItem == null)
        {
            Debug.LogWarning($"[ShopRoom] (레거시) RuntimeItemData 생성 실패: {shopItem.Item?.itemId}");
            playerState.AddTempGold(price);
            return ShopPurchaseResult.Failed;
        }

        bool added = _run.ItemInventory != null && _run.ItemInventory.AddToStaging(runtimeItem);
        if (!added)
        {
            Debug.Log($"[ShopRoom] (레거시) 인벤토리 가득 참 — 환불: {shopItem.Item.itemId}");
            playerState.AddTempGold(price);
            return ShopPurchaseResult.Failed;
        }
        Debug.Log($"[ShopRoom] (레거시) 구매 성공: {shopItem.Item.itemId} ({price}G)");
        slot.Sold = true;
        RunFlowController.Active?.SaveNow("shop-purchase");   // S3: 구매 확정 → 즉시 저장
        OnShopChanged?.Invoke();
        return ShopPurchaseResult.Success;
    }

    private static string ResolveWeaponAddressableKey(string weaponId)
    {
        if (string.IsNullOrEmpty(weaponId)) return null;
        return weaponId;
    }

    // ── 인벤토리/무기/골드 변동 이벤트 ──────────────────────

    private void HookRunEvents()
    {
        if (_eventsHooked || _run == null) return;

        if (_run.ItemInventory != null)
            _run.ItemInventory.OnStagingChanged += OnInventoryChanged;

        if (_run.PlayerState != null)
        {
            _run.PlayerState.OnGoldChanged += OnGoldChanged;
            _goldHooked = true;
        }

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

        if (_goldHooked && _run?.PlayerState != null)
            _run.PlayerState.OnGoldChanged -= OnGoldChanged;
        _goldHooked = false;

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
        RefreshOwnership();
        OnShopChanged?.Invoke();
    }

    private void OnInventoryChanged()
    {
        RefreshOwnership();
        OnShopChanged?.Invoke();
    }

    private void OnWeaponChanged(WeaponData _, GameObject __)
    {
        RefreshOwnership();
        OnShopChanged?.Invoke();
    }

    private void OnGoldChanged(int _) => OnShopChanged?.Invoke();

    private void RefreshOwnership()
    {
        for (int i = 0; i < _slots.Count; i++)
        {
            var slot = _slots[i];
            if (slot == null || slot.Sold) continue;
            slot.Owned = slot.Entry != null && IsAlreadyOwned(slot.Entry);
        }
    }

}
