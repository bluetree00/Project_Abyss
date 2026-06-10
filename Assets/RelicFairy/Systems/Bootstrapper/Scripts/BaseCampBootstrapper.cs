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
    [Tooltip("플레이어 스폰 위치/회전. 비워두면 원점에 스폰(바닥 미배치 시 낙하하므로 씬에 바닥 필요).")]
    [SerializeField] private Transform playerSpawnPoint;

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

            // 시작 연출 (Ch1 StartRoomAsync 진입 연출과 동일 구성):
            // 검정으로 가린 뒤 플레이어 스폰 → 둘러보기 패닝(레터박스+페이드인)으로 드러냄 → 게임플레이 카메라 인계.
            await ScreenFade.Out(0f);

            await SpawnPlayerAsync(ct);

            var cam = GameCameraController.Instance;
            // 카메라 자체 인트로 검정 오버레이 해제 — 진입 연출은 ScreenFade(투어)가 담당하므로 중복.
            // 해제하지 않으면 Awake가 만든 불투명 오버레이가 화면에 그대로 남는다.
            cam?.ClearIntroFade();
            Vector3 tourCenter = playerSpawnPoint != null ? playerSpawnPoint.position : Vector3.zero;
            if (cam != null)
                await cam.PlayStartRoomTourAsync(tourCenter, ct);
            else
                await ScreenFade.In(0.4f, ct); // 카메라 컨트롤러 부재 시에도 검정 해제 보장

            // 둘러보기 종료 → 플레이어 추적 게임플레이 카메라로 인계 (CinemachineFreeLook 리그 필요).
            if (_player != null)
                cam?.HandToGameplayCamera(_player.transform);
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

    private async UniTask SpawnPlayerAsync(CancellationToken ct)
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

        Vector3 pos    = playerSpawnPoint != null ? playerSpawnPoint.position : Vector3.zero;
        Quaternion rot = playerSpawnPoint != null ? playerSpawnPoint.rotation : Quaternion.identity;

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

        // 카메라 인계(HandToGameplayCamera)는 둘러보기 투어 종료 후 Start()에서 수행한다.
        _player = player;
        Debug.Log($"[BaseCampBootstrapper] 플레이어 스폰 완료: {playerBodyKey} at {pos}");
    }

    /// <summary>베이스캠프 진입 대사 연출. 서버 CSV('StartRoom' 시퀀스) 우선, 없으면 인스펙터 SO 폴백.
    /// GameRunBootstrapper.ShowStartRoomDialogueAsync와 동일 구성 — Zone0에서 이전.</summary>
    private async UniTask ShowIntroDialogueAsync(CancellationToken ct)
    {
        var dlgMgr = Managers.DialogueData;
        if (dlgMgr != null && !dlgMgr.IsInitialized)
            await dlgMgr.InitializeAsync();

        DialogueLine[] lines = dlgMgr?.GetLines(IntroSequenceId)
                               ?? introDialogueSO?.Lines;
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
