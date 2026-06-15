using System;
using System.Threading;
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

    [Header("Wisp FreeLook 오빗 (스타트 방 전용)")]
    [SerializeField] private Vector2 wispOrbitTop    = new Vector2(15f,  5f);   // x=height, y=radius
    [SerializeField] private Vector2 wispOrbitMiddle = new Vector2(10f,  6f);
    [SerializeField] private Vector2 wispOrbitBottom = new Vector2(5f,   5f);

    [Header("Start Room Tour (방 둘러보기 패닝)")]
    [SerializeField] private float startRoomTourDuration = 4.5f;  // 둘러보기 속도(느릴수록 길게)
    [SerializeField] private float startRoomTourRadius   = 12f;   // 더 안쪽으로 (방 내부 시점)
    [SerializeField] private float startRoomTourHeight   = 6f;    // 천장(약 11.9) 아래 방 내부로
    [SerializeField] private float startRoomTourArc      = 90f; // 좌우 스윕 각도(도)
    [SerializeField] private float startRoomWispBlendDuration = 1f; // 둘러보기 → Wisp 추적 전환 보간 시간
    [SerializeField] private float startRoomPlayerBlendDuration = 1.2f; // 둘러보기 → 플레이어(CombatGirl) 추적 전환 보간 시간

    [Header("Start Room Tour 시네마틱 프레임 (레터박스)")]
    [SerializeField] private float          startRoomTourFrameDuration = 0.5f;  // 바 슬라이드 인/아웃 시간
    [SerializeField] private float          startRoomTourFrameAspect   = 2.39f; // 목표 종횡비(시네마스코프)
    [SerializeField] private float          startRoomTourDimAlpha      = 0.2f;  // 미세 Dim 강도
    [SerializeField] private float          startRoomTourFadeInDuration = 0.8f; // 진입 검정 → 드러내기 시간
    [SerializeField] private AnimationCurve startRoomTourFrameEase     = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Boss Orbit FreeLook (Dragon Orbit Only)")]
    [SerializeField] private Vector2 bossOrbitTop    = new Vector2(8f, 8f);
    [SerializeField] private Vector2 bossOrbitMiddle = new Vector2(5.5f, 6.5f);
    [SerializeField] private Vector2 bossOrbitBottom = new Vector2(3f, 5f);

    // ── Private ──
    private Vector3 _originalPosition;
    private Quaternion _originalRotation;
    private CinemachineFreeLook _cinemachine;
    private CinemachineBrain _brain;
    private Camera _camera;
    private Image _fadeOverlay;
    private Canvas _fadeCanvas;
    private bool _introStarted;
    private CinemachineFreeLook.Orbit[] _savedOrbits;
    private CancellationTokenSource _panCts;
    private int  _panVersion;
    private bool _isPanning;
    private bool _prePanBrainEnabled;
    private bool _prePanCmEnabled;
    private bool _bossOrbitViewActive;
    private Transform _bossOrbitOwner;
    private Transform _savedBossFollow;
    private Transform _savedBossLookAt;
    private CinemachineFreeLook.Orbit[] _savedBossOrbits;
    private bool _topDownViewActive;
    private bool _savedBrainBeforeTopDown;
    private bool _savedCmBeforeTopDown;
    private Vector3 _savedCamPosBeforeTopDown;
    private Quaternion _savedCamRotBeforeTopDown;
    private Vector3 _savedFollowPosBeforeTopDown;
    private CancellationTokenSource _topDownReturnCts;
    private CancellationTokenSource _topDownAscendCts;

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
        _camera = GetComponent<Camera>();

        // Cinemachine 비활성 (인트로 끝까지)
        if (_brain != null) _brain.enabled = false;
        if (_cinemachine != null) _cinemachine.enabled = false;

        // 화면 검정 오버레이 생성
        CreateFadeOverlay();

        SubscribePlayerBound();
    }

    private void OnDestroy()
    {
        _panCts?.Cancel();
        _panCts?.Dispose();
        _panCts = null;
        _topDownAscendCts?.Cancel();
        _topDownAscendCts?.Dispose();
        _topDownAscendCts = null;
        _topDownReturnCts?.Cancel();
        _topDownReturnCts?.Dispose();
        _topDownReturnCts = null;
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
    /// 맵 등장 연출 직전에 호출. 카메라를 맵 중앙 위에 배치하고 검정 오버레이를 페이드아웃.
    /// 이후 OnPlayerBound → PlayIntroAsync는 현재 카메라 위치에서 플레이어 쪽으로 줌인만 수행한다.
    /// _introStarted 가 true(방 전환 등)이면 즉시 반환해 부작용 없음.
    /// </summary>
    /// <summary>플레이어 텔레포트(절차 방 전환 등) 직후 호출 — Cinemachine을 새 위치로 즉시 스냅(댐핑 패닝 방지).</summary>
    public void SnapToTarget()
    {
        if (_cinemachine == null) _cinemachine = FindObjectOfType<CinemachineFreeLook>(true);
        if (_cinemachine != null) _cinemachine.PreviousStateIsValid = false;
    }

    /// <summary>
    /// 시작방 투어 종료 후 게임플레이 FreeLook으로 카메라 제어권을 넘긴다.
    /// _introStarted를 점유해 레거시 OnPlayerBound→PlayIntroAsync 자동 줌인을 차단하고,
    /// 투어 종료 포즈에서 플레이어 추적 시점으로 부드럽게 보간한 뒤 Brain에 인계한다.
    /// </summary>
    public void HandToGameplayCamera(Transform follow)
    {
        _introStarted = true; // 레거시 줌인 인트로(OnPlayerBound) 차단

        if (_cinemachine == null) _cinemachine = FindObjectOfType<CinemachineFreeLook>(true);
        if (_brain == null) _brain = GetComponent<CinemachineBrain>();

        if (_cinemachine != null && follow != null)
        {
            _cinemachine.Follow = follow;
            _cinemachine.LookAt = follow;
        }

        // 투어 종료 포즈 → 플레이어 추적 시점 수동 보간 후 제어권 인계 (스냅 없는 전환 연출)
        BlendToGameplayAsync(this.GetCancellationTokenOnDestroy()).Forget();
    }

    /// <summary>둘러보기 종료 포즈에서 플레이어 추적 시점으로 보간 (HandToGameplayCamera에서 fire-and-forget).</summary>
    private async UniTaskVoid BlendToGameplayAsync(CancellationToken ct)
    {
        try { await BlendToActiveCameraAsync(startRoomPlayerBlendDuration, ct); }
        catch (OperationCanceledException) { }
    }

    public async UniTask PrepareMapViewAsync(Vector3 mapCenter, float fadeTime, CancellationToken ct)
    {
        if (_introStarted) return;
        if (_fadeOverlay == null) return;

        // 카메라를 맵 중앙 위 고정 위치에 배치 (플레이어 인트로와 동일한 높이/거리 오프셋 재사용)
        Vector3 watchPos = mapCenter + new Vector3(0f, introExtraHeight, -introExtraBack);
        Vector3 lookDir  = mapCenter - watchPos;
        transform.position = watchPos;
        if (lookDir.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.LookRotation(lookDir, Vector3.up);

        // 검정 → 투명 페이드
        _fadeOverlay.color = Color.black;
        float elapsed = 0f;
        while (elapsed < fadeTime)
        {
            if (this == null) return;
            elapsed += Time.deltaTime;
            _fadeOverlay.color = new Color(0f, 0f, 0f, 1f - Mathf.Clamp01(elapsed / fadeTime));
            await UniTask.Yield(ct);
        }
        if (this != null && _fadeOverlay != null)
            _fadeOverlay.color = Color.clear;
    }

    /// <summary>
    /// 카메라 자체 인트로 검정 오버레이(_fadeOverlay)를 즉시 투명 처리한다.
    /// 베이스캠프처럼 진입 연출을 ScreenFade로 처리해 카메라 인트로 페이드를 쓰지 않는 흐름에서,
    /// Awake가 만든 불투명 오버레이가 화면에 남는 것을 방지한다(레거시 줌인 인트로도 차단).
    /// </summary>
    public void ClearIntroFade()
    {
        _introStarted = true; // OnPlayerBound→PlayIntroAsync 자동 줌인 차단
        if (_fadeOverlay != null) _fadeOverlay.color = Color.clear;
    }

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
            // 플레이어 오빗 저장 후 Wisp 전용 값으로 교체
            _savedOrbits = new CinemachineFreeLook.Orbit[]
            {
                _cinemachine.m_Orbits[0],
                _cinemachine.m_Orbits[1],
                _cinemachine.m_Orbits[2],
            };
            _cinemachine.m_Orbits[0] = new CinemachineFreeLook.Orbit { m_Height = wispOrbitTop.x,    m_Radius = wispOrbitTop.y    };
            _cinemachine.m_Orbits[1] = new CinemachineFreeLook.Orbit { m_Height = wispOrbitMiddle.x, m_Radius = wispOrbitMiddle.y };
            _cinemachine.m_Orbits[2] = new CinemachineFreeLook.Orbit { m_Height = wispOrbitBottom.x, m_Radius = wispOrbitBottom.y };

            _cinemachine.Follow = wispTarget;
            _cinemachine.LookAt = wispTarget;

            // 즉시 스냅 대신 둘러보기 종료 포즈에서 Wisp 추적 시점으로 부드럽게 보간
            BlendToWispAsync(this.GetCancellationTokenOnDestroy()).Forget();
        }
        else if (wispTarget != null)
        {
            transform.position = wispTarget.position + _originalPosition;
            transform.rotation = _originalRotation;
            Debug.LogWarning("[GameCameraController] CinemachineFreeLook not found. Using direct camera fallback for Wisp.");
            if (_brain != null) _brain.enabled = true;
        }
        else if (_brain != null)
        {
            _brain.enabled = true;
        }
    }

    /// <summary>둘러보기 종료 포즈에서 Wisp 추적 시점으로 보간 (ActivateForStartRoom에서 fire-and-forget).</summary>
    private async UniTaskVoid BlendToWispAsync(CancellationToken ct)
    {
        try { await BlendToActiveCameraAsync(startRoomWispBlendDuration, ct); }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// 현재(수동) 카메라 포즈에서 활성 Cinemachine 시점으로 수동 보간한 뒤 제어권을 인계한다.
    /// Brain을 끈 채 매 프레임 vcam의 해석 포즈(FinalPosition/Orientation)를 갱신·추종하므로
    /// 타깃(Wisp/플레이어)이 움직여도 끝점이 어긋나지 않는다. 호출 전 Follow/LookAt·오빗을 설정할 것.
    /// </summary>
    private async UniTask BlendToActiveCameraAsync(float duration, CancellationToken ct)
    {
        if (_cinemachine == null) return;

        if (_brain != null) _brain.enabled = false;
        _cinemachine.enabled = true; // vcam이 State를 계산하도록 활성화 (Brain은 꺼두어 스냅 방지)

        Vector3    fromPos = transform.position;
        Quaternion fromRot = transform.rotation;
        float dur = Mathf.Max(0.01f, duration);

        float t = 0f;
        while (t < dur)
        {
            if (this == null) return;
            ct.ThrowIfCancellationRequested();
            t += Time.deltaTime;
            float k    = Mathf.Clamp01(t / dur);
            float ease = 1f - (1f - k) * (1f - k) * (1f - k); // easeOutCubic

            _cinemachine.InternalUpdateCameraState(Vector3.up, Time.deltaTime);
            Vector3    toPos = _cinemachine.State.FinalPosition;
            Quaternion toRot = _cinemachine.State.FinalOrientation;

            transform.position = Vector3.Lerp(fromPos, toPos, ease);
            transform.rotation = Quaternion.Slerp(fromRot, toRot, ease);
            await UniTask.Yield();
        }

        if (this == null) return;

        // Cinemachine에 제어권 인계 — 카메라가 이미 목표 포즈에 도달했으므로 스냅해도 끊김 없음
        _cinemachine.PreviousStateIsValid = false;
        if (_brain != null) _brain.enabled = true;
    }

    public void ActivateBossOrbitView(Transform bossTarget, Transform lookAtTarget = null)
    {
        if (bossTarget == null || _isPanning)
            return;

        EnsureCinemachineRefs();
        if (_cinemachine == null)
            return;

        if (!_bossOrbitViewActive)
        {
            _savedBossFollow = _cinemachine.Follow;
            _savedBossLookAt = _cinemachine.LookAt;
            _savedBossOrbits = new CinemachineFreeLook.Orbit[]
            {
                _cinemachine.m_Orbits[0],
                _cinemachine.m_Orbits[1],
                _cinemachine.m_Orbits[2],
            };
            _bossOrbitViewActive = true;
        }

        _bossOrbitOwner = bossTarget;
        _cinemachine.m_Orbits[0] = new CinemachineFreeLook.Orbit { m_Height = bossOrbitTop.x,    m_Radius = bossOrbitTop.y };
        _cinemachine.m_Orbits[1] = new CinemachineFreeLook.Orbit { m_Height = bossOrbitMiddle.x, m_Radius = bossOrbitMiddle.y };
        _cinemachine.m_Orbits[2] = new CinemachineFreeLook.Orbit { m_Height = bossOrbitBottom.x, m_Radius = bossOrbitBottom.y };
        _cinemachine.Follow = bossTarget;
        _cinemachine.LookAt = lookAtTarget != null ? lookAtTarget : bossTarget;
        _cinemachine.enabled = true;
        if (_brain != null)
            _brain.enabled = true;
    }

    [Header("Dragon Top-Down View")]
    [SerializeField] private float topDownHeight = 35f;
    [SerializeField] private float topDownAscendDuration = 1.2f;

    /// <summary>
    /// 드래곤 탑다운 뷰를 활성화한다.
    /// floorSize(월드 단위, x=가로/z=세로)를 지정하면 카메라 FOV/Aspect 기준으로
    /// Floor 전체가 화면에 들어오는 높이를 계산한다. 생략 시 topDownHeight 고정값을 사용한다.
    /// </summary>
    public void ActivateDragonTopDownView(Vector3 mapCenter, Vector2 floorSize = default)
    {
        if (_topDownViewActive) return;
        EnsureCinemachineRefs();

        // 복귀 중 재활성 시 brain/cm가 꺼진 채 저장되는 버그 방지:
        // 이전 저장값에 따라 brain/cm를 먼저 복원한 뒤 새 상태를 저장한다.
        _topDownReturnCts?.Cancel();
        _topDownAscendCts?.Cancel();
        if (_brain       != null && !_brain.enabled       && _savedBrainBeforeTopDown) _brain.enabled       = true;
        if (_cinemachine != null && !_cinemachine.enabled && _savedCmBeforeTopDown)    _cinemachine.enabled = true;

        _savedBrainBeforeTopDown     = _brain       != null && _brain.enabled;
        _savedCmBeforeTopDown        = _cinemachine != null && _cinemachine.enabled;
        _savedCamPosBeforeTopDown    = transform.position;
        _savedCamRotBeforeTopDown    = transform.rotation;
        _savedFollowPosBeforeTopDown = _cinemachine?.Follow != null
            ? _cinemachine.Follow.position
            : transform.position;

        if (_brain       != null) _brain.enabled       = false;
        if (_cinemachine != null) _cinemachine.enabled = false;

        float height = floorSize.sqrMagnitude > 0f ? ComputeTopDownHeight(floorSize) : topDownHeight;

        // 중간 상태 없이 즉시 탑뷰 스냅 (비동기 상승 애니메이션 제거)
        transform.position = new Vector3(mapCenter.x, mapCenter.y + height, mapCenter.z);
        transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        _topDownViewActive = true;
    }

    /// <summary>Floor 전체(XZ)가 화면에 들어오도록 카메라 FOV/Aspect 기준으로 필요한 높이를 계산한다.</summary>
    private float ComputeTopDownHeight(Vector2 floorSize)
    {
        if (_camera == null) return topDownHeight;

        float tanHalfVFov = Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        if (tanHalfVFov <= 0f) return topDownHeight;

        float heightForDepth = floorSize.y / (2f * tanHalfVFov);
        float heightForWidth = floorSize.x / (2f * tanHalfVFov * _camera.aspect);
        return Mathf.Max(heightForDepth, heightForWidth);
    }

    private async UniTaskVoid ActivateDragonTopDownViewAsync(Vector3 mapCenter)
    {
        _topDownAscendCts?.Cancel();
        _topDownAscendCts?.Dispose();
        _topDownAscendCts = new CancellationTokenSource();
        var token = _topDownAscendCts.Token;

        Vector3    startPos  = transform.position;
        Quaternion startRot  = transform.rotation;
        Vector3    targetPos = new Vector3(mapCenter.x, mapCenter.y + topDownHeight, mapCenter.z);
        Quaternion targetRot = Quaternion.Euler(90f, 0f, 0f);
        float      duration  = Mathf.Max(0.01f, topDownAscendDuration);

        try
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                token.ThrowIfCancellationRequested();
                elapsed += Time.unscaledDeltaTime;
                float ease = PanEase(elapsed / duration);
                transform.position = Vector3.Lerp(startPos, targetPos, ease);
                transform.rotation = Quaternion.Slerp(startRot, targetRot, ease);
                await UniTask.Yield(token);
            }
            transform.position = targetPos;
            transform.rotation = targetRot;
        }
        catch (OperationCanceledException) { }
    }

    public void DeactivateDragonTopDownView(float duration)
    {
        // duration 무시 — 중간 상태 없이 즉시 플레이어 시점으로 복귀
        DeactivateDragonTopDownView();
    }

    public void DeactivateDragonTopDownView()
    {
        if (!_topDownViewActive) return;
        EnsureCinemachineRefs();
        _topDownAscendCts?.Cancel();
        _topDownReturnCts?.Cancel();

        // 플레이어 현재 위치 기준으로 카메라 복원 (저장 당시 오프셋 유지)
        if (_cinemachine?.Follow != null)
        {
            Vector3 offset = _savedCamPosBeforeTopDown - _savedFollowPosBeforeTopDown;
            transform.position = _cinemachine.Follow.position + offset;
        }
        else
        {
            transform.position = _savedCamPosBeforeTopDown;
        }
        transform.rotation = _savedCamRotBeforeTopDown;

        if (_brain       != null && _savedBrainBeforeTopDown) _brain.enabled       = true;
        if (_cinemachine != null && _savedCmBeforeTopDown)    _cinemachine.enabled = true;

        _topDownViewActive = false;
    }

    private async UniTaskVoid DeactivateDragonTopDownViewAsync(float duration)
    {
        if (!_topDownViewActive) return;
        EnsureCinemachineRefs();

        _topDownAscendCts?.Cancel();
        _topDownReturnCts?.Cancel();
        _topDownReturnCts?.Dispose();
        _topDownReturnCts = new CancellationTokenSource();
        var token = _topDownReturnCts.Token;

        Vector3 startPos = transform.position;
        Quaternion startRot = transform.rotation;
        // follow 타겟의 현재 위치 기준으로 목표 계산 → 활성화 시점의 stale 위치 대신 현재 위치 반영
        Vector3 targetPos;
        if (_cinemachine?.Follow != null)
        {
            Vector3 camToFollowOffset = _savedCamPosBeforeTopDown - _savedFollowPosBeforeTopDown;
            targetPos = _cinemachine.Follow.position + camToFollowOffset;
        }
        else
        {
            targetPos = _savedCamPosBeforeTopDown;
        }
        Quaternion targetRot = _savedCamRotBeforeTopDown;
        bool restoreBrain = _savedBrainBeforeTopDown;
        bool restoreCm = _savedCmBeforeTopDown;

        _topDownViewActive = false;
        if (_brain != null) _brain.enabled = false;
        if (_cinemachine != null) _cinemachine.enabled = false;

        try
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                token.ThrowIfCancellationRequested();
                elapsed += Time.unscaledDeltaTime;
                float ease = PanEase(elapsed / duration);
                transform.position = Vector3.Lerp(startPos, targetPos, ease);
                transform.rotation = Quaternion.Slerp(startRot, targetRot, ease);
                await UniTask.Yield(token);
            }

            transform.position = targetPos;
            transform.rotation = targetRot;

            if (_brain != null && restoreBrain) _brain.enabled = true;
            if (_cinemachine != null && restoreCm) _cinemachine.enabled = true;
        }
        catch (OperationCanceledException) { }
    }

    public void DeactivateBossOrbitView(Transform bossTarget = null)
    {
        if (!_bossOrbitViewActive)
            return;
        if (bossTarget != null && _bossOrbitOwner != null && bossTarget != _bossOrbitOwner)
            return;

        EnsureCinemachineRefs();
        if (_cinemachine != null)
        {
            if (_savedBossOrbits != null && _savedBossOrbits.Length == 3)
            {
                _cinemachine.m_Orbits[0] = _savedBossOrbits[0];
                _cinemachine.m_Orbits[1] = _savedBossOrbits[1];
                _cinemachine.m_Orbits[2] = _savedBossOrbits[2];
            }
            _cinemachine.Follow = _savedBossFollow;
            _cinemachine.LookAt = _savedBossLookAt;
        }

        _bossOrbitViewActive = false;
        _bossOrbitOwner = null;
        _savedBossFollow = null;
        _savedBossLookAt = null;
        _savedBossOrbits = null;
    }

    /// <summary>
    /// 스타트 방 캐릭터 선택 직후 호출. Cinemachine이 이미 player를 추적 중인 상태에서
    /// Brain을 끄고 현재 위치(Wisp 근처)에서 player 쪽으로 줌인 후 Cinemachine 복귀.
    /// _introStarted 상태와 무관하게 실행되며 OnIntroComplete는 발생시키지 않는다.
    /// </summary>
    public async UniTaskVoid PlayStartRoomIntroAsync(Transform target)
    {
        if (this == null || target == null) return;

        // Wisp 오빗으로 덮어썼던 플레이어 오빗 복원 — resolved 포즈가 플레이어 시점이 되도록 보간 전에 먼저
        if (_savedOrbits != null && _cinemachine != null)
        {
            _cinemachine.m_Orbits[0] = _savedOrbits[0];
            _cinemachine.m_Orbits[1] = _savedOrbits[1];
            _cinemachine.m_Orbits[2] = _savedOrbits[2];
            _savedOrbits = null;
        }
        if (_cinemachine != null)
        {
            _cinemachine.Follow = target;
            _cinemachine.LookAt = target;
        }

        // Wisp 전환과 동일하게 실제 Cinemachine 해석 포즈로 수렴 (고정 오프셋 스냅 제거 → 핸드오프 튐 없음)
        try { await BlendToActiveCameraAsync(introDuration, this.GetCancellationTokenOnDestroy()); }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// 시작방(허브) 진입 시 방 내부를 천천히 둘러보는 패닝 연출.
    /// center를 중심으로 좌우로 호(arc)를 그리며 스윕한다. Cinemachine을 잠시 끄고 수동 이동.
    /// _introStarted를 건드리지 않으므로 이후 ActivateForStartRoom(Wisp 추적)이 정상 동작한다.
    /// </summary>
    public async UniTask PlayStartRoomTourAsync(Vector3 center, CancellationToken ct = default)
    {
        if (this == null || startRoomTourDuration <= 0f) return;

        if (_cinemachine == null) _cinemachine = FindObjectOfType<CinemachineFreeLook>(true);
        if (_brain == null)       _brain       = GetComponent<CinemachineBrain>();
        if (_brain != null)       _brain.enabled       = false;
        if (_cinemachine != null) _cinemachine.enabled = false;

        float startA = -startRoomTourArc * 0.5f;
        float endA   =  startRoomTourArc * 0.5f;

        // 첫 카메라 위치를 둘러보기 시작 포즈로 맞춤 (부감 → 둘러보기 점프 제거)
        {
            float a0 = startA * Mathf.Deg2Rad;
            Vector3 startPos = center + new Vector3(Mathf.Sin(a0) * startRoomTourRadius, startRoomTourHeight, -Mathf.Cos(a0) * startRoomTourRadius);
            transform.position = startPos;
            transform.rotation = Quaternion.LookRotation(center - startPos, Vector3.up);
        }

        try
        {
            // 시네마틱 진입 — 레터박스 인 + 검정 페이드아웃(드러내기)을 패닝과 함께 동시에
            CinematicFrame.ShowAsync(
                startRoomTourFrameDuration, startRoomTourFrameEase, ct,
                startRoomTourFrameAspect, startRoomTourDimAlpha).Forget();
            ScreenFade.In(startRoomTourFadeInDuration, ct).Forget();

            float t = 0f;
            while (t < startRoomTourDuration)
            {
                if (this == null) return;
                ct.ThrowIfCancellationRequested();
                t += Time.deltaTime;
                float k = PanEase(t / startRoomTourDuration);
                float a = Mathf.Lerp(startA, endA, k) * Mathf.Deg2Rad;
                Vector3 pos = center + new Vector3(Mathf.Sin(a) * startRoomTourRadius, startRoomTourHeight, -Mathf.Cos(a) * startRoomTourRadius);
                transform.position = pos;
                transform.rotation = Quaternion.LookRotation(center - pos, Vector3.up);
                await UniTask.Yield();
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            // 취소·완료 모두 레터박스 아웃 (둘러보기 종료 → 조작 복귀 신호)
            await CinematicFrame.HideAsync(startRoomTourFrameDuration, startRoomTourFrameEase);
        }
    }

    /// <summary>
    /// Cinemachine을 일시 정지하고 카메라를 zoneCenter 위로 이동해 연출을 보여준 뒤 플레이어 추적으로 복귀.
    /// 신규 존 등장 연출(SpawnZoneByIndexAsync)에서 디졸브와 병렬로 호출된다.
    /// </summary>
    /// <param name="customViewOffset">
    /// null이면 기본 광각 부감 시점(introExtraHeight/Back 사용).
    /// 값을 넣으면 zoneCenter 기준 오프셋으로 카메라를 배치 — 보스 클로즈업 등에 사용.
    /// </param>
    /// <summary>팬 완료 직후 (홀드 시작 전) 호출할 콜백. 보스 등장 연출 트리거 등에 사용.</summary>
    public async UniTask PanToZoneAndReturnAsync(
        Vector3 zoneCenter,
        float moveDuration,
        float holdDuration,
        float returnDuration,
        Transform playerTransform,
        CancellationToken ct,
        Vector3? customViewOffset = null,
        System.Action onPanComplete = null,
        Vector3? customLookOffset = null)
    {
        if (this == null) return;

        // 진행 중인 팬이 있으면 취소하고 새 팬으로 교체.
        // 첫 팬 시작 시에만 Cinemachine 원래 상태를 기록해 마지막 팬이 복원한다.
        if (!_isPanning)
        {
            _prePanBrainEnabled = _brain != null && _brain.enabled;
            _prePanCmEnabled    = _cinemachine != null && _cinemachine.enabled;
        }
        _panCts?.Cancel();
        _panCts?.Dispose();
        _panCts = new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_panCts.Token, ct);
        int myVersion = System.Threading.Interlocked.Increment(ref _panVersion);
        _isPanning = true;

        if (_brain != null)       _brain.enabled       = false;
        if (_cinemachine != null) _cinemachine.enabled = false;

        Vector3    fromPos = transform.position;
        Quaternion fromRot = transform.rotation;

        // customViewOffset 미제공 시 기존 광각 부감 오프셋 사용
        Vector3 toPos   = customViewOffset.HasValue
            ? zoneCenter + customViewOffset.Value
            : zoneCenter + new Vector3(0f, introExtraHeight, -introExtraBack);

        // 클로즈업 시 보스 가슴 높이(또는 customLookOffset)를 바라보도록 lookAt 보정
        Vector3 lookAt  = customViewOffset.HasValue
            ? zoneCenter + (customLookOffset ?? new Vector3(0f, 1.5f, 0f))
            : zoneCenter;
        Vector3 lookDir = lookAt - toPos;
        Quaternion toRot = lookDir.sqrMagnitude > 0.01f
            ? Quaternion.LookRotation(lookDir, Vector3.up)
            : fromRot;

        try
        {
            // 1) 존으로 이동
            for (float t = 0f; t < moveDuration; t += Time.deltaTime)
            {
                linked.Token.ThrowIfCancellationRequested();
                float ease = PanEase(t / moveDuration);
                transform.position = Vector3.Lerp(fromPos, toPos, ease);
                transform.rotation = Quaternion.Slerp(fromRot, toRot, ease);
                await UniTask.Yield(linked.Token);
            }
            transform.position = toPos;
            transform.rotation = toRot;

            // 팬 완료 콜백 (보스 등장 연출 등 — 줌된 상태에서 시작하도록)
            onPanComplete?.Invoke();

            // 2) 존 조망 유지
            await UniTask.Delay(TimeSpan.FromSeconds(holdDuration), cancellationToken: linked.Token);

            // 3) 플레이어 위치로 복귀
            Vector3    retStart    = transform.position;
            Quaternion retStartRot = transform.rotation;
            Vector3    retEnd      = playerTransform != null
                ? playerTransform.position + _originalPosition
                : fromPos;

            for (float t = 0f; t < returnDuration; t += Time.deltaTime)
            {
                linked.Token.ThrowIfCancellationRequested();
                float ease = PanEase(t / returnDuration);
                transform.position = Vector3.Lerp(retStart, retEnd, ease);
                transform.rotation = Quaternion.Slerp(retStartRot, _originalRotation, ease);
                await UniTask.Yield(linked.Token);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            // 마지막 팬만 Cinemachine을 원래 상태로 복원한다.
            // 이전 팬은 새 팬이 이어받으므로 복원하지 않는다.
            if (myVersion == _panVersion)
            {
                _isPanning = false;
                if (_brain != null)
                {
                    if (playerTransform != null)
                    {
                        transform.position = playerTransform.position + _originalPosition;
                        transform.rotation = _originalRotation;
                    }
                    if (_prePanBrainEnabled) _brain.enabled = true;
                }
                if (_cinemachine != null && _prePanCmEnabled) _cinemachine.enabled = true;
            }
        }
    }

    /// <summary>
    /// 진행 중인 보스 등장 연출 카메라(팬)를 인수하여 플레이어 추적 위치/회전으로 복귀시키고 Cinemachine을 재개한다.
    /// </summary>
    public async UniTask ReturnToPlayerAsync(Transform playerTransform, float returnDuration, CancellationToken ct)
    {
        if (this == null) return;

        if (!_isPanning)
        {
            _prePanBrainEnabled = _brain != null && _brain.enabled;
            _prePanCmEnabled    = _cinemachine != null && _cinemachine.enabled;
        }
        _panCts?.Cancel();
        _panCts?.Dispose();
        _panCts = new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_panCts.Token, ct);
        int myVersion = System.Threading.Interlocked.Increment(ref _panVersion);
        _isPanning = true;

        if (_brain != null)       _brain.enabled       = false;
        if (_cinemachine != null) _cinemachine.enabled = false;

        Vector3    fromPos = transform.position;
        Quaternion fromRot = transform.rotation;
        Vector3    toPos   = playerTransform != null ? playerTransform.position + _originalPosition : fromPos;
        Quaternion toRot   = _originalRotation;

        try
        {
            for (float t = 0f; t < returnDuration; t += Time.deltaTime)
            {
                linked.Token.ThrowIfCancellationRequested();
                float ease = PanEase(t / returnDuration);
                transform.position = Vector3.Lerp(fromPos, toPos, ease);
                transform.rotation = Quaternion.Slerp(fromRot, toRot, ease);
                await UniTask.Yield(linked.Token);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (myVersion == _panVersion)
            {
                _isPanning = false;
                if (playerTransform != null)
                {
                    transform.position = playerTransform.position + _originalPosition;
                    transform.rotation = _originalRotation;
                }
                if (_brain != null && _prePanBrainEnabled) _brain.enabled = true;
                if (_cinemachine != null && _prePanCmEnabled) _cinemachine.enabled = true;
            }
        }
    }

    private static float PanEase(float t) => 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f);

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

        // PrepareMapViewAsync가 이미 카메라를 맵 위에 배치했을 수 있으므로
        // 현재 위치에서 시작 — 고정 오프셋 재계산을 하지 않아 snap 없이 부드럽게 이어짐
        Vector3 startPos = transform.position;
        Quaternion startRot = transform.rotation;

        Vector3 offset = _originalPosition;
        Quaternion endRot = _originalRotation;

        // PrepareMapViewAsync 미사용 시: 현재 위치가 씬 기본 카메라 위치 → 인트로 시작점 그대로 사용
        // PrepareMapViewAsync 사용 시: 현재 위치가 맵 위 → 거기서 플레이어 쪽으로 줌인

        // 줌인 + 조건부 페이드인
        // _fadeOverlay가 이미 투명(PrepareMapViewAsync 완료)이면 페이드 재실행 없이 줌만 수행
        bool needFade = _fadeOverlay != null && _fadeOverlay.color.a > 0.01f;

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

            // 페이드인 — 오버레이가 이미 투명하면 건너뜀 (맵 등장 연출 후 진입 경로)
            if (needFade && _fadeOverlay != null)
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

        // 탑다운 활성 중이면 Cinemachine 복귀 스킵 — 이후 DeactivateDragonTopDownView가 복원
        if (!_topDownViewActive)
        {
            if (_cinemachine != null) _cinemachine.enabled = true;
            if (_brain != null)       _brain.enabled       = true;
        }
    }

    private void EnsureCinemachineRefs()
    {
        if (_cinemachine == null)
            _cinemachine = FindObjectOfType<CinemachineFreeLook>(true);
        if (_brain == null)
            _brain = GetComponent<CinemachineBrain>();
    }
}
