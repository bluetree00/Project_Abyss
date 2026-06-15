using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;

/// <summary>
/// 월드에 드롭된 서약 오브. 범위 안에서 F 키를 누르면 서약 선택 팝업이 열린다.
/// SpawnAt()으로 코드 스폰하거나 씬에 직접 배치 가능.
/// </summary>
[RequireComponent(typeof(Collider))]
public class WorldCovenantPickup : MonoBehaviour
{
    // ── 직렬화 필드 ────────────────────────────────────────
    [Header("월드 텍스트")]
    [SerializeField] private TMP_FontAsset worldTextFont;
    [SerializeField] private float textHeight = 0.8f;
    [SerializeField] private float textSize   = 3f;

    [Header("VFX")]
    [SerializeField] private string vfxAddressableKey = "VFX_Covenant_Pickup";

    // ── 상수 ────────────────────────────────────────────────
    private const float PromptOffsetY = 1.4f;

    // ── 비공개 필드 ─────────────────────────────────────────
    private string[]     _options;
    private bool         _pickedUp;
    private bool         _playerInRange;
    private Transform    _camTransform;
    private TextMeshPro  _worldText;
    private GameObject   _promptGo;
    private TextMeshPro  _promptText;

    // ── Lifecycle ────────────────────────────────────────────

    private void Start()
    {
        _camTransform = Camera.main != null ? Camera.main.transform : null;
        CreateWorldText();
        CreateVfxAsync().Forget();
    }

    private void Update()
    {
        if (_worldText != null && _camTransform != null)
            _worldText.transform.rotation = _camTransform.rotation;

        if (_promptGo != null && _promptGo.activeSelf && _camTransform != null)
            _promptGo.transform.rotation = _camTransform.rotation;

        if (_pickedUp || !_playerInRange) return;

        if (Input.GetKeyDown(KeyCode.F))
            OpenChoiceAsync(this.GetCancellationTokenOnDestroy()).Forget();
    }

    private void OnDestroy()
    {
        if (_promptGo != null) Destroy(_promptGo);
    }

    // ── Public Methods ───────────────────────────────────────

    /// <summary>제시할 서약 ID 배열로 초기화 (코드 스폰 시).</summary>
    public void Init(string[] options, TMP_FontAsset font = null)
    {
        _options = options;
        if (font != null) worldTextFont = font;
    }

    /// <summary>월드에 서약 픽업 오브젝트를 스폰한다.</summary>
    public static WorldCovenantPickup SpawnAt(Vector3 position, string[] options = null)
    {
        if (options == null || options.Length == 0)
            options = PickRandomOptions(null);

        var go = new GameObject("CovenantPickup");
        go.transform.position = position;

        var col = go.AddComponent<SphereCollider>();
        col.isTrigger = true;
        col.radius = 1.5f;

        var pickup = go.AddComponent<WorldCovenantPickup>();
        pickup.Init(options);
        return pickup;
    }

    /// <summary>
    /// 현재 런에서 미보유 서약 중 랜덤 count개 ID를 반환한다.
    /// handler가 null이면 전체 서약 풀에서 선택한다.
    /// </summary>
    public static string[] PickRandomOptions(CovenantHandler handler, int count = 3)
    {
        var pool = new List<string>(CovenantFactory.AllIds);

        if (handler != null)
            pool.RemoveAll(id => handler.Has(id));

        Shuffle(pool);
        return pool.Take(count).ToArray();
    }

    /// <summary>
    /// 전체 서약 풀에서 exclude를 제외하고 랜덤 count개 ID를 반환한다.
    /// 베이스캠프(CovenantPickup): 핸들러가 없으므로 PlayerLoadout 예약 목록을 exclude로 전달한다.
    /// </summary>
    public static string[] PickRandomOptionsExcluding(ICollection<string> exclude, int count = 3)
    {
        var pool = new List<string>(CovenantFactory.AllIds);
        if (exclude != null)
            pool.RemoveAll(exclude.Contains);

        Shuffle(pool);
        return pool.Take(count).ToArray();
    }

    // ── Private Methods ──────────────────────────────────────

    private async UniTaskVoid OpenChoiceAsync(System.Threading.CancellationToken ct)
    {
        if (_pickedUp) return;
        _pickedUp = true;
        ShowPrompt(false);

        var col = GetComponent<Collider>();
        if (col != null) col.enabled = false;

        var run = GameRunBootstrapper.Instance?.Run;

        string[] ids = _options != null && _options.Length > 0
            ? _options
            : PickRandomOptions(run?.CovenantHandler);

        string selectedId = await CovenantChoiceUI.ChooseAsync(ids, ct);

        if (selectedId != null && run != null && run.CovenantHandler.TryAdd(selectedId))
        {
            ShowAcquireNotice(selectedId, run);
            Debug.Log($"[WorldCovenantPickup] 서약 획득: {selectedId}");
        }

        if (this != null && gameObject != null)
            Destroy(gameObject);
    }

    private static void ShowAcquireNotice(string covenantId, GameRunSession run)
    {
        var hud = UnityEngine.Object.FindFirstObjectByType<HudPresenter>(FindObjectsInactive.Include);
        if (hud == null) return;

        var covenant = run.CovenantHandler.Find(covenantId);
        string displayName = covenant?.DisplayName ?? covenantId;
        hud.ShowBuffNotice($"<color=#CC88FF>서약</color> {displayName} 획득!");
    }

    private async UniTaskVoid CreateVfxAsync()
    {
        if (string.IsNullOrEmpty(vfxAddressableKey)) return;

        var prefab = await Managers.AddressableManager.TryLoadAssetAsync<GameObject>(vfxAddressableKey);
        if (prefab != null && this != null && gameObject != null)
        {
            var vfx = Instantiate(prefab, transform);
            vfx.transform.localPosition = Vector3.zero;
            vfx.name = "VFX_Covenant";
        }
    }

    private void CreateWorldText()
    {
        var textGO = new GameObject("CovenantLabel");
        textGO.transform.SetParent(transform, false);
        textGO.transform.localPosition = Vector3.up * textHeight;

        _worldText = textGO.AddComponent<TextMeshPro>();
        if (worldTextFont != null)
            _worldText.font = worldTextFont;
        _worldText.text = "서약";
        _worldText.fontSize = textSize;
        _worldText.alignment = TextAlignmentOptions.Center;
        _worldText.color = new Color(0.8f, 0.5f, 1f);
        _worldText.textWrappingMode = TextWrappingModes.NoWrap;
        _worldText.sortingOrder = 10;

        TMPOutlineHelper.ApplyDefault(_worldText);
    }

    private void RefreshPrompt()
    {
        if (_promptGo == null) CreatePrompt();
        ShowPrompt(true);
    }

    private void ShowPrompt(bool show)
    {
        if (_promptGo == null) return;
        _promptGo.SetActive(show && !_pickedUp);
    }

    private void CreatePrompt()
    {
        _promptGo = new GameObject("InteractPrompt");
        _promptGo.transform.SetParent(transform, false);
        _promptGo.transform.localPosition = Vector3.up * PromptOffsetY;

        _promptText = _promptGo.AddComponent<TextMeshPro>();
        if (worldTextFont != null)
            _promptText.font = worldTextFont;
        _promptText.fontSize = 4f;
        _promptText.alignment = TextAlignmentOptions.Center;
        _promptText.color = Color.white;
        _promptText.textWrappingMode = TextWrappingModes.NoWrap;
        _promptText.sortingOrder = 11;

        TMPOutlineHelper.ApplyDefault(_promptText);

        _promptText.text = "<color=#FFD700>[F]</color> 서약 선택";
        _promptGo.SetActive(false);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_pickedUp) return;
        if (!IsPlayer(other)) return;
        _playerInRange = true;
        RefreshPrompt();
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsPlayer(other)) return;
        _playerInRange = false;
        ShowPrompt(false);
    }

    private static bool IsPlayer(Collider col)
        => col.GetComponentInParent<PlayerController>() != null;

    private static void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
