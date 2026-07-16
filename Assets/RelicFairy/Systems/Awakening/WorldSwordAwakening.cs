using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;

/// <summary>
/// 무형검 각성 제단 — 대성당 나브 중앙(원탁)에 배치. 인트로 시나리오의 심장.
///
/// 시나리오: 서임받는 순간 스러진 기사의 <b>미완의 검</b>을, 멀린이 다시 쥐어준다.
/// 플레이어가 범위에 들어와 [F]를 누르면 <b>무형검(무명)을 슬롯0에 지급</b>하고 각성 연출을 낸다.
/// (기존엔 무기대에서 무형검+원거리를 함께 줬으나, 시나리오상 무형검은 여기서 하사 — 무기대는 원거리만.)
///
/// 지급 경로는 WeaponForgeAltar.EquipChoiceAsync와 동일(로드아웃 슬롯0 + EquipWeaponToPlayerAsync).
/// 대사(멀린 "미완의 검")는 DIALOGUE_DATA에 등록해 별도 재생 — 여기선 각성 신호(퀘스트 보고)와 지급만 담당.
/// </summary>
[RequireComponent(typeof(Collider))]
public sealed class WorldSwordAwakening : MonoBehaviour
{
    private const float PromptOffsetY = 2.0f;
    private const float TextHeight    = 1.4f;

    [Header("무형검 (기본 지급 주무기)")]
    [SerializeField] private MainWeaponSO namelessWeapon;

    [Header("각성 신호 (온보딩/퀘스트 보고 카테고리)")]
    [SerializeField] private string questCategory = "SwordAwaken";

    [Header("월드 텍스트")]
    [SerializeField] private TMP_FontAsset worldTextFont;
    [SerializeField] private float textSize = 3f;

    private PlayerController _player;
    private bool _claimed;
    private bool _busy;
    private Transform _camTransform;
    private TextMeshPro _worldText;
    private GameObject _promptGo;

    // ── Lifecycle ─────────────────────────────────────────────

    private void Awake() => GetComponent<Collider>().isTrigger = true;

    private void Start()
    {
        _camTransform = Camera.main != null ? Camera.main.transform : null;
        CreateWorldText();
        CreatePrompt();
    }

    private void Update()
    {
        BillboardTexts();
        if (_claimed || _busy || _player == null) return;
        if (Input.GetKeyDown(KeyCode.F))
            AwakenAsync(this.GetCancellationTokenOnDestroy()).Forget();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_claimed) return;
        var p = other.GetComponentInParent<PlayerController>();
        if (p == null) return;
        _player = p;
        ShowPrompt(true);
    }

    private void OnTriggerExit(Collider other)
    {
        var p = other.GetComponentInParent<PlayerController>();
        if (p != null && p == _player) { _player = null; ShowPrompt(false); }
    }

    // ── 각성 ──────────────────────────────────────────────────

    private async UniTaskVoid AwakenAsync(CancellationToken ct)
    {
        if (_claimed || _busy) return;

        var loadout = AppBootstrapper.Instance?.Loadout;
        if (loadout == null || !loadout.IsReady)
        {
            Debug.LogWarning("[WorldSwordAwakening] 캐릭터/유물을 먼저 준비하세요.");
            return;
        }
        if (namelessWeapon == null)
        {
            Debug.LogWarning("[WorldSwordAwakening] namelessWeapon 미할당.");
            return;
        }

        _busy = true;
        ShowPrompt(false);

        try
        {
            // 무형검 → 슬롯0 (로드아웃 + 실제 장착). WeaponForgeAltar와 동일 경로.
            loadout.SetWeaponSlot0(namelessWeapon);
            var player = _player;
            if (player != null)
            {
                await GameRunBootstrapper.EquipWeaponToPlayerAsync(namelessWeapon, player);
                ct.ThrowIfCancellationRequested();
                if (player.WeaponManager != null)
                    await player.WeaponManager.SwitchToSlotAsync(PlayerWeaponManager.Slot0);
            }

            _claimed = true;

            // 각성 신호 — 온보딩/퀘스트가 다음 단계(원거리 무기대)를 개방.
            QuestEvents.Report(questCategory, namelessWeapon != null ? namelessWeapon.name : "Nameless");

            if (player != null)
                GuidelineVisual.Toast(player.transform.position + Vector3.up * 2.4f, "무형검 각성", GuidelineVisual.ToastKind.Relic);

            // 각성 후 HUD(전투) 표시.
            UIRootBootstrapper.Instance?.SetHudStartRoomSuppressed(false);

            Debug.Log($"[WorldSwordAwakening] 무형검 각성: {namelessWeapon.displayName} → 슬롯0");
            DissolveEffect.PlayDisappear(gameObject, 0.6f, () => { if (this != null) Destroy(gameObject); });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Debug.LogWarning($"[WorldSwordAwakening] AwakenAsync 실패: {ex.Message}");
            _busy = false;
            if (_player != null) ShowPrompt(true);
        }
    }

    // ── World Text / Prompt (WorldAwakeningAltar 패턴) ─────────

    private void BillboardTexts()
    {
        if (_camTransform == null) return;
        if (_worldText != null) _worldText.transform.rotation = _camTransform.rotation;
        if (_promptGo != null && _promptGo.activeSelf) _promptGo.transform.rotation = _camTransform.rotation;
    }

    private void CreateWorldText()
    {
        var go = new GameObject("SwordAltarLabel");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.up * TextHeight;

        _worldText = go.AddComponent<TextMeshPro>();
        if (worldTextFont != null) _worldText.font = worldTextFont;
        _worldText.text = "미완의 검";
        _worldText.fontSize = textSize;
        _worldText.alignment = TextAlignmentOptions.Center;
        _worldText.color = new Color(0.95f, 0.85f, 0.55f);
        _worldText.textWrappingMode = TextWrappingModes.NoWrap;
        _worldText.sortingOrder = 10;
        TMPOutlineHelper.ApplyDefault(_worldText);
    }

    private void CreatePrompt()
    {
        _promptGo = new GameObject("InteractPrompt");
        _promptGo.transform.SetParent(transform, false);
        _promptGo.transform.localPosition = Vector3.up * PromptOffsetY;

        var tmp = _promptGo.AddComponent<TextMeshPro>();
        if (worldTextFont != null) tmp.font = worldTextFont;
        tmp.fontSize = 4f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.sortingOrder = 11;
        TMPOutlineHelper.ApplyDefault(tmp);
        tmp.text = "<color=#FFD700>[F]</color> 검을 쥔다";

        _promptGo.SetActive(false);
    }

    private void ShowPrompt(bool on)
    {
        if (_promptGo != null) _promptGo.SetActive(on && !_claimed);
    }
}
