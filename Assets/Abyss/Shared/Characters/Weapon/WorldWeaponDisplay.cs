using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using Cysharp.Threading.Tasks;
using TMPro;

[RequireComponent(typeof(Collider))]
public class WorldWeaponDisplay : MonoBehaviour
{
    [Header("Weapon Data")]
    public WeaponSO weaponSO; // 에디터 배치용

    [Header("월드 텍스트")]
    [SerializeField] private TMP_FontAsset worldTextFont;
    [SerializeField] private float textHeight = 1.2f;
    [SerializeField] private float textSize = 3f;

    private WeaponData _runtimeData;  // 코드로 드랍된 무기 데이터
    private GameObject _weaponInstance;
    private TextMeshPro _worldText;
    private Transform _camTransform;
    private bool _pickedUp = false;

    private async void Start()
    {
        _camTransform = Camera.main != null ? Camera.main.transform : null;

        if (_runtimeData == null && weaponSO != null)
        {
            _runtimeData = new WeaponData(weaponSO);
            await SpawnDisplayAsync(weaponSO.weaponDisplayKey);
        }

        CreateWorldText();
    }

    private void Update()
    {
        if (_worldText != null && _camTransform != null)
            _worldText.transform.rotation = _camTransform.rotation;
    }

    /// <summary>
    /// 런타임 WeaponData로 초기화 (교체 드랍 시 사용)
    /// </summary>
    public void InitFromData(WeaponData data, TMP_FontAsset font = null)
    {
        _runtimeData = data;
        if (font != null) worldTextFont = font;
        SpawnDisplayAsync(data.weaponDisplayKey).Forget();
    }

    /// <summary>
    /// 버린 무기를 월드에 스폰
    /// </summary>
    public static WorldWeaponDisplay SpawnFromData(WeaponData data, Vector3 position)
    {
        var go = new GameObject($"DroppedWeapon_{data.displayName}");
        go.transform.position = position;

        var col = go.AddComponent<SphereCollider>();
        col.isTrigger = true;
        col.radius = 1f;

        var display = go.AddComponent<WorldWeaponDisplay>();
        display.InitFromData(data);
        return display;
    }

    private async UniTask SpawnDisplayAsync(string displayKey)
    {
        if (string.IsNullOrEmpty(displayKey)) return;

        if (_weaponInstance != null)
            Destroy(_weaponInstance);

        var handle = Addressables.InstantiateAsync(displayKey, transform.position, transform.rotation);
        await handle.Task;

        if (handle.Status == AsyncOperationStatus.Succeeded)
        {
            _weaponInstance = handle.Result;
            _weaponInstance.transform.SetParent(transform, true);
        }
        else
        {
            Debug.LogWarning($"[WorldWeaponDisplay] 프리팹 로드 실패: {displayKey}");
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_pickedUp) return;

        var player = other.GetComponent<PlayerController>();
        if (player == null) return;

        _pickedUp = true;

        var data = _runtimeData ?? (weaponSO != null ? new WeaponData(weaponSO) : null);
        if (data == null)
        {
            _pickedUp = false;
            return;
        }

        player.RequestPickup(data, this);
        // Destroy는 팝업 결과 후 ConfirmPickup()에서 처리
    }

    /// <summary>플레이어가 트리거 밖으로 나가면 다시 픽업 가능하도록 리셋</summary>
    private void OnTriggerExit(Collider other)
    {
        if (other.GetComponent<PlayerController>() == null) return;
        _pickedUp = false;
    }

    /// <summary>픽업 확정 — 월드 오브젝트 제거</summary>
    public void ConfirmPickup()
    {
        Destroy(gameObject);
    }

    /// <summary>픽업 취소 — _pickedUp은 플레이어가 나갈 때(OnTriggerExit)까지 유지</summary>
    public void CancelPickup()
    {
        // 콜라이더 조작 없이 _pickedUp이 true인 채로 유지
        // → 팝업 종료 직후 재발동 방지, 플레이어가 나갔다 오면 OnTriggerEnter 재발동 가능
    }

    // ── Private Methods ──

    private void CreateWorldText()
    {
        if (_runtimeData == null) return;

        var textGO = new GameObject("WeaponLabel");
        textGO.transform.SetParent(transform, false);
        textGO.transform.localPosition = Vector3.up * textHeight;

        _worldText = textGO.AddComponent<TextMeshPro>();
        if (worldTextFont != null)
            _worldText.font = worldTextFont;
        _worldText.text = _runtimeData.displayName;
        _worldText.fontSize = textSize;
        _worldText.alignment = TextAlignmentOptions.Center;
        _worldText.color = GetTierColor(_runtimeData.tier);
        _worldText.enableWordWrapping = false;
        _worldText.sortingOrder = 10;

        if (_camTransform != null)
            _worldText.transform.rotation = _camTransform.rotation;
    }

    private static Color GetTierColor(int tier)
    {
        return tier switch
        {
            1 => Color.white,                      // T1 — 일반
            2 => Color.cyan,                       // T2 — 희귀
            3 => new Color(0.8f, 0.4f, 1f),        // T3 — 영웅
            _ => Color.white,
        };
    }
}
