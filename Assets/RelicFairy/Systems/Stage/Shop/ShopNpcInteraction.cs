using System;
using TMPro;
using UnityEngine;

/// <summary>
/// 상점 NPC 1개의 상호작용 컴포넌트.
/// 플레이어가 트리거 영역 안에서 F키를 누르면 OnInteract 이벤트를 발생시킨다.
/// 구독자(ShopRoomController)가 상점 UI 패널을 연다.
///
/// 기존 월드 매대(ShopStallInteraction)의 트리거/프롬프트 컨벤션을 그대로 따르되,
/// 상품 진열·구매는 UI로 위임하므로 여기서는 "[F] 상점"만 띄운다.
/// </summary>
public class ShopNpcInteraction : MonoBehaviour
{
    // ── Constants ───────────────────────────────────────────
    private const float PromptOffsetY = 2.2f;

    // ── [SerializeField] ────────────────────────────────────
    [Header("프롬프트")]
    [SerializeField] private string promptText = "<color=#FFD700>[F]</color> 상점";

    // ── Private ─────────────────────────────────────────────
    private bool _playerInRange;
    private bool _enabled = true;
    private GameObject _promptGo;
    private TextMeshPro _promptTmp;

    // ── Properties ──────────────────────────────────────────
    /// <summary>플레이어가 F로 상호작용했을 때 발생.</summary>
    public event Action OnInteract;

    // ── Lifecycle ───────────────────────────────────────────

    private void Update()
    {
        if (_promptGo != null && _promptGo.activeSelf && Camera.main != null)
            _promptGo.transform.rotation = Camera.main.transform.rotation;

        if (!_enabled || !_playerInRange) return;

        if (Input.GetKeyDown(KeyCode.F))
            OnInteract?.Invoke();
    }

    private void OnTriggerEnter(Collider other)
    {
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
        OnInteract = null;
        if (_promptGo != null)
            Destroy(_promptGo);
    }

    // ── Public Methods ──────────────────────────────────────

    /// <summary>패널이 열려 있는 동안 등 입력을 잠그고 싶을 때 사용.</summary>
    public void SetInteractable(bool value)
    {
        _enabled = value;
        if (!value) ShowPrompt(false);
        else if (_playerInRange) ShowPrompt(true);
    }

    // ── Private Methods ─────────────────────────────────────

    private void ShowPrompt(bool show)
    {
        if (show && _promptGo == null)
            CreatePrompt();
        if (_promptGo != null)
            _promptGo.SetActive(show);
    }

    private void CreatePrompt()
    {
        _promptGo = new GameObject("ShopNpcPrompt");
        _promptGo.transform.SetParent(transform, false);
        _promptGo.transform.localPosition = Vector3.up * PromptOffsetY;

        _promptTmp = _promptGo.AddComponent<TextMeshPro>();
        _promptTmp.fontSize = 4f;
        _promptTmp.alignment = TextAlignmentOptions.Center;
        _promptTmp.color = Color.white;
        _promptTmp.textWrappingMode = TextWrappingModes.NoWrap;
        _promptTmp.text = promptText;

        TMPOutlineHelper.ApplyDefault(_promptTmp);

        _promptGo.SetActive(false);
    }

    private static bool IsPlayer(Collider col)
        => col.GetComponentInParent<PlayerController>() != null;
}
