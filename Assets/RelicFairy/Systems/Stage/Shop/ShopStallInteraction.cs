using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>
/// 상점 진열대 1개의 상호작용 컴포넌트.
/// 플레이어가 트리거 영역 안에서 F키를 누르면 ShopRoomController에 구매 요청.
///
/// 카테고리(Item/Weapon)는 디자이너가 인스펙터에서 매대마다 지정.
/// 상품 할당은 두 경로 중 하나로 들어온다:
///   · Bind(ShopEntry)   — 신규 경로. SHOP_PRICE_DATA + LuckRollTable 기반 추첨 결과.
///   · Bind(ShopItemSO)  — 레거시 경로. ShopCatalogSO 폴백용.
///
/// 인벤토리/무기 상태가 바뀌면 매대도 "보유 중"으로 표시되며 F키 입력이 차단된다.
/// </summary>
public class ShopStallInteraction : MonoBehaviour
{
    // ── 상수 ────────────────────────────────────────────────
    private const float PromptOffsetY = 1.4f;
    private const string OwnedHexColor = "#888888";

    // ── [SerializeField] ────────────────────────────────────
    [Header("매대 설정")]
    [Tooltip("이 매대에서 판매되는 카테고리. 디자이너가 매대마다 지정.")]
    [SerializeField] private ShopCategory category = ShopCategory.Item;

    [Tooltip("무기 시각 자식 루트 (있으면 카테고리=Weapon 일 때만 활성화).")]
    [SerializeField] private GameObject weaponDisplayRoot;

    [Tooltip("아이템 시각 자식 루트 (있으면 카테고리=Item 일 때만 활성화).")]
    [SerializeField] private GameObject itemDisplayRoot;

    [Tooltip("이미 보유/SOLD OUT 상태에서 표시할 라벨. 비어있으면 자동 생성된 프롬프트 텍스트로 표시.")]
    [SerializeField] private TextMeshPro soldOutLabel;

    [Header("등급 시각")]
    [Tooltip("아이템 등급별 VFX SO. 비어있으면 폴백 키 사용 (VFX_Item_<Rarity>).")]
    [SerializeField] private ItemVfxConfig itemVfxConfig;

    [Tooltip("아이템 VFX/무기 모델 호스트 트랜스폼. null이면 itemDisplayRoot/weaponDisplayRoot, 그것도 없으면 자기 자신.")]
    [SerializeField] private Transform displayVfxRoot;

    [Tooltip("호스트 부모 기준 Y 오프셋 (월드 픽업 디스플레이의 vfxYOffset 대응).")]
    [SerializeField] private float displayYOffset = -0.3f;

    // ── 비공개 필드 ─────────────────────────────────────────
    private ShopItemSO _legacyItem;     // ShopCatalogSO 폴백 경로 전용
    private ShopEntry  _entry;          // 신규 경로
    private bool _sold;
    private bool _owned;
    private bool _playerInRange;
    private GameObject _promptGo;
    private TextMeshPro _promptText;

    // 현재 매대에 뜬 시각 객체 + 비동기 취소
    private GameObject _currentDisplayInstance;
    private bool _currentDisplayIsAddressableInstance; // true면 ReleaseInstance, false면 Destroy
    private CancellationTokenSource _visualCts;

    // ── Properties ──────────────────────────────────────────
    /// <summary>레거시 경로: ShopCatalogSO 기반 상품 (ShopEntry 사용 시 null).</summary>
    public ShopItemSO Item => _legacyItem;

    /// <summary>신규 경로: SHOP_PRICE_DATA 기반 entry (ShopItemSO 사용 시 null).</summary>
    public ShopEntry Entry => _entry;

    /// <summary>이 매대의 카테고리 (디자이너 지정 또는 MapBuilder가 TileType 기반으로 주입).</summary>
    public ShopCategory Category => category;

    /// <summary>
    /// MapBuilder가 TileType(ShopStallWeapon/ShopStallItem)에 따라 카테고리를 주입할 때 사용.
    /// 매대 시각(weaponDisplayRoot/itemDisplayRoot)도 즉시 갱신.
    /// </summary>
    public void SetCategory(ShopCategory value)
    {
        category = value;
        ApplyCategoryVisual();
    }

    /// <summary>SOLD 또는 보유 중이어서 입력 받지 않는 상태.</summary>
    public bool IsSold => _sold;

    /// <summary>현재 보유 여부 (인벤토리/무기 매니저 기준 실시간 평가). MarkSold와 별개.</summary>
    public bool IsOwned => _owned;

    /// <summary>구매 요청 시 호출. bool 반환값은 구매 성공 여부.</summary>
    public event Func<ShopStallInteraction, bool> OnPurchaseRequested;

    // ── Lifecycle ───────────────────────────────────────────

    private void Update()
    {
        if (_promptGo != null && _promptGo.activeSelf && Camera.main != null)
            _promptGo.transform.rotation = Camera.main.transform.rotation;

        if (_sold || _owned || !_playerInRange) return;

        if (Input.GetKeyDown(KeyCode.F))
            TryPurchase();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_sold) return;
        if (!IsPlayer(other)) return;

        _playerInRange = true;
        ShowPrompt(true);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsPlayer(other)) return;
        _playerInRange = false;
        ShowPrompt(false);
    }

    private void OnDestroy()
    {
        if (_promptGo != null)
            Destroy(_promptGo);

        if (_visualCts != null)
        {
            _visualCts.Cancel();
            _visualCts.Dispose();
            _visualCts = null;
        }
        ClearDisplayInstance();
    }

    // ── Public Methods ──────────────────────────────────────

    /// <summary>신규 경로: SHOP_PRICE_DATA에서 추첨된 entry를 주입.</summary>
    public void Bind(ShopEntry entry)
    {
        _entry = entry;
        _legacyItem = null;
        _sold = false;
        _owned = false;
        ApplyCategoryVisual();
        RefreshPromptText();
        ApplyDisplayAsync().Forget();
    }

    /// <summary>레거시 경로: ShopCatalogSO 기반 상품 주입 (폴백용).</summary>
    public void Bind(ShopItemSO item)
    {
        _legacyItem = item;
        _entry = null;
        _sold = false;
        _owned = false;
        ApplyCategoryVisual();
        RefreshPromptText();
        ApplyDisplayAsync().Forget();
    }

    /// <summary>ShopRoomController가 강등 fallback도 실패해 빈 매대 처리할 때 호출.</summary>
    public void BindEmpty()
    {
        _legacyItem = null;
        _entry = null;
        _sold = false;
        _owned = false;
        ApplyCategoryVisual();
        RefreshPromptText();
        ClearDisplayInstance();
    }

    /// <summary>구매 성공 처리 (외부에서 강제 마킹할 때 사용).</summary>
    public void MarkSold()
    {
        if (_sold) return;
        _sold = true;
        ShowPrompt(false);
        ApplySoldVisual();
        UpdateSoldOutLabel();
        ClearDisplayInstance();
    }

    /// <summary>
    /// 인벤토리/무기 상태 기준으로 매대를 재평가.
    /// ShopRoomController가 OnInventoryChanged/OnWeaponChanged 이벤트에 반응해 호출한다.
    /// </summary>
    public void SetOwnedState(bool isOwned)
    {
        if (_sold) return; // 이미 구매한 매대는 보유 상태와 무관

        bool changed = _owned != isOwned;
        _owned = isOwned;
        if (!changed) return;

        UpdateSoldOutLabel();
        RefreshPromptText();
        if (_owned) ClearDisplayInstance();
    }

    // ── Private Methods ─────────────────────────────────────

    private void TryPurchase()
    {
        if (_sold || _owned) return;
        if (_entry == null && _legacyItem == null) return;

        bool success = OnPurchaseRequested?.Invoke(this) ?? false;
        if (success)
            MarkSold();
    }

    private void ApplyCategoryVisual()
    {
        if (weaponDisplayRoot != null)
            weaponDisplayRoot.SetActive(category == ShopCategory.Weapon);
        if (itemDisplayRoot != null)
            itemDisplayRoot.SetActive(category == ShopCategory.Item);
    }

    private void ApplySoldVisual()
    {
        var renderers = GetComponentsInChildren<Renderer>();
        foreach (var r in renderers)
        {
            var mat = r.material;
            if (mat.HasProperty("_Color"))
            {
                var c = mat.color;
                c.a = 0.3f;
                mat.color = c;
            }
        }
    }

    private void UpdateSoldOutLabel()
    {
        if (soldOutLabel == null) return;
        soldOutLabel.gameObject.SetActive(_sold || _owned);
        if (_sold) soldOutLabel.text = "SOLD OUT";
        else if (_owned) soldOutLabel.text = "보유 중";
    }

    // ── 월드 프롬프트 ([F] 구매) ────────────────────────────

    private void ShowPrompt(bool show)
    {
        if (show && _promptGo == null)
            CreatePrompt();

        if (_promptGo != null)
            _promptGo.SetActive(show && !_sold);
    }

    private void CreatePrompt()
    {
        _promptGo = new GameObject("ShopStallPrompt");
        _promptGo.transform.SetParent(transform, false);
        _promptGo.transform.localPosition = Vector3.up * PromptOffsetY;

        _promptText = _promptGo.AddComponent<TextMeshPro>();
        _promptText.fontSize = 4f;
        _promptText.alignment = TextAlignmentOptions.Center;
        _promptText.color = Color.white;
        _promptText.enableWordWrapping = false;

        var rect = _promptGo.GetComponent<RectTransform>();
        if (rect != null) rect.sizeDelta = new Vector2(5f, 1.4f);

        TMPOutlineHelper.ApplyDefault(_promptText);

        RefreshPromptText();
        _promptGo.SetActive(false);
    }

    private void RefreshPromptText()
    {
        if (_promptText == null) return;

        // 1) 신규 경로 (ShopEntry)
        if (_entry != null)
        {
            BuildPromptFromEntry();
            return;
        }

        // 2) 레거시 경로 (ShopItemSO)
        if (_legacyItem != null && _legacyItem.Item != null)
        {
            BuildPromptFromLegacyItem();
            return;
        }

        // 3) 빈 매대
        _promptText.text = $"<color={OwnedHexColor}>비어있음</color>";
    }

    private void BuildPromptFromEntry()
    {
        string displayName;
        ItemRarity rarity;
        if (!TryResolveEntryDisplay(_entry, out displayName, out rarity))
        {
            _promptText.text = $"<color={OwnedHexColor}>알 수 없는 상품</color>";
            return;
        }

        int price = Managers.ShopData != null
            ? Managers.ShopData.ResolvePrice(_entry)
            : Mathf.Max(0, _entry.price_override);
        string colorHex = "#" + RarityColorTable.GetHex(rarity);

        if (_owned)
        {
            _promptText.text =
                $"<color={colorHex}>{displayName}</color>\n" +
                $"<color={OwnedHexColor}>보유 중</color>";
            return;
        }

        _promptText.text =
            $"<color={colorHex}>{displayName}</color>\n" +
            $"<color=#FFCC00>{price}G</color>\n" +
            $"<color=#FFD700>[F]</color> 구매";
    }

    private void BuildPromptFromLegacyItem()
    {
        var so = _legacyItem.Item;
        string name = string.IsNullOrEmpty(so.displayName) ? so.itemId : so.displayName;
        string colorHex = "#" + RarityColorTable.GetHex(so.rarity);

        if (_owned)
        {
            _promptText.text =
                $"<color={colorHex}>{name}</color>\n" +
                $"<color={OwnedHexColor}>보유 중</color>";
            return;
        }

        _promptText.text =
            $"<color={colorHex}>{name}</color>\n" +
            $"<color=#FFCC00>{_legacyItem.Price}G</color>\n" +
            $"<color=#FFD700>[F]</color> 구매";
    }

    /// <summary>ShopEntry → 표시 이름/등급 조회. 성공 시 true.</summary>
    private static bool TryResolveEntryDisplay(ShopEntry entry, out string displayName, out ItemRarity rarity)
    {
        displayName = null;
        rarity = ItemRarity.Common;
        if (entry == null || string.IsNullOrEmpty(entry.target_id)) return false;

        var cat = ShopCategoryExtensions.FromChartString(entry.category);
        if (cat == ShopCategory.Weapon)
        {
            var equipMgr = Managers.ServerEquipment;
            if (equipMgr == null) return false;
            var equip = equipMgr.GetById(entry.target_id);
            if (equip == null) return false;
            displayName = !string.IsNullOrEmpty(equip.weapon_name) ? equip.weapon_name : entry.target_id;
            Enum.TryParse(equip.rarity, ignoreCase: true, out rarity);
            return true;
        }

        // Item
        var itemSO = ItemSORegistry.Find(entry.target_id);
        if (itemSO == null)
        {
            displayName = entry.target_id;
            return true; // 이름은 id로라도 표시
        }
        displayName = !string.IsNullOrEmpty(itemSO.displayName) ? itemSO.displayName : itemSO.itemId;
        rarity = itemSO.rarity;
        return true;
    }

    private static bool IsPlayer(Collider col)
    {
        return col.GetComponentInParent<PlayerController>() != null;
    }

    // ── 등급 시각 (무기 모델 / 아이템 VFX) ──────────────────

    private const string FallbackVfxKeyCommon    = "VFX_Item_Common";
    private const string FallbackVfxKeyRare      = "VFX_Item_Rare";
    private const string FallbackVfxKeyEpic      = "VFX_Item_Epic";
    private const string FallbackVfxKeyLegendary = "VFX_Item_Legendary";

    /// <summary>
    /// Bind 직후 호출. 카테고리에 따라 무기 모델 또는 아이템 VFX를 비동기 로드해 매대 위에 띄운다.
    /// </summary>
    private async UniTaskVoid ApplyDisplayAsync()
    {
        ClearDisplayInstance();

        // 빈 매대거나 보유/판매 상태면 시각 표시 X
        if (_entry == null && _legacyItem == null) return;
        if (_owned || _sold) return;

        if (_visualCts != null)
        {
            _visualCts.Cancel();
            _visualCts.Dispose();
        }
        _visualCts = new CancellationTokenSource();
        var ct = _visualCts.Token;

        try
        {
            if (_entry != null)
            {
                var cat = ShopCategoryExtensions.FromChartString(_entry.category);
                if (cat == ShopCategory.Weapon || category == ShopCategory.Weapon)
                    await SpawnWeaponDisplayFromEntryAsync(_entry, ct);
                else
                    await SpawnItemDisplayFromEntryAsync(_entry, ct);
                return;
            }

            // 레거시 경로 — ShopItemSO는 항상 Item 카테고리
            if (_legacyItem != null && _legacyItem.Item != null)
                await SpawnItemDisplayFromRarityAsync(_legacyItem.Item.rarity, ct);
        }
        catch (OperationCanceledException)
        {
            // 매대 재바인딩으로 인한 정상 취소 — swallow
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ShopStall] 시각 적용 실패: {e.Message}");
        }
    }

    private async UniTask SpawnWeaponDisplayFromEntryAsync(ShopEntry entry, CancellationToken ct)
    {
        if (entry == null || string.IsNullOrEmpty(entry.target_id)) return;

        var equipMgr = Managers.ServerEquipment;
        if (equipMgr == null) return;

        var equip = equipMgr.GetById(entry.target_id);
        if (equip == null) return;

        string displayKey = equip.weapon_display_key;
        if (string.IsNullOrEmpty(displayKey)) return;

        var parent = ResolveDisplayParent(ShopCategory.Weapon);
        if (parent == null) return;

        // 키 존재 사전 검증 — Addressable 카탈로그에 등록 안 된 키면 InstantiateAsync 호출 자체를 생략
        var locHandle = Addressables.LoadResourceLocationsAsync(displayKey, typeof(GameObject));
        await locHandle.Task.AsUniTask().AttachExternalCancellation(ct);
        bool hasLocation = locHandle.Status == AsyncOperationStatus.Succeeded
                           && locHandle.Result != null && locHandle.Result.Count > 0;
        Addressables.Release(locHandle);

        if (!hasLocation)
        {
            Debug.LogWarning($"[ShopStall] 무기 디스플레이 키 미등록 → 매대 시각 생략: {displayKey}");
            return;
        }

        // WorldWeaponDisplay와 동일하게 Addressables.InstantiateAsync 사용 → ReleaseInstance로 해제
        UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<GameObject> handle;
        try
        {
            handle = Addressables.InstantiateAsync(displayKey, parent.position, parent.rotation, parent);
            await handle.Task.AsUniTask().AttachExternalCancellation(ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e)
        {
            Debug.LogWarning($"[ShopStall] 무기 InstantiateAsync 실패 ({displayKey}): {e.Message}");
            return;
        }

        if (this == null || gameObject == null)
        {
            // 매대가 destroy됐다면 인스턴스만 정리
            if (handle.Status == AsyncOperationStatus.Succeeded && handle.Result != null)
                Addressables.ReleaseInstance(handle);
            return;
        }

        if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
        {
            Debug.LogWarning($"[ShopStall] 무기 모델 로드 실패: {displayKey}");
            return;
        }

        _currentDisplayInstance = handle.Result;
        _currentDisplayIsAddressableInstance = true;

        var pos = _currentDisplayInstance.transform.localPosition;
        pos.y += displayYOffset;
        _currentDisplayInstance.transform.localPosition = pos;
    }

    private async UniTask SpawnItemDisplayFromEntryAsync(ShopEntry entry, CancellationToken ct)
    {
        if (entry == null || string.IsNullOrEmpty(entry.target_id)) return;

        var itemSO = ItemSORegistry.Find(entry.target_id);
        ItemRarity rarity = itemSO != null ? itemSO.rarity : ItemRarity.Common;

        await SpawnItemDisplayFromRarityAsync(rarity, ct);
    }

    private async UniTask SpawnItemDisplayFromRarityAsync(ItemRarity rarity, CancellationToken ct)
    {
        string vfxKey = itemVfxConfig != null
            ? itemVfxConfig.GetDisplayVfxKey(rarity)
            : GetFallbackVfxKey(rarity);

        if (string.IsNullOrEmpty(vfxKey)) return;

        var parent = ResolveDisplayParent(ShopCategory.Item);
        if (parent == null) return;

        // 아이템 VFX는 LoadAsset + Instantiate (캐시 활용 + 일반 Destroy로 해제)
        GameObject prefab;
        try
        {
            prefab = await Managers.AddressableManager
                .LoadAssetAsync<GameObject>(vfxKey)
                .AttachExternalCancellation(ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e)
        {
            Debug.LogWarning($"[ShopStall] 아이템 VFX 로드 실패 ({vfxKey}): {e.Message}");
            return;
        }

        if (prefab == null || this == null || gameObject == null) return;

        _currentDisplayInstance = Instantiate(prefab, parent);
        _currentDisplayIsAddressableInstance = false;

        var pos = _currentDisplayInstance.transform.localPosition;
        pos.y += displayYOffset;
        _currentDisplayInstance.transform.localPosition = pos;
    }

    /// <summary>
    /// 시각 부착 부모 결정. displayVfxRoot 우선, 없으면 카테고리별 기본 루트, 최종 폴백은 transform.
    /// </summary>
    private Transform ResolveDisplayParent(ShopCategory cat)
    {
        if (displayVfxRoot != null) return displayVfxRoot;

        if (cat == ShopCategory.Weapon && weaponDisplayRoot != null)
            return weaponDisplayRoot.transform;
        if (cat == ShopCategory.Item && itemDisplayRoot != null)
            return itemDisplayRoot.transform;

        return transform;
    }

    /// <summary>현재 매대 위 시각 인스턴스 제거 (등급/카테고리 무관, 생성 경로에 따라 분기 해제).</summary>
    private void ClearDisplayInstance()
    {
        if (_currentDisplayInstance == null) return;

        if (_currentDisplayIsAddressableInstance)
        {
            // Addressables.InstantiateAsync 결과 → ReleaseInstance. 실패 시 Destroy로 폴백.
            if (!Addressables.ReleaseInstance(_currentDisplayInstance))
                Destroy(_currentDisplayInstance);
        }
        else
        {
            // LoadAsset + Instantiate → 일반 Destroy
            Destroy(_currentDisplayInstance);
        }

        _currentDisplayInstance = null;
        _currentDisplayIsAddressableInstance = false;
    }

    private static string GetFallbackVfxKey(ItemRarity rarity) => rarity switch
    {
        ItemRarity.Common    => FallbackVfxKeyCommon,
        ItemRarity.Rare      => FallbackVfxKeyRare,
        ItemRarity.Epic      => FallbackVfxKeyEpic,
        ItemRarity.Legendary => FallbackVfxKeyLegendary,
        _                    => FallbackVfxKeyCommon,
    };
}
