using TMPro;
using UnityEngine;

/// <summary>
/// BaseCamp 배치용 <b>원거리 파츠 테스트 제단</b>.
/// 범위 안에서 F를 누르면 <see cref="UI_RangedPartsTestPanel"/>을 토글한다.
///
/// 획득 경제(상점 재료·재련소 제작)가 확정되기 전까지 5종 효과를 바로 시험하기 위한 도구다.
/// 다른 제단(각성·서약)과 같은 규약 — 트리거 진입 표시 + F 상호작용 + 빌보드 텍스트.
/// </summary>
[RequireComponent(typeof(Collider))]
public sealed class RangedPartsTestAltar : MonoBehaviour
{
    private const float PromptOffsetY = 1.8f;
    private const float LabelOffsetY  = 1.2f;
    private const float TriggerRadius = 2.5f;

    [Header("월드 텍스트")]
    [SerializeField] private TMP_FontAsset worldTextFont;
    [SerializeField] private float textSize = 3f;

    private bool        _playerInRange;
    private Transform   _camTransform;
    private TextMeshPro _worldText;
    private GameObject  _promptGo;

    // ── Lifecycle ───────────────────────────────────────────

    private void Awake()
    {
        // 트리거가 아니면 상호작용이 통째로 죽고, 오히려 플레이어를 막는 벽이 된다.
        // 씬에 어떻게 배치됐든 여기서 보정한다(WeaponForgeAltar와 동일).
        var col = GetComponent<Collider>();
        col.isTrigger = true;
        if (col is SphereCollider sphere && sphere.radius < TriggerRadius) sphere.radius = TriggerRadius;
    }

    private void Start()
    {
        _camTransform = Camera.main != null ? Camera.main.transform : null;
        CreateWorldText();
        CreatePrompt();
    }

    private void Update()
    {
        Billboard();

        if (!_playerInRange) return;
        if (UIInputGate.Blocked) return;
        if (Input.GetKeyDown(KeyCode.F))
            UI_RangedPartsTestPanel.Toggle();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsPlayer(other)) return;
        _playerInRange = true;
        _promptGo?.SetActive(true);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsPlayer(other)) return;
        _playerInRange = false;
        _promptGo?.SetActive(false);
    }

    // ── Public 팩토리 ───────────────────────────────────────

    /// <summary>코드로 제단을 세운다(씬에 직접 배치해도 동작한다).</summary>
    public static RangedPartsTestAltar SpawnAt(Vector3 position)
    {
        var go = new GameObject("RangedPartsTestAltar");
        go.transform.position = position;

        var col = go.AddComponent<SphereCollider>();
        col.isTrigger = true;
        col.radius    = TriggerRadius;

        return go.AddComponent<RangedPartsTestAltar>();
    }

    // ── 내부 ────────────────────────────────────────────────

    private static bool IsPlayer(Collider c)
        => c != null && (c.CompareTag("Player") || c.GetComponentInParent<PlayerController>() != null);

    private void Billboard()
    {
        if (_camTransform == null) return;
        if (_worldText != null) _worldText.transform.rotation = _camTransform.rotation;
        if (_promptGo != null && _promptGo.activeSelf) _promptGo.transform.rotation = _camTransform.rotation;
    }

    private void CreateWorldText()
    {
        var go = new GameObject("AltarLabel");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.up * LabelOffsetY;

        _worldText = go.AddComponent<TextMeshPro>();
        if (worldTextFont != null) _worldText.font = worldTextFont;
        _worldText.text      = "원거리 파츠 [테스트]";
        _worldText.fontSize  = textSize;
        _worldText.alignment = TextAlignmentOptions.Center;
        _worldText.color     = new Color(0.45f, 0.85f, 1f);
        _worldText.textWrappingMode = TextWrappingModes.NoWrap;
        _worldText.sortingOrder = UISortingOrder.WorldLabel;
        TMPOutlineHelper.ApplyDefault(_worldText);
    }

    private void CreatePrompt()
    {
        _promptGo = new GameObject("InteractPrompt");
        _promptGo.transform.SetParent(transform, false);
        _promptGo.transform.localPosition = Vector3.up * PromptOffsetY;

        var t = _promptGo.AddComponent<TextMeshPro>();
        if (worldTextFont != null) t.font = worldTextFont;
        t.text      = "<color=#FFD700>[F]</color> 파츠 테스트";
        t.fontSize  = 4f;
        t.alignment = TextAlignmentOptions.Center;
        t.color     = Color.white;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.sortingOrder = UISortingOrder.WorldPrompt;
        TMPOutlineHelper.ApplyDefault(t);

        _promptGo.SetActive(false);
    }
}
