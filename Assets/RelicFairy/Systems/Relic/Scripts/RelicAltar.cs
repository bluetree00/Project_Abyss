using TMPro;
using UnityEngine;

/// <summary>
/// 시작방 유물 제단. CombatGirl 플레이어가 범위에 들어와 F를 누르면 해당 유물 클래스를
/// 기존 플레이어에 적용한다(새 스폰 X). 유물 1개 확정형 — 획득 시 다른 제단은 제거.
/// 캐릭터 전시(스폰)와 분리된 별도 오브젝트. (WorldAwakeningAltar 상호작용 패턴 기반)
/// </summary>
[RequireComponent(typeof(Collider))]
public class RelicAltar : MonoBehaviour
{
    private const float PromptOffsetY = 1.8f;

    [Header("유물")]
    [SerializeField] private RelicClassSO relicClass;

    [Header("월드 텍스트")]
    [SerializeField] private TMP_FontAsset worldTextFont;
    [SerializeField] private float textHeight = 1.4f;
    [SerializeField] private float textSize = 3f;

    private PlayerController _player;
    private bool _claimed;
    private Transform _camTransform;
    private TextMeshPro _worldText;
    private GameObject _promptGo;
    private TextMeshPro _promptText;

    public RelicClassSO RelicClass => relicClass;

    private void Start()
    {
        _camTransform = Camera.main != null ? Camera.main.transform : null;
        CreateWorldText();
        CreatePrompt();
    }

    private void Update()
    {
        BillboardTexts();
        if (_claimed || _player == null) return;
        if (Input.GetKeyDown(KeyCode.F)) Claim();
    }

    private void OnTriggerEnter(Collider other)
    {
        var p = other.GetComponentInParent<PlayerController>();
        if (p == null || _claimed) return;
        _player = p;
        ShowPrompt(true);
    }

    private void OnTriggerExit(Collider other)
    {
        var p = other.GetComponentInParent<PlayerController>();
        if (p != null && p == _player)
        {
            _player = null;
            ShowPrompt(false);
        }
    }

    private void Claim()
    {
        if (_player == null || relicClass == null) return;
        _claimed = true;
        ShowPrompt(false);

        // 기존 CombatGirl 플레이어에 유물 적용 + 런 유지(전투 존 재스폰 시에도 유지)
        _player.SetRelicAndApply(relicClass);
        AppBootstrapper.Instance?.Loadout?.SetRelic(relicClass);

        Debug.Log($"[RelicAltar] 유물 획득: {relicClass.DisplayName} ({relicClass.Id})");

        // 유물 1개 확정 — 다른 제단 제거
        foreach (var altar in FindObjectsByType<RelicAltar>(FindObjectsSortMode.None))
            if (altar != this) Destroy(altar.gameObject);

        DissolveEffect.PlayDisappear(gameObject, 0.5f, () => { if (this != null) Destroy(gameObject); });
    }

    // ── 월드 텍스트 / 프롬프트 (WorldAwakeningAltar 패턴) ──────────────────────

    private void BillboardTexts()
    {
        if (_camTransform == null) return;
        if (_worldText != null) _worldText.transform.rotation = _camTransform.rotation;
        if (_promptGo != null && _promptGo.activeSelf) _promptGo.transform.rotation = _camTransform.rotation;
    }

    private void CreateWorldText()
    {
        var go = new GameObject("RelicLabel");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.up * textHeight;

        _worldText = go.AddComponent<TextMeshPro>();
        if (worldTextFont != null) _worldText.font = worldTextFont;
        _worldText.text = relicClass != null ? relicClass.DisplayName : "유물";
        _worldText.fontSize = textSize;
        _worldText.alignment = TextAlignmentOptions.Center;
        _worldText.color = new Color(0.9f, 0.7f, 0.2f);
        _worldText.enableWordWrapping = false;
        _worldText.sortingOrder = 10;
        TMPOutlineHelper.ApplyDefault(_worldText);
    }

    private void CreatePrompt()
    {
        _promptGo = new GameObject("InteractPrompt");
        _promptGo.transform.SetParent(transform, false);
        _promptGo.transform.localPosition = Vector3.up * PromptOffsetY;

        _promptText = _promptGo.AddComponent<TextMeshPro>();
        if (worldTextFont != null) _promptText.font = worldTextFont;
        _promptText.fontSize = 4f;
        _promptText.alignment = TextAlignmentOptions.Center;
        _promptText.color = Color.white;
        _promptText.enableWordWrapping = false;
        _promptText.sortingOrder = 11;
        TMPOutlineHelper.ApplyDefault(_promptText);
        _promptText.text = "<color=#FFD700>[F]</color> 유물 획득";

        _promptGo.SetActive(false);
    }

    private void ShowPrompt(bool on)
    {
        if (_promptGo != null) _promptGo.SetActive(on);
    }
}
