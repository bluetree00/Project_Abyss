using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;

/// <summary>
/// 시작방 무기 모루. 범위 진입 후 [F]를 누르면 선택 팝업이 떠서
/// 근거리(대검/카타나) 1종 + 원거리(보우/석궁) 1종을 고르고 확정한다.
/// 확정 시 두 무기를 동시에 장착(근거리=슬롯0 활성, 원거리=슬롯1)하고 모루를 소비한다.
/// (RelicAltar 상호작용 패턴 기반)
/// </summary>
[RequireComponent(typeof(Collider))]
public class WeaponForgeAltar : MonoBehaviour
{
    private const float PromptOffsetY = 1.8f;
    private const float TextHeight = 1.4f;

    [Header("근거리 옵션 (대검 / 카타나)")]
    [SerializeField] private MainWeaponSO[] meleeOptions = new MainWeaponSO[2];

    [Header("원거리 옵션 (보우 / 석궁)")]
    [SerializeField] private MainWeaponSO[] rangedOptions = new MainWeaponSO[2];

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
            var popup = await Managers.UI.ShowPopupUIAndGetAsync<UI_WeaponForgePopup>();
            if (popup == null)
            {
                Debug.LogWarning("[WeaponForgeAltar] UI_WeaponForgePopup 로드 실패.");
                _busy = false;
                return;
            }

            popup.Setup(meleeOptions, rangedOptions);
            var choice = await popup.WaitForChoiceAsync().AttachExternalCancellation(ct);

            if (!choice.HasValue || choice.Value.Melee == null || choice.Value.Ranged == null)
            {
                // 취소: 모루 유지, 재상호작용 허용
                _busy = false;
                if (_player != null) ShowPrompt(true);
                return;
            }

            _claimed = true;
            await EquipChoiceAsync(loadout, choice.Value, ct);

            Debug.Log($"[WeaponForgeAltar] 장비 확정: 근접={choice.Value.Melee.displayName}, 원거리={choice.Value.Ranged.displayName}");
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

    private async UniTask EquipChoiceAsync(PlayerLoadout loadout, UI_WeaponForgePopup.ForgeChoice choice, CancellationToken ct)
    {
        loadout.SetWeaponSlot0(choice.Melee);
        loadout.SetWeaponSlot1(choice.Ranged);

        var player = _player;
        if (player == null) return;

        // 근접 → 슬롯0, 원거리 → 슬롯1 순서 장착 (둘 다 빈 슬롯 가정)
        await GameRunBootstrapper.EquipWeaponToPlayerAsync(choice.Melee, player);
        await GameRunBootstrapper.EquipWeaponToPlayerAsync(choice.Ranged, player);
        ct.ThrowIfCancellationRequested();

        // 마지막 장착(원거리)이 활성화되므로 근접(슬롯0)으로 되돌려 시작
        if (player.WeaponManager != null)
            await player.WeaponManager.SwitchToSlotAsync(PlayerWeaponManager.Slot0);
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
        _worldText.text = "무기 모루";
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
        tmp.text = "<color=#FFD700>[F]</color> 무기 제작";

        _promptGo.SetActive(false);
    }

    private void ShowPrompt(bool on)
    {
        if (_promptGo != null) _promptGo.SetActive(on && !_claimed);
    }
}
