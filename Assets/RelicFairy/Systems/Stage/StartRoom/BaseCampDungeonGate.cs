using UnityEngine;

/// <summary>
/// 베이스캠프 던전 입장 포탈. 유물(Relic)+무형검(WeaponSlot0)+원거리(WeaponSlot1)가 모두 준비되면
/// 활성화되고, 활성 상태에서 플레이어가 트리거에 진입하면 던전 씬으로 전환한다(BaseCampBootstrapper.EnterDungeon).
///
/// 순서 강제는 이 게이트 <b>한 곳</b>에서만 한다(스테이션마다 배리어로 막지 않음, 하데스식).
/// 미준비 상태로 진입하면 <b>부족분 + 획득 위치</b>를 HUD 텍스트로 안내한다.
/// 무형검은 각성 제단, 원거리는 모루, 유물은 빛나는 제단에서 획득한다.
/// (StartRoomGate 스타트 방 모드 게이팅 패턴 기반)
/// </summary>
[RequireComponent(typeof(Collider))]
public sealed class BaseCampDungeonGate : MonoBehaviour
{
    [SerializeField, Tooltip("준비 완료 시 활성화할 포탈 비주얼 오브젝트(선택)")]
    private GameObject portalActive;

    [Header("미충족 안내 (부족분 + 위치 설명)")]
    [SerializeField, Tooltip("차단 안내 재출력 최소 간격(초) — 트리거 머무름 스팸 방지")]
    private float noticeCooldown = 3f;
    [TextArea, SerializeField]
    private string missingWeaponHint = "무형검이 없다 — 코즈웨이 입구의 <b>각성 제단</b>에서 검을 쥐어라.";
    [TextArea, SerializeField]
    private string missingRelicHint = "유물이 없다 — <b>빛나는 제단</b>에서 유물을 선택하라.";
    [TextArea, SerializeField]
    private string missingRangedHint = "원거리 장비가 없다 — <b>모루</b>에서 활이나 석궁을 골라라.";
    [TextArea, SerializeField]
    private string missingBothHint = "준비가 덜 됐다 — <b>각성 제단</b>(무형검) · <b>모루</b>(원거리) · <b>빛나는 제단</b>(유물)을 먼저 들러라.";

    private bool _entered;
    private HudPresenter _hud;
    private float _lastNoticeTime = -999f;
    private bool _portalShown;

    private void Reset()
    {
        if (TryGetComponent<Collider>(out var col)) col.isTrigger = true;
    }

    private void Start()
    {
        // 포탈 비주얼은 준비 완료(유물+무형검) 시에만 보인다.
        // 트리거(게이트)는 항상 켜둔다 — 미달 상태로 다가오면 부족분 안내가 나가야 하므로.
        if (portalActive != null)
        {
            _portalShown = IsLoadoutReady();
            portalActive.SetActive(_portalShown);
        }
    }

    private void Update() => SyncPortalVisual();

    private void OnTriggerEnter(Collider other) => TryEnter(other);
    private void OnTriggerStay(Collider other)  => TryEnter(other);

    /// <summary>충돌 시점에 전제조건을 검사한다(상시 폴링 없음). 미달이면 부족분 안내만 하고 통과시키지 않는다.</summary>
    private void TryEnter(Collider other)
    {
        if (_entered) return;
        if (other.GetComponentInParent<PlayerController>() == null) return;

        if (!IsLoadoutReady())
        {
            ShowMissingNotice();
            return;
        }

        _entered = true;
        BaseCampBootstrapper.Instance?.EnterDungeon();
    }

    // ── Private Methods ───────────────────────────────────────

    /// <summary>준비 상태를 포탈 비주얼에 반영. 유물·무기는 다른 제단에서 바뀌고 변경 통지가 없어
    /// 매 프레임 확인한다 — null 검사 2회뿐이며 상태가 바뀔 때만 SetActive를 호출한다.</summary>
    private void SyncPortalVisual()
    {
        if (portalActive == null) return;

        bool ready = IsLoadoutReady();
        if (ready == _portalShown) return;

        _portalShown = ready;
        portalActive.SetActive(ready);
    }

    /// <summary>부족한 전제조건(무형검/유물)과 그 획득 위치를 HUD로 안내. 원거리는 필수 아님 → 제외.</summary>
    private void ShowMissingNotice()
    {
        if (Time.unscaledTime - _lastNoticeTime < noticeCooldown) return;
        _lastNoticeTime = Time.unscaledTime;

        var loadout = AppBootstrapper.Instance?.Loadout;
        if (loadout == null) return;

        bool noWeapon = loadout.WeaponSlot0 == null;
        bool noRanged = loadout.WeaponSlot1 == null;
        bool noRelic  = loadout.Relic == null;

        int missing = (noWeapon ? 1 : 0) + (noRanged ? 1 : 0) + (noRelic ? 1 : 0);

        string msg;
        if (missing == 0)   return;   // 준비됐는데 여기 온 건 레이스 — 다음 프레임 Update가 처리
        else if (missing > 1) msg = missingBothHint;   // 둘 이상 부족 — 통합 안내
        else if (noWeapon)    msg = missingWeaponHint;
        else if (noRanged)    msg = missingRangedHint;
        else                  msg = missingRelicHint;

        if (_hud == null) _hud = FindFirstObjectByType<HudPresenter>(FindObjectsInactive.Include);
        _hud?.ShowBuffNotice(msg);
        Debug.Log($"[DungeonGate] 진입 차단: {msg}");
    }

    private static bool IsLoadoutReady()
    {
        var loadout = AppBootstrapper.Instance?.Loadout;
        return loadout != null
            && loadout.Relic != null
            && loadout.WeaponSlot0 != null    // 무형검 — 각성 제단
            && loadout.WeaponSlot1 != null;   // 원거리 — 모루. [서약 폐기] 서약은 대기방 조립 제단에서 획득 — 게이트 조건서 제외.
    }
}
