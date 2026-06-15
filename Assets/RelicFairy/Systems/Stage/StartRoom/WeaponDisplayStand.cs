using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using Cysharp.Threading.Tasks;
using TMPro;

/// <summary>
/// 스타트 방 무기 진열대.
/// WorldWeaponDisplay와 동일한 패턴(트리거 범위 진입 → [F] 프롬프트 → F키 획득)으로 동작하되
/// 픽업 시 WeaponManager가 아닌 AppBootstrapper.Loadout에 등록한다.
/// (캐릭터 선택이 선행되어야 하며, 캐릭터 미선택 시 무시됨)
/// </summary>
[RequireComponent(typeof(Collider))]
public class WeaponDisplayStand : MonoBehaviour
{
    [Header("Weapon Data")]
    [SerializeField] private WeaponSO weaponSO;

    [Header("월드 텍스트")]
    [SerializeField] private TMP_FontAsset worldTextFont;
    [SerializeField] private float textHeight = 1.2f;
    [SerializeField] private float textSize = 3f;

    private const float PromptOffsetY = 2.1f;

    private bool _selected;
    private bool _playerInRange;
    private PlayerController _cachedPlayer;
    private GameObject _weaponInstance;
    private TextMeshPro _worldText;
    private Transform _camTransform;
    private GameObject _promptGo;

    // ── Lifecycle ───────────────────────────────────────────────

    private void Awake()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    private async void Start()
    {
        _camTransform = Camera.main != null ? Camera.main.transform : null;

        if (weaponSO != null && !string.IsNullOrEmpty(weaponSO.weaponDisplayKey))
            await SpawnDisplayAsync(weaponSO.weaponDisplayKey);

        CreateWorldText();
    }

    private void Update()
    {
        if (_worldText != null && _camTransform != null)
            _worldText.transform.rotation = _camTransform.rotation;

        if (_promptGo != null && _promptGo.activeSelf && _camTransform != null)
            _promptGo.transform.rotation = _camTransform.rotation;

        if (_selected || !_playerInRange) return;

        if (Input.GetKeyDown(KeyCode.F))
            TrySelect();
    }

    private void OnDestroy()
    {
        if (_promptGo != null) Destroy(_promptGo);
    }

    // ── Display Spawn ────────────────────────────────────────────

    private async UniTask SpawnDisplayAsync(string displayKey)
    {
        var handle = Addressables.InstantiateAsync(displayKey, transform.position, transform.rotation);
        await handle.Task;

        if (handle.Status == AsyncOperationStatus.Succeeded)
        {
            _weaponInstance = handle.Result;
            _weaponInstance.transform.SetParent(transform, true);
        }
        else
        {
            Debug.LogWarning($"[WeaponDisplayStand] 디스플레이 로드 실패: {displayKey}");
        }
    }

    // ── Trigger ──────────────────────────────────────────────────

    private void OnTriggerEnter(Collider other)
    {
        if (_selected) return;
        var pc = other.GetComponentInParent<PlayerController>();
        if (pc == null) return;

        _cachedPlayer = pc;
        _playerInRange = true;
        ShowPrompt(true);
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.GetComponentInParent<PlayerController>() == null) return;
        _playerInRange = false;
        _cachedPlayer = null;
        ShowPrompt(false);
    }

    // ── Selection ────────────────────────────────────────────────

    private void TrySelect()
    {
        if (_selected || _cachedPlayer == null) return;

        var loadout = AppBootstrapper.Instance?.Loadout;
        if (loadout == null || !loadout.IsReady)
        {
            Debug.LogWarning("[WeaponDisplayStand] 캐릭터를 먼저 선택하세요.");
            return;
        }

        _selected = true;
        ShowPrompt(false);
        loadout.SetWeaponSlot0(weaponSO);

        // 스타트 방에서 즉시 장착 — 로컬 변수로 캡처 후 fire-and-forget
        var player = _cachedPlayer;
        GameRunBootstrapper.EquipWeaponToPlayerAsync(weaponSO, player).Forget();

        // 나머지 무기 진열대 디졸브 퇴장
        var allStands = FindObjectsByType<WeaponDisplayStand>(FindObjectsSortMode.None);
        foreach (var stand in allStands)
        {
            if (stand == this) continue;
            stand.DismissStand();
        }

        Debug.Log($"[WeaponDisplayStand] 무기 선택 및 즉시 장착: {weaponSO?.displayName}");

        if (_weaponInstance != null) Destroy(_weaponInstance);
        Destroy(gameObject);
    }

    /// <summary>다른 무기가 선택됐을 때 이 진열대를 디졸브로 퇴장시킨다.</summary>
    public void DismissStand()
    {
        if (_selected) return;
        _selected = true;
        ShowPrompt(false);
        DissolveEffect.PlayDisappear(gameObject, 0.5f, () => { if (this != null) Destroy(gameObject); });
    }

    // ── World UI ─────────────────────────────────────────────────

    private void CreateWorldText()
    {
        if (weaponSO == null) return;

        var textGO = new GameObject("WeaponLabel");
        textGO.transform.SetParent(transform, false);
        textGO.transform.localPosition = Vector3.up * textHeight;

        _worldText = textGO.AddComponent<TextMeshPro>();
        if (worldTextFont != null) _worldText.font = worldTextFont;
        _worldText.text = weaponSO.displayName;
        _worldText.fontSize = textSize;
        _worldText.alignment = TextAlignmentOptions.Center;
        _worldText.textWrappingMode = TextWrappingModes.NoWrap;
        _worldText.sortingOrder = 10;

        if (_camTransform != null)
            _worldText.transform.rotation = _camTransform.rotation;
    }

    private void ShowPrompt(bool show)
    {
        if (show && _promptGo == null) CreatePrompt();
        if (_promptGo != null) _promptGo.SetActive(show && !_selected);
    }

    private void CreatePrompt()
    {
        _promptGo = new GameObject("InteractPrompt");
        _promptGo.transform.SetParent(transform, false);
        _promptGo.transform.localPosition = Vector3.up * PromptOffsetY;

        var tmp = _promptGo.AddComponent<TextMeshPro>();
        if (worldTextFont != null) tmp.font = worldTextFont;
        tmp.text = "<color=#FFD700>[F]</color> 획득";
        tmp.fontSize = 4f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.sortingOrder = 11;

        _promptGo.SetActive(false);
    }
}
