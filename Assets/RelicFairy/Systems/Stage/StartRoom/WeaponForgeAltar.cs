using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;

/// <summary>
/// 시작방 무기대. [F] → <b>보조 원거리</b> 선택 팝업.
///
/// 기획 피벗: 주무기 무형검은 <b>각성 제단(WorldSwordAwakening)에서 하사</b>되므로 여기서 근접을 고르지 않는다.
/// 곁에 둘 원거리만 고른다(활=선택, 석궁=특전 잠금). 확정 시 원거리=슬롯1 장착(무형검은 이미 슬롯0).
/// (RelicAltar 상호작용 패턴 기반)
/// </summary>
[RequireComponent(typeof(Collider))]
public class WeaponForgeAltar : MonoBehaviour
{
    private const float PromptOffsetY = 1.8f;
    private const float TextHeight = 1.4f;

    [Header("주무기 — 무형검(기본 지급)")]
    [SerializeField] private MainWeaponSO namelessWeapon;

    [Header("원거리 옵션")]
    [SerializeField] private MainWeaponSO bowOption;
    [Tooltip("석궁 — 특전 무기. 팝업에 잠금 상태로만 노출(선택 불가).")]
    [SerializeField] private MainWeaponSO crossbowOption;

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

    private void Awake()
    {
        GetComponent<Collider>().isTrigger = true;
    }

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
            ClaimAsync(this.GetCancellationTokenOnDestroy()).Forget();
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
        if (p != null && p == _player)
        {
            _player = null;
            ShowPrompt(false);
        }
    }

    // ── Selection ─────────────────────────────────────────────

    private async UniTaskVoid ClaimAsync(CancellationToken ct)
    {
        if (_claimed || _busy) return;

        var loadout = AppBootstrapper.Instance?.Loadout;
        if (loadout == null || !loadout.IsReady)
        {
            Debug.LogWarning("[WeaponForgeAltar] 캐릭터/유물을 먼저 선택하세요.");
            return;
        }

        _busy = true;
        ShowPrompt(false);

        try
        {
            var popup = await Managers.UI.ShowPopupUIAndGetAsync<UI_RangedForgePopup>();
            if (popup == null)
            {
                Debug.LogWarning("[WeaponForgeAltar] UI_RangedForgePopup 로드 실패.");
                _busy = false;
                return;
            }

            var entries = new List<UI_RangedForgePopup.Entry>(2);
            if (bowOption != null)
                entries.Add(new UI_RangedForgePopup.Entry { Weapon = bowOption });
            if (crossbowOption != null)
                entries.Add(new UI_RangedForgePopup.Entry { Weapon = crossbowOption, Locked = true, LockReason = "특전 해금" });

            popup.Setup(entries);
            var ranged = await popup.WaitForChoiceAsync().AttachExternalCancellation(ct);

            if (ranged == null)
            {
                // 취소: 무기대 유지, 재상호작용 허용
                _busy = false;
                if (_player != null) ShowPrompt(true);
                return;
            }

            _claimed = true;
            await EquipChoiceAsync(loadout, ranged, ct);

            Debug.Log($"[WeaponForgeAltar] 보조 원거리 확정: {ranged.displayName}");
            DissolveEffect.PlayDisappear(gameObject, 0.5f, () => { if (this != null) Destroy(gameObject); });
        }
        catch (OperationCanceledException)
        {
            // 오브젝트 파괴 등으로 취소 — 무시
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[WeaponForgeAltar] ClaimAsync 실패: {ex.Message}");
            _busy = false;
        }
    }

    private async UniTask EquipChoiceAsync(PlayerLoadout loadout, WeaponSO ranged, CancellationToken ct)
    {
        // 무형검(슬롯0)은 각성 제단(WorldSwordAwakening)에서만 하사된다 — 무기대는 보조 원거리(슬롯1)만.
        // 폴백 없음: 각성을 거치지 않았으면 무형검을 주지 않는다(원거리만).
        loadout.SetWeaponSlot1(ranged);

        // 퀘스트: 장비 선택 보고 (범용 채널)
        QuestEvents.Report("Equip", ranged != null ? ranged.name : "Weapon");

        var player = _player;
        if (player == null) return;

        // 원거리 → 항상 슬롯1(보조/Q) 고정 장착. 비활성으로 두어 주무기(무형검) 활성 상태를 건드리지 않는다.
        // 무형검 스테이션을 참조하지 않음 — 획득 순서 독립.
        await GameRunBootstrapper.EquipWeaponToPlayerAsync(
            ranged, player, PlayerWeaponManager.Slot1, setActive: false);
        ct.ThrowIfCancellationRequested();

        // 장착 완료 → 허브에서 전투 HUD 표시(테스트용).
        UIRootBootstrapper.Instance?.SetHudStartRoomSuppressed(false);
    }

    // ── World Text / Prompt (RelicAltar 패턴) ─────────────────

    private void BillboardTexts()
    {
        if (_camTransform == null) return;
        if (_worldText != null) _worldText.transform.rotation = _camTransform.rotation;
        if (_promptGo != null && _promptGo.activeSelf) _promptGo.transform.rotation = _camTransform.rotation;
    }

    private void CreateWorldText()
    {
        var go = new GameObject("ForgeLabel");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.up * TextHeight;

        _worldText = go.AddComponent<TextMeshPro>();
        if (worldTextFont != null) _worldText.font = worldTextFont;
        _worldText.text = "무기대";
        _worldText.fontSize = textSize;
        _worldText.alignment = TextAlignmentOptions.Center;
        _worldText.color = new Color(0.85f, 0.85f, 0.95f);
        _worldText.textWrappingMode = TextWrappingModes.NoWrap;
        _worldText.sortingOrder = 10;
        TMPOutlineHelper.ApplyDefault(_worldText);

        if (_camTransform != null) _worldText.transform.rotation = _camTransform.rotation;
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
        tmp.text = "<color=#FFD700>[F]</color> 보조 무기";

        _promptGo.SetActive(false);
    }

    private void ShowPrompt(bool on)
    {
        if (_promptGo != null) _promptGo.SetActive(on && !_claimed);
    }
}
