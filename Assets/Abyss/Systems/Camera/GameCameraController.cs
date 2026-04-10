using UnityEngine;
using Cysharp.Threading.Tasks;

/// <summary>
/// 게임 카메라 컨트롤러.
/// - 시작 연출: 멀리서 줌인 → 플레이어 위치로 이동
/// - 이후: 플레이어를 따라가며 현재 오프셋/회전 유지
/// </summary>
public class GameCameraController : MonoBehaviour
{
    // ── Constants ──
    private static readonly Vector3 DEFAULT_OFFSET = new(0f, 5f, -2f);

    // ── SerializeField ──
    [Header("Follow")]
    [SerializeField] private float followSmooth = 8f;

    [Header("Intro")]
    [SerializeField] private float introStartHeight = 25f;
    [SerializeField] private float introStartDistance = 15f;
    [SerializeField] private float introDuration = 2f;

    // ── Private ──
    private Transform _target;
    private Vector3 _offset;
    private Quaternion _rotation;
    private bool _introPlaying;
    private bool _following;

    // ── Properties ──
    public static GameCameraController Instance { get; private set; }

    // ── Lifecycle ──

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // 현재 카메라의 오프셋/회전을 기준값으로 저장
        _offset = transform.localPosition.sqrMagnitude > 0.01f
            ? transform.localPosition
            : DEFAULT_OFFSET;
        _rotation = transform.rotation;
    }

    private void LateUpdate()
    {
        if (!_following || _target == null || _introPlaying) return;

        Vector3 desired = _target.position + _offset;
        transform.position = Vector3.Lerp(transform.position, desired, followSmooth * Time.deltaTime);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ── Public Methods ──

    /// <summary>플레이어 바인딩 + 시작 연출.</summary>
    public void BindTarget(Transform target, bool playIntro = true)
    {
        _target = target;

        if (playIntro)
            PlayIntroAsync().Forget();
        else
            SnapToTarget();
    }

    /// <summary>즉시 플레이어 위치로 스냅.</summary>
    public void SnapToTarget()
    {
        if (_target == null) return;
        transform.position = _target.position + _offset;
        transform.rotation = _rotation;
        _following = true;
    }

    // ── Private Methods ──

    private async UniTaskVoid PlayIntroAsync()
    {
        if (_target == null) return;
        _introPlaying = true;
        _following = false;

        // 시작 위치: 플레이어 위 + 뒤로 멀리
        Vector3 targetPos = _target.position + _offset;
        Vector3 introOffset = new Vector3(0f, introStartHeight, -introStartDistance);
        Vector3 startPos = _target.position + introOffset;

        transform.position = startPos;
        transform.rotation = _rotation;

        // 줌인 (멀리서 → 플레이어 오프셋 위치로)
        float elapsed = 0f;
        while (elapsed < introDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / introDuration;
            // EaseOutCubic
            float ease = 1f - (1f - t) * (1f - t) * (1f - t);

            transform.position = Vector3.Lerp(startPos, targetPos, ease);
            await UniTask.Yield();
        }

        transform.position = targetPos;
        _introPlaying = false;
        _following = true;
    }
}
