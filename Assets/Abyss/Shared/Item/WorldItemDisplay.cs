using UnityEngine;
using TMPro;

/// <summary>
/// 월드에 드롭된 아이템 표시 + 픽업 처리.
/// WorldWeaponDisplay와 동일 패턴.
/// </summary>
[RequireComponent(typeof(Collider))]
public class WorldItemDisplay : MonoBehaviour
{
    [Header("에디터 배치용")]
    [SerializeField] private ItemSO itemSO;

    [Header("비주얼")]
    [SerializeField] private float rotateSpeed = 90f;
    [SerializeField] private float bobAmplitude = 0.2f;
    [SerializeField] private float bobFrequency = 1.5f;

    [Header("월드 텍스트")]
    [SerializeField] private TMP_FontAsset worldTextFont;
    [SerializeField] private float textHeight = 0.8f;
    [SerializeField] private float textSize = 3f;

    private RuntimeItemData _runtimeData;
    private TextMeshPro _worldText;
    private Transform _camTransform;
    private bool _pickedUp;
    private Vector3 _startPos;

    private void Start()
    {
        _startPos = transform.position;
        _camTransform = Camera.main != null ? Camera.main.transform : null;

        if (_runtimeData == null && itemSO != null)
            _runtimeData = RuntimeItemData.FromSO(itemSO);

        CreateWorldText();
    }

    private void Update()
    {
        // 회전 + 부유 연출
        transform.Rotate(Vector3.up, rotateSpeed * Time.deltaTime, Space.World);
        var pos = _startPos;
        pos.y += Mathf.Sin(Time.time * bobFrequency * Mathf.PI * 2f) * bobAmplitude;
        transform.position = pos;

        // 텍스트 빌보드 (카메라를 향함)
        if (_worldText != null && _camTransform != null)
            _worldText.transform.rotation = _camTransform.rotation;
    }

    /// <summary>런타임 데이터로 초기화 (코드 드롭 시).</summary>
    public void InitFromData(RuntimeItemData data, TMP_FontAsset font = null)
    {
        _runtimeData = data;
        if (font != null)
            worldTextFont = font;
    }

    /// <summary>런타임 데이터로 월드에 스폰.</summary>
    public static WorldItemDisplay SpawnFromData(RuntimeItemData data, Vector3 position)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = $"DroppedItem_{data.displayName}";
        go.transform.position = position;
        go.transform.localScale = Vector3.one * 0.5f;

        // 기존 콜라이더를 트리거로 교체
        var existingCol = go.GetComponent<Collider>();
        if (existingCol != null) Object.Destroy(existingCol);

        var col = go.AddComponent<SphereCollider>();
        col.isTrigger = true;
        col.radius = 2f;

        // 색상으로 등급 표시
        var renderer = go.GetComponent<Renderer>();
        if (renderer != null)
        {
            var mat = renderer.material;
            switch (data.rarity)
            {
                case ItemRarity.Common: mat.color = Color.white;  break;
                case ItemRarity.Rare:   mat.color = Color.cyan;   break;
                case ItemRarity.Epic:   mat.color = new Color(0.6f, 0.2f, 1f); break;
            }
        }

        var display = go.AddComponent<WorldItemDisplay>();
        display.InitFromData(data);
        return display;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_pickedUp) return;

        var player = other.GetComponent<PlayerController>();
        if (player == null) return;

        if (_runtimeData == null) return;

        _pickedUp = true;

        // 즉시 콜라이더 비활성화 (중복 트리거 방지)
        var col = GetComponent<Collider>();
        if (col != null) col.enabled = false;

        // 블록 등록: shape_id가 있으면 퍼즐 그리드에 Shape 추가
        if (_runtimeData.shapeId > 0)
        {
            var bridge = BlockSynergyBridge.Instance;
            if (bridge != null)
                bridge.RegisterShapeFromItem(_runtimeData.shapeId);
        }

        // 인벤토리에 추가
        var run = GameRunBootstrapper.Instance?.Run;
        run?.ItemInventory.AddItem(_runtimeData);

        Debug.Log($"[WorldItemDisplay] 아이템 획득: {_runtimeData.displayName} ({_runtimeData.rarity}) shape={_runtimeData.shapeId}");
        ConfirmPickup();
    }

    private void OnTriggerExit(Collider other)
    {
        // 이미 획득 확정된 경우 리셋하지 않음
    }

    /// <summary>픽업 확정 — 오브젝트 즉시 비활성화 후 제거.</summary>
    public void ConfirmPickup()
    {
        gameObject.SetActive(false);
        Destroy(gameObject);
    }

    /// <summary>픽업 취소.</summary>
    public void CancelPickup()
    {
        // _pickedUp은 OnTriggerExit까지 유지
    }

    public RuntimeItemData GetRuntimeData() => _runtimeData;

    // ── Private Methods ──

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

        // 텍스트가 아이템과 함께 회전하지 않도록 독립 rotation
        if (_camTransform != null)
            _worldText.transform.rotation = _camTransform.rotation;
    }

    private static Color GetRarityColor(ItemRarity rarity)
    {
        return rarity switch
        {
            ItemRarity.Common => Color.white,
            ItemRarity.Rare   => Color.cyan,
            ItemRarity.Epic   => new Color(0.8f, 0.4f, 1f),
            _                 => Color.white,
        };
    }
}
