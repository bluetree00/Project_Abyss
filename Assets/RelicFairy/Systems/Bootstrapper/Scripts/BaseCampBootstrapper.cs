using System;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;

/// <summary>
/// 영속 허브(BaseCamp) 씬의 초기화 부트스트래퍼. GameRunBootstrapper 패턴을 따른다.
/// PR1 범위: 플레이어(CombatGirl 베이스 몸) 스폰 + 카메라 추적 + 전투 HUD 억제 + 로딩 UI 해제.
/// 선택대/제단/서약 등 허브 컨텐츠와 로드아웃 게이팅은 PR2에서 이식한다.
/// </summary>
public sealed class BaseCampBootstrapper : MonoBehaviour
{
    // 서버에서 로드하는 대화 시퀀스 ID. 원래 Zone0(시작방)에서 쓰던 것과 동일 — 영속 허브로 이전.
    private const string IntroSequenceId = "StartRoom";

    public static BaseCampBootstrapper Instance { get; private set; }

    [Header("Spawn")]
    [Tooltip("초회(온보딩) 스폰 위치/회전 — 입구. 비워두면 원점(바닥 미배치 시 낙하).")]
    [SerializeField] private Transform playerSpawnPoint;

    [Tooltip("복귀(온보딩 완료 후) 스폰 위치 — 유물/장비 제단 부근. 비우면 playerSpawnPoint 사용.")]
    [SerializeField] private Transform returnSpawnPoint;

    [Tooltip("스폰할 CombatGirl 베이스 몸 Addressables 키. GameRunBootstrapper.startBodyKey와 동일.")]
    [SerializeField] private string playerBodyKey = "PlayerCharacter";

    [Header("Intro Dialogue")]
    [Tooltip("베이스캠프 진입 시 재생할 대화 시퀀스 SO. 서버 CSV에 'StartRoom' 시퀀스가 없을 때 폴백으로 사용.")]
    [SerializeField] private DialogueSequenceSO introDialogueSO;

    private PlayerController _player;

    public PlayerController Player => _player;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private async void Start()
    {
        // AppBootstrapper 준비(자동 로그인·Addressables 초기화) 대기.
        // null이면 씬 직접 실행으로 간주해 즉시 통과.
        await UniTask.WaitUntil(() => AppBootstrapper.Instance == null || AppBootstrapper.Instance.IsReady);

        var ct = this.GetCancellationTokenOnDestroy();
        try
        {
            EnsureCameraController();

            // 허브에서는 전투 HUD를 억제(시작방과 동일 처리).
            UIRootBootstrapper.Instance?.SetHudStartRoomSuppressed(true);

            // 시작 연출: 검정으로 가린 뒤 플레이어 스폰 → 게임플레이 카메라 인계 → 페이드인.
            // (예전 '둘러보기 패닝 투어'는 제거됨 — 주변을 보여주지 않고 곧장 플레이어 시점으로 시작)
            await ScreenFade.Out(0f);

            await SpawnPlayerAsync(ct);

            var cam = GameCameraController.Instance;
            // 카메라 자체 인트로 검정 오버레이 해제. 해제하지 않으면 Awake가 만든 불투명 오버레이가 화면에 남는다.
            cam?.ClearIntroFade();

            // 둘러보기 투어 없이 곧장 플레이어 추적 게임플레이 카메라로 인계 (CinemachineFreeLook 리그 필요).
            if (_player != null)
                cam?.HandToGameplayCamera(_player.transform);

            await ScreenFade.In(0.4f, ct);
        }
        catch (OperationCanceledException) { return; }

        // 로딩 UI 해제 — Tutorial/InGame과 동일하게 부트스트래퍼가 책임진다.
        AppBootstrapper.Instance?.NotifySceneReady();

        // Zone0(시작방)에 있던 진입 대사 연출 — 영속 허브로 이전. 로딩 해제 후 재생.
        try { await ShowIntroDialogueAsync(ct); }
        catch (OperationCanceledException) { }
    }

    private void OnDestroy()
    {
        if (ReferenceEquals(Instance, this))
            Instance = null;
    }

    /// <summary>던전 입장 게이트가 호출. 던전 씬을 로드하면 GameRunBootstrapper가
    /// IsNewRunPending+Loadout.IsReady를 감지해 Zone0를 대기 방으로 띄운다
    /// (GameRunBootstrapper.StartWaitingRoomAsync). 여기서는 씬 전환만 책임진다.</summary>
    public void EnterDungeon()
    {
        // 게이트 통과 = 새 런 시작. 로비를 거치지 않은 진입(에디터 직접 Play 등)에서도
        // Ch1 부트스트래퍼가 대기 방 흐름(newRunFromHub)을 타도록 새 런 신호를 세운다.
        AppBootstrapper.Instance?.MarkNewRunPending();
        AppBootstrapper.Instance?.RequestLoad(Define.Scene.GameScene_Ch1);
    }

    private async UniTask SpawnPlayerAsync(CancellationToken ct, Vector3? overridePos = null, Quaternion? overrideRot = null)
    {
        // 로드아웃에 body 키 등록 — WeaponDisplayStand가 Loadout.IsReady를 요구하고,
        // 던전 진입(StartWaitingRoomAsync→SpawnPlayerAsync)이 이 키로 CombatGirl을 스폰한다.
        AppBootstrapper.Instance?.Loadout?.SetCharacter(null, playerBodyKey);

        var prefab = await Managers.AddressableManager.LoadAssetAsync<GameObject>(playerBodyKey);
        if (prefab == null)
        {
            Debug.LogError($"[BaseCampBootstrapper] 플레이어 몸 프리팹 로드 실패: {playerBodyKey}");
            return;
        }

        // 복귀(온보딩 완료) 시 유물/장비 제단 부근 스폰, 초회는 입구 스폰.
        var spawn = (BaseCampOnboardingDirector.IsCompleted && returnSpawnPoint != null)
            ? returnSpawnPoint : playerSpawnPoint;
        Vector3 pos    = overridePos ?? (spawn != null ? spawn.position : Vector3.zero);
        Quaternion rot = overrideRot ?? (spawn != null ? spawn.rotation : Quaternion.identity);

        var go = Instantiate(prefab, pos, rot);
        var player = go.GetComponent<PlayerController>();
        if (player == null)
        {
            Debug.LogError($"[BaseCampBootstrapper] PlayerController 없음: {playerBodyKey}");
            Destroy(go);
            return;
        }

        // InitAsync 완료 대기 (WeaponManager 준비 = 초기화 완료 신호).
        await UniTask.WaitUntil(() => player.WeaponManager != null, cancellationToken: ct);

        // 선택된 유물 적용 (Loadout.Relic 없으면 no-op).
        player.SetRelicAndApply(AppBootstrapper.Instance?.Loadout?.Relic);

        // 장비(무기) 적용 — 허브에서도 장착·테스트 가능하도록 Loadout 무기를 장착.
        await EquipLoadoutWeaponsAsync(player, ct);

        // 전역 플레이어 등록 — 카메라 오클루전 페이더 등 Managers.Player를 참조하는 시스템이
        // 허브(런 아님) 씬에서도 플레이어를 잡을 수 있게 한다.
        Managers.Player?.SetPlayer(player.transform);

        // 카메라 인계(HandToGameplayCamera)는 둘러보기 투어 종료 후 Start()에서 수행한다.
        _player = player;

        // 장비/유물 보유 시 전투 HUD 표시(허브 테스트용).
        UpdateHudForLoadout();

        Debug.Log($"[BaseCampBootstrapper] 플레이어 스폰 완료: {playerBodyKey} at {pos}");
    }

    /// <summary>Loadout에 기록된 무기(슬롯0/1)를 플레이어에 장착한다. 둘 다 없으면 no-op.
    /// WeaponForgeAltar.EquipChoiceAsync와 동일한 장착 경로(빈 슬롯 순서 장착 → 슬롯0 복귀).</summary>
    private async UniTask EquipLoadoutWeaponsAsync(PlayerController player, CancellationToken ct)
    {
        var lo = AppBootstrapper.Instance?.Loadout;
        if (lo == null || player == null) return;

        bool any = false;
        if (lo.WeaponSlot0 != null) { await GameRunBootstrapper.EquipWeaponToPlayerAsync(lo.WeaponSlot0, player); any = true; }
        if (lo.WeaponSlot1 != null) { await GameRunBootstrapper.EquipWeaponToPlayerAsync(lo.WeaponSlot1, player); any = true; }
        ct.ThrowIfCancellationRequested();

        if (any && player.WeaponManager != null)
            await player.WeaponManager.SwitchToSlotAsync(PlayerWeaponManager.Slot0);
    }

    /// <summary>장비/유물 보유 시 전투 HUD를 표시(허브 테스트), 없으면 억제 유지.</summary>
    private void UpdateHudForLoadout()
    {
        var lo = AppBootstrapper.Instance?.Loadout;
        bool equipped = lo != null && (lo.WeaponSlot0 != null || lo.WeaponSlot1 != null || lo.Relic != null);
        // 허브에서도 유물/룬 패시브 버프뷰가 뜨도록 전투 HUD 억제 해제(플레이어 바인딩은
        // HudBootstrapper가 Managers.Player 채널로 타이밍 무관하게 처리).
        if (equipped) UIRootBootstrapper.Instance?.SetHudStartRoomSuppressed(false);
    }

    /// <summary>유물 등 로드아웃 변경 후 허브 플레이어를 제자리에서 재스폰해 클린하게 재적용한다
    /// (유물 핫스왑 시 패시브/컴포넌트 중첩을 피하기 위한 재스폰-온-스왑).</summary>
    public void RespawnWithLoadout()
    {
        RespawnAsync(this.GetCancellationTokenOnDestroy()).Forget();
    }

    private async UniTaskVoid RespawnAsync(CancellationToken ct)
    {
        Vector3 pos = _player != null ? _player.transform.position
                    : (playerSpawnPoint != null ? playerSpawnPoint.position : Vector3.zero);
        Quaternion rot = _player != null ? _player.transform.rotation : Quaternion.identity;

        if (_player != null) { Destroy(_player.gameObject); _player = null; }

        try
        {
            await SpawnPlayerAsync(ct, pos, rot);
        }
        catch (OperationCanceledException) { return; }

        // 재스폰된 플레이어로 게임플레이 카메라 추적 재인계.
        if (_player != null)
            GameCameraController.Instance?.HandToGameplayCamera(_player.transform);
    }

    /// <summary>
    /// 베이스캠프 진입 대사. 복귀 사유별 분기 —
    /// 사망 복귀=RunFail(카운트 기반 랜덤/마일스톤), 클리어 복귀=RunClear, 그 외(최초/일반)=StartRoom 방문분기.
    /// 서버 CSV 우선, 없으면 SO 폴백.
    /// </summary>
    private async UniTask ShowIntroDialogueAsync(CancellationToken ct)
    {
        var dlgMgr = Managers.DialogueData;
        if (dlgMgr != null && !dlgMgr.IsInitialized)
            await dlgMgr.InitializeAsync();

        var reason = RunReturnTracker.ConsumeReason(out int count);
        DialogueLine[] lines = reason switch
        {
            RunReturnTracker.Reason.Death => dlgMgr?.GetCountLines("RunFail", count),
            RunReturnTracker.Reason.Clear => dlgMgr?.GetCountLines("RunClear", count),
            _                             => dlgMgr?.GetVisitLines(IntroSequenceId),
        };
        lines ??= introDialogueSO?.Lines;
        if (lines == null || lines.Length == 0) return;

        var popup = await Managers.UI.ShowPopupUIAndGetAsync<UI_DialoguePopup>();
        if (popup == null) return;

        await popup.ShowAsync(lines);
    }

    /// <summary>Main Camera에 GameCameraController가 없으면 부착. GameRunBootstrapper.EnsureCameraController와 동일.</summary>
    private void EnsureCameraController()
    {
        var cam = Camera.main;
        if (cam == null) cam = FindFirstObjectByType<Camera>();
        if (cam == null) return;

        if (cam.GetComponent<GameCameraController>() == null)
            cam.gameObject.AddComponent<GameCameraController>();
    }
}
