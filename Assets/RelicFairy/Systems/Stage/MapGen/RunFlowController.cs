using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;

/// <summary>
/// 절차적(하데스형) 격리 룸 진행 오케스트레이터.
/// RunSequencer로 출구를 롤하고, GameRunBootstrapper.BuildProcRoomAsync로 방을 빌드,
/// ProcRoomGate로 다음 방 전환을 트리거한다.
///
/// 물리 연결 없음 — 방 1개씩 고정 앵커에 격리 빌드하고, 통과 시 이전 방을 디스폰한다.
/// 레거시 contiguous 경로(ZoneProgressionService 등)와 병행하며 서로 간섭하지 않는다.
/// </summary>
public class RunFlowController : MonoBehaviour
{
    [SerializeField] private RunStructureConfig _structureConfig;
    [SerializeField, Tooltip("_structureConfig 비었을 때 Addressables로 로드할 키. 런타임 AddComponent 생성 경로 대응(에셋만 만들고 인스펙터 배선 불가).")]
    private string _structureConfigKey = "RUN_STRUCTURE_DEFAULT";
    // 해석된 런 구조(IRunStructure): CSV(RUN_STRUCTURE) 정본 → SO 폴백. 인터페이스라 [SerializeField] 불가하여 별도 보유.
    private IRunStructure _resolvedStructure;
    [SerializeField] private string _poolKey = "CHAPTER_1_ROOM_POOL";
    [SerializeField, Tooltip("시작 방 pool_key. 비우면 첫 Normal 방 사용.")]
    private string _startPoolKey;
    [SerializeField, Tooltip("방을 빌드할 고정 앵커. 비우면 원점.")]
    private Transform _anchor;
    [SerializeField, Tooltip("0이면 매 런 랜덤 시드.")]
    private int _seed;

    [Header("전환 연출 (방 진입 와이프)")]
    [SerializeField, Tooltip("덮기 시간(초). 텔레포트만 가리게 짧게.")]   private float          _coverDuration  = 0.12f;
    [SerializeField, Tooltip("드러내기 시간(초). 복귀 후 디졸브로 생성 노출.")] private float          _revealDuration = 0.20f;
    [SerializeField, Tooltip("디졸브 시작 후 복귀까지 지연(초). 디졸브 진행 중에 들어가게.")] private float _revealDelay = 0.3f;
    [SerializeField] private AnimationCurve _coverCurve  = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [SerializeField] private AnimationCurve _revealCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("게이트 문 (봉인 → 정보 공개)")]
    [SerializeField, Tooltip("클리어 시 문 정보 공개(색 전환+글로우) 시간(초).")] private float _gateRevealDuration = 0.6f;
    [SerializeField, Tooltip("공개 시 글로우(emission) 세기.")]                  private float _gateGlowIntensity = 2.5f;

    // 봉인(전투 중 막힌 문) / 입구 잠금 색 — 중립 톤
    private static readonly Color SealedColor = new Color(0.10f, 0.11f, 0.13f);
    private static readonly Color LockedColor = new Color(0.06f, 0.06f, 0.07f);

    private RunSequencer                 _sequencer;
    private RunPlan                      _runPlan; // 런 시작 시 산출한 명시적 일정표(가시성/미리보기/드리프트 검증용)
    private List<ZonePoolEntry>          _pool;
    private System.Random                _rng;
    private ProcRoomResult               _current;
    private RoomWaveController           _currentWave;

    /// <summary>현재 활성 런 플로우. 재련소/상점/서약 등이 행동 확정 시 SaveNow를 호출하기 위한 진입점.</summary>
    public static RunFlowController Active { get; private set; }

    private bool     _currentRoomCleared;   // 현재 방 클리어 여부(세이브 기록)
    private bool     _resumedRoomCleared;   // 이어하기: 저장 당시 방이 클리어 상태였는가(1회성)
    private DoorPlan _lastPlan;             // 재저장(S2/S3)용 방 정보 캐시
    private DoorEdge _lastEdge;
    private int      _lastMirror;
    private bool     _hasLastPlan;
    private readonly List<GateView>      _gates = new();
    private GameObject                   _entranceLock;
    private CancellationTokenSource      _cts;

    // 게이트 포탈 VFX — 런타임 AddComponent 생성이라 인스펙터 배선 불가 → Addressable 프리로드 캐시.
    private const string GatePortalKey = "ProcGatePortal";
    private GameObject _gatePortalPrefab;
    private bool       _gatePortalTried;

    /// <summary>게이트 1개의 시각/물리 구성요소 묶음. 봉인 시 blocker로 막고, 공개 시 marker 색 전환.</summary>
    private sealed class GateView
    {
        public ProcRoomGate gate;
        public Renderer     marker;
        public Collider     blocker; // 봉인: solid(통과 차단) / 공개: 비활성
        public GameObject   portal;  // 게이트 포탈 VFX — 봉인 시 비활성, 공개 시 활성(열림 연출)
        public Transform    door;    // 봉인 석문 — 봉인 시 닫힘(낙하), 공개 시 위로 올라가며 열림
        public float        openH;   // 개구부 높이(문 낙하/상승 거리)
        public Vector3      sealedLocalPos; // 석문의 닫힘(봉인) 로컬 위치 — 낙하 대기 중에도 목표를 잃지 않게 보관
        public bool         opening;        // 공개(상승) 시작됨 — 진행 중인 낙하가 이걸 보고 물러난다
    }

    private Vector3 _baseAnchor;
    private int     _anchorToggle;
    private int     _heading; // 현재 진행 방향(0=N,1=E,2=S,3=W). 탄 출구 엣지로 갱신 → 다음 방 회전에 사용.
    private int     _masterSeed; // 런 마스터 시드 (세이브/이어하기 결정성).
    private bool    _resuming;   // 이어하기 재생성 중 — 중복 저장 억제용.
    private string  _resolvedStructureKey; // 이번 런의 챕터별 구조 config 키(StartRun/Resume에서 주입). 비면 _structureConfigKey.

    // ── Public ──────────────────────────────────────

    /// <summary>이번 런의 명시적 일정표(깊이별 출구 종류). 맵 미리보기 UI 등의 데이터 소스. 시작 전엔 null.</summary>
    public RunPlan RunPlan => _runPlan;

    /// <summary>절차 런 시작 — 풀 로드 → 시퀀서 생성 → 첫 방 진입.
    /// anchor: 방을 빌드할 고정 월드 위치(허브와 겹치지 않게 먼 곳). null이면 _anchor 또는 원점.
    /// poolKeyOverride: 챕터별 룸 풀 키(CHAPTER_N_ROOM_POOL). 비우면 직렬화된 _poolKey 사용.
    /// structureKeyOverride: 챕터별 레벨 스파인 키(CHAPTER_N_RUN_STRUCTURE). 비우면 _structureConfigKey 공유 기본.</summary>
    public async UniTask StartRunAsync(Vector3? anchor = null, string poolKeyOverride = null, string structureKeyOverride = null)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
        var ct = _cts.Token;
        _baseAnchor = anchor ?? (_anchor != null ? _anchor.position : Vector3.zero);
        _resolvedStructureKey = structureKeyOverride;

        var poolKey = !string.IsNullOrEmpty(poolKeyOverride) ? poolKeyOverride : _poolKey;
        _pool = await Managers.ZoneLayout.LoadPoolAsync(poolKey);
        if (_pool == null || _pool.Count == 0)
        {
            Debug.LogError($"[RunFlow] 풀 로드 실패: {poolKey}");
            return;
        }

        await EnsureStructureConfigAsync();

        int seed    = _seed != 0 ? _seed : Environment.TickCount;
        _masterSeed = seed;
        _rng        = new System.Random(seed);
        _sequencer  = new RunSequencer(_pool, _resolvedStructure, seed);
        _runPlan    = _sequencer.BuildPlan(); // 시작 시 전체 일정표 1회 산출(시드+config 순수 함수)
        DumpRunPlan(seed);

        var startEntry = FindStartEntry();
        if (startEntry == null)
        {
            Debug.LogError("[RunFlow] 시작 방 없음");
            return;
        }

        await EnterRoomAsync(new DoorPlan { kind = RoomPlanKind.Normal, entry = startEntry }, DoorEdge.North, ct);
    }

    /// <summary>
    /// 이어하기: 저장된 마스터 시드 + 시퀀서 상태로 절차 흐름을 복원하고,
    /// 저장된 현재 방을 동일 시드로 재생성해 입구에서 재개한다.
    /// 방 내부 전투 상태(적 위치/HP)는 직렬화하지 않으므로 전투는 새로 시작된다(하데스식).
    /// </summary>
    public async UniTask ResumeAsync(RunMetaSnapshot meta, Vector3 anchor, string poolKey, CancellationToken externalCt, string structureKeyOverride = null)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt, this.GetCancellationTokenOnDestroy());
        var ct = _cts.Token;

        _resolvedStructureKey = structureKeyOverride;
        _baseAnchor   = anchor;
        _masterSeed   = meta.masterSeed;
        _anchorToggle = meta.anchorToggle;
        _heading      = meta.heading;

        // 저장 당시 방이 클리어 상태였는가 → true면 재생성 시 몹을 스폰하지 않고 출구만 연다.
        _resumedRoomCleared = meta.currentRoomCleared;

        // 재련소 결정적 롤 스트림 재개 위치 — 방 안 저장 후 재접속해도 같은 롤을 다시 굴리지 못하게.
        var resumeSession = GameRunBootstrapper.Instance?.Run;
        if (resumeSession != null) resumeSession.CrucibleRollIndex = meta.crucibleRollIndex;

        var key = !string.IsNullOrEmpty(poolKey) ? poolKey : _poolKey;
        _pool = await Managers.ZoneLayout.LoadPoolAsync(key);
        if (_pool == null || _pool.Count == 0)
        {
            Debug.LogError($"[RunFlow] 이어하기 풀 로드 실패: {key}");
            return;
        }

        await EnsureStructureConfigAsync();

        _rng       = new System.Random(_masterSeed);
        _sequencer = new RunSequencer(_pool, _resolvedStructure, _masterSeed);
        _sequencer.RestoreState(meta.visitCount, meta.seqPhase, meta.shopUsed, meta.eventUsed, meta.cooldowns);
        _runPlan   = _sequencer.BuildPlan(); // 이어하기: 동일 시드+config로 일정표 재생성(직렬화 없음, 원본과 동일)
        DumpRunPlan(_masterSeed);

        var entry = !string.IsNullOrEmpty(meta.currentRoomPoolKey)
            ? _pool.Find(p => string.Equals(p.pool_key, meta.currentRoomPoolKey, StringComparison.OrdinalIgnoreCase))
            : null;
        if (entry == null) entry = FindStartEntry();

        var plan = new DoorPlan { kind = (RoomPlanKind)meta.currentRoomKind, entry = entry };

        _resuming = true;
        try
        {
            await EnterRoomAsync(plan, (DoorEdge)meta.heading, ct, meta.currentRoomMirror);
        }
        finally { _resuming = false; }

        Debug.Log($"[RunFlow] 이어하기 완료 — visit={meta.visitCount}, room={meta.currentRoomPoolKey}");
    }

    /// <summary>
    /// 보스 클리어 후 다음 챕터로 procgen을 이어서 재시작한다(씬 유지).
    /// 현재 방(보스 아레나)은 새 시작 방 진입 시 디스폰되고, 런 상태(인벤토리/버프)는 GameRunSession에 유지된다.
    /// 시드는 마스터 시드+챕터 번호로 결정적이라 이어하기 재현에 안전하다.
    /// GameRunBootstrapper.AdvanceChapterAsync가 챕터 갱신·키 해석 후 호출한다.
    /// </summary>
    public async UniTask StartNextChapterAsync(string poolKey, string structureKey)
    {
        var ct = _cts != null ? _cts.Token : this.GetCancellationTokenOnDestroy();

        // 이전 챕터 웨이브 구독·게이트 정리
        if (_currentWave != null) { _currentWave.OnRoomCleared -= HandleRoomCleared; _currentWave = null; }
        ClearGates();

        _resolvedStructureKey = structureKey;
        _structureConfig      = null; // 새 챕터 스파인 재로드 강제

        var key  = !string.IsNullOrEmpty(poolKey) ? poolKey : _poolKey;
        var pool = await Managers.ZoneLayout.LoadPoolAsync(key);
        if (pool == null || pool.Count == 0)
        {
            Debug.LogError($"[RunFlow] 다음 챕터 풀 로드 실패: {key}");
            return;
        }
        _pool = pool;
        await EnsureStructureConfigAsync();

        int chapterNum = (int)(GameRunBootstrapper.Instance?.Run?.CurrentChapter ?? 0);
        int seed       = RunSequencer.Combine(_masterSeed, 7000 + chapterNum); // 챕터별 결정적 시드
        _rng           = new System.Random(seed);
        _sequencer     = new RunSequencer(_pool, _resolvedStructure, seed);
        _runPlan       = _sequencer.BuildPlan();
        DumpRunPlan(seed);

        _heading = (int)DoorEdge.North;

        var startEntry = FindStartEntry();
        if (startEntry == null)
        {
            Debug.LogError("[RunFlow] 다음 챕터 시작 방 없음");
            return;
        }

        await EnterRoomAsync(new DoorPlan { kind = RoomPlanKind.Normal, entry = startEntry }, DoorEdge.North, ct);
        Debug.Log($"[RunFlow] 챕터 {chapterNum} 진행 시작 — pool={key}");
    }

    // ── Private ─────────────────────────────────────

    /// <summary>런 구조(IRunStructure)를 해석한다. 우선순위: 인스펙터 SO(명시 오버라이드)
    /// → CSV 정본(RUN_STRUCTURE, 서버 CDN) → SO 오프라인 폴백(Addressables) → null(전 방 Normal 안전동작).
    /// 실패해도 진행은 막지 않음 — null이면 RunSequencer가 전 방 Normal로 동작(구조만 비활성).</summary>
    private async UniTask EnsureStructureConfigAsync()
    {
        var chapter = GameRunBootstrapper.Instance?.Run?.CurrentChapter ?? ChapterId.Chapter1;

        // 0순위: 인스펙터 직접 배선(개발자 명시 오버라이드).
        if (_structureConfig != null)
        {
            _resolvedStructure = _structureConfig;
            LogResolvedStructure("Inspector-SO", chapter);
            return;
        }

        // 1순위: CSV 정본 — RUN_STRUCTURE(CDN). 챕터는 현재 런 세션에서 해석.
        _resolvedStructure = Managers.RunStructureData?.Get(chapter);
        if (_resolvedStructure != null)
        {
            LogResolvedStructure("CSV", chapter);
            return;
        }

        // 2순위: SO 오프라인 폴백 — Addressables(챕터별 키 → 공유 기본 키).
        string key = !string.IsNullOrEmpty(_resolvedStructureKey) ? _resolvedStructureKey : _structureConfigKey;
        if (!string.IsNullOrEmpty(key))
            _resolvedStructure = await Managers.AddressableManager.TryLoadAssetAsync<RunStructureConfig>(key);

        if (_resolvedStructure == null && !string.IsNullOrEmpty(_structureConfigKey) && key != _structureConfigKey)
        {
            Debug.Log($"[RunFlow] 챕터 구조 '{key}' 없음 — 공유 기본 '{_structureConfigKey}' 폴백.");
            _resolvedStructure = await Managers.AddressableManager.TryLoadAssetAsync<RunStructureConfig>(_structureConfigKey);
        }

        if (_resolvedStructure == null)
            Debug.LogWarning($"[RunFlow] 런 구조 로드 실패: {key} — 전 방 Normal로 진행(구조 비활성).");
        else
            LogResolvedStructure($"Addressable-SO({key})", chapter);
    }

    // 해석된 런 구조의 실제 수치를 한 줄로 출력 — CSV/SO 어느 소스가 들어갔는지 확인용.
    // RUN_STRUCTURE CDN 업로드 여부에 따라 소스가 갈리므로 진단 가치가 남아 있어 유지한다.
    // 릴리즈 빌드에서는 Conditional이 호출문과 본문(StringBuilder 조립 포함)을 전부 제거한다.
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    private void LogResolvedStructure(string source, ChapterId chapter)
    {
        var s = _resolvedStructure;
        if (s == null) return;
        var ms = new System.Text.StringBuilder();
        for (int v = 1; v <= s.BossThreshold; v++)
        {
            var k = s.GetMilestoneKind(v);
            if (k.HasValue) ms.Append($"{v}:{k.Value} ");
        }
        RFLog.D($"[RunStructure검증] {chapter} 소스={source} boss@{s.BossThreshold} " +
                  $"shop{s.ShopChance:0.##}/{s.ShopMaxPerChapter} event{s.EventChance:0.##}/{s.EventMaxPerChapter} elite{s.EliteChance:0.##} " +
                  $"diff[0→{s.DifficultyAt(0):0.##} / {s.BossThreshold}→{s.DifficultyAt(s.BossThreshold):0.##}] ms{{{ms.ToString().Trim()}}}");
    }

    private ZonePoolEntry FindStartEntry()
    {
        if (!string.IsNullOrEmpty(_startPoolKey))
        {
            var byKey = _pool.Find(p => string.Equals(p.pool_key, _startPoolKey, StringComparison.OrdinalIgnoreCase));
            if (byKey != null) return byKey;
        }
        return _pool.Find(p => string.Equals(p.category, "Normal", StringComparison.OrdinalIgnoreCase)) ?? _pool[0];
    }

    private async UniTask EnterRoomAsync(DoorPlan plan, DoorEdge fromEdge, CancellationToken ct, int forcedMirror = -1)
    {
        var grb = GameRunBootstrapper.Instance;
        if (grb == null || plan.entry == null) return;

        // 게이트 포탈 VFX 참조 — 부트스트래퍼 직렬화 참조만 사용. null이면 포탈 없음(석문이 시각 담당).
        // (Addressable 폴백 제거 — 문짝에 안 맞는 대형 스킬 VFX가 되살아나는 문제)
        if (!_gatePortalTried)
        {
            _gatePortalTried = true;
            _gatePortalPrefab = grb.GatePortalPrefab;
        }

        var dir = WipeDir(fromEdge);
        _heading = (int)fromEdge; // 탄 출구의 절대 방향 = 새 진행 방향 → 다음 방을 이만큼 회전

        // 보스방은 직전 방의 회전이 어떻게 들어갔든 항상 정면(회전 0)으로 입장한다.
        // _heading은 방 빌드 회전(BuildProcRoomAsync)과 카메라 heading 스냅(MovePlayer→SetHeadingImmediate) 둘 다의 소스라,
        // 여기서 0(North=정면)으로 강제하면 방·카메라·이동 입력 기준이 모두 정면으로 일치한다.
        // (카메라가 heading 0으로 스냅되면 PlayerController가 불연속 스냅을 감지해 이동 기준도 정면으로 재정렬한다.)
        if (plan.kind == RoomPlanKind.Boss) _heading = (int)DoorEdge.North;

        await ScreenFade.CoverAsync(dir, KindColor(plan.kind), _coverDuration, _coverCurve, ct); // 짧게 덮어 텔레포트 가림

        var prevRoom = _current?.roomGO; // 새 방 준비까지 이전 방 유지 → 플레이어 발판 보존(추락 방지)

        // 리프프로그 앵커: 이전 방과 겹치지 않게 z를 번갈아 배치 (최대 2개 방만 잠깐 공존)
        var roomAnchor = _baseAnchor + new Vector3(0f, 0f, (_anchorToggle++ % 2) * 300f);

        // 미러: 이어하기는 저장값 강제, 신규는 방별 자식 시드로 결정적 산출
        int mirrorRoll = forcedMirror >= 0
            ? forcedMirror
            : (new System.Random(RunSequencer.Combine(_masterSeed ^ 0x1357, _sequencer.VisitCount)).Next(2));
        bool mirror = mirrorRoll == 1;
        // 방 빌드 RNG — 같은 (마스터 시드, visitCount)면 스포너 플랜까지 동일(이어하기 시 방 완전 재현)
        var roomRng = new System.Random(RunSequencer.Combine(_masterSeed, _sequencer.VisitCount));
        // 외형 RNG — 블록 배리언트·장식 회전용. roomRng와 스트림을 분리한 이유:
        // roomRng는 스포너 플랜 다음에 상점/재련소 구성 롤에도 쓰인다. 외형 뽑기를 같은 스트림에서
        // 소비하면 그 뒤 롤이 전부 밀려 기존 세이브의 상점 구성이 달라진다. 별도 시드로 격리한다.
        var visualRng = new System.Random(RunSequencer.Combine(_masterSeed ^ 0x5EED, _sequencer.VisitCount));
        var result  = await grb.BuildProcRoomAsync(plan.entry, roomAnchor, mirror, _heading, ct, roomRng, visualRng); // 블록 숨김 상태로 빌드(디졸브는 여기서)
        if (result == null)
        {
            await ScreenFade.RevealAsync(dir, _revealDuration, _revealCurve, ct);
            return;
        }

        _current = result;
        MovePlayer(result.entryPos);

        // 방 진입 연출(디졸브·리빌·대사·봉인) 동안 입력 잠금 — 봉인이 리빌/대사 뒤에 서므로,
        // 그 전에 플레이어가 출구로 달려나가면 뒤에서 석문이 떨어져 통로에 갇히는 문제를 막는다.
        // 봉인 완료 후 해제한다(SetPlayerInput(true)). 취소 경로들에서도 반드시 해제.
        SetPlayerInput(false);

        // [서약] 방 진입 통보
        GameRunBootstrapper.Instance?.Run?.CovenantHandler?.OnRoomEnter();
        // 이동 완료 후 이전 방 디스폰 — 자식 배치 파괴로 단발 Destroy 스파이크를 분산(플레이어는 이미 신규 방).
        if (prevRoom != null)
        {
            DestroyRoomStaggeredAsync(prevRoom, ct).Forget();
        }
        else
        {
            // 첫 절차 방 진입: 허브 대기방(BlockMap_zone_0_)이 원점에 잔존하면 보스룸 등과 겹친다 → 정리.
            var waitingRoom = grb.ConsumeWaitingRoomMap();
            if (waitingRoom != null) DestroyRoomStaggeredAsync(waitingRoom, ct).Forget();
        }

        // 디졸브 먼저 시작 → 약간 지연 → 화면 복귀(디졸브 진행 중 진입) → 완료 대기. 첫 방 포함 모든 절차 방에 적용.
        var dissolve = result.blocks != null
            ? new DissolveEntrance().PlayAsync(result.blocks, default, ct)
            : UniTask.CompletedTask;
        if (_revealDelay > 0f)
            await UniTask.Delay(TimeSpan.FromSeconds(_revealDelay), ignoreTimeScale: true, cancellationToken: ct);
        await ScreenFade.RevealAsync(dir, _revealDuration, _revealCurve, ct);
        await dissolve;

        // 전환 중 컨트롤러 파괴/취소(플레이 종료 등) 가드 — 이후 transform 접근 시 MissingReferenceException 방지
        if (this == null || ct.IsCancellationRequested) { SetPlayerInput(true); return; }

        // 방 입장 대사 이벤트 — 보스룸 등 특정 방 진입 시 챕터별/방문변형 대사 재생.
        await PlayRoomEntryDialogueAsync(plan.kind, ct);
        if (this == null || ct.IsCancellationRequested) { SetPlayerInput(true); return; }

        // 재저장(S2/S3)용 캐시 — 방 정보를 들고 있어야 임의 시점에 다시 저장할 수 있다.
        _lastPlan    = plan;
        _lastEdge    = fromEdge;
        _lastMirror  = mirrorRoll;
        _hasLastPlan = true;

        // 새 방 진입 → 방 스코프 상태 리셋. (이어하기 재생성 중에는 복원값을 덮지 않는다)
        if (!_resuming)
        {
            _currentRoomCleared = false;
            var s = GameRunBootstrapper.Instance?.Run;
            if (s != null) s.CrucibleRollIndex = 0;   // 방마다 시드가 다르므로 롤 카운터도 새로
        }

        // 이어하기 + '저장 당시 이미 클리어된 방'이면 몬스터를 다시 스폰하지 않고 출구만 연다.
        // (이게 없으면 보상은 챙긴 채 몹이 부활해 중복 파밍이 된다.)
        bool restoreCleared = _resuming && _resumedRoomCleared;
        _resumedRoomCleared = false;   // 1회성 — 다음 방으로 새어나가지 않게

        // 출구 문 봉인 + 입구 잠금. entry로 석문 등장 방식을 고른다.
        void SealRoom(SealDoorEntry entry)
        {
            CreateSealedGates(entry);
            if (result.hasEntrance) LockEntrance(result.entrance);
        }
        // 봉인 없이 출구만 세운다(잠그지 않음) — 비전투 방(상점/재련소)은 자유 통행.
        void OpenGatesOnly() { CreateGatesUnsealed(); }

        // 실제 전투가 있는 방인가 = 스포너로 RoomWaveController가 붙은 방.
        // 봉인의 목적은 '전투 중 도주 차단'이므로, 이 여부가 봉인 여부를 결정한다.
        //   - 전투방(Normal/Elite/PreBoss/Boss, Event 전투 챌린지): 봉인 → 클리어까지 가둠
        //   - 비전투방(Shop/Crucible, 비전투 Event): 봉인 안 함 → 즉시 봉인·해제하던 깜빡임 제거
        // (방 종류 무관하게 무조건 봉인 → 상점·보스에서도 강제 작동하던 문제 해소)
        bool hasCombat = result.roomGO != null
                         && result.roomGO.GetComponent<RoomWaveController>() != null;
        // 보스방은 procgen 봉인 석문(입구 잠금·출구 게이트)·부감 팬·낙하/슬램 연출을 일절 하지 않는다.
        // 경계(전투 중 이탈 차단)와 출구는 커스텀 아레나 프리팹이 소유하기 때문 — 아래 isBossRoom 분기 참조.
        bool isBossRoom  = plan.kind == RoomPlanKind.Boss;
        bool freshCombat = !restoreCleared && hasCombat && !isBossRoom;

        var introCam    = GameCameraController.Instance;
        var introPlayer = GameRunBootstrapper.Instance?.Run?.Player;
        if (isBossRoom)
        {
            // 보스방: procgen 봉인 석문을 입구·출구 어디에도 만들지 않는다(오브젝트·연출 모두 없음).
            //   · 전투 중 이탈 차단(경계)은 커스텀 아레나 프리팹(BossRoomController의 barrier/introWalls)이 소유.
            //   · 출구는 보스 처치 후 BossExitPath→ChapterGate가 담당 — RollExits가 Phase.Done이라
            //     RevealGates가 애초에 호출되지 않으므로 여기서 게이트를 세워도 영영 공개되지 않는다(순수 사장).
            // 입구 석문 슬램·출구 석문 낙하가 보스 등장 연출을 깨던 문제 제거.
            ClearGates();   // 이전 방 게이트 잔재만 정리 — 새 게이트는 만들지 않는다
        }
        else if (freshCombat && introCam != null)
        {
            // 신규 전투방: 카메라가 방을 넓게 보여주는 동안 문이 잠기고, 그 후에 몬스터가 나온다.
            // (몬스터 Activate는 이 await 뒤 웨이브 분기에서 실행 → 연출 종료 전까지 스폰 안 됨)
            //
            // 게이트(통과 차단 콜라이더)는 연출 <b>전</b>에 세운다. 예전엔 onWide에서야 만들어져
            // 부감 팬이 올라가는 ~0.9초 동안 출구가 뻥 뚫려 있었고, 그 사이 통로로 나간 플레이어는
            // 뒤에서 석문이 떨어지며 통로에 갇혔다. 낙하 <b>연출</b>만 와이드샷 시점으로 미룬다.
            SealRoom(SealDoorEntry.Parked);
            if (result.hasCeiling)
            {
                // 천장 있는 실내 방(성채 등): 상공 부감 팬은 천장만 비추므로 생략한다.
                // 카메라는 게임플레이 시점 유지, 문 봉인·몬스터 스폰 페이싱은 동일(연출 시간만큼 대기 후 석문 낙하).
                await UniTask.Delay(TimeSpan.FromSeconds(0.9f), ignoreTimeScale: true, cancellationToken: ct);
                PlaySealDoorDrops();
                await UniTask.Delay(TimeSpan.FromSeconds(1.0f), ignoreTimeScale: true, cancellationToken: ct);
            }
            else
            {
                await introCam.PlayRoomEntryIntroAsync(
                    result.roomGO.transform.position,
                    introPlayer != null ? introPlayer.transform : null,
                    riseDuration: 0.9f, holdDuration: 1.0f, returnDuration: 0.7f,
                    wideHeight: 48f, wideBack: 20f,
                    onWide: PlaySealDoorDrops, ct);
            }
            if (this == null || ct.IsCancellationRequested) { SetPlayerInput(true); return; }
        }
        else if (hasCombat)
        {
            // 이어하기 클리어 전(재생성) 전투방 — 연출 없이 닫힌 상태로 봉인 (보스방은 위 분기에서 처리)
            SealRoom(SealDoorEntry.Closed);
        }
        else
        {
            OpenGatesOnly();     // 비전투방 — 문은 세우되 봉인하지 않는다
        }

        // 봉인/게이트가 서고 나서야 조작 복원 — 리빌·대사 창 동안 출구로 못 나가게 잠갔던 것을 푼다.
        SetPlayerInput(true);

        if (restoreCleared)
        {
            if (result.roomGO != null && result.roomGO.TryGetComponent<RoomWaveController>(out var clearedWave))
                clearedWave.enabled = false;   // Activate() 미호출 + 비활성 → 몬스터 스폰 없음
            _currentWave = null;
            // 정상 클리어 경로를 그대로 탄다: 저장은 _resuming 가드로 무시되고,
            // RollExits는 롤 '전' 상태로 저장돼 있었으므로 같은 시드로 동일 출구가 재현된다.
            HandleRoomCleared();
        }
        // 디졸브 후 클리어 알림 구독 → 출구 게이트 공개. 전투 없는 방(스포너 0)은 즉시 공개.
        else if (result.roomGO != null && result.roomGO.TryGetComponent<RoomWaveController>(out _currentWave))
        {
            _currentWave.OnRoomCleared += HandleRoomCleared;
            _currentWave.Activate();
        }
        else
        {
            _currentWave = null;
            // 비전투 방: 상호작용 챌린지(도박 등)가 있으면 해결(OnResolved)까지 클리어 지연, 없으면 즉시 공개.
            var challenge = result.roomGO != null ? result.roomGO.GetComponent<IInteractionChallenge>() : null;
            if (challenge != null)
            {
                System.Action onResolved = null;
                onResolved = () => { challenge.OnResolved -= onResolved; HandleRoomCleared(); };
                challenge.OnResolved += onResolved;
            }
            else
            {
                HandleRoomCleared();
            }
        }

        // 방 경계 자동저장 (suspend-on-save). 이어하기 재생성 중에는 생략(동일 상태 재저장 방지).
        if (!_resuming)
            SaveRunState(plan, fromEdge, mirrorRoll);
    }

    /// <summary>현재 진행 상태를 즉시 저장한다(S2 클리어 직후 / S3 행동·이벤트 확정 시).
    /// 방 정보는 마지막 방 빌드 시점 캐시를 재사용하므로 방 경계가 아니어도 안전하다.</summary>
    public void SaveNow(string reason)
    {
        if (_resuming || !_hasLastPlan) return;   // 복원 중 재저장 방지
        SaveRunState(_lastPlan, _lastEdge, _lastMirror);
        Debug.Log($"[RunFlow] 자동저장 — {reason}");
    }

    /// <summary>방 입장 대사 이벤트. 현재는 보스룸만 — 챕터별 BossRoom_Ch{N}_Enter를 방문변형(첫/반복)으로 재생.</summary>
    private async UniTask PlayRoomEntryDialogueAsync(RoomPlanKind kind, CancellationToken ct)
    {
        if (kind != RoomPlanKind.Boss) return;

        var dlg = Managers.DialogueData;
        if (dlg == null) return;
        if (!dlg.IsInitialized) await dlg.InitializeAsync();

        var chapter = GameRunBootstrapper.Instance?.Run?.CurrentChapter ?? ChapterId.Chapter1;
        var lines = dlg.GetVisitLines($"BossRoom_Ch{(int)chapter}_Enter");
        if (lines == null || lines.Length == 0) return;

        // 선택/편집 UI가 열려있으면 닫힐 때까지 대기 후 대사.
        await Managers.UI.WaitUntilNoBlockingPopupAsync();

        var popup = await Managers.UI.ShowPopupUIAndGetAsync<UI_DialoguePopup>();
        if (popup == null) return;
        try { await popup.ShowAsync(lines); }
        catch (System.OperationCanceledException) { }
    }

    /// <summary>현재 방 진입 시점의 런 전체 상태를 로컬에 직렬화한다. 전투 도중이 아닌 방 경계 1회.</summary>
    private void SaveRunState(DoorPlan plan, DoorEdge fromEdge, int mirror)
    {
        var pm      = RunProgressManager.Instance;
        var session = GameRunBootstrapper.Instance?.Run;
        if (pm == null || session == null || !session.IsRunning || _sequencer == null) return;

        var cooldowns = new List<CooldownKV>();
        foreach (var kv in _sequencer.Cooldowns)
            cooldowns.Add(new CooldownKV { key = kv.Key, turns = kv.Value });

        var meta = new RunMetaSnapshot
        {
            masterSeed         = _masterSeed,
            visitCount         = _sequencer.VisitCount,
            seqPhase           = _sequencer.PhaseInt,
            shopUsed           = _sequencer.ShopUsed,
            eventUsed          = _sequencer.EventUsed,
            heading            = (int)fromEdge,
            anchorToggle       = _anchorToggle,
            currentRoomPoolKey = plan.entry?.pool_key,
            currentRoomKind    = (int)plan.kind,
            currentRoomMirror  = mirror,
            currentRoomCleared = _currentRoomCleared,
            crucibleRollIndex  = session.CrucibleRollIndex,
            cooldowns          = cooldowns,
        };

        pm.SaveRunLocal(session, meta);
    }

    private void MovePlayer(Vector3 pos)
    {
        // 바인딩된 런 우선(플레이어를 BindPlayer한 세션), 없으면 AppBootstrapper 폴백
        var player = GameRunBootstrapper.Instance?.Run?.Player
                  ?? AppBootstrapper.Instance?.CurrentRun?.Player;
        if (player == null)
        {
            Debug.LogWarning("[RunFlow] MovePlayer: 플레이어 참조 없음 — 이동 불가");
            return;
        }

        // Rigidbody 텔레포트: transform.position만으론 물리 위치/보간이 덮어쓰므로 rb.position 동기화 + 속도 0
        // (FallRecoveryController와 동일 패턴)
        player.transform.position = pos;
        if (player.TryGetComponent<Rigidbody>(out var rb))
        {
            rb.position        = pos;
            rb.linearVelocity  = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        // 카메라를 새 방의 진행 방향으로 정렬한다.
        //
        // 방은 _heading에 맞춰 90° 단위로 회전해 지어지지만(BuildProcRoomAsync: 입구=뒤, 직진=헤딩),
        // FreeLook의 수평각(m_XAxis)은 이전 값을 그대로 유지한다. 예전엔 SnapToTarget()만 불렀는데
        // 그건 위치만 스냅하고 각도는 건드리지 않아, 출구 방향(N/E/S/W)이 바뀔 때마다
        // "화면 기준 앞쪽"이 방마다 달라졌다.
        // SetHeadingImmediate는 내부에서 PreviousStateIsValid도 꺼주므로 SnapToTarget을 대체한다.
        // 전환 커버로 화면이 가려진 동안 호출되므로 회전이 눈에 띄지 않는다.
        GameCameraController.Instance?.SetHeadingImmediate(_heading * 90f);
        Debug.Log($"[RunFlow] 플레이어 이동 → {pos} (heading={(DoorEdge)_heading})");
    }

    /// <summary>방 진입 연출 동안 플레이어 조작을 잠그거나(false) 푼다(true).
    /// 봉인이 리빌·대사 뒤에 서므로, 그 창에서 출구로 달려나가 통로에 갇히는 것을 막는 컷신 채널.
    /// MovePlayer와 동일하게 바인딩 런 우선 → AppBootstrapper 폴백으로 플레이어를 찾는다.</summary>
    private void SetPlayerInput(bool enabled)
    {
        var player = GameRunBootstrapper.Instance?.Run?.Player
                  ?? AppBootstrapper.Instance?.CurrentRun?.Player;
        player?.SetInputEnabled(enabled);
    }

    /// <summary>이전 방을 자식 단위로 몇 프레임에 나눠 파괴해 단발 대량 Destroy 스파이크를 분산한다.
    /// 플레이어는 이미 신규 방으로 이동·이전 방은 화면 밖(리프프로그 앵커)이라 안전하다.</summary>
    private async UniTaskVoid DestroyRoomStaggeredAsync(GameObject room, CancellationToken ct)
    {
        if (room == null) return;

        // 이전 방의 드랍(골드 코인·클리어 보상·버린 아이템)은 부모가 없어 방 파괴로 안 지워진다.
        // → 다음 방에 흔적으로 떠다니지 않도록 여기서 일괄 정리.
        RoomScopedDrop.ClearAll();
        // 장판(독/빛 등)도 씬 루트 스폰 + 긴 수명이라 방 파괴로 안 지워지고 다음 방 바닥에 남는다.
        GroundFieldBase.DespawnAll();

        var children = new List<Transform>(room.transform.childCount);
        foreach (Transform c in room.transform) children.Add(c);

        const int PerFrame = 40;
        for (int i = 0; i < children.Count; i++)
        {
            if (children[i] != null) Destroy(children[i].gameObject);
            if ((i + 1) % PerFrame == 0)
            {
                try { await UniTask.Yield(ct); }
                catch (OperationCanceledException) { break; } // 취소 시 남은 자식은 아래 Destroy(room)으로 일괄 정리
            }
        }

        if (room != null) Destroy(room); // 부모 파괴로 남은 자식까지 정리
    }

    private void HandleRoomCleared()
    {
        if (_currentWave != null) _currentWave.OnRoomCleared -= HandleRoomCleared;

        // ── S2: 클리어 직후 자동저장 (전투 보상·드랍 획득분 보존) ──
        // ⚠️ 반드시 RollExits() '앞'에서 저장한다. 롤 뒤에 저장하면 시퀀서(shopUsed/eventUsed/쿨다운)가
        //    이미 진행된 상태로 기록되고, 복원 시 RollExits가 한 번 더 돌아 '이중 반영'된다.
        //    롤 전 상태로 저장해두면 복원 때 같은 시드로 다시 굴려 동일한 출구가 재현된다.
        _currentRoomCleared = true;
        SaveNow("room-cleared");   // 복원 중(_resuming)이면 내부에서 무시

        var exits = _sequencer.RollExits();
        VerifyAgainstPlan(_sequencer.VisitCount, exits); // 라이브 종류가 일정표와 일치하는지(드리프트) 검증
        if (exits == null || exits.Count == 0)
        {
            Debug.Log("[RunFlow] 출구 없음 — 런 종료(보스 처치 등)");
            return;
        }
        RevealGates(exits);
    }

    /// <summary>산출된 일정표를 한 줄씩 콘솔에 덤프한다 — 레벨디자인 가시성/마일스톤 검증용. 런 시작 1회.</summary>
    private void DumpRunPlan(int seed)
    {
        if (_runPlan == null) return;
        var sb = new System.Text.StringBuilder();
        sb.Append($"[RunPlan] seed={seed} chambers={_runPlan.Chambers.Count}");
        foreach (var ch in _runPlan.Chambers)
        {
            sb.Append($"\n  #{ch.visitIndex} → ");
            for (int i = 0; i < ch.exitKinds.Length; i++)
            {
                if (i > 0) sb.Append(" / ");
                sb.Append(KindKor(ch.exitKinds[i]));
            }
        }
        Debug.Log(sb.ToString());
    }

    /// <summary>라이브 RollExits 종류가 사전 일정표와 일치하는지 검증(드리프트 감지). 불일치 시 경고만(동작 영향 없음).</summary>
    private void VerifyAgainstPlan(int visitIndex, List<DoorPlan> liveExits)
    {
        if (_runPlan == null || liveExits == null) return;
        foreach (var ch in _runPlan.Chambers)
        {
            if (ch.visitIndex != visitIndex) continue;
            bool match = ch.exitKinds.Length == liveExits.Count;
            for (int i = 0; match && i < liveExits.Count; i++)
                if (ch.exitKinds[i] != liveExits[i].kind) match = false;
            if (!match)
                Debug.LogWarning($"[RunPlan] 드리프트 감지 visit={visitIndex} — 일정표와 라이브 출구 종류 불일치.");
            return;
        }
    }

    /// <summary>봉인 석문이 등장하는 방식.</summary>
    private enum SealDoorEntry
    {
        /// <summary>즉시 낙하 연출.</summary>
        Drop,
        /// <summary>개구부 위에 대기 — <see cref="PlaySealDoorDrops"/>가 일제히 낙하시킨다.
        /// 통과 차단 콜라이더는 이미 서 있으므로 대기 중에도 방 밖으로 나갈 수 없다.</summary>
        Parked,
        /// <summary>연출 없이 닫힌 상태로 시작(보스방 — 낙하 연출이 보스 등장을 깬다).</summary>
        Closed,
    }

    /// <summary>빌드 시 모든 출구 슬롯에 봉인(막힌) 게이트를 미리 만든다 — 전투 중엔 통과 불가.</summary>
    private void CreateSealedGates(SealDoorEntry entry = SealDoorEntry.Drop)
    {
        ClearGates();
        if (_current?.exits == null) return;
        foreach (var slot in _current.exits)
            _gates.Add(CreateSealedGate(slot, sealDoor: true, entry));
    }

    /// <summary>대기(Parked) 중인 석문을 일제히 낙하시킨다. 카메라가 방을 넓게 잡은 순간 호출.</summary>
    private void PlaySealDoorDrops()
    {
        bool primaryAssigned = false;
        for (int i = 0; i < _gates.Count; i++)
        {
            var door = _gates[i]?.door;
            if (door == null) continue;
            SealDoorDropAsync(_gates[i], primary: !primaryAssigned).Forget();
            primaryAssigned = true;   // 흔들림·사운드는 대표 문 1회(중복 방지)
        }
    }

    /// <summary>
    /// 비전투 방(상점/재련소 등)용 — 출구 게이트를 만들되 <b>석문 낙하 봉인을 하지 않는다.</b>
    /// 곧바로 HandleRoomCleared→RevealGates가 armed로 전환하므로, 봉인 연출 없이 자유 통행이 된다.
    /// (모든 방을 무조건 봉인해 상점·보스에서도 석문이 떨어지고, 같은 프레임에 해제돼 깜빡이던 문제 해소)
    /// </summary>
    private void CreateGatesUnsealed()
    {
        ClearGates();
        if (_current?.exits == null) return;
        foreach (var slot in _current.exits)
            _gates.Add(CreateSealedGate(slot, sealDoor: false, SealDoorEntry.Drop));
    }

    /// <summary>클리어 시 롤된 출구를 슬롯에 매칭해 색 전환+글로우로 공개하고 통과 가능하게 한다.</summary>
    private void RevealGates(List<DoorPlan> exits)
    {
        int n = Mathf.Min(_gates.Count, exits.Count);
        var marks = new List<(Transform target, string label, Color color)>(n);
        for (int i = 0; i < n; i++)
        {
            if (_gates[i] == null) continue;
            RevealGateAsync(_gates[i], exits[i]).Forget();

            // 카메라가 정면 고정이라 옆쪽 출구는 화면 밖으로 나간다 —
            // 나침반 HUD로 "어느 방향에 어떤 방"인지 항상 보이게 한다.
            var tr = _gates[i].gate != null ? _gates[i].gate.transform : null;
            if (tr != null)
                marks.Add((tr,
                           KindGlyph(exits[i].kind) + " " + KindKor(exits[i].kind),
                           KindBrightColor(exits[i].kind)));
        }
        if (marks.Count > 0) ExitCompassHud.Create().SetExits(marks);
        // 매칭 안 된 여분 슬롯은 봉인 상태 유지(목적지 없음)
    }

    /// <param name="sealDoor">true면 봉인 석문을 세워 가둔다(전투방). false면 석문 없이 통과 대기(비전투방).</param>
    /// <param name="entry">석문 등장 방식(즉시 낙하 / 대기 / 연출 없이 닫힘).</param>
    private GateView CreateSealedGate(ProcExitSlot slot, bool sealDoor, SealDoorEntry entry)
    {
        var go = CreateGatePanel("ProcGate_Sealed", slot, SealedColor, out var marker, out var blocker, withPortal: true);

        // 트리거 — 공개 후 통과 감지용 (봉인 중에는 armed=false)
        var trig = go.AddComponent<BoxCollider>();
        trig.isTrigger = true;
        trig.size = new Vector3(MarkerW(slot), MarkerH(slot), 3f);

        var gate = go.AddComponent<ProcRoomGate>();
        gate.InitializeSealed(slot.edge, OnGateChosen);

        var portal = go.transform.Find("GatePortalVfx");

        // 출구 봉인 석문 — 전투 시작 시 웅장하게 낙하해 봉인(플레이어가 보는 앞쪽). 클리어 시 위로 열리며 포탈 공개.
        // 비전투방(sealDoor=false)은 석문을 만들지 않는다 → 봉인 낙하/즉시해제 깜빡임 없음.
        float oh   = MarkerH(slot);
        var   door = sealDoor ? SpawnSealDoor(go.transform, MarkerW(slot), oh) : null;
        if (door != null && marker != null) marker.enabled = false; // 석문이 시각 담당(색 패널 숨김)

        var view = new GateView
        {
            gate = gate, marker = marker, blocker = blocker,
            portal = portal != null ? portal.gameObject : null,
            door = door, openH = oh,
            sealedLocalPos = door != null ? door.localPosition : Vector3.zero,
        };

        // 통과 차단 콜라이더(blocker)는 이 시점에 이미 서 있다 — 석문이 언제 내려오든 방 밖으로 못 나간다.
        if (door != null && entry == SealDoorEntry.Parked)
            door.localPosition = view.sealedLocalPos + Vector3.up * Mathf.Max(1f, oh);   // 낙하 대기 위치
        else if (door != null && entry == SealDoorEntry.Drop)
            SealDoorDropAsync(view, primary: _gates.Count == 0).Forget(); // 첫 문에서만 흔들림·사운드
        // Closed: 생성 위치가 곧 봉인 위치 — 아무것도 하지 않는다.

        return view;
    }

    /// <summary>봉인 석문이 높은 곳에서 강하게 가속 낙하해 "쿵" 봉인 — 웅장한 동적 연출.
    /// primary=대표 문(카메라 흔들림+사운드 1회, 중복 방지). 착지 시 먼지 VFX.</summary>
    private async UniTaskVoid SealDoorDropAsync(GateView view, bool primary)
    {
        var ct = _cts != null ? _cts.Token : this.GetCancellationTokenOnDestroy();
        if (view?.door == null) return;

        var     door      = view.door;
        float   openH     = view.openH;
        Vector3 sealedPos = view.sealedLocalPos;
        Vector3 upPos = sealedPos + Vector3.up * Mathf.Max(1f, openH); // 개구부 바로 위에서 시작(불필요한 장거리 낙하 제거)
        float   dur       = 0.6f;
        door.localPosition = upPos;
        try
        {
            // 예비 동작 — 잠깐 떠 있다 떨어짐(무게감/주목)
            await UniTask.Delay(TimeSpan.FromSeconds(0.08), ignoreTimeScale: true, cancellationToken: ct);

            float t = 0f;
            while (t < dur)
            {
                if (door == null || view.opening) return;   // 클리어로 이미 열리기 시작했으면 낙하는 물러난다
                ct.ThrowIfCancellationRequested();
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / dur);
                // SmoothStep — 끝에서 감속 정착(프레임당 이동폭이 커지지 않아 매끄러움). 임팩트는 착지 흔들림·먼지가 담당.
                door.localPosition = Vector3.Lerp(upPos, sealedPos, Mathf.SmoothStep(0f, 1f, k));
                await UniTask.Yield();
            }
            if (view.opening) return;
            door.localPosition = sealedPos;
        }
        catch (OperationCanceledException) { return; }

        PlayDoorImpact(door, openH, primary); // 먼지 + (대표문) 흔들림·사운드
    }

    /// <summary>석문 착지 임팩트 — 먼지 VFX(모든 문) + 카메라 흔들림·봉인 사운드(대표 문 1회). 리소스 없으면 해당 요소 생략.</summary>
    private void PlayDoorImpact(Transform door, float openH, bool primary)
    {
        if (door == null) return;
        var grb = GameRunBootstrapper.Instance;

        // 먼지 — 개구부 바닥 중앙에 스폰. (게이트 원점 = 개구부 중앙, 지면은 openH/2 아래)
        // 문마다 낸다. 예전엔 primary 체크가 위에 있어 대표 문에서만 먼지가 나서
        // 나머지 문은 소리 없이 툭 내려앉는 것처럼 보였다(문 하나만 제대로 닫히는 느낌).
        var dust = grb != null ? grb.GateSealDustVfx : null;
        if (dust != null)
        {
            Vector3 ground = door.parent != null
                ? door.parent.position + Vector3.down * (openH * 0.5f)
                : door.position;
            var fx = Instantiate(dust, ground, Quaternion.identity);
            Destroy(fx, 3f);
        }

        if (!primary) return;   // 흔들림·사운드만 대표 문 1회 — 중복 재생 방지

        HitFeelService.CameraShake(0.30f, 0.32f);              // 웅장한 착지 임팩트
        if (grb != null && grb.GateSealSfx != null)
            Managers.Sound?.Play(grb.GateSealSfx);              // 봉인 사운드(사용자 클립)
    }

    /// <summary>봉인 석문이 위로 올라가며 열림(클리어 공개). 감속 정착.</summary>
    /// <param name="view">봉인 위치·상태를 들고 있는 게이트. 현재 위치에서 열림 위치로 올린다 —
    /// 대기(Parked)·낙하 중 어느 상태에서 불려도 목표가 어긋나지 않는다
    /// (예전엔 호출 시점의 localPosition을 '봉인 위치'로 오인해 낙하 도중 열리면 엉뚱한 높이로 갔다).</param>
    private async UniTaskVoid SealDoorRaiseAsync(GateView view)
    {
        var ct = _cts != null ? _cts.Token : this.GetCancellationTokenOnDestroy();
        if (view?.door == null) return;

        var     door      = view.door;
        Vector3 sealedPos = door.localPosition;              // 시작 = 지금 있는 자리(중간에서 이어받기)
        Vector3 upPos     = view.sealedLocalPos + Vector3.up * Mathf.Max(1f, view.openH);
        float   dur       = 0.5f;
        try
        {
            float t = 0f;
            while (t < dur)
            {
                if (door == null) return;
                ct.ThrowIfCancellationRequested();
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / dur);
                float e = 1f - (1f - k) * (1f - k); // ease-out
                door.localPosition = Vector3.Lerp(sealedPos, upPos, e);
                await UniTask.Yield();
            }
            door.localPosition = upPos;
        }
        catch (OperationCanceledException) { return; }
    }

    /// <summary>들어온 입구를 잠금 패널로 막는다 (되돌아가기 차단). 가벼운 잠금 페이드인 연출.</summary>
    // SM_ArchWall_01a 석문 메시 로컬 바운드(m) — 개구부에 맞춰 스케일 계산용.
    private const float SealDoorMeshW = 7f;
    private const float SealDoorMeshH = 11.5f;

    private void LockEntrance(ProcExitSlot slot)
    {
        if (_entranceLock != null) Destroy(_entranceLock);
        _entranceLock = CreateGatePanel("ProcEntranceLock", slot, LockedColor, out var lockMarker, out _, withPortal: false);

        // 갇힘 방지: 봉인 연출(낙하) 중에도 통과를 확실히 막는 풀사이즈 정적 콜라이더.
        // 시각 문은 위에서 내려오지만 이 콜라이더는 처음부터 완전한 크기로 입구를 막는다.
        var hardBlock = new GameObject("EntranceHardBlocker");
        hardBlock.transform.SetParent(_entranceLock.transform, false);
        var bc = hardBlock.AddComponent<BoxCollider>();
        bc.size = new Vector3(MarkerW(slot) + 1f, MarkerH(slot) * 2f + 2f, 2.5f); // 폭·높이 넉넉히 — 뛰어넘기/틈새 통과 차단

        // 석문 오브젝트 리소스(Gothic 석재)를 시각으로 사용 — 큐브 패널 렌더러는 숨겨 콜라이더만 남긴다.
        float oh = MarkerH(slot);
        Transform doorTr = SpawnSealDoor(_entranceLock.transform, MarkerW(slot), oh);
        if (doorTr != null && lockMarker != null) lockMarker.enabled = false;

        LockEntranceAsync(_entranceLock, doorTr, oh).Forget();
    }

    /// <summary>봉인 석문(Gothic 석재)을 개구부 크기에 맞춰 닫힘 위치에 인스턴스화. 프리팹 없으면 null.</summary>
    private Transform SpawnSealDoor(Transform parent, float ow, float oh)
    {
        var doorPrefab = GameRunBootstrapper.Instance?.GateSealDoorPrefab;
        if (doorPrefab == null) return null;
        var door = Instantiate(doorPrefab, parent);
        door.name = "SealDoor";
        door.transform.localRotation = Quaternion.identity;
        door.transform.localScale    = new Vector3(ow / SealDoorMeshW, oh / SealDoorMeshH, 1f);
        // 코너 피벗(0..W,0..H) → 개구부 중앙 정렬(게이트 원점 = 개구부 중앙)
        door.transform.localPosition = new Vector3(-ow * 0.5f, -oh * 0.5f, -0.25f);
        SetLayerRecursive(door, 8); // Wall 레이어
        // 문 메시의 콜라이더(MeshCollider 등)는 불필요 — 게이트 blocker가 통과 차단 담당. 제거로 인스턴스화·물리 비용 절감.
        foreach (var col in door.GetComponentsInChildren<Collider>()) Destroy(col);
        return door.transform;
    }

    private static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform c in go.transform) SetLayerRecursive(c.gameObject, layer);
    }

    /// <summary>게이트/잠금 패널 공통 생성 — 개구부 크기로 채운 솔리드 큐브. blocker는 통과 차단용 콜라이더.</summary>
    private GameObject CreateGatePanel(string name, ProcExitSlot slot, Color color, out Renderer marker, out Collider blocker, bool withPortal)
    {
        float openW = MarkerW(slot);
        float openH = MarkerH(slot);

        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.position = slot.worldPos + new Vector3(0f, openH * 0.5f, 0f);
        if (slot.edge == DoorEdge.East || slot.edge == DoorEdge.West)
            go.transform.localRotation = Quaternion.Euler(0f, 90f, 0f); // 통로를 가로지르게

        // 얇은 색 프레임(=통과 차단 콜라이더 겸 봉인/공개 색 표시). 두께를 얇게 해 포탈이 앞뒤로 보이게.
        var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
        panel.name = "Marker";
        panel.transform.SetParent(go.transform, false);
        panel.transform.localScale = new Vector3(openW, openH, 0.12f);

        panel.TryGetComponent(out blocker);
        panel.TryGetComponent(out marker);
        // 빌드에서 프리미티브 기본 머티리얼이 핑크로 스트립되는 문제 → URP/Lit 명시 할당.
        RuntimePrimitiveMaterial.Apply(marker, color);
        // 시각은 석문이 담당 — 색 패널은 통과 차단 콜라이더 역할만, 렌더러는 항상 숨김(레거시 색/글로우 제거).
        marker.enabled = false;

        // 게이트 포탈 VFX — 봉인 시 비활성으로 심고, 공개 때 활성화(열림 연출). 실패 시 색 패널만.
        if (withPortal && _gatePortalPrefab != null)
        {
            var portal = Instantiate(_gatePortalPrefab, go.transform);
            portal.name = "GatePortalVfx";
            portal.transform.localPosition = Vector3.zero;
            portal.transform.localRotation = Quaternion.identity;
            float s = Mathf.Min(openW, openH) * 0.5f; // 개구부 크기에 맞춤(원본 루트 스케일 (1,10,1) 무시하고 균일화)
            portal.transform.localScale = new Vector3(s, s, s);
            portal.SetActive(false);
        }

        return go;
    }

    private static float MarkerW(ProcExitSlot slot) => slot.openingWidth  > 0.01f ? slot.openingWidth  : 4f;
    private static float MarkerH(ProcExitSlot slot) => slot.openingHeight > 0.01f ? slot.openingHeight : 3f;

    /// <summary>문 엣지 → 전환 와이프 스크린 방향. 직진=위 / 우턴=오른쪽 / 좌턴=왼쪽.</summary>
    private static Vector2 WipeDir(DoorEdge edge) => edge switch
    {
        DoorEdge.North => Vector2.up,
        DoorEdge.East  => Vector2.right,
        DoorEdge.West  => Vector2.left,
        DoorEdge.South => Vector2.down,
        _              => Vector2.up,
    };

    /// <summary>방 종류 → 전환 커버 색 (전투/정예/상점/보스 단서).</summary>
    private static Color KindColor(RoomPlanKind kind) => kind switch
    {
        RoomPlanKind.Boss    => new Color(0.55f, 0.08f, 0.08f),
        RoomPlanKind.PreBoss => new Color(0.45f, 0.10f, 0.14f),
        RoomPlanKind.Elite   => new Color(0.32f, 0.12f, 0.52f),
        RoomPlanKind.Shop    => new Color(0.08f, 0.32f, 0.12f),
        RoomPlanKind.Event   => new Color(0.30f, 0.20f, 0.05f),
        RoomPlanKind.Crucible => new Color(0.52f, 0.25f, 0.08f), // 구리톤(대장간)
        RoomPlanKind.Refinery => new Color(0.10f, 0.30f, 0.45f), // 청록톤(정제소)
        _                    => new Color(0.05f, 0.06f, 0.10f), // Normal
    };

    private void ClearGates()
    {
        ExitCompassHud.Instance?.Clear();   // 방 전환 — 이전 방 출구 안내 제거
        for (int i = 0; i < _gates.Count; i++)
            if (_gates[i]?.gate != null) Destroy(_gates[i].gate.gameObject);
        _gates.Clear();
        if (_entranceLock != null) { Destroy(_entranceLock); _entranceLock = null; }
    }

    /// <summary>봉인 → 공개: 색 전환 + 글로우 펄스, 차단 콜라이더 해제, 통과 가능(arm).</summary>
    private async UniTaskVoid RevealGateAsync(GateView view, DoorPlan plan)
    {
        var ct  = _cts != null ? _cts.Token : this.GetCancellationTokenOnDestroy();
        var rend = view.marker;
        Color to = KindColor(plan.kind);

        // 봉인 석문이 위로 올라가며 열림 → 그 뒤 포탈이 드러난다. + 열림 사운드.
        if (view.door != null)
        {
            view.opening = true;   // 진행 중인 낙하가 있으면 여기서 손을 뗀다
            SealDoorRaiseAsync(view).Forget();
            Managers.Sound?.PlayEvent(SoundEvent.DoorOpen);
        }

        // 포탈 VFX 활성화 — "문이 열린다" 연출. 포탈이 있으면 레거시 색 슬래브(불투명 큐브)를 숨겨 포탈로 대체.
        if (view.portal != null)
        {
            view.portal.SetActive(true);
            if (rend != null) rend.enabled = false; // 보라 사각형 제거 → 포탈이 개구부를 채움
        }

        // 다음 방 정보 라벨 — 봉인 중엔 없다가 공개와 함께 페이드인
        TextMeshProUGUI label = (rend != null) ? CreateGateLabel(rend.transform.parent, plan.kind, rend.transform.localScale.y) : null;

        if (rend != null)
        {
            var mat = rend.material;
            mat.EnableKeyword("_EMISSION");
            Color from = mat.color;
            float dur = Mathf.Max(0.01f, _gateRevealDuration);
            try
            {
                float t = 0f;
                while (t < dur)
                {
                    if (rend == null) break;
                    ct.ThrowIfCancellationRequested();
                    t += Time.deltaTime;
                    float k    = Mathf.Clamp01(t / dur);
                    float glow = Mathf.Sin(k * Mathf.PI); // 0→1→0 펄스
                    mat.color = Color.Lerp(from, to, k);
                    mat.SetColor("_EmissionColor", to * (glow * _gateGlowIntensity));
                    if (label != null) { var lc = label.color; lc.a = k; label.color = lc; }
                    await UniTask.Yield();
                }
            }
            catch (OperationCanceledException) { return; }
            if (rend != null)
            {
                rend.material.color = to;
                rend.material.SetColor("_EmissionColor", to * 0.4f); // 잔광
            }
            if (label != null) { var lc = label.color; lc.a = 1f; label.color = lc; }
        }

        if (view.blocker != null) view.blocker.enabled = false; // 통과 차단 해제
        view.gate?.Reveal(plan);                                 // plan 바인딩 + arm
    }

    /// <summary>게이트에 다음 방 종류 라벨(월드 스페이스)을 만든다. 알파 0으로 시작 — 공개 연출과 함께 페이드인.</summary>
    private TextMeshProUGUI CreateGateLabel(Transform gateTr, RoomPlanKind kind, float openH)
    {
        var go = new GameObject("GateLabel");
        go.transform.SetParent(gateTr, false);
        // 게이트 원점 = 개구부 중앙(지면에서 openH/2 위). 라벨을 지면 기준 ~2.2m로 내려 화면에 보이게 한다.
        go.transform.localPosition = new Vector3(0f, -openH * 0.5f + 2.2f, 0f);

        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        if (Camera.main != null) go.transform.rotation = Camera.main.transform.rotation; // 카메라 향해 빌보드(1회)

        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta  = new Vector2(360f, 110f);
        rt.localScale = Vector3.one * 0.02f;

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text      = KindGlyph(kind) + " " + KindKor(kind);
        tmp.fontSize  = 62f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        // 방 종류별 밝은 색 + 검은 테두리로 어느 배경에서도 읽히게. 알파 0 시작(공개 시 페이드인).
        var c = KindBrightColor(kind); c.a = 0f;
        tmp.color            = c;
        tmp.outlineColor     = new Color(0f, 0f, 0f, 1f);
        tmp.outlineWidth     = 0.22f;
        return tmp;
    }

    /// <summary>방 종류 → 라벨용 밝은 색(가독성). KindColor는 패널용 어두운 톤이라 텍스트엔 부적합.</summary>
    private static Color KindBrightColor(RoomPlanKind kind) => kind switch
    {
        RoomPlanKind.Boss     => new Color(1f, 0.30f, 0.30f),
        RoomPlanKind.PreBoss  => new Color(1f, 0.45f, 0.45f),
        RoomPlanKind.Elite    => new Color(0.72f, 0.45f, 1f),
        RoomPlanKind.Shop     => new Color(0.40f, 1f, 0.55f),
        RoomPlanKind.Event    => new Color(1f, 0.85f, 0.35f),
        RoomPlanKind.Crucible => new Color(1f, 0.60f, 0.25f),
        RoomPlanKind.Refinery => new Color(0.45f, 0.85f, 1f),
        _                     => new Color(0.85f, 0.90f, 1f), // 전투(Normal)
    };

    /// <summary>방 종류 → 라벨 앞 기호(폰트에 있는 안전한 글리프만).</summary>
    private static string KindGlyph(RoomPlanKind kind) => kind switch
    {
        RoomPlanKind.Boss     => "▲",
        RoomPlanKind.PreBoss  => "▲",
        RoomPlanKind.Elite    => "◆",
        RoomPlanKind.Shop     => "■",
        RoomPlanKind.Event    => "◇",
        RoomPlanKind.Crucible => "●",
        RoomPlanKind.Refinery => "◈",
        _                     => "▪",
    };

    /// <summary>방 종류 → 표시용 한글 라벨.</summary>
    private static string KindKor(RoomPlanKind kind) => kind switch
    {
        RoomPlanKind.Boss    => "보스",
        RoomPlanKind.PreBoss => "보스 전",
        RoomPlanKind.Elite   => "정예",
        RoomPlanKind.Shop    => "상점",
        RoomPlanKind.Event   => "이벤트",
        RoomPlanKind.Crucible => "재련소",
        RoomPlanKind.Refinery => "정제소",
        _                    => "전투",
    };

    /// <summary>입구를 봉인 — 무거운 석문이 개구부 위에서 가속 낙하해 "쿵" 닫히고 카메라가 흔들린다(임팩트).
    /// 통과 차단은 EntranceHardBlocker 정적 콜라이더가 처음부터 담당하므로 낙하 중 갇힘이 없다.</summary>
    private async UniTaskVoid LockEntranceAsync(GameObject lockGo, Transform door, float openH)
    {
        var ct = _cts != null ? _cts.Token : this.GetCancellationTokenOnDestroy();
        // 석문이 있으면 그걸, 없으면 큐브 패널을 낙하 애니 대상으로.
        Transform tr = door;
        if (tr == null)
        {
            if (lockGo == null) return;
            var rend = lockGo.GetComponentInChildren<Renderer>();
            tr = rend != null ? rend.transform : null;
        }
        if (tr == null) return;

        Vector3 sealedPos = tr.localPosition;              // 닫힘(개구부 봉인) 위치
        float   rise      = Mathf.Max(1f, openH);
        Vector3 upPos     = sealedPos + Vector3.up * rise; // 열림(개구부 위로 올라간) 위치
        float   openDur   = 0.5f;
        float   holdDur    = 0.45f;
        float   fallDur   = 0.5f;
        tr.localPosition = sealedPos;                      // 닫힌 상태에서 시작
        try
        {
            // 1) 열림 — 석문이 위로 올라가며 통로를 연다(감속 정착). "도착 = 통로 확보"
            float t = 0f;
            while (t < openDur)
            {
                if (tr == null) return;
                ct.ThrowIfCancellationRequested();
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / openDur);
                float e = 1f - (1f - k) * (1f - k);        // ease-out
                tr.localPosition = Vector3.Lerp(sealedPos, upPos, e);
                await UniTask.Yield();
            }
            tr.localPosition = upPos;

            // 2) 잠깐 열린 채 유지 — 통로가 보이는 순간
            await UniTask.Delay(TimeSpan.FromSeconds(holdDur), ignoreTimeScale: true, cancellationToken: ct);

            // 3) 닫힘 — SmoothStep으로 매끄럽게 낙하 정착(프레임 점프 방지). 임팩트는 착지 연출이 담당.
            t = 0f;
            while (t < fallDur)
            {
                if (tr == null) return;
                ct.ThrowIfCancellationRequested();
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / fallDur);
                tr.localPosition = Vector3.Lerp(upPos, sealedPos, Mathf.SmoothStep(0f, 1f, k));
                await UniTask.Yield();
            }
            tr.localPosition = sealedPos;
        }
        catch (OperationCanceledException) { return; }

        // 4) 착지 임팩트 — 먼지 + 카메라 흔들림 + 봉인 사운드(무거운 석문이 바닥에 꽂히는 피드백).
        PlayDoorImpact(tr, openH, primary: true);
    }

    private async UniTaskVoid TransitionAsync(DoorPlan plan, DoorEdge edge)
    {
        var ct = _cts != null ? _cts.Token : this.GetCancellationTokenOnDestroy();
        try
        {
            foreach (var g in _gates)
                g?.gate?.Disarm();

            ClearGates();
            _sequencer.CommitEntry(plan);

            await EnterRoomAsync(plan, edge, ct); // 빌드 완료 후 이전 방 디스폰(추락 방지)
        }
        catch (OperationCanceledException) { }
    }

    private void Awake() => Active = this;

    private void OnDestroy()
    {
        if (Active == this) Active = null;
        if (_currentWave != null) _currentWave.OnRoomCleared -= HandleRoomCleared;
        _cts?.Cancel();
        _cts?.Dispose();
    }

    // ── Event Handlers ──────────────────────────────

    private void OnGateChosen(ProcRoomGate gate)
    {
        TransitionAsync(gate.Plan, gate.Edge).Forget();
    }
}
