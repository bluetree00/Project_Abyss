using System;
using TMPro;
using UnityEngine;

/// <summary>
/// 무형검 픽업 — 플레이어가 트리거 범위에 들어오면 바로 OnPickup을 발생시킨다(11-02 사용자 「입구를 지나 무형검 근처에 가면 연출을 바로 시작」).
/// <see cref="startOnApproach"/>를 끄면 예전처럼 범위 안에서 F키를 누를 때.
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
    [Header("발동")]
    [Tooltip("켜면 범위에 들어오는 즉시 시작(프롬프트 없음). 끄면 범위 안에서 F.")]
    [SerializeField] private bool startOnApproach = true;

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
    /// <summary>검 받기 시작(1회) — 범위에 들어올 때(또는 F). 인자 = 플레이어.</summary>
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
        if (UIInputGate.Blocked) return;

        if (startOnApproach || Input.GetKeyDown(KeyCode.F))
        {
            _consumed = true;
            ShowPrompt(false);
            OnPickup?.Invoke(_player);   // 줍는 소리는 검이 손에 맺히는 순간 연출이 낸다
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        var p = other.GetComponentInParent<PlayerController>();
        if (p == null) return;
        _player = p;
        _playerInRange = true;
        if (!_consumed && !startOnApproach) ShowPrompt(true);
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

        TMPOutlineHelper.ApplySoftShadow(tmp);

        _promptGo.SetActive(false);
    }
}
