using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Game_Intro 씬 전용 부트스트래퍼. BaseCampBootstrapper의 경량 패턴을 따른다.
/// 데이터 초기화는 생략(BaseCamp에서 완료). 플레이어 스폰 + 카메라 인계 + 사망 시 인트로 완료 처리.
/// </summary>
public sealed class IntroBootstrapper : MonoBehaviour
{
    public static IntroBootstrapper Instance { get; private set; }

    [Header("Spawn")]
    [SerializeField] private Transform playerSpawnPoint;
    [SerializeField] private string playerBodyKey = "PlayerCharacter";

    private PlayerController _player;
    public PlayerController Player => _player;

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private async void Start()
    {
        await UniTask.WaitUntil(() => AppBootstrapper.Instance == null || AppBootstrapper.Instance.IsReady);

        var ct = this.GetCancellationTokenOnDestroy();
        try
        {
            Managers.Sound.PlayBgmAsync("Game_Intro").Forget();
            EnsureCameraController();

            // @UIRoot는 Addressable 비동기 로드라 이 시점에 아직 없을 수 있다.
            // ?. 로 넘기면 억제가 조용히 무시되고, HudBootstrapper의 LateUpdate 가드가 풀려
            // 보스 바인딩 시점에 전투 전인데도 HUD가 켜진다 → 준비될 때까지 기다렸다가 건다.
            await WaitForUIRootAsync(ct);
            UIRootBootstrapper.Instance?.SetHudStartRoomSuppressed(true);

            await ScreenFade.Out(0f);
            await SpawnPlayerAsync(ct);

            var cam = GameCameraController.Instance;
            cam?.ClearIntroFade();

            // 오프닝 연출(IntroOpeningDirector)이 있으면 카메라·페이드를 그쪽이 소유한다.
            // 여기서 게임플레이 카메라를 인계하면 그 블렌드가 컷신의 TakeManualControl을 덮어써
            // 석상~무형검 샷이 통째로 스킵된다. 페이드도 프롤로그 페이지를 걷어버린다.
            bool hasOpeningDirector = FindFirstObjectByType<IntroOpeningDirector>(FindObjectsInactive.Include) != null;
            if (!hasOpeningDirector)
            {
                if (_player != null)
                    cam?.HandToGameplayCamera(_player.transform);
                Managers.Sound.CrossfadeBgmAsync("Game_Intro_play").Forget();
                await ScreenFade.In(0.4f, ct);
            }
        }
        catch (OperationCanceledException) { return; }

        AppBootstrapper.Instance?.NotifySceneReady();
    }

    private void OnDestroy()
    {
        if (ReferenceEquals(Instance, this)) Instance = null;
    }

    // ── 플레이어 스폰 ──────────────────────────────────────────────────────

    private async UniTask SpawnPlayerAsync(CancellationToken ct)
    {
        AppBootstrapper.Instance?.Loadout?.SetCharacter(null, playerBodyKey);

        var prefab = await Managers.AddressableManager.LoadAssetAsync<GameObject>(playerBodyKey);
        if (prefab == null)
        {
            Debug.LogError($"[IntroBootstrapper] 플레이어 프리팹 로드 실패: {playerBodyKey}");
            return;
        }

        var pos = playerSpawnPoint != null ? playerSpawnPoint.position : Vector3.zero;
        var rot = playerSpawnPoint != null ? playerSpawnPoint.rotation : Quaternion.identity;

        var go = Instantiate(prefab, pos, rot);
        if (!go.TryGetComponent<PlayerController>(out var player))
        {
            Destroy(go);
            return;
        }

        await UniTask.WaitUntil(() => player.WeaponManager != null, cancellationToken: ct);

        Managers.Player?.SetPlayer(player.transform);
        _player = player;
    }

    // ── 사망 처리 ──────────────────────────────────────────────────────────

    /// <summary>인트로 씬에서 플레이어 사망 시 PlayerController가 호출.</summary>
    public void HandleIntroDeath() => HandleIntroDeathAsync(this.GetCancellationTokenOnDestroy()).Forget();

    private async UniTaskVoid HandleIntroDeathAsync(CancellationToken ct)
    {
        try
        {
            await ScreenFade.Out(1.0f, ct);

            IntroCompletionTracker.MarkCompleted();
            // 인트로 패배는 '런 실패'가 아니라 프롤로그의 일부다 — 복귀 사유를 기록하지 않는다.
            // 기록하면 BaseCamp 첫 진입에서 RunFail(반복 복귀) 대사가 나와 초회 각성 대사를 덮어쓴다.
            //
            // 프롤로그에서 곧장 넘어온 진입임을 알린다 — BaseCamp는 이때만 입구에서 스폰하고,
            // 그 뒤의 모든 복귀(사망/클리어)는 유물층에서 시작한다.
            AppBootstrapper.Instance?.MarkFromIntro();
            AppBootstrapper.Instance?.RequestLoad(Define.Scene.BaseCamp);
        }
        catch (OperationCanceledException) { }
    }

    // ── 헬퍼 ───────────────────────────────────────────────────────────────

    /// <summary>@UIRoot가 준비될 때까지 대기(최대 3초). 없어도 진행은 계속한다.</summary>
    private static async UniTask WaitForUIRootAsync(CancellationToken ct)
    {
        const float TimeoutSec = 3f;
        float t = 0f;
        try
        {
            while (UIRootBootstrapper.Instance == null && t < TimeoutSec)
            {
                t += Time.unscaledDeltaTime;
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }
        catch (OperationCanceledException) { }

        if (UIRootBootstrapper.Instance == null)
            Debug.LogWarning("[IntroBootstrapper] @UIRoot 미준비 — HUD 억제를 걸지 못했다.");
    }

    private static void EnsureCameraController()
    {
        var cam = Camera.main;
        if (cam != null && !cam.TryGetComponent<GameCameraController>(out _))
            cam.gameObject.AddComponent<GameCameraController>();
    }
}
