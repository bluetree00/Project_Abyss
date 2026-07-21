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
            EnsureCameraController();
            UIRootBootstrapper.Instance?.SetHudStartRoomSuppressed(true);

            await ScreenFade.Out(0f);
            await SpawnPlayerAsync(ct);

            var cam = GameCameraController.Instance;
            cam?.ClearIntroFade();
            if (_player != null)
                cam?.HandToGameplayCamera(_player.transform);

            await ScreenFade.In(0.4f, ct);
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
            RunReturnTracker.RecordRunEnd(isCleared: false);
            AppBootstrapper.Instance?.RequestLoad(Define.Scene.BaseCamp);
        }
        catch (OperationCanceledException) { }
    }

    // ── 헬퍼 ───────────────────────────────────────────────────────────────

    private static void EnsureCameraController()
    {
        var cam = Camera.main;
        if (cam != null && !cam.TryGetComponent<GameCameraController>(out _))
            cam.gameObject.AddComponent<GameCameraController>();
    }
}
