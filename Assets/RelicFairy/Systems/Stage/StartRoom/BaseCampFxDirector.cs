using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 베이스캠프 「멀린의 공간」 연출 담당(4단계, 09-27 사용자 지시 · 설계서 3장 표).
///
/// ① <b>멀린이 공간을 만들어 건넨다</b>(처음 한 번) — 회랑 문턱에 처음 오르면 카메라가 광장 전경으로 물러나고,
///    숨겨 둔 공방·성소·제단이 멀린의 빛(금빛 디졸브)으로 하나씩 생겨난다. 끝나면 즉시 상호작용 가능.
///    문턱을 거치지 않고 광장에서 처음 시작하는 경우(복귀 스폰)는 스폰 직후 한 번.
///    두 번째부터는 처음부터 서 있다(슬롯별 기록).
/// ② <b>무형검 받기</b> — 카메라가 받침으로 다가가고 천장 틈 별빛이 밝아진 뒤, 검이 손에 맺히는 순간 금빛 가장자리.
///    입력은 1.5초 이하로만 막는다(<see cref="WorldSwordAwakening"/>이 호출).
/// ③ <b>소환</b> — 별빛 기둥이 떨어져 발밑 고리가 깔리고 → 그 빛 속에서 몸이 디졸브로 맺히고 → 고리가 한 번 퍼진다.
///    처음(소환의 방) 약 2.9초 · 몸이 맺힐 때(2.4초)까지 조작 막음 / 복귀(매 판) 약 1.8초 · 1.35초부터 조작.
///    사망 복귀 보랏빛 · 그 외 금빛(<see cref="BaseCampBootstrapper"/>가 호출).
/// ④ <b>얻기</b> — 원거리·파츠·유물을 고르면 빛 구슬이 출처에서 떠올라(처음만 한 바퀴) 몸으로 스민다. 조작은 막지 않는다.
/// ⑤ <b>준비 룬 · 포탈 열림</b>(매 판) — 광장 소환진 룬 셋(원거리·파츠·유물)이 하나씩 켜지고, 셋이면 성문 아치 등불이 켜지며 「심연이 열렸다」.
/// ⑥ <b>심연 진입</b>(매 판) — 성문에서 포탈로 내려갈수록 어두워지고, 포탈에서만 조작을 막고 잉크 와이프로 덮는다.
/// ⑦ <b>성문 막</b> — 준비 전엔 보랏빛 막이 통로를 실제로 막고(앞에 서면 부족분 안내), 준비되면 금빛으로 걷힌다.
/// ⑧ <b>장비 → 파츠</b> — 파츠는 원거리 무기에 끼우는 것이라 옆 작업대는 원거리 무기를 얻어야 깨어난다(공방에서 빛이 건너감).
/// 카메라는 <see cref="GameCameraController.PlayOnboardingRevealAsync"/>를 쓴다 — 중단돼도 finally가 Brain·FreeLook을 되살린다.
/// 연출 시작·끝은 <c>[BaseCampFx]</c> 로그(보스 레인 실측: 입력 막힘 누적 시간).
/// 흔들림은 쓰지 않는다 — 강조는 빛·화면 가장자리 금빛(설계서 3장 공통).
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public sealed class BaseCampFxDirector : MonoBehaviour
{
    public enum SummonKind { First, Return, Death, Clear }
    public enum AcquireKind { Ranged, Part, Relic }

    private const string ConjureKeyPrefix = "basecamp_conjure_seen_v1_slot";
    private const string AcquireKeyPrefix = "basecamp_acquire_seen_v1_slot";
    private const int RingSegments = 40;
    private const int RuneCount = 3;   // 원거리 · 파츠 · 유물(무형검은 소환의 방에서 먼저 받는다)
    private const float RuneFlashPeak = 4f;       // 준비 룬·파츠 공방이 켜질 때 번쩍임 — 14는 화면이 튀었다(09-29 사용자)
    private const float PartsPathDraw = 1.0f;     // 공방 → 파츠 공방 바닥 빛 길이 뻗는 시간
    private const float PartsPathHold = 0.8f;
    private const float PartsPathFade = 1.2f;
    private const float PartsGlowFade = 0.9f;     // 파츠 받침 빛이 켜지고 꺼지는 시간
    private const float PartsRingRadius = 3.2f;   // 파츠 받침(반지름 약 4 m) 안쪽 고리
    private const float PartsLightMax = 2.5f;
    private const float PartsMoteRate = 6f;
    private const float LoadingWaitMax = 6f;
    // 성문 막 세로 3단 — 높이 비율과 알파 비율(아래·중간 진하게, 위만 투명)
    private const int VeilRows = 3;
    private static readonly float[] VeilRowHeights = { 0f, 0.55f, 1f };
    private static readonly float[] VeilRowAlpha = { 1f, 0.85f, 0f };

    public static BaseCampFxDirector Instance { get; private set; }

    [Header("① 멀린이 만드는 공간 (처음 한 번)")]
    [Tooltip("나타날 순서대로 — 공방·성소·제단 루트. 허수아비처럼 몬스터 초기화가 걸린 것은 넣지 않는다.")]
    [SerializeField] private GameObject[] conjureStations;
    [SerializeField] private Transform plazaCenter;
    [SerializeField, Tooltip("광장 가운데에서 이 거리 안에서 스폰하면 문턱 없이 바로 재생")]
    private float plazaRadius = 30f;
    [SerializeField] private Vector3 vistaOffset = new(0f, 16f, -26f);
    [SerializeField] private float stationInterval = 0.55f;
    [SerializeField] private float stationDissolve = 0.9f;
    [SerializeField] private Color conjureEdge = new(1f, 0.82f, 0.45f);
    [SerializeField, TextArea] private string merlinLine = "네가 고를 것들을 이곳에 두지. 검 다음은 활, 그다음은 기사의 유물이다.";

    [Header("② 무형검 받기")]
    [SerializeField] private Light starShaft;
    [SerializeField] private float shaftBoost = 2.5f;

    [Header("③ 소환 — 처음(소환의 방) · 복귀(매 판)")]
    [Tooltip("별빛 기둥·발밑 고리 선 재질 — 마력 결계 띠와 같은 Hovl Laser2")]
    [SerializeField] private Material beamMaterial;
    [Tooltip("형태가 맺히는 순간 터지는 빛 — Hit 18 nova orange")]
    [SerializeField] private GameObject burstGold;
    [Tooltip("사망 복귀용 — Hit 17 nova violet")]
    [SerializeField] private GameObject burstViolet;
    [SerializeField] private Color summonGold = new(1f, 0.78f, 0.38f);
    [SerializeField] private Color summonViolet = new(0.66f, 0.42f, 1f);

    // ④ 얻기 — 원거리 · 파츠 · 유물: 런타임 금빛 입자(runeGlowMaterial). 예전 Projectile 18 nova 구슬은 가까이서 지름 2 m 번개 구로 튀었다(09-29 사용자).

    [Header("⑤ 준비 룬 · 포탈 열림 · ⑥ 심연 진입 (매 판)")]
    [Tooltip("성문 앞 마커(GateFront, 정면이 성문) — 아치 등불이 통로 양옆에 서고, 여기서 포탈까지 내려갈수록 어두워진다")]
    [SerializeField] private Transform gateFront;
    [SerializeField] private Transform portalCenter;
    [Tooltip("룬 빛 입자 재질 — 마력 결계 빛 입자와 같은 Hovl Glow1cg. 투사체 이펙트를 반복 재생하면 지름 4~5 m 빛 구가 돼 문과 화면을 덮었다(ae 09-28)")]
    [SerializeField] private Material runeGlowMaterial;
    [Tooltip("소환진(스폰 자리 돌 고리) 룬 줄 반지름(월드 m) — 매 판 스폰 자리에서 첫 화면에 보인다")]
    [SerializeField] private float runeCircleRadius = 5.0f;
    [Tooltip("소환진 룬이 가리킬 곳 — 원거리(장비 공방) · 파츠(파츠 공방) · 유물(성문 쪽). 비면 성문 방향")]
    [SerializeField] private Transform[] runeTargets = new Transform[RuneCount];
    [SerializeField] private Color runeLitColor = new(1f, 0.8f, 0.45f, 1f);
    [SerializeField] private Color runeDimColor = new(0.55f, 0.42f, 1f, 0.55f);
    [SerializeField] private string abyssOpenTitle = "심연이 열렸다";
    [SerializeField, TextArea] private string abyssOpenSubtitle = "성문 너머 계단 아래, 포탈로 내려가라";
    [Tooltip("심연의 문 구역 표지 — 포탈이 열리면 부제를 바꾼다")]
    [SerializeField] private ZoneSign abyssSign;
    [SerializeField] private string abyssSignOpenSubtitle = "포탈이 열렸다 — 계단 아래로";

    [Header("⑦ 성문 막 — 닫힘(보랏빛 막) → 열림(금빛으로 걷히고 심연 쪽으로 빨려 드는 빛)")]
    [Tooltip("막 재질 — 가장자리 마력 결계의 막과 같은 Hovl Trail13cg(정점 색으로 위로 옅어짐)")]
    [SerializeField] private Material gateVeilMaterial;
    [Tooltip("통로 폭(월드 m) — 성문 충돌 틈 3.8 m 안쪽")]
    [SerializeField] private float gatePassageWidth = 3.6f;
    [SerializeField] private float gateVeilHeight = 5.0f;
    [Tooltip("GateFront에서 성문 안쪽으로 — 마커는 문 앞면보다 0.6 m 광장 쪽")]
    [SerializeField] private float gateVeilDepth = 0.9f;
    [SerializeField] private Color gateVeilColor = new(0.5f, 0.42f, 1f, 0.6f);
    [SerializeField] private float gateOpenDuration = 1.2f;
    [Tooltip("준비가 덜 된 채 막 앞에 서면 부족분 안내 — 막이 실제로 통로를 막는다(09-28 사용자)")]
    [SerializeField] private BaseCampDungeonGate dungeonGate;

    [Header("⑧ 장비 → 파츠 — 원거리 무기를 얻으면 바닥 빛 길이 파츠 공방으로 이어지고 받침이 빛난다")]
    [Tooltip("옛 파츠 궤도 구슬(프리팹 Dressing/FX/PartsOrbital, Hovl Buff orbital) — 글자 박힌 큰 구슬이 튀고 뜻이 애매해(09-29 사용자) 이제 켜지 않는다. 시작할 때 끈다. 공방·작업대 위치는 runeTargets 0·1")]
    [SerializeField] private GameObject partsOrbital;
    [SerializeField] private float descentExposure = -1.6f;
    [SerializeField] private float descentVignette = 0.4f;
    [SerializeField] private Color abyssWipeColor = new(0.16f, 0.08f, 0.26f);
    [SerializeField] private float abyssWipeDuration = 0.7f;

    private bool _conjurePending;
    private bool _conjuring;
    private CancellationToken _destroyCt;
    private readonly Vector3[] _ringPoints = new Vector3[RingSegments];

    // 소환진 룬 셋(원거리·파츠·유물) + 성문 아치 등불 둘(셋 다 준비되면 켜짐)
    private GameObject[] _runeDim;
    private GameObject[] _runeLit;
    private readonly bool[] _runeOn = new bool[RuneCount];
    private readonly GameObject[] _archDim = new GameObject[2];
    private readonly GameObject[] _archLit = new GameObject[2];
    private bool _archOn;
    private string _abyssClosedSubtitle;

    private bool _partsAwake;
    private LineRenderer _partsRing;
    private Light _partsLight;
    private ParticleSystem _partsMotes;
    private float _partsGlow;                    // 0 꺼짐 ~ 1 켜짐
    private CancellationTokenSource _partsGlowCts;

    // 성문 막
    private Transform _gateRoot;
    private BoxCollider _gateBlocker;
    private float _veilTouchUntil;
    private Mesh _veilMesh;
    private Color[] _veilColors;
    private MeshRenderer _veilRenderer;
    private LineRenderer _veilBand;
    private ParticleSystem _veilMotes;
    private ParticleSystem _abyssDraw;
    private Light _gateLight;
    private CancellationTokenSource _gateCts;
    private bool _abyssOpenAnnounced;
    private bool _enteringAbyss;
    private int _acquireInFlight;

    private Volume _descentVolume;
    private VolumeProfile _descentProfile;
    private float _descentWeight = -1f;

    /// <summary>처음 광장 연출이 아직 남아 있는가(끝나기 전엔 공방이 숨겨져 있다).</summary>
    public bool ConjurePending => _conjurePending;

    // ── Lifecycle ─────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        GetComponent<BoxCollider>().isTrigger = true;
        _destroyCt = this.GetCancellationTokenOnDestroy();

        _conjurePending = PlayerPrefs.GetInt(ConjureKey(), 0) == 0;
        if (_conjurePending) SetStationsActive(false);
    }

    private void Start()
    {
        if (abyssSign != null) _abyssClosedSubtitle = abyssSign.Subtitle;
        if (partsOrbital != null) partsOrbital.SetActive(false);   // 옛 궤도 구슬 — 이제 켜지 않는다(파츠 받침 빛이 대신, UpdateReadyRunes)
        BuildReadyRunes();
        BuildGateVeil();
        BuildDescentVolume();
        if (_conjurePending) WaitForPlazaSpawnAsync(_destroyCt).Forget();
    }

    private void Update()
    {
        UpdateReadyRunes();
        UpdateGateNotice();
        UpdateDescentDarkness();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!_conjurePending || _conjuring) return;
        var p = other.GetComponentInParent<PlayerController>();
        if (p != null) ConjureAsync(p, _destroyCt).Forget();
    }

    private void OnDestroy()
    {
        _gateCts?.Cancel();
        _gateCts?.Dispose();
        _partsGlowCts?.Cancel();
        _partsGlowCts?.Dispose();
        if (_veilMesh != null) Destroy(_veilMesh);
        if (_descentProfile != null) Destroy(_descentProfile);
        if (ReferenceEquals(Instance, this)) Instance = null;
    }

    // ── Public Methods ────────────────────────────────────────

    /// <summary>「처음 한 번」 연출 기록(공간 생성 · 얻기)을 슬롯에서 지운다 — 새 게임·디버그 메뉴.</summary>
    public static void ClearFirstTimeForSlot(int slot)
    {
        PlayerPrefs.DeleteKey(ConjureKeyPrefix + slot);
        foreach (AcquireKind k in Enum.GetValues(typeof(AcquireKind)))
            PlayerPrefs.DeleteKey(AcquireKeyPrefix + slot + "_" + k);
        PlayerPrefs.Save();
    }

    /// <summary>무형검 받기 연출 — <see cref="WorldSwordAwakening"/>이 넘겨받기와 <b>나란히</b> 돌린다. 입력 막음 ≤ 1.5초.</summary>
    public async UniTask PlaySwordReceiveAsync(Transform pedestal, PlayerController player, CancellationToken ct)
    {
        var cam = GameCameraController.Instance;
        if (pedestal == null || player == null) return;

        Debug.Log("[BaseCampFx] 무형검 받기 시작");
        float t0 = Time.unscaledTime;
        player.SetInputEnabled(false);
        OnboardingGuideArrow.Suppressed = true;   // 다가가는 카메라 앞에 문 위 ▼가 크게 걸렸다(ae 09-28)
        float baseIntensity = starShaft != null ? starShaft.intensity : 0f;
        try
        {
            var camTask = cam != null
                ? cam.PlayOnboardingRevealAsync(pedestal.position, new Vector3(0f, 3.2f, -5.5f), 1.4f, 0.45f, 0.5f, 0.45f, player.transform, ct)
                : UniTask.CompletedTask;
            var lightTask = PulseLightAsync(starShaft, baseIntensity, baseIntensity * shaftBoost, 0.4f, 1.0f, ct);

            await UniTask.Delay(TimeSpan.FromSeconds(1.1f), DelayType.UnscaledDeltaTime, cancellationToken: ct);
            FinisherEdgeService.Pulse();   // 검이 손에 맺히는 순간(넘겨받기 1.1초와 맞춤)
            await UniTask.WhenAll(camTask, lightTask);
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (starShaft != null) starShaft.intensity = baseIntensity;
            if (player != null) player.SetInputEnabled(true);
            OnboardingGuideArrow.Suppressed = false;
            Debug.Log($"[BaseCampFx] 무형검 받기 끝 — 입력 막힘 {Time.unscaledTime - t0:0.00}초");
        }
    }

    /// <summary>
    /// 소환 — 별빛 기둥이 서고 발밑 고리가 퍼지며 기사가 빛 속에서 형태가 된다.
    /// 부트스트래퍼가 <b>화면이 검을 때</b>(디졸브 재질을 데운 뒤) 부른다 — 몸을 숨긴 채 로딩 덮개가 걷히길 기다렸다가 시작한다.
    /// 디졸브는 스케일 시간으로 돌므로, 시간을 멈추는 시작 대사는 이 연출이 끝난 뒤에 띄운다.
    /// </summary>
    public async UniTask PlaySummonArrivalAsync(PlayerController player, SummonKind kind, CancellationToken ct)
    {
        if (player == null) return;
        bool first = kind == SummonKind.First;
        Color c = kind == SummonKind.Death ? summonViolet : summonGold;
        // 빛이 먼저, 몸은 그 안에서 — 기둥과 디졸브가 같은 순간에 시작해 캐릭터가 이미 서 있는 것처럼 보였다(09-28 사용자).
        // 별빛 기둥이 하늘에서 떨어져 발밑 고리가 깔리고 → 그 빛 속에서 몸이 디졸브로 맺히고 → 맺히는 순간 고리가 한 번 퍼진다.
        float beamFall = first ? 0.6f : 0.35f;
        float appearAt = first ? 1.0f : 0.45f;
        float appearDur = first ? 1.4f : 0.9f;
        float formedAt = appearAt + appearDur;
        float unlockAt = formedAt;
        float total = formedAt + 0.45f;
        float beamTop = first ? 16f : 24f;
        float beamWidth = first ? 1.4f : 1.1f;
        Vector3 feet = player.transform.position;

        bool unlocked = false;
        player.SetInputEnabled(false);
        OnboardingGuideArrow.Suppressed = true;
        // 로딩 화면이 다 걷힐 때까지 몸을 숨긴 채 기다린다 — 전엔 소환 앞 절반이 「불러오는 중」 덮개 아래로 지나갔다(ae 09-28 캡처).
        var hidden = HideRenderers(player.gameObject);
        LineRenderer beam = null, ring = null;
        Light light = null;
        float shaftBase = starShaft != null ? starShaft.intensity : 0f;
        float t0 = Time.unscaledTime;
        try
        {
            for (float w = 0f; w < LoadingWaitMax && IsLoadingCoverUp(); w += Time.unscaledDeltaTime)
                await UniTask.Yield(ct);

            Debug.Log($"[BaseCampFx] 소환 시작 — {kind} (로딩 대기 {Time.unscaledTime - t0:0.00}초)");
            t0 = Time.unscaledTime;
            beam = MakeLine("~SummonBeam", 2, false);
            ring = MakeLine("~SummonRing", RingSegments, true);
            light = MakeLight(feet + Vector3.up * 1.6f, c);

            bool appeared = false, burst = false;
            for (float t = 0f; t < total; t += Time.unscaledDeltaTime)
            {
                // 기둥 — 하늘에서 발밑까지 떨어져 내리고(beamFall) 마지막 0.45초에 사라진다
                float fade = Mathf.Min(Mathf.Clamp01(t / 0.2f), Mathf.Clamp01((total - t) / 0.45f));
                float fall = Mathf.Clamp01(t / beamFall);
                fall = 1f - (1f - fall) * (1f - fall);
                // 몸이 맺히는 동안 기둥이 가늘고 옅어진다 — 넓고 밝은 기둥이 맺히는 몸을 통째로 가렸다(ae 09-28 첫 소환 캡처)
                float form = Mathf.Clamp01((t - appearAt) / appearDur);
                float thin = Mathf.Lerp(1f, 0.35f, form);
                SetBeam(beam, feet, Mathf.Lerp(beamTop, 0.05f, fall), beamTop, beamWidth * fade * thin, c, fade * Mathf.Lerp(1f, 0.55f, form));
                // 발밑 고리 — 기둥이 닿으면 깔려 은은히 빛나다가, 몸이 맺히는 순간 한 번 퍼지며 사라진다
                if (t < beamFall) SetRing(ring, feet, 0.8f, c, 0f);
                else if (t < formedAt) SetRing(ring, feet, Mathf.Lerp(0.8f, 1.2f, (t - beamFall) / (formedAt - beamFall)), c, 0.9f * fade);
                else
                {
                    float k = Mathf.Clamp01((t - formedAt) / 0.45f);
                    SetRing(ring, feet, Mathf.Lerp(1.2f, 2.6f, 1f - (1f - k) * (1f - k)), c, (1f - k) * 0.9f);
                }
                if (light != null) light.intensity = 10f * fade * fall;
                if (first && starShaft != null)
                    starShaft.intensity = Mathf.Lerp(shaftBase, shaftBase * shaftBoost, Mathf.Min(Mathf.Clamp01(t / 0.8f), fade));

                if (!appeared && t >= appearAt)
                {
                    appeared = true;
                    RevealBodyAsync(player.gameObject, hidden, appearDur, c, ct).Forget();   // 빛 속에서 몸이 맺힌다
                }
                if (!burst && t >= formedAt - 0.05f)
                {
                    burst = true;
                    SpawnBurst(kind == SummonKind.Death ? burstViolet : burstGold, feet + Vector3.up * 0.9f, first ? 0.4f : 0.3f);
                    if (first) FinisherEdgeService.Pulse();   // 형태가 맺히는 순간 — 흔들림 대신 가장자리 금빛
                }
                if (!unlocked && t >= unlockAt)
                {
                    unlocked = true;
                    player.SetInputEnabled(true);
                }
                await UniTask.Yield(ct);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            ShowRenderers(hidden);   // 중단돼도 몸은 반드시 보이게(이미 켜졌으면 그대로)
            if (beam != null) Destroy(beam.gameObject);
            if (ring != null) Destroy(ring.gameObject);
            if (light != null) Destroy(light.gameObject);
            if (first && starShaft != null) starShaft.intensity = shaftBase;
            if (!unlocked && player != null) player.SetInputEnabled(true);
            OnboardingGuideArrow.Suppressed = false;
            Debug.Log($"[BaseCampFx] 소환 끝 — {kind} · 입력 막힘 {(unlocked ? unlockAt : Time.unscaledTime - t0):0.00}초");
        }
    }

    /// <summary>
    /// 얻기 연출 — 빛 구슬이 출처에서 떠올라(처음만 한 바퀴) 몸으로 날아와 스민다. 조작은 막지 않는다.
    /// 목표는 매 프레임 <c>Managers.Player</c>를 다시 본다 — 유물은 고르는 순간 몸을 새로 만들므로(재스폰) 새 몸을 따라간다.
    /// </summary>
    public void PlayAcquire(Vector3 from, AcquireKind kind) => AcquireAsync(from, kind, _destroyCt).Forget();

    /// <summary>심연 진입 — 포탈에서만 조작을 막고 잉크 와이프로 덮은 뒤 던전으로(<see cref="BaseCampDungeonGate"/>가 호출).</summary>
    public async UniTaskVoid EnterAbyssAsync(PlayerController player)
    {
        if (_enteringAbyss) return;
        _enteringAbyss = true;
        Debug.Log("[BaseCampFx] 심연 진입 — 잉크 와이프");
        if (player != null) player.SetInputEnabled(false);
        try
        {
            await ScreenFade.CoverAsync(Vector2.down, abyssWipeColor, abyssWipeDuration, null, _destroyCt);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception e) { Debug.LogException(e); }   // 와이프가 실패해도 던전에는 들어간다(소프트락 방지)
        BaseCampBootstrapper.Instance?.EnterDungeon();
    }

    // ── Private Methods ───────────────────────────────────────

    private static int ActiveSlot() => RunProgressManager.Instance?.ActiveSlotIndex ?? 0;
    private static string ConjureKey() => ConjureKeyPrefix + ActiveSlot();

    private void SetStationsActive(bool on)
    {
        if (conjureStations == null) return;
        foreach (var s in conjureStations)
            if (s != null) s.SetActive(on);
    }

    /// <summary>문턱을 거치지 않고 광장에서 시작하면(복귀 스폰) 스폰 뒤 바로 한 번.</summary>
    private async UniTaskVoid WaitForPlazaSpawnAsync(CancellationToken ct)
    {
        try
        {
            // 부트스트래퍼 준비 끝(페이드인 · 카메라 인계와 스폰 방향 스냅 · 시작 대사 닫힘)까지 기다린다 —
            // 전엔 플레이어가 생기자마자 시작해 검은 로딩 뒤에서 지나갔고, 끝날 때 스냅 전 heading으로 돌아가 첫 화면이 깨졌다(ae 09-28).
            await UniTask.WaitUntil(() => Managers.Player?.PlayerTransform != null && GameCameraController.Instance != null
                                          && (BaseCampBootstrapper.Instance == null || BaseCampBootstrapper.Instance.IsReady), cancellationToken: ct);
            var pt = Managers.Player.PlayerTransform;
            if (plazaCenter == null || Vector3.Distance(pt.position, plazaCenter.position) > plazaRadius) return;
            await UniTask.Delay(TimeSpan.FromSeconds(0.5f), DelayType.UnscaledDeltaTime, cancellationToken: ct);
            if (Managers.UI != null) await Managers.UI.WaitUntilNoBlockingPopupAsync();
            var pc = pt.GetComponentInParent<PlayerController>();
            if (pc != null) await ConjureAsync(pc, ct);
        }
        catch (OperationCanceledException) { }
    }

    private async UniTask ConjureAsync(PlayerController player, CancellationToken ct)
    {
        if (!_conjurePending || _conjuring) return;
        _conjuring = true;

        Debug.Log("[BaseCampFx] 멀린의 공간 생성 시작");
        float t0 = Time.unscaledTime;
        player.SetInputEnabled(false);
        // 전경 동안 HUD를 숨긴다 — 켜 둔 HUD 뒤로 바닥 이름판·이름표가 끼어 겹쳐 보였다(ae·98 09-28). 끝나면 페이드로 되살린다.
        UIRootBootstrapper.Instance?.SetHudStartRoomSuppressed(true);
        ZoneSign.LabelsHidden = true;
        OnboardingGuideArrow.Suppressed = true;
        try
        {
            int n = conjureStations != null ? conjureStations.Length : 0;
            float hold = 0.6f + n * stationInterval + stationDissolve;
            var cam = GameCameraController.Instance;
            var camTask = cam != null && plazaCenter != null
                ? cam.PlayOnboardingRevealAsync(plazaCenter.position, vistaOffset, 1.5f, 1.2f, hold, 0.9f, player.transform, ct)
                : UniTask.CompletedTask;

            await UniTask.Delay(TimeSpan.FromSeconds(1.2f), DelayType.UnscaledDeltaTime, cancellationToken: ct);   // 전경이 잡힌 뒤
            FinisherEdgeService.Pulse();
            // 멀린 내레이션 자막(하단 중앙 · 화자 줄) — HUD의 형제라 HUD를 숨겨도 보이고, 구역 이름 배너와 겹치지 않는다(98 제안 09-28)
            RelicFairy.UI.UI_BossBark.Show(merlinLine, RelicFairy.UI.BossBarkType.MerlinNarration, DialogueSpeaker.Merlin);

            for (int i = 0; i < n; i++)
            {
                var s = conjureStations[i];
                if (s == null) continue;
                s.SetActive(true);
                if (s.GetComponentInChildren<Renderer>() != null)
                    DissolveEffect.PlayAppear(s, stationDissolve, null, default, conjureEdge);
                FlashAsync(s.transform.position + Vector3.up * 1.6f, ct).Forget();   // 멀린의 빛 — 렌더러 없는 스테이션도 「생겨남」이 보이게
                await UniTask.Delay(TimeSpan.FromSeconds(stationInterval), DelayType.UnscaledDeltaTime, cancellationToken: ct);
            }
            await camTask;

            _conjurePending = false;
            PlayerPrefs.SetInt(ConjureKey(), 1);
            PlayerPrefs.Save();
        }
        catch (OperationCanceledException) { }
        finally
        {
            // 중단돼도 공방은 반드시 서 있어야 한다(상호작용 불가 소프트락 방지).
            if (_conjurePending) SetStationsActive(true);
            _conjurePending = false;
            _conjuring = false;
            if (player != null) player.SetInputEnabled(true);
            ZoneSign.LabelsHidden = false;
            OnboardingGuideArrow.Suppressed = false;
            var lo = AppBootstrapper.Instance?.Loadout;
            if (lo != null && (lo.WeaponSlot0 != null || lo.WeaponSlot1 != null || lo.Relic != null))
                UIRootBootstrapper.Instance?.RevealHudAsync(0.5f).Forget();   // 장비가 있어 HUD가 보이던 상태로만 되돌린다
            Debug.Log($"[BaseCampFx] 멀린의 공간 생성 끝 — 입력 막힘 {Time.unscaledTime - t0:0.00}초");
        }
    }

    private async UniTaskVoid AcquireAsync(Vector3 from, AcquireKind kind, CancellationToken ct)
    {
        if (runeGlowMaterial == null) return;
        string key = AcquireKeyPrefix + ActiveSlot() + "_" + kind;
        bool first = PlayerPrefs.GetInt(key, 0) == 0;
        if (first) { PlayerPrefs.SetInt(key, 1); PlayerPrefs.Save(); }
        Debug.Log($"[BaseCampFx] 얻기 연출 — {kind}{(first ? " (처음)" : "")}");

        ParticleSystem mote = null;
        _acquireInFlight++;
        try
        {
            if (kind == AcquireKind.Relic)
            {
                FlashAsync(from, ct, RuneFlashPeak).Forget();   // 석상 눈빛
                await UniTask.Delay(TimeSpan.FromSeconds(0.25f), DelayType.UnscaledDeltaTime, cancellationToken: ct);
            }
            mote = MakeAcquireMote(from);
            var mt = mote.transform;

            if (first && kind != AcquireKind.Relic)
            {
                // 떠오름(처음만) — 예전의 한 바퀴 돌기는 중심이 옆으로 비껴 있어 튀어 보였다(09-29 사용자) → 떠올라 곧장 날아간다
                Vector3 top = from + Vector3.up * 1.2f;
                for (float t = 0f; t < 0.45f; t += Time.unscaledDeltaTime)
                {
                    float k = t / 0.45f;
                    mt.position = Vector3.Lerp(from, top, k * (2f - k));
                    await UniTask.Yield(ct);
                }
            }

            // 몸으로 — 매 프레임 목표를 다시 본다(움직이거나 재스폰돼도 따라간다). 유물은 위에서 곧게 내려앉는다.
            float fly = first ? 0.65f : 0.55f;
            float arc = kind == AcquireKind.Relic ? 0f : 0.8f;
            Vector3 start = mt.position;
            Vector3 end = start;
            for (float t = 0f; t < fly; t += Time.unscaledDeltaTime)
            {
                if (TryGetAcquireTarget(kind, out var target)) end = target;
                float k = t / fly;
                mt.position = Vector3.Lerp(start, end, k * k * (3f - 2f * k)) + Vector3.up * (Mathf.Sin(k * Mathf.PI) * arc);
                await UniTask.Yield(ct);
            }
            if (TryGetAcquireTarget(kind, out var last)) end = last;

            // 몸에 스미는 빛 — 은은한 번짐(입자 몇 개) + 약한 광원. 불티 폭발(Hit nova)은 불똥이 화면 전체로 튀었다(09-29 사용자)
            mt.position = end;
            mote.Emit(first ? 16 : 10);
            FlashAsync(end, ct, 3f).Forget();
            if (first) FinisherEdgeService.Pulse();
        }
        catch (OperationCanceledException) { }
        finally
        {
            _acquireInFlight--;
            if (mote != null) StopAndDestroy(mote.gameObject, 1.0f);
        }
    }

    /// <summary>얻기 빛 — 작은 금빛 입자가 꼬리를 남기며 날아간다(월드 공간 방출) + 주변을 살짝 비추는 광원.</summary>
    private ParticleSystem MakeAcquireMote(Vector3 at)
    {
        var ps = MakeMotes("~AcquireMote", null, at, Vector3.one * 0.08f, 70f, 0.45f, 0.36f, runeLitColor, Vector3.zero, Vector3.zero);
        var main = ps.main;
        main.startSize = new ParticleSystem.MinMaxCurve(0.22f, 0.36f);
        var l = ps.gameObject.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = runeLitColor;
        l.range = 3f;
        l.intensity = 1.5f;
        l.shadows = LightShadows.None;
        ps.Play();
        return ps;
    }

    /// <summary>원거리·파츠는 등(원거리 무기가 걸리는 곳), 유물은 가슴.</summary>
    private static bool TryGetAcquireTarget(AcquireKind kind, out Vector3 target)
    {
        var pt = Managers.Player?.PlayerTransform;
        if (pt == null) { target = default; return false; }
        target = kind == AcquireKind.Relic
            ? pt.position + Vector3.up * 1.1f
            : pt.position + Vector3.up * 1.3f - pt.forward * 0.25f;
        return true;
    }

    // ── ⑤ 준비 룬 ──

    /// <summary>
    /// 소환진 룬 셋(매 판 스폰 자리 — 첫 화면에 보임, 설계서 「소환진 룬 줄」) = 원거리·파츠·유물 하나씩 +
    /// 성문 아치 등불 둘(통로 양옆 벽 앞 — 셋 다 준비되면 켜짐, 설계서 「셋이면 아치 점등」). 걷는 길 위에는 아무것도 두지 않는다.
    /// 게임 카메라는 발 +4.8 m보다 높은 것을 광장 어디서도 못 잡는다(ae 09-28 계산) → 전부 낮게.
    /// </summary>
    private void BuildReadyRunes()
    {
        if (runeGlowMaterial == null || gateFront == null) return;
        if (plazaCenter != null)
        {
            _runeDim = new GameObject[RuneCount];
            _runeLit = new GameObject[RuneCount];
            for (int i = 0; i < RuneCount; i++)
            {
                Vector3 p = CircleRunePosition(i);
                _runeLit[i] = MakeRuneGlow($"~ReadyRune_Circle_Lit_{i}", p, 0.75f, runeLitColor, true);
                _runeDim[i] = MakeRuneGlow($"~ReadyRune_Circle_Dim_{i}", p, 0.4f, runeDimColor, false);
                _runeLit[i].SetActive(false);
            }
        }
        for (int s = 0; s < 2; s++)
        {
            Vector3 p = ArchLampPosition(s == 0 ? -1f : 1f);
            string side = s == 0 ? "L" : "R";
            _archLit[s] = MakeRuneGlow($"~ReadyRune_GateLamp_Lit_{side}", p, 1.0f, runeLitColor, true);
            _archDim[s] = MakeRuneGlow($"~ReadyRune_GateLamp_Dim_{side}", p, 0.5f, runeDimColor, false);
            _archLit[s].SetActive(false);
        }
    }

    /// <summary>아치 등불 — 통로(폭 3.8 m) 양옆 벽 앞, 사람 키 조금 위.</summary>
    private Vector3 ArchLampPosition(float side)
        => gateFront.position + gateFront.right * (side * 2.9f) + Vector3.up * 2.2f - gateFront.forward * 0.3f;

    /// <summary>
    /// 작은 맥동 빛 — 입자 몇 개(Glow1cg)가 겹쳐 숨 쉬듯 밝아졌다 옅어지고, 옅은 불티가 천천히 오른다. withLight면 약한 점광원.
    /// 자동 방출이라 매 프레임 코드가 돌지 않는다.
    /// </summary>
    private GameObject MakeRuneGlow(string name, Vector3 at, float size, Color color, bool withLight)
    {
        var go = new GameObject(name);
        go.transform.position = at;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = true;
        main.duration = 1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 2.0f);   // 크기·수명이 들쭉날쭉하면 깜빡임으로 보였다(09-29) → 고르게
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(size * 0.85f, size);
        main.startColor = color;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 24;
        main.playOnAwake = true;

        var emission = ps.emission;
        emission.rateOverTime = 9f;
        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = size * 0.12f;

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(-0.03f, 0.03f);
        vel.y = new ParticleSystem.MinMaxCurve(0.05f, 0.22f);
        vel.z = new ParticleSystem.MinMaxCurve(-0.03f, 0.03f);

        var fade = ps.colorOverLifetime;
        fade.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.35f), new GradientAlphaKey(0f, 1f) });
        fade.color = g;

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = runeGlowMaterial;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        ps.Play();

        if (withLight)
        {
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = color;
            l.range = 3.5f;
            l.intensity = 2f;
            l.shadows = LightShadows.None;
        }
        return go;
    }

    /// <summary>소환진 고리 위 — 각 룬이 얻는 곳(장비 공방 · 파츠 공방 · 유물 성소) 쪽을 가리킨다.</summary>
    private Vector3 CircleRunePosition(int i)
    {
        var target = runeTargets != null && i < runeTargets.Length && runeTargets[i] != null ? runeTargets[i] : gateFront;
        Vector3 dir = target.position - plazaCenter.position;
        dir.y = 0f;
        return plazaCenter.position + dir.normalized * runeCircleRadius + Vector3.up * 0.9f;
    }

    /// <summary>로드아웃은 여러 제단에서 바뀌고 변경 통지가 없어 매 프레임 본다(null 검사뿐, 바뀔 때만 켜고 끈다 — 게이트와 같은 방식).</summary>
    private void UpdateReadyRunes()
    {
        if (_archLit[0] == null) return;
        var lo = AppBootstrapper.Instance?.Loadout;
        if (lo == null) return;
        // 준비 전(스폰·시작 대사 중)에 이미 채워진 것은 조용히 켠다 — 소환 연출 위로 번쩍이지 않게
        bool loud = BaseCampBootstrapper.Instance == null || BaseCampBootstrapper.Instance.IsReady;
        bool ranged = lo.WeaponSlot1 != null;
        bool part = !string.IsNullOrEmpty(lo.StartPartId);
        bool relic = lo.Relic != null;
        SetRune(0, ranged, loud);
        SetRune(1, part, loud);
        SetRune(2, relic, loud);
        SetPartsAwake(ranged && !part, loud);   // 원거리를 얻었고 파츠는 아직 — 파츠 공방이 「다음」

        bool open = ranged && part && relic && lo.WeaponSlot0 != null;
        if (open == _archOn) return;
        _archOn = open;
        for (int s = 0; s < 2; s++)
        {
            _archDim[s].SetActive(!open);
            _archLit[s].SetActive(open);
        }
        if (abyssSign != null) abyssSign.SetSubtitle(open ? abyssSignOpenSubtitle : _abyssClosedSubtitle);
        SetGateOpen(open, loud);
        if (!open || _abyssOpenAnnounced) return;
        _abyssOpenAnnounced = true;   // 허브 방문마다 한 번
        if (loud) AnnounceAbyssOpenAsync(_destroyCt).Forget();
    }

    private void SetRune(int i, bool on, bool loud)
    {
        if (_runeOn[i] == on) return;
        _runeOn[i] = on;
        if (_runeLit == null) return;
        _runeDim[i].SetActive(!on);
        _runeLit[i].SetActive(on);
        if (on && loud) FlashAsync(_runeLit[i].transform.position, _destroyCt, RuneFlashPeak).Forget();
    }

    // ── ⑧ 장비 → 파츠 ──

    /// <summary>
    /// 원거리 무기를 얻고 파츠는 아직이면 파츠 공방이 「다음」이다(파츠는 원거리 무기에 끼우는 것 — 09-28 사용자).
    /// 막 얻은 순간(loud)엔 바닥 빛 길이 이어진 뒤 받침이 켜지고, 이미 그런 상태로 들어왔거나 파츠를 고르면 조용히 켜고 끈다.
    /// </summary>
    private void SetPartsAwake(bool awake, bool loud)
    {
        if (_partsAwake == awake) return;
        _partsAwake = awake;
        if (awake && loud) PartsWakeAsync(_destroyCt).Forget();
        else FadePartsGlow(awake ? 1f : 0f, awake ? 0f : PartsGlowFade);
    }

    /// <summary>
    /// 원거리 무기가 몸에 스민 뒤 — 공방에서 파츠 공방까지 바닥에 금빛 길이 뻗고, 파츠 받침에 빛 고리·약한 광원·오르는 빛이 천천히 켜진다.
    /// 09-29 사용자 「원거리를 고르면 나오는 이펙트가 튀고 애매하다」 — 불쑥 생겨 날아가던 구슬 · 섬광 14 · 글자 박힌 궤도 구슬(Buff orbital)을 걷었다.
    /// 바닥 선은 내려다보는 카메라에 가장 잘 읽히고, 길의 끝이 곧 가야 할 곳이다. 조작은 막지 않는다.
    /// </summary>
    private async UniTaskVoid PartsWakeAsync(CancellationToken ct)
    {
        LineRenderer path = null;
        try
        {
            await UniTask.WaitUntil(() => _acquireInFlight <= 0, cancellationToken: ct);
            var forge = runeTargets != null && runeTargets.Length > 1 ? runeTargets[0] : null;
            var parts = runeTargets != null && runeTargets.Length > 1 ? runeTargets[1] : null;
            if (forge != null && parts != null)
                path = MakeLine("~PartsPath", 2, false);
            if (path != null)
            {
                Vector3 dir = parts.position - forge.position;
                dir.y = 0f;
                dir.Normalize();
                float y = Mathf.Max(forge.position.y, parts.position.y) + 0.08f;
                Vector3 a = forge.position + dir * 1.8f, b = parts.position - dir * PartsRingRadius;   // 탁자 앞에서 파츠 고리 가장자리까지
                a.y = b.y = y;
                path.transform.rotation = Quaternion.Euler(90f, 0f, 0f);   // 바닥에 눕힌다(고리와 같은 방식)
                path.alignment = LineAlignment.TransformZ;
                path.widthMultiplier = 0.22f;
                path.SetPosition(0, a);
                for (float t = 0f; t < PartsPathDraw; t += Time.unscaledDeltaTime)
                {
                    float k = Mathf.SmoothStep(0f, 1f, t / PartsPathDraw);
                    path.SetPosition(1, Vector3.Lerp(a, b, k));
                    SetPathColor(path, 0.85f);
                    await UniTask.Yield(ct);
                }
                path.SetPosition(1, b);
            }
            FadePartsGlow(1f, PartsGlowFade);
            if (parts != null) FlashAsync(parts.position + Vector3.up * 1.2f, ct, RuneFlashPeak).Forget();
            Debug.Log("[BaseCampFx] 파츠 공방 깨어남 — 바닥 빛 길 + 받침 빛");

            if (path == null) return;
            await UniTask.Delay(TimeSpan.FromSeconds(PartsPathHold), DelayType.UnscaledDeltaTime, cancellationToken: ct);
            for (float t = 0f; t < PartsPathFade; t += Time.unscaledDeltaTime)
            {
                SetPathColor(path, 0.85f * (1f - t / PartsPathFade));
                await UniTask.Yield(ct);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (path != null) Destroy(path.gameObject);
        }
    }

    /// <summary>길 색 — 뻗는 머리 쪽이 밝고 꼬리(공방 쪽)는 옅다.</summary>
    private void SetPathColor(LineRenderer lr, float alpha)
    {
        var c = runeLitColor;
        lr.startColor = new Color(c.r, c.g, c.b, alpha * 0.35f);
        lr.endColor = new Color(c.r, c.g, c.b, alpha);
    }

    /// <summary>파츠 받침 빛(고리·광원·오르는 빛)을 to까지 천천히 — 번쩍이지 않고 고르게.</summary>
    private void FadePartsGlow(float to, float duration)
    {
        _partsGlowCts?.Cancel();
        _partsGlowCts?.Dispose();
        _partsGlowCts = CancellationTokenSource.CreateLinkedTokenSource(_destroyCt);
        FadePartsGlowAsync(to, duration, _partsGlowCts.Token).Forget();
    }

    private async UniTaskVoid FadePartsGlowAsync(float to, float duration, CancellationToken ct)
    {
        if (!EnsurePartsGlow()) return;
        float from = _partsGlow;
        try
        {
            for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
            {
                ApplyPartsGlow(Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / duration)));
                await UniTask.Yield(ct);
            }
            ApplyPartsGlow(to);
        }
        catch (OperationCanceledException) { }
    }

    private bool EnsurePartsGlow()
    {
        if (_partsRing != null) return true;
        var parts = runeTargets != null && runeTargets.Length > 1 ? runeTargets[1] : null;
        if (parts == null) return false;
        _partsRing = MakeLine("~PartsRing", RingSegments, true);
        if (_partsRing == null) return false;
        SetRing(_partsRing, parts.position, PartsRingRadius, runeLitColor, 0f);

        var lightGo = new GameObject("~PartsLight");
        lightGo.transform.position = parts.position + Vector3.up * 1.6f;
        _partsLight = lightGo.AddComponent<Light>();
        _partsLight.type = LightType.Point;
        _partsLight.color = runeLitColor;
        _partsLight.range = 7f;
        _partsLight.shadows = LightShadows.None;
        _partsLight.intensity = 0f;

        if (runeGlowMaterial != null)
        {
            _partsMotes = MakeMotes("~PartsMotes", parts, Vector3.up * 0.2f, new Vector3(4.5f, 0.2f, 4.5f), 0f, 2.6f, 0.3f,
                                    runeLitColor, new Vector3(-0.05f, 0.35f, -0.05f), new Vector3(0.05f, 0.8f, 0.05f));
            _partsMotes.Play();
        }
        return true;
    }

    private void ApplyPartsGlow(float k)
    {
        _partsGlow = k;
        var parts = runeTargets[1];
        SetRing(_partsRing, parts.position, PartsRingRadius, runeLitColor, 0.7f * k);
        _partsRing.enabled = k > 0.01f;
        if (_partsLight != null) _partsLight.intensity = PartsLightMax * k;
        if (_partsMotes != null)
        {
            var em = _partsMotes.emission;
            em.rateOverTime = PartsMoteRate * k;
        }
    }

    private async UniTaskVoid AnnounceAbyssOpenAsync(CancellationToken ct)
    {
        try
        {
            // 날아가는 얻기 구슬이 다 도착한 뒤 한 박자 — 유물 빛과 배너가 같은 순간에 겹쳤다(ae 09-28 캡처)
            await UniTask.WaitUntil(() => _acquireInFlight <= 0, cancellationToken: ct);
            await UniTask.Delay(TimeSpan.FromSeconds(0.6f), DelayType.UnscaledDeltaTime, cancellationToken: ct);
            foreach (var lamp in _archLit)
                if (lamp != null) FlashAsync(lamp.transform.position, ct).Forget();   // 아치 점등
            if (portalCenter != null) FlashAsync(portalCenter.position + Vector3.up * 3f, ct).Forget();
            ZoneSign.RequestTitle(abyssOpenTitle, abyssOpenSubtitle);
            Debug.Log("[BaseCampFx] 심연이 열렸다 — 준비 룬 셋");
        }
        catch (OperationCanceledException) { }
    }

    // ── ⑦ 성문 막 ──

    /// <summary>
    /// 성문 통로를 가로지르는 마력 막 — 준비가 덜 됐다는 표시(09-28 사용자 「문도 이펙트가 필요해」) · 닫힘 동안 실제로 막는다(「개선 진행」).
    /// 닫힘: 막(양면 띠, 위로 옅어짐) + 문턱 룬 띠 + 막을 타고 오르는 빛 입자.
    /// 열림: 막이 금빛으로 번지며 걷히고, 심연 쪽으로 빨려 드는 빛 입자 + 통로 안 금빛. 자동 방출이라 평소엔 매 프레임 코드가 돌지 않는다.
    /// </summary>
    private void BuildGateVeil()
    {
        if (gateFront == null || gateVeilMaterial == null || runeGlowMaterial == null) return;
        var root = new GameObject("~GateVeil").transform;
        root.SetPositionAndRotation(gateFront.position + gateFront.forward * gateVeilDepth,
                                    Quaternion.LookRotation(Vector3.ProjectOnPlane(gateFront.forward, Vector3.up), Vector3.up));
        _gateRoot = root;
        float hw = gatePassageWidth * 0.5f;

        // 막이 실제로 통로를 막는다(닫힘 동안) — 통로 충돌 틈(3.8 m)보다 조금 넓게. 안내는 막 앞에서(UpdateGateNotice)
        _gateBlocker = root.gameObject.AddComponent<BoxCollider>();
        _gateBlocker.size = new Vector3(gatePassageWidth + 0.8f, gateVeilHeight, 0.4f);
        _gateBlocker.center = new Vector3(0f, gateVeilHeight * 0.5f, 0f);

        // 막 — 양면 띠, 3단(아래 · 중간은 진하게 → 위만 투명). 정점 색으로 칠해 열릴 때 색·알파만 바꾼다.
        // 2단(아래→위 곧장 투명)이었을 땐 막에 붙으면 카메라가 위에서 투명한 윗부분을 통해 봐서 「보이지 않는 벽」이 됐다(ae 09-28).
        const int cols = 8;
        var verts = new Vector3[(cols + 1) * VeilRows];
        var uv = new Vector2[verts.Length];
        _veilColors = new Color[verts.Length];
        for (int i = 0; i <= cols; i++)
        {
            float u = i / (float)cols;
            float x = Mathf.Lerp(-hw, hw, u);
            for (int r = 0; r < VeilRows; r++)
            {
                verts[i * VeilRows + r] = new Vector3(x, gateVeilHeight * VeilRowHeights[r], 0f);
                uv[i * VeilRows + r] = new Vector2(u * 2f, VeilRowHeights[r]);
            }
        }
        var tris = new int[cols * (VeilRows - 1) * 12];
        for (int i = 0, k = 0; i < cols; i++)
        {
            for (int r = 0; r < VeilRows - 1; r++)
            {
                int a = i * VeilRows + r, b = a + 1, c = a + VeilRows, d = c + 1;
                tris[k++] = a; tris[k++] = b; tris[k++] = c;  tris[k++] = c; tris[k++] = b; tris[k++] = d;   // 앞
                tris[k++] = a; tris[k++] = c; tris[k++] = b;  tris[k++] = c; tris[k++] = d; tris[k++] = b;   // 뒤
            }
        }
        _veilMesh = new Mesh { name = "GateVeil", vertices = verts, uv = uv, triangles = tris };
        PaintVeil(gateVeilColor);
        _veilMesh.RecalculateBounds();

        var veil = new GameObject("Veil");
        veil.transform.SetParent(root, false);
        veil.AddComponent<MeshFilter>().sharedMesh = _veilMesh;
        _veilRenderer = veil.AddComponent<MeshRenderer>();
        _veilRenderer.sharedMaterial = gateVeilMaterial;
        _veilRenderer.shadowCastingMode = ShadowCastingMode.Off;
        _veilRenderer.receiveShadows = false;

        // 문턱 룬 띠 — 바닥에 눕힌 빛 선(마력 결계 띠와 같은 방식)
        if (beamMaterial != null)
        {
            var bandGo = new GameObject("ThresholdBand");
            bandGo.transform.SetParent(root, false);
            bandGo.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            _veilBand = bandGo.AddComponent<LineRenderer>();
            _veilBand.useWorldSpace = true;
            _veilBand.alignment = LineAlignment.TransformZ;
            _veilBand.textureMode = LineTextureMode.Tile;
            _veilBand.sharedMaterial = beamMaterial;
            _veilBand.widthMultiplier = 0.55f;   // 위에서 내려다볼 때도 「여기가 경계」로 읽히게 넓게
            _veilBand.shadowCastingMode = ShadowCastingMode.Off;
            _veilBand.receiveShadows = false;
            _veilBand.positionCount = 2;
            _veilBand.SetPosition(0, root.TransformPoint(new Vector3(-hw, 0.06f, 0f)));
            _veilBand.SetPosition(1, root.TransformPoint(new Vector3(hw, 0.06f, 0f)));
        }

        // 막을 타고 오르는 빛(닫힘) · 심연 쪽(성문 안, +Z)으로 빨려 드는 빛(열림)
        _veilMotes = MakeMotes("VeilMotes", root, new Vector3(0f, gateVeilHeight * 0.35f, 0f), new Vector3(gatePassageWidth, gateVeilHeight * 0.7f, 0.15f),
                               14f, 2.2f, 0.22f, gateVeilColor, new Vector3(-0.05f, 0.25f, -0.03f), new Vector3(0.05f, 0.6f, 0.03f));
        _abyssDraw = MakeMotes("AbyssDraw", root, new Vector3(0f, 2f, 0f), new Vector3(gatePassageWidth, 3.6f, 0.3f),
                               18f, 2.4f, 0.2f, runeLitColor, new Vector3(-0.1f, -0.25f, 1.4f), new Vector3(0.1f, 0.05f, 2.4f));
        _veilMotes.Play();

        var lightGo = new GameObject("GateLight");
        lightGo.transform.SetParent(root, false);
        lightGo.transform.localPosition = new Vector3(0f, 2.5f, 3f);
        _gateLight = lightGo.AddComponent<Light>();
        _gateLight.type = LightType.Point;
        _gateLight.color = runeLitColor;
        _gateLight.range = 7f;
        _gateLight.intensity = 0f;
        _gateLight.shadows = LightShadows.None;
        ApplyGateState(0f);
    }

    /// <summary>닫힌 막 앞(광장 쪽 2.2 m 안)에 서면 부족분 안내 — 재출력 간격은 게이트가 지킨다.</summary>
    private void UpdateGateNotice()
    {
        if (_archOn || _gateBlocker == null || dungeonGate == null) return;
        var pt = Managers.Player?.PlayerTransform;
        if (pt == null) return;
        Vector3 local = _gateRoot.InverseTransformPoint(pt.position);
        if (!(local.z > -2.2f && local.z < 0.3f && Mathf.Abs(local.x) < gatePassageWidth)) return;
        dungeonGate.ShowMissingNotice();
        // 막에 닿았다(몸 반경 + 막 두께) — 한 번 번쩍
        if (local.z > -0.9f && Mathf.Abs(local.x) < gatePassageWidth * 0.5f + 0.4f && Time.unscaledTime >= _veilTouchUntil)
            VeilTouchAsync(_gateRoot.TransformPoint(new Vector3(local.x, 1.4f, 0f)), _destroyCt).Forget();
    }

    private void PaintVeil(Color c)
    {
        for (int i = 0; i < _veilColors.Length; i += VeilRows)
            for (int r = 0; r < VeilRows; r++)
            {
                var v = c;
                v.a *= VeilRowAlpha[r];
                _veilColors[i + r] = v;
            }
        _veilMesh.colors = _veilColors;
    }

    /// <summary>닫힌 막에 닿으면 한 번 번쩍 — 막히는 순간이 「보이지 않는 벽」으로 느껴지지 않게(ae 09-28). 1초에 한 번.</summary>
    private async UniTaskVoid VeilTouchAsync(Vector3 at, CancellationToken ct)
    {
        _veilTouchUntil = Time.unscaledTime + 1f;
        // 약하게 — 번쩍이 부족분 안내 글자 뒤에서 밝아 읽기 어려웠다(ae 09-28)
        FlashAsync(at, ct, 5f).Forget();
        if (_veilMotes != null) _veilMotes.Emit(18);
        var hot = Color.Lerp(gateVeilColor, Color.white, 0.25f);
        hot.a = 1f;
        try
        {
            for (float t = 0f; t < 0.6f; t += Time.unscaledDeltaTime)
            {
                if (_archOn) return;   // 그사이 열리면 여는 연출에 맡긴다
                float k = t < 0.12f ? t / 0.12f : 1f - (t - 0.12f) / 0.48f;
                PaintVeil(Color.Lerp(gateVeilColor, hot, k));
                await UniTask.Yield(ct);
            }
            if (!_archOn) PaintVeil(gateVeilColor);
        }
        catch (OperationCanceledException) { }
    }

    private void SetGateOpen(bool open, bool loud)
    {
        if (_veilMesh == null) return;
        _gateCts?.Cancel();
        _gateCts?.Dispose();
        _gateCts = CancellationTokenSource.CreateLinkedTokenSource(_destroyCt);
        if (open && loud) OpenGateAsync(_gateCts.Token).Forget();
        else ApplyGateState(open ? 1f : 0f);
    }

    /// <summary>k 0 = 닫힘, 1 = 열림 — 막 색 보랏빛 → 금빛 · 알파 → 0, 입자 교대, 통로 안 금빛.</summary>
    private void ApplyGateState(float k)
    {
        var c = Color.Lerp(gateVeilColor, runeLitColor, Mathf.Clamp01(k * 1.6f));
        c.a = gateVeilColor.a * (1f - k);
        PaintVeil(c);
        bool shown = k < 0.999f;
        _veilRenderer.enabled = shown;
        _gateBlocker.enabled = k < 0.5f;   // 걷히는 도중 절반부터 지나갈 수 있다
        if (_veilBand != null)
        {
            _veilBand.enabled = shown;
            _veilBand.startColor = _veilBand.endColor = new Color(c.r, c.g, c.b, 0.9f * (1f - k));
        }
        _gateLight.intensity = 3f * k;
        bool drawing = k > 0.5f;
        if (drawing != _abyssDraw.isEmitting)
        {
            if (drawing) { _abyssDraw.Play(); _veilMotes.Stop(false, ParticleSystemStopBehavior.StopEmitting); }
            else { _abyssDraw.Stop(false, ParticleSystemStopBehavior.StopEmitting); _veilMotes.Play(); }
        }
    }

    private async UniTaskVoid OpenGateAsync(CancellationToken ct)
    {
        try
        {
            FlashAsync(_veilRenderer.transform.position + Vector3.up * 2f, ct).Forget();
            for (float t = 0f; t < gateOpenDuration; t += Time.unscaledDeltaTime)
            {
                ApplyGateState(t / gateOpenDuration);
                await UniTask.Yield(ct);
            }
            ApplyGateState(1f);
            Debug.Log("[BaseCampFx] 성문 막 걷힘");
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>상자 안에서 자동 방출되는 빛 입자(Glow1cg). 속도는 부모 기준(성문 막은 +Z가 심연 쪽).</summary>
    private ParticleSystem MakeMotes(string name, Transform parent, Vector3 localPos, Vector3 box, float rate, float lifetime, float size,
                                     Color color, Vector3 velMin, Vector3 velMax)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = true;
        main.duration = 1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.7f, lifetime);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(size * 0.5f, size);
        main.startColor = color;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 200;
        main.playOnAwake = false;

        var emission = ps.emission;
        emission.rateOverTime = rate;
        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = box;

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.Local;
        vel.x = new ParticleSystem.MinMaxCurve(velMin.x, velMax.x);
        vel.y = new ParticleSystem.MinMaxCurve(velMin.y, velMax.y);
        vel.z = new ParticleSystem.MinMaxCurve(velMin.z, velMax.z);

        var fade = ps.colorOverLifetime;
        fade.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(0f, 1f) });
        fade.color = g;

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = runeGlowMaterial;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        return ps;
    }

    // ── ⑥ 심연 진입: 내려갈수록 어두워짐 ──

    /// <summary>런타임 전역 볼륨 — 공유 프로파일 에셋을 건드리지 않는다(씬 PP 프로파일은 여러 씬이 공유).</summary>
    private void BuildDescentVolume()
    {
        if (gateFront == null || portalCenter == null) return;
        var go = new GameObject("~DescentDarkness");
        go.transform.SetParent(transform, false);
        _descentVolume = go.AddComponent<Volume>();
        _descentVolume.isGlobal = true;
        _descentVolume.priority = 50f;
        _descentVolume.weight = 0f;
        _descentProfile = ScriptableObject.CreateInstance<VolumeProfile>();
        _descentProfile.Add<UnityEngine.Rendering.Universal.ColorAdjustments>(true).postExposure.Override(descentExposure);
        var vg = _descentProfile.Add<UnityEngine.Rendering.Universal.Vignette>(true);
        vg.intensity.Override(descentVignette);
        vg.smoothness.Override(0.6f);
        _descentVolume.sharedProfile = _descentProfile;
    }

    /// <summary>성문 → 포탈 진행률(가로 거리)로 가중치 — 계단 높이가 아니라 가로로(설계서 4장, 계단 떨림 교훈).</summary>
    private void UpdateDescentDarkness()
    {
        if (_descentVolume == null) return;
        var pt = Managers.Player?.PlayerTransform;
        if (pt == null) return;
        Vector3 ab = portalCenter.position - gateFront.position; ab.y = 0f;
        Vector3 ap = pt.position - gateFront.position; ap.y = 0f;
        // 선형 — 부드러운 곡선(smoothstep)은 층계참(가운데쯤)에서 −19%로 약했다(ae 09-28 실측)
        float w = Mathf.Clamp01(Vector3.Dot(ap, ab) / Mathf.Max(0.01f, ab.sqrMagnitude));
        if (Mathf.Abs(w - _descentWeight) < 0.005f) return;
        _descentWeight = w;
        _descentVolume.weight = w;
    }

    // ── 공용 조각 ──

    /// <summary>씬 전환 로딩 덮개가 아직 떠 있는가(페이드아웃이 끝나면 비활성).</summary>
    private static bool IsLoadingCoverUp()
    {
        var loading = UI_SceneLoading.Instance;
        return loading != null && loading.gameObject.activeInHierarchy;
    }

    /// <summary>켜져 있던 렌더러만 끄고 목록으로 돌려준다(되켤 때 원래 꺼져 있던 것은 건드리지 않게).</summary>
    private static System.Collections.Generic.List<Renderer> HideRenderers(GameObject root)
    {
        var list = new System.Collections.Generic.List<Renderer>();
        foreach (var r in root.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled) continue;
            r.enabled = false;
            list.Add(r);
        }
        return list;
    }

    private static void ShowRenderers(System.Collections.Generic.List<Renderer> list)
    {
        if (list == null) return;
        foreach (var r in list)
            if (r != null) r.enabled = true;
    }

    /// <summary>디졸브 재질이 입혀졌는지 볼 몸 렌더러 하나(입자·글자·「~」 도우미 제외 — 디졸브 대상과 같은 기준).</summary>
    private static Renderer FirstBodyRenderer(System.Collections.Generic.List<Renderer> list)
    {
        foreach (var r in list)
            if ((r is SkinnedMeshRenderer || r is MeshRenderer) && !r.gameObject.name.StartsWith("~") && !r.TryGetComponent<TMPro.TMP_Text>(out _))
                return r;
        return null;
    }

    /// <summary>이펙트 프리팹을 띄운다 — 원본 투사체의 충돌체·강체는 끈다(시각만). loop=true면 계속 돈다(룬·날아가는 구슬).</summary>
    private static GameObject SpawnFx(GameObject prefab, Vector3 at, float scale, bool loop)
    {
        var go = Instantiate(prefab, at, Quaternion.identity);
        go.transform.localScale = Vector3.one * scale;
        foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) { rb.isKinematic = true; rb.detectCollisions = false; }
        foreach (var col in go.GetComponentsInChildren<Collider>(true)) col.enabled = false;
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
        {
            var main = ps.main;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;   // 부모 배율을 따르게(Local 배율 함정)
            if (!loop) continue;
            ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            main.loop = true;
            ps.Play(false);
        }
        return go;
    }

    private static void SpawnBurst(GameObject prefab, Vector3 at, float scale)
    {
        if (prefab == null) return;
        Destroy(SpawnFx(prefab, at, scale, false), 3f);
    }

    /// <summary>방출을 멈추고 남은 불티가 사그라든 뒤 지운다.</summary>
    private static void StopAndDestroy(GameObject go, float delay)
    {
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
            ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
        foreach (var l in go.GetComponentsInChildren<Light>(true)) l.enabled = false;
        Destroy(go, delay);
    }

    private LineRenderer MakeLine(string name, int points, bool loop)
    {
        if (beamMaterial == null) return null;
        var go = new GameObject(name);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.loop = loop;
        lr.positionCount = points;
        lr.sharedMaterial = beamMaterial;
        lr.textureMode = LineTextureMode.Stretch;
        lr.shadowCastingMode = ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.widthMultiplier = 0f;
        if (loop)
        {
            // 바닥에 눕힌 고리 — 마력 결계 띠와 같은 방식(자식을 눕혀 TransformZ 정렬)
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            lr.alignment = LineAlignment.TransformZ;
        }
        return lr;
    }

    /// <summary>빛 속에서 몸이 맺힌다 — 디졸브 재질이 입혀진(=투명해진) 뒤에 숨겨 둔 렌더러를 켠다(재질 교체가 한 프레임 늦어도 맨몸이 번쩍 보이지 않게).</summary>
    private static async UniTaskVoid RevealBodyAsync(GameObject body, System.Collections.Generic.List<Renderer> hidden, float duration, Color c, CancellationToken ct)
    {
        try
        {
            if (body == null) return;
            var probe = FirstBodyRenderer(hidden);
            var before = probe != null ? probe.sharedMaterial : null;
            DissolveEffect.PlayAppear(body, duration, null, default, new Color(c.r * 2.4f, c.g * 2.4f, c.b * 2.4f, 1f));
            for (int f = 0; f < 10 && probe != null && probe.sharedMaterial == before; f++)
                await UniTask.Yield(ct);
        }
        catch (OperationCanceledException) { }
        finally { ShowRenderers(hidden); }
    }

    private static void SetBeam(LineRenderer lr, Vector3 feet, float bottom, float top, float width, Color c, float alpha)
    {
        if (lr == null) return;
        lr.SetPosition(0, feet + Vector3.up * bottom);
        lr.SetPosition(1, feet + Vector3.up * top);
        lr.widthMultiplier = width;
        lr.startColor = new Color(c.r, c.g, c.b, alpha);
        lr.endColor = new Color(c.r, c.g, c.b, alpha * 0.15f);   // 위로 갈수록 하늘로 흩어진다
    }

    private void SetRing(LineRenderer lr, Vector3 feet, float radius, Color c, float alpha)
    {
        if (lr == null) return;
        for (int i = 0; i < RingSegments; i++)
        {
            float a = i * Mathf.PI * 2f / RingSegments;
            _ringPoints[i] = feet + new Vector3(Mathf.Cos(a) * radius, 0.06f, Mathf.Sin(a) * radius);
        }
        lr.SetPositions(_ringPoints);
        lr.widthMultiplier = 0.16f;
        lr.startColor = lr.endColor = new Color(c.r, c.g, c.b, alpha);
    }

    private static Light MakeLight(Vector3 at, Color c)
    {
        var go = new GameObject("~SummonLight");
        go.transform.position = at;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = c;
        l.range = 9f;
        l.shadows = LightShadows.None;
        l.intensity = 0f;
        return l;
    }

    /// <summary>한 번 번쩍이는 금빛(생성 순간). 임시 광원 — 끝나면 지운다.</summary>
    private async UniTaskVoid FlashAsync(Vector3 at, CancellationToken ct, float peak = 14f)
    {
        var go = new GameObject("ConjureFlash");
        go.transform.position = at;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = conjureEdge;
        l.range = 9f;
        l.shadows = LightShadows.None;
        l.intensity = 0f;
        try { await PulseLightAsync(l, 0f, peak, 0.18f, 0.8f, ct); }
        catch (OperationCanceledException) { }
        finally { if (go != null) Destroy(go); }
    }

    private static async UniTask PulseLightAsync(Light light, float from, float peak, float up, float down, CancellationToken ct)
    {
        if (light == null) return;
        try
        {
            for (float t = 0f; t < up; t += Time.unscaledDeltaTime)
            {
                light.intensity = Mathf.Lerp(from, peak, t / up);
                await UniTask.Yield(ct);
            }
            for (float t = 0f; t < down; t += Time.unscaledDeltaTime)
            {
                light.intensity = Mathf.Lerp(peak, from, t / down);
                await UniTask.Yield(ct);
            }
        }
        finally { if (light != null) light.intensity = from; }
    }
}
