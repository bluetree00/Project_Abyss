using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>
/// 한 씬에서 "맵(프리팹)"을 교체(파기 + 생성)하는 스포너.
/// - Addressables key(=RoomData.prefab)로 Instantiate
/// - 이전 맵은 ReleaseInstance로 정리
/// - (선택) 페이드 전환 + 최소 표시시간으로 자연스러운 전환
/// </summary>
public sealed class StageMapSpawner : MonoBehaviour
{
    [Header("Spawn Root")]
    [SerializeField] private Transform mapRoot;                 // 생성될 맵의 부모(정리용)
    [SerializeField] private Transform playerSpawnPoint;        // (선택) 플레이어 스폰 위치

    [Header("Transition (Optional)")]
    [SerializeField] private CanvasGroup fadeCanvasGroup;       // (선택) 화면 페이드용 캔버스그룹
    [SerializeField] private float fadeOutDuration = 0.25f;
    [SerializeField] private float fadeInDuration  = 0.25f;
    [SerializeField] private float minTransitionTime = 0.35f;   // 너무 빨리 끝나면 깜빡임 방지

    [Header("Debug")]
    [SerializeField] private bool logDebug = false;

    public bool IsChanging { get; private set; }
    public GameObject CurrentMapInstance => _currentInstance;
    public string CurrentMapKey => _currentMapKey;

    public event Action<string> OnChangeStarted;   // mapKey
    public event Action<string> OnChangeFinished;  // mapKey

    private GameObject _currentInstance;
    private string _currentMapKey;
    private AsyncOperationHandle<GameObject>? _currentHandle;

    private void Awake()
    {
        if (mapRoot == null)
            mapRoot = this.transform;

        // 페이드가 있으면 초기 상태를 "보임"으로
        if (fadeCanvasGroup != null)
        {
            fadeCanvasGroup.alpha = 0f;
            fadeCanvasGroup.blocksRaycasts = false;
            fadeCanvasGroup.interactable = false;
        }
    }

    /// <summary>
    /// mapKey(Addressables Address)를 받아 맵을 교체한다.
    /// - 이전 맵 Release
    /// - 새 맵 Instantiate
    /// - (옵션) 페이드 전환
    /// </summary>
    public async UniTask<bool> ChangeMapAsync(string mapKey)
    {
        if (string.IsNullOrWhiteSpace(mapKey))
        {
            Debug.LogError("[StageMapSpawner] ChangeMapAsync failed: mapKey is null/empty");
            return false;
        }

        if (IsChanging)
        {
            if (logDebug) Debug.LogWarning("[StageMapSpawner] ChangeMapAsync ignored: already changing");
            return false;
        }

        IsChanging = true;
        OnChangeStarted?.Invoke(mapKey);

        float startedAt = Time.realtimeSinceStartup;

        try
        {
            if (logDebug) Debug.Log($"[StageMapSpawner] ChangeMap 시작: {mapKey}");

            // 1) 페이드 아웃 (선택)
            await FadeAsync(targetAlpha: 1f, fadeOutDuration);

            // 2) 기존 맵 정리
            ReleaseCurrentMap();

            // 3) 새 맵 생성
            var handle = Addressables.InstantiateAsync(mapKey, mapRoot);
            _currentHandle = handle;

            _currentInstance = await handle.Task;
            _currentMapKey = mapKey;

            if (_currentInstance == null)
            {
                Debug.LogError($"[StageMapSpawner] Instantiate returned null. key='{mapKey}'");
                return false;
            }

            // 4) (선택) 맵이 제공하는 스폰 포인트 사용
            //    - 맵 프리팹에 SpawnPoint(태그 or 컴포넌트) 같은 걸 넣어도 됨.
            //    - 여기서는 외부에서 지정한 playerSpawnPoint만 제공.
            //    실제 플레이어 이동은 호출자(GameRun/PlayerManager)가 처리하는 걸 권장.
            if (logDebug)
                Debug.Log($"[StageMapSpawner] Map spawned: {_currentInstance.name} under {mapRoot.name}");

            // 5) 최소 전환 시간 보장(너무 빠르면 깜빡임 방지)
            float elapsed = Time.realtimeSinceStartup - startedAt;
            float remain = minTransitionTime - elapsed;
            if (remain > 0f)
                await UniTask.Delay(TimeSpan.FromSeconds(remain), ignoreTimeScale: true);

            // 6) 페이드 인 (선택)
            await FadeAsync(targetAlpha: 0f, fadeInDuration);

            OnChangeFinished?.Invoke(mapKey);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[StageMapSpawner] ChangeMapAsync exception: {e}");
            return false;
        }
        finally
        {
            IsChanging = false;
        }
    }

    /// <summary>
    /// 콜백/버튼에서 편하게 쓰는 동기 진입점.
    /// </summary>
    public void ChangeMap(string mapKey)
    {
        ChangeMapAsync(mapKey).Forget();
    }

    /// <summary>
    /// 현재 맵을 릴리즈한다.
    /// </summary>
    public void ReleaseCurrentMap()
    {
        if (_currentInstance != null)
        {
            // Addressables.InstantiateAsync로 만든 인스턴스는 ReleaseInstance로 정리
            Addressables.ReleaseInstance(_currentInstance);
            _currentInstance = null;
        }

        // handle을 들고 있더라도 인스턴스를 ReleaseInstance하면 보통 충분.
        // (혹시 handle 기반으로 관리하고 있다면 아래를 활성화할 수 있음)
        if (_currentHandle.HasValue)
        {
            // ReleaseInstance를 이미 했으면 중복 릴리즈가 될 수 있으니 주의.
            // Addressables.Release(_currentHandle.Value); // 필요시만 사용
            _currentHandle = null;
        }

        _currentMapKey = null;
    }

    private async UniTask FadeAsync(float targetAlpha, float duration)
    {
        if (fadeCanvasGroup == null || duration <= 0f)
            return;

        float start = fadeCanvasGroup.alpha;
        float t = 0f;

        // 페이드 중엔 입력 막기(원하면)
        fadeCanvasGroup.blocksRaycasts = targetAlpha >= 0.5f;
        fadeCanvasGroup.interactable = false;

        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / duration);
            fadeCanvasGroup.alpha = Mathf.Lerp(start, targetAlpha, k);
            await UniTask.Yield(PlayerLoopTiming.Update);
        }

        fadeCanvasGroup.alpha = targetAlpha;

        // 완전히 밝아지면 입력 풀기
        if (Mathf.Approximately(targetAlpha, 0f))
            fadeCanvasGroup.blocksRaycasts = false;
    }

    // (선택) 플레이어 스폰 위치를 외부에서 얻고 싶을 때
    public bool TryGetPlayerSpawnPoint(out Vector3 pos, out Quaternion rot)
    {
        if (playerSpawnPoint == null)
        {
            pos = default;
            rot = default;
            return false;
        }

        pos = playerSpawnPoint.position;
        rot = playerSpawnPoint.rotation;
        return true;
    }
}
