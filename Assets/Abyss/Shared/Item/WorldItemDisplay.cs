using UnityEngine;
using Cysharp.Threading.Tasks;

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

    private RuntimeItemData _runtimeData;
    private bool _pickedUp;
    private Vector3 _startPos;

    private void Start()
    {
        _startPos = transform.position;

        if (_runtimeData == null && itemSO != null)
            _runtimeData = RuntimeItemData.FromSO(itemSO);
    }

    private void Update()
    {
        // 회전 + 부유 연출
        transform.Rotate(Vector3.up, rotateSpeed * Time.deltaTime, Space.World);
        var pos = _startPos;
        pos.y += Mathf.Sin(Time.time * bobFrequency * Mathf.PI * 2f) * bobAmplitude;
        transform.position = pos;
    }

    /// <summary>런타임 데이터로 초기화 (코드 드롭 시).</summary>
    public void InitFromData(RuntimeItemData data)
    {
        _runtimeData = data;
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

        // 블록 등록: shape_id가 있으면 퍼즐 그리드에 Shape 추가
        if (_runtimeData.shapeId > 0)
        {
            var bridge = BlockSynergyBridge.Instance;
            if (bridge != null)
                bridge.RegisterShapeFromItem(_runtimeData.shapeId);
        }

        // TODO: RunItemInventory 연결 (GameRunSession에 소유권 추가 후)
        Debug.Log($"[WorldItemDisplay] 아이템 획득: {_runtimeData.displayName} ({_runtimeData.rarity}) shape={_runtimeData.shapeId}");
        ConfirmPickup();
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.GetComponent<PlayerController>() == null) return;
        _pickedUp = false;
    }

    /// <summary>픽업 확정 — 오브젝트 제거.</summary>
    public void ConfirmPickup()
    {
        Destroy(gameObject);
    }

    /// <summary>픽업 취소.</summary>
    public void CancelPickup()
    {
        // _pickedUp은 OnTriggerExit까지 유지
    }

    public RuntimeItemData GetRuntimeData() => _runtimeData;
}
