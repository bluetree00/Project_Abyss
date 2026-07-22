using System;
using TMPro;
using UnityEngine;

/// <summary>
/// 무형검 픽업 — 플레이어가 트리거 범위 안에서 F키를 누르면 OnPickup을 발생시킨다.
/// 상점/제단 상호작용(ShopNpcInteraction)의 프롬프트·트리거 컨벤션을 그대로 따른다.
///
/// 구독자(IntroMordredDirector)가 픽업 순간 검 획득 연출 + 리치 타락 시퀀스를 시작한다.
/// 1회만 발동하며, 발동 후 프롬프트를 숨긴다.
/// </summary>
[RequireComponent(typeof(Collider))]
public sealed class IntroSwordPickup : MonoBehaviour
{
    // ── Constants ───────────────────────────────────────────
    private const float PromptOffsetY = 2.0f;

    // ── [SerializeField] ────────────────────────────────────
    [Header("프롬프트")]
    [SerializeField] private string promptText = "<color=#CFC4FF>[F]</color> 무형검을 손에 쥔다";
    [Tooltip("프롬프트를 띄울 트리거 콜라이더 크기(반경). Awake에서 SphereCollider로 강제.")]
    [SerializeField] private float triggerRadius = 3f;

    // ── Private ─────────────────────────────────────────────
    private bool _playerInRange;
    private bool _consumed;
    private GameObject _promptGo;
    private PlayerController _player;

    // ── Properties ──────────────────────────────────────────
    /// <summary>플레이어가 F로 검을 획득했을 때 발생(1회). 인자 = 획득한 플레이어.</summary>
    public event Action<PlayerController> OnPickup;

    // ── Lifecycle ───────────────────────────────────────────
    private void Awake()
    {
        var col = GetComponent<Collider>();
        col.isTrigger = true;
        if (col is SphereCollider sphere) sphere.radius = triggerRadius;
    }

    private void Update()
    {
        if (_promptGo != null && _promptGo.activeSelf && Camera.main != null)
            _promptGo.transform.rotation = Camera.main.transform.rotation;

        if (_consumed || !_playerInRange) return;

        if (Input.GetKeyDown(KeyCode.F))
        {
            _consumed = true;
            ShowPrompt(false);
            OnPickup?.Invoke(_player);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        var p = other.GetComponentInParent<PlayerController>();
        if (p == null) return;
        _player = p;
        _playerInRange = true;
        if (!_consumed) ShowPrompt(true);
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.GetComponentInParent<PlayerController>() == null) return;
        _playerInRange = false;
        ShowPrompt(false);
    }

    private void OnDestroy()
    {
        OnPickup = null;
        if (_promptGo != null) Destroy(_promptGo);
    }

    // ── Private Methods ─────────────────────────────────────
    private void ShowPrompt(bool show)
    {
        if (show && _promptGo == null) CreatePrompt();
        if (_promptGo != null) _promptGo.SetActive(show);
    }

    private void CreatePrompt()
    {
        _promptGo = new GameObject("IntroSwordPrompt");
        _promptGo.transform.SetParent(transform, false);
        _promptGo.transform.localPosition = Vector3.up * PromptOffsetY;

        var tmp = _promptGo.AddComponent<TextMeshPro>();
        tmp.fontSize = 3.5f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.text = promptText;

        TMPOutlineHelper.ApplyDefault(tmp);

        _promptGo.SetActive(false);
    }
}
