using UnityEngine;
using Cinemachine;
using Cysharp.Threading.Tasks;

/// <summary>
/// 게임 카메라 컨트롤러.
/// - 씬 시작 시 카메라를 멀리 배치
/// - OnPlayerBound 이벤트 수신 → 인트로 애니메이션 → Cinemachine에 제어권 반환
/// </summary>
public class GameCameraController : MonoBehaviour
{
    // ── SerializeField ──
    [Header("Intro")]
    [SerializeField] private float introExtraHeight = 30f;
    [SerializeField] private float introExtraBack = 10f;
    [SerializeField] private float introDuration = 3f;
    [SerializeField] private float introDelay = 0.3f;

    // ── Private ──
    private Vector3 _originalPosition;
    private Quaternion _originalRotation;
    private CinemachineFreeLook _cinemachine;
    private CinemachineBrain _brain;

    // ── Properties ──
    public static GameCameraController Instance { get; private set; }

    // ── Lifecycle ──

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // 원래 카메라 세팅 저장
        _originalPosition = transform.position;
        _originalRotation = transform.rotation;

        _cinemachine = FindObjectOfType<CinemachineFreeLook>(true);
        _brain = GetComponent<CinemachineBrain>();

        // 즉시 멀리 배치 (게임 시작부터 하늘에서 시작)
        MoveToIntroPosition();

        // Cinemachine 즉시 비활성화 (인트로 끝날 때까지)
        if (_brain != null) _brain.enabled = false;
        if (_cinemachine != null) _cinemachine.enabled = false;

        // 이벤트 구독
        SubscribePlayerBound();
    }

    private void OnDestroy()
    {
        UnsubscribePlayerBound();
        if (Instance == this) Instance = null;
    }

    // ── Private Methods ──

    private void MoveToIntroPosition()
    {
        Vector3 startPos = _originalPosition + new Vector3(0f, introExtraHeight, -introExtraBack);
        Vector3 lookDir = _originalPosition - startPos;
        Quaternion startRot = lookDir.sqrMagnitude > 0.01f
            ? Quaternion.LookRotation(lookDir, Vector3.up)
            : _originalRotation;

        transform.position = startPos;
        transform.rotation = startRot;
    }

    private void SubscribePlayerBound()
    {
        // GameRunBootstrapper → GameRunSession.OnPlayerBound 구독
        var bootstrapper = FindObjectOfType<GameRunBootstrapper>(true);
        if (bootstrapper != null && bootstrapper.Run != null)
        {
            bootstrapper.Run.OnPlayerBound += OnPlayerBound;
            return;
        }

        // 아직 Run이 없으면 폴링
        WaitAndSubscribeAsync().Forget();
    }

    private async UniTaskVoid WaitAndSubscribeAsync()
    {
        await UniTask.WaitUntil(() =>
            GameRunBootstrapper.Instance != null &&
            GameRunBootstrapper.Instance.Run != null);

        GameRunBootstrapper.Instance.Run.OnPlayerBound += OnPlayerBound;
    }

    private void UnsubscribePlayerBound()
    {
        if (GameRunBootstrapper.Instance?.Run != null)
            GameRunBootstrapper.Instance.Run.OnPlayerBound -= OnPlayerBound;
    }

    // ── Event Handlers ──

    private void OnPlayerBound(PlayerController player)
    {
        if (player == null) return;
        PlayIntroAsync(player.transform).Forget();
    }

    private async UniTaskVoid PlayIntroAsync(Transform target)
    {
        // Cinemachine 확실히 비활성
        if (_cinemachine == null)
            _cinemachine = FindObjectOfType<CinemachineFreeLook>(true);
        if (_brain == null)
            _brain = GetComponent<CinemachineBrain>();

        if (_brain != null) _brain.enabled = false;
        if (_cinemachine != null) _cinemachine.enabled = false;

        // 시작 위치 (현재 이미 멀리 가 있음)
        Vector3 startPos = transform.position;
        Quaternion startRot = transform.rotation;

        // 최종 위치 (원래 카메라 세팅 기준 + 플레이어 위치 반영)
        Vector3 offset = _originalPosition; // 씬에 설정된 카메라 위치가 곧 오프셋
        Vector3 endPos = target.position + (offset - Vector3.zero); // 플레이어가 원점이 아닐 수 있음
        Quaternion endRot = _originalRotation;

        // 잠시 대기 (맵 렌더링)
        await UniTask.Delay(
            System.TimeSpan.FromSeconds(introDelay),
            ignoreTimeScale: true);

        // 줌인 애니메이션
        float elapsed = 0f;
        while (elapsed < introDuration)
        {
            if (target == null) break;
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / introDuration);
            float ease = 1f - (1f - t) * (1f - t) * (1f - t);

            Vector3 currentEnd = target.position + offset;
            transform.position = Vector3.Lerp(startPos, currentEnd, ease);
            transform.rotation = Quaternion.Slerp(startRot, endRot, ease);
            await UniTask.Yield();
        }

        // 최종 스냅
        transform.position = target.position + offset;
        transform.rotation = endRot;

        // Cinemachine 활성화 → follow 재개
        if (_cinemachine != null) _cinemachine.enabled = true;
        if (_brain != null) _brain.enabled = true;
    }
}
