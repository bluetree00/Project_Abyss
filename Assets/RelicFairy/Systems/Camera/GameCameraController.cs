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

    [Header("Start Room Tour (방 둘러보기 패닝)")]
    [SerializeField] private float startRoomTourDuration = 4.5f;  // 둘러보기 속도(느릴수록 길게)
    [SerializeField] private float startRoomTourRadius   = 12f;   // 더 안쪽으로 (방 내부 시점)
    [SerializeField] private float startRoomTourHeight   = 6f;    // 천장(약 11.9) 아래 방 내부로
    [SerializeField] private float startRoomTourArc      = 90f; // 좌우 스윕 각도(도)
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

    [Header("DeathKnight Player Orbit (낮은 시야각 — 유리 너머 보스 확인용)")]
    [SerializeField] private Vector2 dkPlayerOrbitTop    = new Vector2(5f,  2f);
    [SerializeField] private Vector2 dkPlayerOrbitMiddle = new Vector2(5f,  2f);
    [SerializeField] private Vector2 dkPlayerOrbitBottom = new Vector2(5f, 4.7f);

    [Header("Processional View (대성당 나브 — 낮은 정면 웅장)")]
    [Tooltip("나브 구간 카메라 오빗(Height, Radius). 낮은 높이+큰 반경 = 낮게 뒤에서 정면. 인게임서 튜닝.")]
    [SerializeField] private Vector2 processionalOrbitTop      = new Vector2(5f, 12f);
    [SerializeField] private Vector2 processionalOrbitMiddle   = new Vector2(2.5f, 13f);
    [SerializeField] private Vector2 processionalOrbitBottom   = new Vector2(0.8f, 12f);
    // 참고: 이 구간에서 heading(수평각)은 건드리지 않는다. 시선 방향은 CameraHeadingZone이 전담한다.

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
    private CinemachineFreeLook.Orbit[] _savedDKPlayerOrbits;
    private CinemachineFreeLook.Orbit[] _savedProcessionalOrbits;

    // heading 회전 연출(RotateHeadingTo) 중복 실행 방지 — 새 요청이 오면 이전 회전을 취소한다.
    private CancellationTokenSource _headingCts;

    // 구역 카메라 진입 전 기본 오빗. 최초 ApplyZoneOrbit에서 1회 저장하고 계속 유지한다(구역 릴레이).
    private CinemachineFreeLook.Orbit[] _savedZoneOrbits;
    private CancellationTokenSource     _dkOrbitTransitionCts;
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

    /// <summary>
    /// 보스 시점·탑다운·패닝·DK 연출 등이 <b>FreeLook 궤도를 점유 중</b>인지.
    /// 전투 동적 프레이밍(CombatCameraFraming)은 이 동안 궤도를 건드리지 않고 양보한다
    /// — 안 그러면 연출이 저장/복원하는 궤도를 매 프레임 덮어써서 연출이 깨진다.
    /// </summary>
    public bool IsOrbitOverridden =>
        _bossOrbitViewActive || _topDownViewActive || _isPanning
        || _savedDKPlayerOrbits != null || _savedProcessionalOrbits != null;

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

        _cinemachine = FindFirstObjectByType<CinemachineFreeLook>(FindObjectsInactive.Include);
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
        _dkOrbitTransitionCts?.Cancel();
        _dkOrbitTransitionCts?.Dispose();
        _dkOrbitTransitionCts = null;
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
        _fadeCanvas.sortingOrder = UISortingOrder.CameraFade;

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
        var bootstrapper = FindFirstObjectByType<GameRunBootstrapper>(FindObjectsInactive.Include);
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
        if (_cinemachine == null) _cinemachine = FindFirstObjectByType<CinemachineFreeLook>(FindObjectsInactive.Include);
        if (_cinemachine != null) _cinemachine.PreviousStateIsValid = false;
    }

    /// <summary>
    /// 씬 시작 직후 플레이어 스폰 전에 호출. 카메라를 스폰 위치 기준 최종 위치로 즉시 이동.
    /// 씬 에디터 카메라 위치가 벽 안에 있어 관통하는 현상을 방지한다.
    /// </summary>
    public void PrePositionAtSpawn(Vector3 spawnPos)
    {
        transform.position = spawnPos + _originalPosition;
        transform.rotation = _originalRotation;
    }

    /// <summary>
    /// 시작방 투어 종료 후 게임플레이 FreeLook으로 카메라 제어권을 넘긴다.
    /// _introStarted를 점유해 레거시 OnPlayerBound→PlayIntroAsync 자동 줌인을 차단하고,
    /// 투어 종료 포즈에서 플레이어 추적 시점으로 부드럽게 보간한 뒤 Brain에 인계한다.
    /// </summary>
    /// <param name="alignHeadingToTarget">
    /// true면 heading을 대상 rotation으로 맞춘다(최초 스폰 — 카메라를 등 뒤로 정렬).
    /// false면 현재 heading을 유지한다(허브 재스폰 등 — 대상의 '바라보는 방향'으로 카메라가 튀는 것 방지).
    /// </param>
    public void HandToGameplayCamera(Transform follow, bool alignHeadingToTarget = true)
    {
        BindGameplayFollow(follow, alignHeadingToTarget);
        // 투어 종료 포즈 → 플레이어 추적 시점 수동 보간 후 제어권 인계 (스냅 없는 전환 연출)
        BlendToGameplayAsync(this.GetCancellationTokenOnDestroy()).Forget();
    }

    /// <summary>
    /// <see cref="HandToGameplayCamera"/>의 대기 가능 버전 — <b>보간이 끝나야 반환한다</b>.
    ///
    /// 컷신은 인계 뒤 곧바로 대사를 띄우는데, fire-and-forget 판은 완료를 알 방법이 없어 호출측이
    /// 고정 딜레이로 추측할 수밖에 없었다(블렌드와 딜레이가 같은 1.2초라 항상 덜 끝난 채 대사가 나갔다).
    /// 카메라가 자리를 잡은 뒤 대사가 나가야 하는 구간에서는 이쪽을 쓴다.
    /// </summary>
    public async UniTask HandToGameplayCameraAsync(Transform follow, bool alignHeadingToTarget = true,
                                                   CancellationToken ct = default)
    {
        BindGameplayFollow(follow, alignHeadingToTarget);
        try { await BlendToActiveCameraAsync(startRoomPlayerBlendDuration, ct); }
        catch (OperationCanceledException) { }
    }

    /// <summary>인계 공통 준비 — 레거시 줌인 차단 + FreeLook Follow/LookAt·heading 세팅.</summary>
    private void BindGameplayFollow(Transform follow, bool alignHeadingToTarget)
    {
        _introStarted = true; // 레거시 줌인 인트로(OnPlayerBound) 차단

        if (_cinemachine == null) _cinemachine = FindFirstObjectByType<CinemachineFreeLook>(FindObjectsInactive.Include);
        if (_brain == null) _brain = GetComponent<CinemachineBrain>();

        if (_cinemachine == null || follow == null) return;

        _cinemachine.Follow = follow;
        _cinemachine.LookAt = follow;

        // FreeLook 수평각은 이전 값을 그대로 유지하므로, 최초 인계 시엔 대상이 보는 방향으로 맞춰준다.
        // 단 허브 재스폰(유물 핫스왑 등)에서는 대상 rotation이 '플레이어가 제단을 보던 방향'이라
        // 이걸 heading으로 쓰면 카메라가 틀어진다 → 그때는 현재 heading을 유지한다(alignHeadingToTarget=false).
        if (alignHeadingToTarget)
            SetHeadingImmediate(follow.eulerAngles.y);
    }

    // ── Camera Zone (구역별 카메라) ─────────────────────────────
    /// <summary>
    /// 구역 진입 시 오빗을 그 구역 값으로 전환한다. <see cref="CameraZone"/>이 호출.
    /// 최초 1회 현재(기본) 오빗을 저장해 <see cref="RestoreZoneOrbit"/>의 복귀 기준으로 삼는다.
    /// 구역끼리는 릴레이(다음 구역이 덮어씀)이므로 저장값은 계속 유지한다.
    /// </summary>
    public void ApplyZoneOrbit(Vector2 top, Vector2 mid, Vector2 bot, float duration = 1.2f)
    {
        EnsureCinemachineRefs();
        if (_cinemachine == null) return;

        if (_savedZoneOrbits == null)
        {
            _savedZoneOrbits = new CinemachineFreeLook.Orbit[]
            {
                _cinemachine.m_Orbits[0],
                _cinemachine.m_Orbits[1],
                _cinemachine.m_Orbits[2],
            };
        }

        TransitionDKOrbitAsync(top, mid, bot, duration, this.GetCancellationTokenOnDestroy()).Forget();
    }

    /// <summary>구역 이탈 시 기본(구역 진입 전) 오빗으로 복귀. 저장값이 없으면 무시.</summary>
    public void RestoreZoneOrbit(float duration = 1.2f)
    {
        EnsureCinemachineRefs();
        if (_cinemachine == null || _savedZoneOrbits == null) return;

        var top = new Vector2(_savedZoneOrbits[0].m_Height, _savedZoneOrbits[0].m_Radius);
        var mid = new Vector2(_savedZoneOrbits[1].m_Height, _savedZoneOrbits[1].m_Radius);
        var bot = new Vector2(_savedZoneOrbits[2].m_Height, _savedZoneOrbits[2].m_Radius);
        TransitionDKOrbitAsync(top, mid, bot, duration, this.GetCancellationTokenOnDestroy()).Forget();
    }

    /// <summary>현재 FreeLook 수평 heading(도). 연속 보간(CameraBlendZone)에서 현재값 기준으로 damp할 때 사용.</summary>
    public float CurrentHeading
    {
        get
        {
            EnsureCinemachineRefs();
            return _cinemachine != null ? _cinemachine.m_XAxis.Value : 0f;
        }
    }

    /// <summary>
    /// heading 값만 갱신한다(스냅 플래그 없음). <see cref="CameraBlendZone"/>처럼 <b>매 프레임 비율로</b>
    /// 값을 넣는 용도. SetHeadingImmediate는 PreviousStateIsValid를 꺼서 보간을 끊으므로
    /// 매 프레임 호출하면 카메라가 떨린다 — 연속 갱신에는 이쪽을 쓴다.
    /// </summary>
    public void SetHeadingRaw(float yawDeg)
    {
        EnsureCinemachineRefs();
        if (_cinemachine == null) return;
        _cinemachine.m_XAxis.Value = yawDeg;
    }

    /// <summary>FreeLook 수평 heading(m_XAxis)을 즉시 지정 각도로 맞춘다. 스폰/텔레포트 직후용.</summary>
    public void SetHeadingImmediate(float yawDeg)
    {
        EnsureCinemachineRefs();
        if (_cinemachine == null) return;

        _cinemachine.m_XAxis.Value = yawDeg;
        _cinemachine.PreviousStateIsValid = false;   // 보간 없이 새 각도로 스냅
    }

    /// <summary>
    /// FreeLook 수평 heading을 목표 각도로 <b>부드럽게</b> 돌린다(연출용).
    /// 최단 회전 방향으로 보간하며, 도중 플레이어 입력이 있으면 중단하지 않는다(연출 우선).
    /// </summary>
    public void RotateHeadingTo(float targetYawDeg, float duration = 1.5f)
    {
        EnsureCinemachineRefs();
        if (_cinemachine == null) return;

        _headingCts?.Cancel();
        _headingCts?.Dispose();
        _headingCts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
        RotateHeadingAsync(targetYawDeg, duration, _headingCts.Token).Forget();
    }

    private async UniTaskVoid RotateHeadingAsync(float targetYawDeg, float duration, CancellationToken ct)
    {
        try
        {
            float start = _cinemachine.m_XAxis.Value;
            float delta = Mathf.DeltaAngle(start, targetYawDeg);   // 최단 방향
            if (duration <= 0f || Mathf.Abs(delta) < 0.1f)
            {
                _cinemachine.m_XAxis.Value = targetYawDeg;
                return;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                ct.ThrowIfCancellationRequested();
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
                _cinemachine.m_XAxis.Value = start + delta * t;
                await UniTask.Yield(ct);
            }
            _cinemachine.m_XAxis.Value = start + delta;
        }
        catch (OperationCanceledException) { }
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
    /// 현재(수동) 카메라 포즈에서 활성 Cinemachine 시점으로 수동 보간한 뒤 제어권을 인계한다.
    /// Brain을 끈 채 매 프레임 vcam의 해석 포즈(FinalPosition/Orientation)를 갱신·추종하므로
    /// 타깃(플레이어)이 움직여도 끝점이 어긋나지 않는다. 호출 전 Follow/LookAt·오빗을 설정할 것.
    /// </summary>
    private async UniTask BlendToActiveCameraAsync(float duration, CancellationToken ct)
    {
        if (_cinemachine == null) return;

        // 이 블렌드가 도는 중에 컷신이 TakeManualControl()을 걸 수 있다(부트 인계 ↔ 오프닝 연출 경합).
        // 버전을 캡처해 두고, 중간에 바뀌면 즉시 물러난다. 이게 없으면 블렌드가 끝나며
        // Brain을 다시 켜고 _isPanning을 풀어버려 컷신 카메라가 통째로 무시된다.
        int myVersion = _panVersion;

        if (_brain != null) _brain.enabled = false;
        _cinemachine.enabled = true; // vcam이 State를 계산하도록 활성화 (Brain은 꺼두어 스냅 방지)

        Vector3    fromPos = transform.position;
        Quaternion fromRot = transform.rotation;
        float dur = Mathf.Max(0.01f, duration);

        float t = 0f;
        while (t < dur)
        {
            if (this == null) return;
            if (myVersion != _panVersion) return;   // 컷신이 제어권을 가져갔다
            ct.ThrowIfCancellationRequested();
            // ⚠️ <b>unscaled 고정.</b> 이 블렌드는 fire-and-forget으로 돌고, 호출측(대기방 진입)은
            //    곧바로 대사 팝업을 띄운다. 대사 팝업은 BlocksGameplay라 timeScale을 0으로 잡는다 —
            //    scaled 시간을 쓰면 t가 한 프레임도 늘지 않아 <b>블렌드가 중간 포즈에서 얼어붙는다.</b>
            //    카메라가 정면으로 돌아오지 못한 채 대사 내내 비스듬히 고정되던 원인(2026-08-20 QA).
            //    Brain 재활성·_isPanning 해제도 루프 뒤에 있어 그동안 통째로 보류된다.
            float dt = Time.unscaledDeltaTime;
            t += dt;
            float k    = Mathf.Clamp01(t / dur);
            float ease = 1f - (1f - k) * (1f - k) * (1f - k); // easeOutCubic

            _cinemachine.InternalUpdateCameraState(Vector3.up, dt);
            Vector3    toPos = _cinemachine.State.FinalPosition;
            Quaternion toRot = _cinemachine.State.FinalOrientation;

            transform.position = Vector3.Lerp(fromPos, toPos, ease);
            transform.rotation = Quaternion.Slerp(fromRot, toRot, ease);
            await UniTask.Yield();
        }

        if (this == null) return;
        if (myVersion != _panVersion) return;   // 컷신이 제어권을 가져갔다면 인계하지 않는다

        // Cinemachine에 제어권 인계 — 카메라가 이미 목표 포즈에 도달했으므로 스냅해도 끊김 없음
        _cinemachine.PreviousStateIsValid = false;
        if (_brain != null) _brain.enabled = true;

        // 수동 제어(TakeManualControl) 잠금 해제 — 이게 없으면 _isPanning이 true로 남아
        // 이후 보스전 오빗 전환·구역 카메라가 전부 무시되고 카메라가 고정된다.
        _isPanning = false;
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

        _topDownViewActive = true;
        ActivateDragonTopDownViewAsync(mapCenter, height).Forget();
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

    private async UniTaskVoid ActivateDragonTopDownViewAsync(Vector3 mapCenter, float height)
    {
        _topDownAscendCts?.Cancel();
        _topDownAscendCts?.Dispose();
        _topDownAscendCts = new CancellationTokenSource();
        var token = _topDownAscendCts.Token;

        Vector3    startPos  = transform.position;
        Quaternion startRot  = transform.rotation;
        Vector3    targetPos = new Vector3(mapCenter.x, mapCenter.y + height, mapCenter.z);
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
        if (duration <= 0f)
            DeactivateDragonTopDownView();
        else
            DeactivateDragonTopDownViewAsync(duration).Forget();
    }

    public void DeactivateDragonTopDownView()
    {
        DeactivateDragonTopDownViewAsync(topDownAscendDuration).Forget();
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
    /// DK Phase1 고정위치 텔레포트 시 호출. 플레이어 추적은 유지한 채 오빗 높이만 낮춰
    /// 유리 너머 보스를 볼 수 있는 시야각으로 서서히 전환한다.
    /// </summary>
    public void ActivateDKPlayerOrbit(float duration = 1.0f)
    {
        EnsureCinemachineRefs();
        if (_cinemachine == null) return;

        if (_savedDKPlayerOrbits == null)
        {
            _savedDKPlayerOrbits = new CinemachineFreeLook.Orbit[]
            {
                _cinemachine.m_Orbits[0],
                _cinemachine.m_Orbits[1],
                _cinemachine.m_Orbits[2],
            };
        }

        TransitionDKOrbitAsync(
            dkPlayerOrbitTop, dkPlayerOrbitMiddle, dkPlayerOrbitBottom,
            duration, this.GetCancellationTokenOnDestroy()).Forget();
    }

    /// <summary>DK 보스가 플레이어 근처로 텔레포트하거나 사망 시 호출. 원래 플레이어 오빗으로 서서히 복원.</summary>
    public void DeactivateDKPlayerOrbit(float duration = 1.0f)
    {
        EnsureCinemachineRefs();
        if (_cinemachine == null || _savedDKPlayerOrbits == null) return;

        var targetTop    = new Vector2(_savedDKPlayerOrbits[0].m_Height, _savedDKPlayerOrbits[0].m_Radius);
        var targetMiddle = new Vector2(_savedDKPlayerOrbits[1].m_Height, _savedDKPlayerOrbits[1].m_Radius);
        var targetBottom = new Vector2(_savedDKPlayerOrbits[2].m_Height, _savedDKPlayerOrbits[2].m_Radius);
        _savedDKPlayerOrbits = null;

        TransitionDKOrbitAsync(targetTop, targetMiddle, targetBottom,
            duration, this.GetCancellationTokenOnDestroy()).Forget();
    }

    private async UniTaskVoid TransitionDKOrbitAsync(
        Vector2 targetTop, Vector2 targetMiddle, Vector2 targetBottom,
        float duration, CancellationToken destroyCt)
    {
        _dkOrbitTransitionCts?.Cancel();
        _dkOrbitTransitionCts?.Dispose();
        _dkOrbitTransitionCts = CancellationTokenSource.CreateLinkedTokenSource(destroyCt);
        var token = _dkOrbitTransitionCts.Token;

        if (_cinemachine == null) return;

        var fromTop    = new Vector2(_cinemachine.m_Orbits[0].m_Height, _cinemachine.m_Orbits[0].m_Radius);
        var fromMiddle = new Vector2(_cinemachine.m_Orbits[1].m_Height, _cinemachine.m_Orbits[1].m_Radius);
        var fromBottom = new Vector2(_cinemachine.m_Orbits[2].m_Height, _cinemachine.m_Orbits[2].m_Radius);
        float dur = Mathf.Max(0.01f, duration);

        try
        {
            for (float t = 0f; t < dur; t += Time.deltaTime)
            {
                token.ThrowIfCancellationRequested();
                float k    = Mathf.Clamp01(t / dur);
                float ease = k * k * (3f - 2f * k); // smoothstep

                var top = Vector2.Lerp(fromTop,    targetTop,    ease);
                var mid = Vector2.Lerp(fromMiddle, targetMiddle, ease);
                var bot = Vector2.Lerp(fromBottom, targetBottom, ease);

                _cinemachine.m_Orbits[0] = new CinemachineFreeLook.Orbit { m_Height = top.x, m_Radius = top.y };
                _cinemachine.m_Orbits[1] = new CinemachineFreeLook.Orbit { m_Height = mid.x, m_Radius = mid.y };
                _cinemachine.m_Orbits[2] = new CinemachineFreeLook.Orbit { m_Height = bot.x, m_Radius = bot.y };

                await UniTask.Yield(token);
            }

            _cinemachine.m_Orbits[0] = new CinemachineFreeLook.Orbit { m_Height = targetTop.x,    m_Radius = targetTop.y };
            _cinemachine.m_Orbits[1] = new CinemachineFreeLook.Orbit { m_Height = targetMiddle.x, m_Radius = targetMiddle.y };
            _cinemachine.m_Orbits[2] = new CinemachineFreeLook.Orbit { m_Height = targetBottom.x, m_Radius = targetBottom.y };
        }
        catch (OperationCanceledException) { }
    }

    // ── Processional View (대성당 나브) ─────────────────────────
    /// <summary>
    /// 나브 진입 시 호출 — FreeLook 오빗을 <b>낮은 시점</b>으로 전환해 아치 아케이드가 위로 솟아 보이게 한다.
    /// <b>수평각(heading)은 건드리지 않는다</b> — 시선 방향은 CameraHeadingZone이 정하며, 여기서 리센터를 켜면
    /// 이 리그의 m_Heading이 WorldForward라 월드 +Z로 끌려가 그 각도가 초기화된다.
    /// 오빗 전환은 DK 전환(TransitionDKOrbitAsync)을 재사용한다. 값은 인게임서 튜닝.
    /// </summary>
    public void ActivateProcessionalView(float duration = 1.2f)
    {
        EnsureCinemachineRefs();
        if (_cinemachine == null) return;

        if (_savedProcessionalOrbits == null)
        {
            _savedProcessionalOrbits = new CinemachineFreeLook.Orbit[]
            {
                _cinemachine.m_Orbits[0],
                _cinemachine.m_Orbits[1],
                _cinemachine.m_Orbits[2],
            };
        }

        // heading(수평각)은 건드리지 않는다 — 오빗(높이/거리)만 낮춘다.
        // 이 리그는 m_Heading.m_Definition = WorldForward라, 리센터를 켜면 진행방향이 아니라
        // 월드 +Z로 끌려가 CameraHeadingZone이 맞춰둔 각도가 초기화된다. 방어적으로 꺼둔다.
        _cinemachine.m_RecenterToTargetHeading.m_enabled = false;

        TransitionDKOrbitAsync(
            processionalOrbitTop, processionalOrbitMiddle, processionalOrbitBottom,
            duration, this.GetCancellationTokenOnDestroy()).Forget();
    }

    /// <summary>나브 이탈 시 호출 — 원래 게임플레이 오빗/리센터로 서서히 복원.</summary>
    public void DeactivateProcessionalView(float duration = 1.2f)
    {
        EnsureCinemachineRefs();
        if (_cinemachine == null || _savedProcessionalOrbits == null) return;

        var top = new Vector2(_savedProcessionalOrbits[0].m_Height, _savedProcessionalOrbits[0].m_Radius);
        var mid = new Vector2(_savedProcessionalOrbits[1].m_Height, _savedProcessionalOrbits[1].m_Radius);
        var bot = new Vector2(_savedProcessionalOrbits[2].m_Height, _savedProcessionalOrbits[2].m_Radius);
        _savedProcessionalOrbits = null;

        TransitionDKOrbitAsync(top, mid, bot, duration, this.GetCancellationTokenOnDestroy()).Forget();
    }

    /// <summary>
    /// 스타트 방 캐릭터 선택 직후 호출. Cinemachine이 이미 player를 추적 중인 상태에서
    /// Brain을 끄고 현재 위치에서 player 쪽으로 줌인 후 Cinemachine 복귀.
    /// _introStarted 상태와 무관하게 실행되며 OnIntroComplete는 발생시키지 않는다.
    /// </summary>
    public async UniTaskVoid PlayStartRoomIntroAsync(Transform target)
    {
        if (this == null || target == null) return;

        // 저장된 플레이어 오빗이 있으면 복원 — resolved 포즈가 플레이어 시점이 되도록 보간 전에 먼저
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

        // 실제 Cinemachine 해석 포즈로 수렴 (고정 오프셋 스냅 제거 → 핸드오프 튐 없음)
        try { await BlendToActiveCameraAsync(introDuration, this.GetCancellationTokenOnDestroy()); }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// 시작방(허브) 진입 시 방 내부를 천천히 둘러보는 패닝 연출.
    /// center를 중심으로 좌우로 호(arc)를 그리며 스윕한다. Cinemachine을 잠시 끄고 수동 이동.
    /// _introStarted를 건드리지 않으므로 이후 카메라 핸드오프가 정상 동작한다.
    /// </summary>
    public async UniTask PlayStartRoomTourAsync(Vector3 center, CancellationToken ct = default)
    {
        if (this == null || startRoomTourDuration <= 0f) return;

        if (_cinemachine == null) _cinemachine = FindFirstObjectByType<CinemachineFreeLook>(FindObjectsInactive.Include);
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
    /// 온보딩 연출: 현재 카메라 위치에서 target으로 **천천히 넓게 클로즈업**으로 이동→잠시 비춤→**조금 빠르게 복귀**.
    /// orbit 없음. Cinemachine을 일시 정지하고 카메라 transform을 직접 보간한 뒤 원위치 복귀+추적 재개.
    /// </summary>
    public async UniTask PlayOnboardingRevealAsync(
        Vector3 target, Vector3 viewOffset, float lookHeight,
        float moveDuration, float holdDuration, float returnDuration,
        Transform playerTransform, CancellationToken ct = default)
    {
        if (this == null) return;
        if (_brain == null)       _brain       = GetComponent<CinemachineBrain>();
        if (_cinemachine == null) _cinemachine = FindFirstObjectByType<CinemachineFreeLook>(FindObjectsInactive.Include);

        if (_brain != null)       _brain.enabled       = false;
        if (_cinemachine != null) _cinemachine.enabled = false;

        Vector3    startPos = transform.position;
        Quaternion startRot = transform.rotation;

        // viewOffset(예: (0,3,-6))은 "대상 뒤/위"를 뜻하는 <b>로컬</b> 오프셋이다.
        // 그대로 월드에 더하면 항상 월드 −Z(고정 방위)에서만 대상을 바라봐, 플레이어가 어느 방향에서
        // 접근하든·카메라 heading이 무엇이든 연출 각도가 고정된다. 현재 카메라 heading을 기준으로 오프셋을
        // 회전시켜, 지금 보고 있던 축을 유지한 채 대상으로 다가가게 한다(연출 전후 시점 연속).
        float headingYaw = _cinemachine != null ? _cinemachine.m_XAxis.Value : transform.eulerAngles.y;
        Vector3 worldOffset = Quaternion.Euler(0f, headingYaw, 0f) * viewOffset;

        Vector3 toPos   = target + worldOffset;
        Vector3 lookDir = (target + Vector3.up * lookHeight) - toPos;
        Quaternion toRot = lookDir.sqrMagnitude > 0.001f ? Quaternion.LookRotation(lookDir, Vector3.up) : startRot;

        try
        {
            await MoveCameraAsync(startPos, startRot, toPos, toRot, moveDuration, ct);    // 천천히 넓게 클로즈업
            if (holdDuration > 0f)
                await UniTask.Delay(TimeSpan.FromSeconds(holdDuration), DelayType.UnscaledDeltaTime, cancellationToken: ct);

            // 복귀: 플레이어의 **현재 위치**를 추적하는 게임플레이 카메라 포즈로 블렌드(정확) — 조금 빠르게.
            if (_cinemachine != null && playerTransform != null)
            {
                _cinemachine.Follow = playerTransform;
                _cinemachine.LookAt = playerTransform;

                // heading(m_XAxis) 복원 — 연출 진입 시점의 값으로 되돌린다.
                // 연출 동안 Cinemachine을 끄고 transform을 직접 돌렸을 뿐 m_XAxis는 그대로이므로,
                // 이 값을 안 맞추면 재활성화된 vcam이 '연출 각도'가 아니라 옛 heading으로 포즈를 계산해
                // 복귀 후 시선이 틀어진다. 진입 heading으로 고정하면 원래 게임플레이 시점으로 정확히 돌아온다.
                _cinemachine.m_XAxis.Value = headingYaw;
            }
            await BlendToActiveCameraAsync(returnDuration, ct);   // 라이브 플레이어 vcam 포즈로 + brain 복원
        }
        catch (OperationCanceledException) { }
        finally
        {
            // 취소 등으로 중단돼도 게임플레이 카메라 복원 보장
            if (_cinemachine != null) _cinemachine.enabled = true;
            if (_brain != null)       _brain.enabled       = true;
        }
    }

    /// <summary>카메라 transform을 from→to로 PanEase 보간(unscaled). PlayOnboardingRevealAsync 전용.</summary>
    private async UniTask MoveCameraAsync(Vector3 fromP, Quaternion fromR, Vector3 toP, Quaternion toR, float dur, CancellationToken ct)
    {
        float d = Mathf.Max(0.01f, dur), t = 0f;
        while (t < d)
        {
            if (this == null) return;
            ct.ThrowIfCancellationRequested();
            t += Time.unscaledDeltaTime;
            float k = PanEase(t / d);
            transform.position = Vector3.Lerp(fromP, toP, k);
            transform.rotation = Quaternion.Slerp(fromR, toR, k);
            await UniTask.Yield(PlayerLoopTiming.Update);
        }
        transform.position = toP;
        transform.rotation = toR;
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
        // TopDown 해제 애니메이션이 진행 중이면 취소 — 같은 프레임에 transform을 동시 제어하면 카메라가 튀는 문제 방지
        _topDownReturnCts?.Cancel();
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

            // 3) 플레이어 위치로 복귀 — Cinemachine 실제 포즈로 블렌드 (스냅 없음)
            await BlendToActiveCameraAsync(returnDuration, linked.Token);
        }
        catch (OperationCanceledException) { }
        finally
        {
            // BlendToActiveCameraAsync가 정상 경로에선 brain + _isPanning을 이미 복원.
            // 취소·예외 시에만 보장한다.
            if (myVersion == _panVersion)
            {
                _isPanning = false;
                if (_cinemachine != null && _prePanCmEnabled) _cinemachine.enabled = true;
                if (_brain != null && _prePanBrainEnabled)    _brain.enabled       = true;
            }
        }
    }

    /// <summary>방 진입 전용 연출 — 카메라가 방을 넓게 부감으로 끌어올려 보여주고(넓어진 순간 onWide 호출 → 문 잠금),
    /// 잠깐 유지 후 플레이어로 복귀. 보스 팬과 별개의 전용 경로. 몬스터 스폰은 이 UniTask 완료 후 호출자가 시작한다.</summary>
    /// <param name="wideHeight">방 중앙 위로 끌어올릴 높이(방 크기에 맞춰 조정).</param>
    /// <param name="wideBack">뒤로 물러날 거리(부감 각도).</param>
    public async UniTask PlayRoomEntryIntroAsync(
        Vector3 roomCenter, Transform playerTransform,
        float riseDuration, float holdDuration, float returnDuration,
        float wideHeight, float wideBack,
        System.Action onWide, CancellationToken ct)
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

        // 방 전체를 담는 넓은 부감 — 방 중앙 위로 끌어올려 내려다본다.
        //
        // ⚠️ 물러나는 방향은 <b>월드 -Z가 아니라 지금 카메라가 보던 방향의 반대</b>다.
        //    월드 고정으로 두면, 동쪽 문으로 들어와 카메라 헤딩이 90°인 상태에서 부감이 항상 0°를
        //    바라보게 되어 <b>화면이 통째로 돌았다가 연출이 끝나면 도로 돌아온다</b>(2026-08-20 QA).
        //    플레이어 기준 구도를 유지하면 위로 물러났다 돌아오는 동작만 남고 회전은 사라진다.
        Vector3 backDir = -transform.forward;
        backDir.y = 0f;
        backDir = backDir.sqrMagnitude > 0.001f ? backDir.normalized : Vector3.back;

        Vector3    toPos   = roomCenter + Vector3.up * wideHeight + backDir * wideBack;
        Vector3    lookDir = roomCenter - toPos;
        Quaternion toRot   = lookDir.sqrMagnitude > 0.01f
            ? Quaternion.LookRotation(lookDir, Vector3.up)
            : fromRot;

        try
        {
            // 1) 넓게 끌어올림
            for (float t = 0f; t < riseDuration; t += Time.deltaTime)
            {
                linked.Token.ThrowIfCancellationRequested();
                float e = PanEase(t / riseDuration);
                transform.position = Vector3.Lerp(fromPos, toPos, e);
                transform.rotation = Quaternion.Slerp(fromRot, toRot, e);
                await UniTask.Yield(linked.Token);
            }
            transform.position = toPos;
            transform.rotation = toRot;

            // 넓게 보이는 순간 → 문 잠금 시작
            onWide?.Invoke();

            // 2) 방 조망 유지 (문이 잠기는 동안)
            await UniTask.Delay(TimeSpan.FromSeconds(holdDuration), cancellationToken: linked.Token);

            // 3) 넓은 뷰 → 플레이어 추적 게임플레이 포즈로 자연스럽게 수렴.
            //    Cinemachine이 해석한 실제 포즈로 블렌드하므로 브레인 인계 시 팝(다시 위로 튐)이 없다.
            //    (수동 오프셋 복귀 + SnapToTarget는 Cinemachine 해석 포즈와 어긋나 '한 번 더 위로 이동' 유발 → 폐기)
            if (_cinemachine != null && playerTransform != null)
            {
                _cinemachine.Follow = playerTransform;
                _cinemachine.LookAt = playerTransform;
            }
            await BlendToActiveCameraAsync(returnDuration, linked.Token);
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (myVersion == _panVersion)
            {
                _isPanning = false;
                // BlendToActiveCameraAsync가 정상 경로에선 이미 Cinemachine 해석 포즈로 수렴 + 브레인 복원.
                // 취소 등 중단 시에만 게임플레이 카메라 복원을 보장한다.
                if (_cinemachine != null && _prePanCmEnabled) _cinemachine.enabled = true;
                if (_brain != null && _prePanBrainEnabled)    _brain.enabled       = true;
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
        if (_cinemachine == null) _cinemachine = FindFirstObjectByType<CinemachineFreeLook>(FindObjectsInactive.Include);
        if (_brain == null) _brain = GetComponent<CinemachineBrain>();
        if (_brain != null) _brain.enabled = false;
        if (_cinemachine != null) _cinemachine.enabled = false;

        Vector3 offset = _originalPosition;
        Quaternion endRot = _originalRotation;

        // _fadeOverlay가 이미 투명(PrepareMapViewAsync 완료)이면 페이드 재실행 없이 줌만 수행
        bool needFade = _fadeOverlay != null && _fadeOverlay.color.a > 0.01f;

        Vector3 startPos;
        Quaternion startRot;

        if (needFade)
        {
            // PrepareMapViewAsync 미사용(프리팹 맵 등): 오버레이로 가려진 상태이므로
            // 씬 기본 카메라 위치 대신 플레이어 스폰 위치 기준으로 즉시 스냅.
            // 페이드인 후 카메라가 이미 올바른 위치에 있어 스폰 전/후 위치가 동일해짐.
            startPos = target.position + offset;
            startRot = endRot;
            transform.position = startPos;
            transform.rotation = startRot;
        }
        else
        {
            // PrepareMapViewAsync가 이미 카메라를 맵 위에 배치했으므로 현재 위치에서 줌인 시작
            startPos = transform.position;
            startRot = transform.rotation;
        }

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

    /// <summary>
    /// 팬 없이 즉시 수동 카메라 제어를 시작한다.
    /// Cinemachine/Brain을 비활성화하고 PanToZoneAndReturnAsync 없이 보스 등장 연출을 진행할 때 사용.
    /// ReturnToPlayerAsync 호출 시 자동으로 원래 상태로 복원된다.
    /// </summary>
    public void TakeManualControl()
    {
        EnsureCinemachineRefs();
        if (!_isPanning)
        {
            _prePanBrainEnabled = _brain != null && _brain.enabled;
            _prePanCmEnabled    = _cinemachine != null && _cinemachine.enabled;
        }
        _panCts?.Cancel();
        _panCts?.Dispose();
        _panCts = new CancellationTokenSource();
        System.Threading.Interlocked.Increment(ref _panVersion);
        _isPanning = true;
        if (_brain != null)       _brain.enabled       = false;
        if (_cinemachine != null) _cinemachine.enabled = false;
    }

    private void EnsureCinemachineRefs()
    {
        if (_cinemachine == null)
            _cinemachine = FindFirstObjectByType<CinemachineFreeLook>(FindObjectsInactive.Include);
        if (_brain == null)
            _brain = GetComponent<CinemachineBrain>();
    }
}
