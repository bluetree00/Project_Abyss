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

    // ── Private ──
    private Vector3 _originalPosition;
    private Quaternion _originalRotation;
    private CinemachineFreeLook _cinemachine;
    private CinemachineBrain _brain;
    private Image _fadeOverlay;
    private Canvas _fadeCanvas;
    private bool _introStarted;
    private CinemachineFreeLook.Orbit[] _savedOrbits;
    private CancellationTokenSource _panCts;
    private int  _panVersion;
    private bool _isPanning;
    private bool _prePanBrainEnabled;
    private bool _prePanCmEnabled;

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
        _panCts?.Cancel();
        _panCts?.Dispose();
        _panCts = null;
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

    /// <summary>
    /// 스타트 방 캐릭터 선택 직후 호출. Cinemachine이 이미 player를 추적 중인 상태에서
    /// Brain을 끄고 현재 위치(Wisp 근처)에서 player 쪽으로 줌인 후 Cinemachine 복귀.
    /// _introStarted 상태와 무관하게 실행되며 OnIntroComplete는 발생시키지 않는다.
    /// </summary>
    public async UniTaskVoid PlayStartRoomIntroAsync(Transform target)
    {
        if (this == null || target == null) return;

        if (_brain != null)       _brain.enabled       = false;
        if (_cinemachine != null) _cinemachine.enabled = false;

        Vector3    startPos = transform.position;
        Quaternion startRot = transform.rotation;

        float elapsed = 0f;
        while (elapsed < introDuration)
        {
            if (this == null || target == null) break;
            elapsed += Time.deltaTime;
            float t    = Mathf.Clamp01(elapsed / introDuration);
            float ease = 1f - (1f - t) * (1f - t) * (1f - t);

            transform.position = Vector3.Lerp(startPos, target.position + _originalPosition, ease);
            transform.rotation = Quaternion.Slerp(startRot, _originalRotation, ease);

            await UniTask.Yield();
        }

        if (this == null) return;

        if (target != null)
        {
            transform.position = target.position + _originalPosition;
            transform.rotation = _originalRotation;
        }

        // 저장해둔 플레이어 오빗 복원 (Wisp 오빗으로 덮어썼던 것 되돌리기)
        if (_savedOrbits != null && _cinemachine != null)
        {
            _cinemachine.m_Orbits[0] = _savedOrbits[0];
            _cinemachine.m_Orbits[1] = _savedOrbits[1];
            _cinemachine.m_Orbits[2] = _savedOrbits[2];
            _savedOrbits = null;
        }

        if (_cinemachine != null) _cinemachine.enabled = true;
        if (_brain != null)       _brain.enabled       = true;
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
        System.Action onPanComplete = null)
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

        // 클로즈업 시 보스 가슴 높이를 바라보도록 lookAt 보정
        Vector3 lookAt  = customViewOffset.HasValue
            ? zoneCenter + new Vector3(0f, 1.5f, 0f)
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

        // Cinemachine 복귀
        if (_cinemachine != null) _cinemachine.enabled = true;
        if (_brain != null) _brain.enabled = true;
    }
}
