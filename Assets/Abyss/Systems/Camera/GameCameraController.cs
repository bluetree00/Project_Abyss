using System;
using UnityEngine;
using UnityEngine.UI;
using Cinemachine;
using Cysharp.Threading.Tasks;

/// <summary>
/// 게임 카메라 컨트롤러.
/// - 씬 시작: 화면 검정 (페이드 오버레이)
/// - OnPlayerBound → 멀리서 줌인 + 페이드인
/// - 완료 후 Cinemachine에 제어권 반환
/// </summary>
public class GameCameraController : MonoBehaviour
{
    // ── SerializeField ──
    [Header("Intro")]
    [SerializeField] private float introExtraHeight = 30f;
    [SerializeField] private float introExtraBack = 10f;
    [SerializeField] private float introDuration = 2.5f;
    [SerializeField] private float fadeInStart = 0.2f;
    [SerializeField] private float fadeInDuration = 1.5f;

    // ── Private ──
    private Vector3 _originalPosition;
    private Quaternion _originalRotation;
    private CinemachineFreeLook _cinemachine;
    private CinemachineBrain _brain;
    private Image _fadeOverlay;
    private Canvas _fadeCanvas;
    private bool _introStarted;

    // ── Properties ──
    public static GameCameraController Instance { get; private set; }

    // ── Events ──
    /// <summary>카메라 인트로 줌인이 완전히 끝난 직후 발생</summary>
    public event Action OnIntroComplete;

    // ── Lifecycle ──

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        _originalPosition = transform.position;
        _originalRotation = transform.rotation;

        _cinemachine = FindObjectOfType<CinemachineFreeLook>(true);
        _brain = GetComponent<CinemachineBrain>();

        // Cinemachine 비활성 (인트로 끝까지)
        if (_brain != null) _brain.enabled = false;
        if (_cinemachine != null) _cinemachine.enabled = false;

        // 화면 검정 오버레이 생성
        CreateFadeOverlay();

        SubscribePlayerBound();
    }

    private void OnDestroy()
    {
        UnsubscribePlayerBound();
        if (_fadeCanvas != null) Destroy(_fadeCanvas.gameObject);
        if (Instance == this) Instance = null;
    }

    // ── Private Methods ──

    private void CreateFadeOverlay()
    {
        var go = new GameObject("IntroFade", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(Image));
        go.transform.SetParent(null);
        DontDestroyOnLoad(go);

        _fadeCanvas = go.GetComponent<Canvas>();
        _fadeCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _fadeCanvas.sortingOrder = 9999;

        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        _fadeOverlay = go.GetComponent<Image>();
        _fadeOverlay.color = Color.black;
        _fadeOverlay.raycastTarget = false;

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private void SubscribePlayerBound()
    {
        var bootstrapper = FindObjectOfType<GameRunBootstrapper>(true);
        if (bootstrapper != null && bootstrapper.Run != null)
        {
            bootstrapper.Run.OnPlayerBound += OnPlayerBound;
            return;
        }
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

    // ── Public Methods ──

    /// <summary>
    /// 스타트 방(Wisp) 진입 시 호출.
    /// 검정 오버레이를 즉시 제거하고 Cinemachine이 Wisp를 추적하도록 설정한다.
    /// </summary>
    public void ActivateForStartRoom(Transform wispTarget)
    {
        if (_introStarted) return;
        _introStarted = true;

        if (_cinemachine == null)
            _cinemachine = FindObjectOfType<CinemachineFreeLook>(true);
        if (_brain == null)
            _brain = GetComponent<CinemachineBrain>();

        if (_fadeCanvas != null)
        {
            Destroy(_fadeCanvas.gameObject);
            _fadeCanvas = null;
            _fadeOverlay = null;
        }

        if (wispTarget != null && _cinemachine != null)
        {
            _cinemachine.Follow = wispTarget;
            _cinemachine.LookAt = wispTarget;
            _cinemachine.enabled = true;
        }
        else if (wispTarget != null)
        {
            transform.position = wispTarget.position + _originalPosition;
            transform.rotation = _originalRotation;
            Debug.LogWarning("[GameCameraController] CinemachineFreeLook not found. Using direct camera fallback for Wisp.");
        }

        if (_brain != null)
            _brain.enabled = true;
    }

    // ── Event Handlers ──

    private void OnPlayerBound(PlayerController player)
    {
        if (this == null || player == null) return;
        if (_introStarted) return;
        _introStarted = true;
        PlayIntroAsync(player.transform).Forget();
    }

    private async UniTaskVoid PlayIntroAsync(Transform target)
    {
        if (this == null) return;

        // Cinemachine 확실히 OFF
        if (_cinemachine == null) _cinemachine = FindObjectOfType<CinemachineFreeLook>(true);
        if (_brain == null) _brain = GetComponent<CinemachineBrain>();
        if (_brain != null) _brain.enabled = false;
        if (_cinemachine != null) _cinemachine.enabled = false;

        // 오프셋 계산
        Vector3 offset = _originalPosition;
        Vector3 endPos = target.position + offset;
        Quaternion endRot = _originalRotation;

        // 시작 위치 (멀리)
        Vector3 startPos = endPos + new Vector3(0f, introExtraHeight, -introExtraBack);
        Vector3 lookDir = target.position - startPos;
        Quaternion startRot = lookDir.sqrMagnitude > 0.01f
            ? Quaternion.LookRotation(lookDir, Vector3.up)
            : endRot;

        transform.position = startPos;
        transform.rotation = startRot;

        // 줌인 + 페이드인
        float elapsed = 0f;
        while (elapsed < introDuration)
        {
            if (this == null || target == null) break;
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / introDuration);
            float ease = 1f - (1f - t) * (1f - t) * (1f - t);

            // 카메라 이동
            Vector3 currentEnd = target.position + offset;
            transform.position = Vector3.Lerp(startPos, currentEnd, ease);
            transform.rotation = Quaternion.Slerp(startRot, endRot, ease);

            // 페이드인 (fadeInStart 이후부터)
            if (_fadeOverlay != null)
            {
                float fadeT = Mathf.Clamp01((elapsed - fadeInStart) / fadeInDuration);
                _fadeOverlay.color = new Color(0, 0, 0, 1f - fadeT);
            }

            await UniTask.Yield();
        }

        if (this == null) return;

        // 완료
        if (target != null)
        {
            transform.position = target.position + offset;
            transform.rotation = endRot;
        }

        if (_fadeOverlay != null)
            _fadeOverlay.color = Color.clear;

        // 오버레이 제거
        if (_fadeCanvas != null)
        {
            Destroy(_fadeCanvas.gameObject);
            _fadeCanvas = null;
            _fadeOverlay = null;
        }

        // 인트로 완료 알림 (플레이어 등장 연출 트리거)
        OnIntroComplete?.Invoke();

        // Cinemachine 복귀
        if (_cinemachine != null) _cinemachine.enabled = true;
        if (_brain != null) _brain.enabled = true;
    }
}
