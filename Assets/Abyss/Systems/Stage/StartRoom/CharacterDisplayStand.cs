using Cysharp.Threading.Tasks;
using System.Threading;
using UnityEngine;

/// <summary>
/// 스타트 방 캐릭터 진열대.
/// WispController가 트리거에 진입하면 상호작용 프롬프트를 표시하고
/// F키 입력 시 AppBootstrapper.Loadout에 캐릭터를 등록한다.
/// </summary>
[RequireComponent(typeof(Collider))]
public class CharacterDisplayStand : MonoBehaviour, IWispInteractable
{
    [Header("Character Data")]
    [SerializeField] private CharacterData characterData;
    [SerializeField] private string characterPrefabKey = "Knight";

    [Header("Display")]
    [SerializeField, Tooltip("진열 캐릭터가 바라볼 Y 회전")]
    private float displayRotationY = 180f;
    [SerializeField, Tooltip("전시 캐릭터 Y 오프셋")]
    private float displayOffsetY = 0f;

    [Header("Interaction")]
    [SerializeField, Tooltip("근접 시 활성화할 [F] 상호작용 프롬프트 오브젝트 (선택)")]
    private GameObject interactPrompt;

    private bool _selected;
    private GameObject _displayInstance;
    private WispController _nearbyWisp;

    // ── Lifecycle ───────────────────────────────────────────────

    private void Awake()
    {
        var col = GetComponent<Collider>();
        col.isTrigger = true;
        if (col is BoxCollider box)
            box.size = new Vector3(3f, 2f, 3f);

        if (interactPrompt != null) interactPrompt.SetActive(false);
    }

    private void Start()
    {
        SpawnDisplayAsync(this.GetCancellationTokenOnDestroy()).Forget();
    }

    private void OnDestroy()
    {
        if (_displayInstance != null)
            Destroy(_displayInstance);
    }

    // ── Display Spawn ────────────────────────────────────────────

    private async UniTaskVoid SpawnDisplayAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(characterPrefabKey)) return;

        var addr = Managers.AddressableManager;
        if (addr == null) return;

        GameObject prefab;
        try
        {
            prefab = await addr.LoadAssetAsync<GameObject>(characterPrefabKey);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[CharacterDisplayStand] 프리팹 로드 실패: {characterPrefabKey}\n{e.Message}");
            return;
        }

        if (prefab == null || ct.IsCancellationRequested) return;

        var tempHost = new GameObject("_CharDisplayTemp");
        tempHost.SetActive(false);

        _displayInstance = Instantiate(
            prefab,
            transform.position + Vector3.up * displayOffsetY,
            Quaternion.Euler(0f, displayRotationY, 0f),
            tempHost.transform);

        DisableGameplayComponents(_displayInstance);

        _displayInstance.transform.SetParent(transform, true);
        Destroy(tempHost);
        _displayInstance.SetActive(true);

        Debug.Log($"[CharacterDisplayStand] 전시 생성: {characterData?.characterName} ({characterPrefabKey})");
    }

    private static void DisableGameplayComponents(GameObject go)
    {
        foreach (var behaviour in go.GetComponentsInChildren<Behaviour>(true))
        {
            if (behaviour is Animator) continue;
            behaviour.enabled = false;
        }

        var rb = go.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.constraints = RigidbodyConstraints.FreezeAll;
        }

        foreach (var col in go.GetComponentsInChildren<Collider>(true))
            col.enabled = false;
    }

    // ── Trigger / Interaction ────────────────────────────────────

    private void OnTriggerEnter(Collider other)
    {
        if (_selected) return;
        var wisp = other.GetComponentInParent<WispController>();
        if (wisp == null) return;

        _nearbyWisp = wisp;
        wisp.SetNearbyStand(this);
        if (interactPrompt != null) interactPrompt.SetActive(true);
    }

    private void OnTriggerExit(Collider other)
    {
        var wisp = other.GetComponentInParent<WispController>();
        if (wisp == null) return;
        if (_nearbyWisp != wisp) return;

        wisp.ClearNearbyStand(this);
        _nearbyWisp = null;
        if (interactPrompt != null) interactPrompt.SetActive(false);
    }

    public void ClearInteraction(WispController wisp)
    {
        if (_nearbyWisp != wisp) return;
        wisp.ClearNearbyStand(this);
        _nearbyWisp = null;
        if (interactPrompt != null) interactPrompt.SetActive(false);
    }

    /// <summary>WispController.Update()에서 F키 감지 시 호출.</summary>
    public void TrySelect(WispController wisp)
    {
        if (_selected || wisp == null) return;

        if (characterData == null)
        {
            Debug.LogError("[CharacterDisplayStand] characterData가 null입니다 — Inspector에서 CharacterData SO를 할당하세요.");
            return;
        }

        _selected = true;
        if (interactPrompt != null) interactPrompt.SetActive(false);

        // 전시 오브젝트 즉시 제거
        if (_displayInstance != null)
        {
            Destroy(_displayInstance);
            _displayInstance = null;
        }

        var loadout = AppBootstrapper.Instance?.Loadout;
        if (loadout == null) return;

        loadout.SetCharacter(characterData, characterPrefabKey);
        Managers.CharacterData?.SetCharacterData(characterData, characterPrefabKey);

        // 위습 위치에 선택된 캐릭터 스폰 (위습은 스폰 완료 후 제거됨)
        GameRunBootstrapper.Instance?.SpawnCharacterInStartRoomAsync(
            characterPrefabKey,
            wisp.transform.position,
            wisp.transform.rotation,
            wisp).Forget();

        // WispController 상호작용 정리 (wisp GO는 async 메서드가 제거함)
        wisp.ClearNearbyStand(this);
        _nearbyWisp = null;

        Debug.Log($"[CharacterDisplayStand] 캐릭터 선택: {characterData.characterName} ({characterPrefabKey})");
    }
}
