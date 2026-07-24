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
    private const float SwordVisualHeight = 0.6f;   // 제단 위에 뜬 검의 높이

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
    private GameObject _swordVisual;

    // ── Lifecycle ─────────────────────────────────────────────

    private void Awake() => GetComponent<Collider>().isTrigger = true;

    private void Start()
    {
        // 이미 주무기를 가진 상태(복귀 런)면 제단은 존재 이유가 없다 — 이 제단은 '첫' 주무기를 하사하는 곳이다.
        // 남겨두면 F로 재획득돼 강화·진화한 슬롯0 무기가 T0 무형검으로 덮어써진다.
        // (제단은 획득 시 자기를 Destroy하지만 그건 그 세션 한정 — 씬을 다시 로드하면 _claimed=false로 되살아난다.)
        if (AlreadyArmed()) { Destroy(gameObject); return; }

        _camTransform = Camera.main != null ? Camera.main.transform : null;
        CreateWorldText();
        CreatePrompt();
        SpawnSwordVisualAsync(this.GetCancellationTokenOnDestroy()).Forget();
    }

    /// <summary>이미 주무기(슬롯0)를 보유했는가. 세이브 복원이 늦게 끝나는 경우를 대비해 매 판정 시점에 다시 확인한다.</summary>
    private static bool AlreadyArmed()
    {
        var lo = AppBootstrapper.Instance?.Loadout;
        return lo != null && lo.WeaponSlot0 != null;
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
        // 세이브 복원이 Start 이후에 끝난 경우를 대비한 재확인 — 이미 무장했으면 프롬프트조차 띄우지 않는다.
        if (AlreadyArmed()) return;
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
        // [최종 방어] 이미 주무기가 있으면 절대 덮어쓰지 않는다.
        // 재련소에서 강화·진화한 무기를 T0 무형검으로 되돌리는 사고를 막는 마지막 관문.
        if (loadout.WeaponSlot0 != null)
        {
            Debug.LogWarning($"[WorldSwordAwakening] 이미 주무기 보유({loadout.WeaponSlot0.name}) — 무형검 재지급을 건너뛴다.");
            _claimed = true;
            ShowPrompt(false);
            if (this != null) Destroy(gameObject);
            return;
        }

        _busy = true;
        ShowPrompt(false);

        try
        {
            // 무형검 → 슬롯0 (로드아웃 예약 + 실제 장착).
            // 항상 Slot0 고정·활성. 원거리 스테이션 상태를 참조하지 않는다(획득 순서 독립).
            loadout.SetWeaponSlot0(namelessWeapon);
            var player = _player;
            if (player != null)
            {
                await GameRunBootstrapper.EquipWeaponToPlayerAsync(
                    namelessWeapon, player, PlayerWeaponManager.Slot0, setActive: true);
                ct.ThrowIfCancellationRequested();
            }

            _claimed = true;

            // 각성 신호 — 온보딩/퀘스트가 다음 단계(원거리 무기대)를 개방.
            QuestEvents.Report(questCategory, namelessWeapon != null ? namelessWeapon.name : "Nameless");

            if (player != null)
                GuidelineVisual.Toast(player.transform.position + Vector3.up * 2.4f, "무형검 각성", GuidelineVisual.ToastKind.Relic);

            // 각성 후 HUD(전투) 표시.
            UIRootBootstrapper.Instance?.SetHudStartRoomSuppressed(false);

            Debug.Log($"[WorldSwordAwakening] 무형검 각성: {namelessWeapon.displayName} → 슬롯0");

            // 제단 위의 검은 <b>디졸브를 쓰지 않는다</b>. DissolveEffect는 머티리얼을 통째로 갈아끼우는데
            // 무형검 비주얼은 반투명 안개 머티리얼(M_NamelessFog)이라 셰이더가 맞지 않아 핑크로 깨진다
            // (인트로 IntroMordredDirector가 같은 이유로 알파 페이드를 쓴다). 알파만 낮춰 지운다.
            if (_swordVisual != null)
            {
                await MaterialFade.FadeOutAsync(_swordVisual, 0.6f, ct);
                // 제단 디졸브가 이 렌더러까지 집어삼켜 머티리얼을 되살리지 않도록 먼저 걷어낸다.
                Destroy(_swordVisual);
                _swordVisual = null;
            }

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

    // ── 검 비주얼 ─────────────────────────────────────────────

    /// <summary>
    /// 제단에 무형검 본체를 띄운다. 프리팹은 무기 SO의 weaponDisplayKey(= 인트로에 놓인 것과 동일한
    /// 메시·안개 머티리얼)를 그대로 쓴다 — 인트로에서 본 검과 여기서 쥐는 검이 어긋나면 안 된다.
    /// 로드 실패해도 각성 상호작용 자체는 계속 동작해야 하므로 예외를 삼킨다.
    /// </summary>
    private async UniTaskVoid SpawnSwordVisualAsync(CancellationToken ct)
    {
        string key = namelessWeapon != null ? namelessWeapon.weaponDisplayKey : null;
        if (string.IsNullOrEmpty(key)) return;

        try
        {
            var prefab = await Managers.AddressableManager.TryLoadAssetAsync<GameObject>(key);
            ct.ThrowIfCancellationRequested();
            if (prefab == null || this == null) return;

            _swordVisual = Instantiate(prefab, transform);
            _swordVisual.transform.localPosition = Vector3.up * SwordVisualHeight;
            _swordVisual.transform.localRotation = Quaternion.identity;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Debug.LogWarning($"[WorldSwordAwakening] 검 비주얼 로드 실패({key}): {ex.Message}");
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
