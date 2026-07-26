using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Game_Intro 성당 아레나를 씬 임베드 대신 런타임 Addressable로 생성한다.
///
/// 같은 아레나가 Ch3 보스전용 Addressable 번들에 이미 들어 있어, 씬에 굽으면 세트 1벌이 통째로 중복된다.
/// 인트로도 런타임 생성으로 맞춰 번들을 공유한다(인트로 씬 빌드데이터 −2GB대).
///
/// 인트로용 배치/레이어/제거 오버라이드는 프리팹 배리언트 <c>Arena_Boss_Ch3_Intro</c>가 담고 있고,
/// 그 배리언트는 원본 <c>Arena_Boss_Ch3</c>를 참조만 하므로 메시/텍스처는 여전히 1벌이다.
///
/// 아레나 내부를 직접 참조하던 연출 필드 2개(<see cref="IntroMordredDirector"/>.bossRoom,
/// <see cref="IntroOpeningDirector"/>.altarAnchor)는 생성 직후 여기서 주입한다.
/// </summary>
public sealed class IntroArenaLoader : MonoBehaviour
{
    // ── Constants ────────────────────────────────────────────────────
    private const string ArenaKey       = "Arena_Boss_Ch3_Intro";
    private const string AltarAnchorName = "SM_Statue_01b";   // 제단 천사상 — 오프닝 카메라 기준점

    // ── Static ───────────────────────────────────────────────────────
    private static IntroArenaLoader _instance;

    // ── Private ──────────────────────────────────────────────────────
    private readonly UniTaskCompletionSource _ready = new UniTaskCompletionSource();
    private Transform _arena;

    // ── Properties ───────────────────────────────────────────────────
    /// <summary>생성된 아레나 루트. 로드 전이거나 실패했으면 null.</summary>
    public Transform Arena => _arena;

    // ── Lifecycle ────────────────────────────────────────────────────
    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(this); return; }
        _instance = this;
        LoadAsync(this.GetCancellationTokenOnDestroy()).Forget();
    }

    private void OnDestroy()
    {
        if (ReferenceEquals(_instance, this)) _instance = null;
    }

    // ── Public Methods ───────────────────────────────────────────────
    /// <summary>아레나 생성·주입이 끝날 때까지 대기. 씬에 로더가 없으면 즉시 완료된다.</summary>
    public static UniTask ReadyAsync() =>
        _instance != null ? _instance._ready.Task : UniTask.CompletedTask;

    // ── Private Methods ──────────────────────────────────────────────
    private async UniTaskVoid LoadAsync(CancellationToken ct)
    {
        try
        {
            // Addressables 초기화가 끝나야 인스턴스화할 수 있다(IntroBootstrapper와 같은 게이트).
            await UniTask.WaitUntil(
                () => AppBootstrapper.Instance == null || AppBootstrapper.Instance.IsReady,
                cancellationToken: ct);

            var go = await Managers.AddressableManager.InstantiateAsync(ArenaKey);
            if (go == null)
            {
                Debug.LogError($"[IntroArenaLoader] 아레나 로드 실패 — Addressable 키 '{ArenaKey}' 없음. 인트로 성당이 비어 보인다.");
                _ready.TrySetResult();
                return;
            }

            // 씬 임베드 시점과 동일한 원점 배치(오프닝 카메라·연출 좌표가 전부 이 기준).
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            go.transform.localScale = Vector3.one;
            _arena = go.transform;

            Rebind(_arena);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            Debug.LogError($"[IntroArenaLoader] 아레나 생성 중 예외: {ex}");
        }

        _ready.TrySetResult();
    }

    /// <summary>아레나 내부를 가리키던 연출 참조를 새로 생성된 인스턴스로 다시 연결한다.</summary>
    private static void Rebind(Transform arena)
    {
        var bossRoom = arena.GetComponentInChildren<BossRoomController>(true);
        if (bossRoom == null)
            Debug.LogError("[IntroArenaLoader] 아레나에 BossRoomController 없음 — 튜토리얼 보스전이 시작되지 않는다.");
        else
        {
            var mordred = FindFirstObjectByType<IntroMordredDirector>(FindObjectsInactive.Include);
            if (mordred != null) mordred.SetBossRoom(bossRoom);
        }

        var altar = arena.Find(AltarAnchorName);
        if (altar == null)
            Debug.LogError($"[IntroArenaLoader] 아레나에 '{AltarAnchorName}' 없음 — 오프닝 제단 샷 구도가 어긋난다.");
        else
        {
            var opening = FindFirstObjectByType<IntroOpeningDirector>(FindObjectsInactive.Include);
            if (opening != null) opening.SetAltarAnchor(altar);
        }
    }
}
