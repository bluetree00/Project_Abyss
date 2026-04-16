using UnityEngine;
using Cysharp.Threading.Tasks;
using TMPro;

/// <summary>
/// 월드에 드롭된 아이템 표시 + 픽업 처리.
/// 등급별 VFX 이펙트를 Addressable로 로드하여 표시.
/// </summary>
[RequireComponent(typeof(Collider))]
public class WorldItemDisplay : MonoBehaviour
{
    // ── 직렬화 필드 ─────────────────────────────────────────
    [Header("에디터 배치용")]
    [SerializeField] private ItemSO itemSO;

    [Header("월드 텍스트")]
    [SerializeField] private TMP_FontAsset worldTextFont;
    [SerializeField] private float textHeight = 0.8f;
    [SerializeField] private float textSize = 3f;

    [Header("VFX 설정")]
    [SerializeField] private ItemVfxConfig vfxConfig;
    [SerializeField] private float vfxYOffset = -0.5f;

    // ── 비공개 필드 ─────────────────────────────────────────
    private const float VfxAudioVolume = 0.15f;

    private RuntimeItemData _runtimeData;
    private TextMeshPro _worldText;
    private Transform _camTransform;
    private bool _pickedUp;
    private GameObject _vfxInstance;
    private float _spawnTime;

    // ── Lifecycle ───────────────────────────────────────────

    private void Start()
    {
        _spawnTime = Time.time;
        _camTransform = Camera.main != null ? Camera.main.transform : null;

        if (_runtimeData == null && itemSO != null)
            _runtimeData = RuntimeItemData.FromSO(itemSO);

        // 기존에 붙어있는 메시 제거 (에디터 배치 프리팹에 Sphere 등이 있을 수 있음)
        StripMeshComponents(gameObject);

        CreateVfxVisualAsync().Forget();
        CreateWorldText();
    }

    private void Update()
    {
        // 텍스트 빌보드
        if (_worldText != null && _camTransform != null)
            _worldText.transform.rotation = _camTransform.rotation;
    }

    // ── Public Methods ──────────────────────────────────────

    /// <summary>런타임 데이터로 초기화 (코드 드롭 시).</summary>
    public void InitFromData(RuntimeItemData data, TMP_FontAsset font = null, ItemVfxConfig config = null)
    {
        _runtimeData = data;
        if (font != null) worldTextFont = font;
        if (config != null) vfxConfig = config;
    }

    /// <summary>런타임 데이터로 월드에 스폰.</summary>
    public static WorldItemDisplay SpawnFromData(RuntimeItemData data, Vector3 position, ItemVfxConfig config = null)
    {
        var go = new GameObject($"DroppedItem_{data.displayName}");
        go.transform.position = position;

        var col = go.AddComponent<SphereCollider>();
        col.isTrigger = true;
        col.radius = 1f;

        var display = go.AddComponent<WorldItemDisplay>();
        display.InitFromData(data, config: config);
        return display;
    }

    public RuntimeItemData GetRuntimeData() => _runtimeData;

    /// <summary>픽업 확정 — 오브젝트 즉시 비활성화 후 제거.</summary>
    public void ConfirmPickup()
    {
        gameObject.SetActive(false);
        Destroy(gameObject);
    }

    /// <summary>픽업 취소.</summary>
    public void CancelPickup() { }

    // ── Event Handlers ──────────────────────────────────────

    private void OnTriggerEnter(Collider other)
    {
        if (_pickedUp) return;
        if (Time.time - _spawnTime < 1f) return; // 스폰 후 1초 보호

        var player = other.GetComponent<PlayerController>();
        if (player == null) return;
        if (_runtimeData == null) return;

        _pickedUp = true;

        var col = GetComponent<Collider>();
        if (col != null) col.enabled = false;

        if (_runtimeData.shapeId > 0)
        {
            var bridge = BlockSynergyBridge.Instance;
            if (bridge != null)
                bridge.RegisterShapeFromItem(_runtimeData.shapeId);
        }

        var run = GameRunBootstrapper.Instance?.Run;
        run?.ItemInventory.AddItem(_runtimeData);

        // 아이템 효과: OnItemPickup hook
        run?.EffectManager?.OnItemPickup(_runtimeData);

        // HUD 알림
        ShowPickupNotice();

        Debug.Log($"[WorldItemDisplay] 아이템 획득: {_runtimeData.displayName} ({_runtimeData.rarity}) shape={_runtimeData.shapeId}");
        ConfirmPickup();
    }

    private void ShowPickupNotice()
    {
        var hud = UnityEngine.Object.FindObjectOfType<HudPresenter>(true);
        if (hud == null || _runtimeData == null) return;

        string color = _runtimeData.rarity switch
        {
            ItemRarity.Rare => "#00FFFF",
            ItemRarity.Epic => "#CC66FF",
            _               => "#FFFFFF",
        };

        // 효과 요약
        var sb = new System.Text.StringBuilder();
        sb.Append($"<color={color}>{_runtimeData.displayName}</color> 획득!");

        foreach (var eff in _runtimeData.effects)
        {
            if (string.IsNullOrEmpty(eff.effectType)) continue;
            string sign = eff.value >= 0 ? "+" : "";
            sb.Append($"\n  {eff.effectType} {sign}{eff.value}");
            if (eff.trigger != "Always")
                sb.Append($" ({eff.trigger})");
        }

        hud.ShowBuffNotice(sb.ToString());
    }

    // ── Private Methods ─────────────────────────────────────

    private async UniTaskVoid CreateVfxVisualAsync()
    {
        if (_runtimeData == null) return;

        string vfxKey = ResolveVfxKey(_runtimeData.rarity);

        if (!string.IsNullOrEmpty(vfxKey))
        {
            try
            {
                var prefab = await Managers.AddressableManager.LoadAssetAsync<GameObject>(vfxKey);
                if (prefab != null && this != null && gameObject != null)
                {
                    _vfxInstance = Instantiate(prefab, transform);
                    _vfxInstance.transform.localPosition = new Vector3(0f, vfxYOffset, 0f);
                    _vfxInstance.name = "VFX_Display";
                    ReduceAudioVolume(_vfxInstance);
                    return;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[WorldItemDisplay] VFX 로드 실패 ({vfxKey}): {e.Message} — 폴백 사용");
            }
        }

        // VFX 로드 실패 시 빈 오브젝트 (시각 없음, 콜라이더만 남음)
    }

    private string ResolveVfxKey(ItemRarity rarity)
    {
        if (vfxConfig != null)
            return vfxConfig.GetDisplayVfxKey(rarity);

        return rarity switch
        {
            ItemRarity.Common => "VFX_Item_Common",
            ItemRarity.Rare   => "VFX_Item_Rare",
            ItemRarity.Epic   => "VFX_Item_Epic",
            _                 => "VFX_Item_Common",
        };
    }

    private static void ReduceAudioVolume(GameObject vfx)
    {
        var sources = vfx.GetComponentsInChildren<AudioSource>(true);
        foreach (var src in sources)
            src.volume = VfxAudioVolume;
    }

    /// <summary>오브젝트 본체의 MeshRenderer/MeshFilter 제거 (자식 VFX는 유지).</summary>
    private static void StripMeshComponents(GameObject go)
    {
        var meshRenderer = go.GetComponent<MeshRenderer>();
        if (meshRenderer != null) Destroy(meshRenderer);

        var meshFilter = go.GetComponent<MeshFilter>();
        if (meshFilter != null) Destroy(meshFilter);
    }

    private void CreateWorldText()
    {
        if (_runtimeData == null) return;

        var textGO = new GameObject("ItemLabel");
        textGO.transform.SetParent(transform, false);
        textGO.transform.localPosition = Vector3.up * textHeight;

        _worldText = textGO.AddComponent<TextMeshPro>();
        if (worldTextFont != null)
            _worldText.font = worldTextFont;
        _worldText.text = _runtimeData.displayName;
        _worldText.fontSize = textSize;
        _worldText.alignment = TextAlignmentOptions.Center;
        _worldText.color = GetRarityColor(_runtimeData.rarity);
        _worldText.enableWordWrapping = false;
        _worldText.sortingOrder = 10;

        if (_camTransform != null)
            _worldText.transform.rotation = _camTransform.rotation;
    }

    private static Color GetRarityColor(ItemRarity rarity) => rarity switch
    {
        ItemRarity.Common => Color.white,
        ItemRarity.Rare   => Color.cyan,
        ItemRarity.Epic   => new Color(0.8f, 0.4f, 1f),
        _                 => Color.white,
    };
}
