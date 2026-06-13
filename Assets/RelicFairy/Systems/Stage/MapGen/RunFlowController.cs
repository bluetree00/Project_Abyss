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
    private readonly List<GateView>      _gates = new();
    private GameObject                   _entranceLock;
    private CancellationTokenSource      _cts;

    /// <summary>게이트 1개의 시각/물리 구성요소 묶음. 봉인 시 blocker로 막고, 공개 시 marker 색 전환.</summary>
    private sealed class GateView
    {
        public ProcRoomGate gate;
        public Renderer     marker;
        public Collider     blocker; // 봉인: solid(통과 차단) / 공개: 비활성
    }

    private Vector3 _baseAnchor;
    private int     _anchorToggle;
    private int     _heading; // 현재 진행 방향(0=N,1=E,2=S,3=W). 탄 출구 엣지로 갱신 → 다음 방 회전에 사용.
    private int     _masterSeed; // 런 마스터 시드 (세이브/이어하기 결정성).
    private bool    _resuming;   // 이어하기 재생성 중 — 중복 저장 억제용.

    // ── Public ──────────────────────────────────────

    /// <summary>이번 런의 명시적 일정표(깊이별 출구 종류). 맵 미리보기 UI 등의 데이터 소스. 시작 전엔 null.</summary>
    public RunPlan RunPlan => _runPlan;

    /// <summary>절차 런 시작 — 풀 로드 → 시퀀서 생성 → 첫 방 진입.
    /// anchor: 방을 빌드할 고정 월드 위치(허브와 겹치지 않게 먼 곳). null이면 _anchor 또는 원점.
    /// poolKeyOverride: 챕터별 룸 풀 키(CHAPTER_N_ROOM_POOL). 비우면 직렬화된 _poolKey 사용.</summary>
    public async UniTask StartRunAsync(Vector3? anchor = null, string poolKeyOverride = null)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
        var ct = _cts.Token;
        _baseAnchor = anchor ?? (_anchor != null ? _anchor.position : Vector3.zero);

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
        _sequencer  = new RunSequencer(_pool, _structureConfig, seed);
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
    public async UniTask ResumeAsync(RunMetaSnapshot meta, Vector3 anchor, string poolKey, CancellationToken externalCt)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt, this.GetCancellationTokenOnDestroy());
        var ct = _cts.Token;

        _baseAnchor   = anchor;
        _masterSeed   = meta.masterSeed;
        _anchorToggle = meta.anchorToggle;
        _heading      = meta.heading;

        var key = !string.IsNullOrEmpty(poolKey) ? poolKey : _poolKey;
        _pool = await Managers.ZoneLayout.LoadPoolAsync(key);
        if (_pool == null || _pool.Count == 0)
        {
            Debug.LogError($"[RunFlow] 이어하기 풀 로드 실패: {key}");
            return;
        }

        await EnsureStructureConfigAsync();

        _rng       = new System.Random(_masterSeed);
        _sequencer = new RunSequencer(_pool, _structureConfig, _masterSeed);
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

    // ── Private ─────────────────────────────────────

    /// <summary>인스펙터 배선이 없으면(AddComponent 생성 경로) Addressables로 구조 config를 로드한다.
    /// 실패해도 진행은 막지 않음 — config=null이면 RunSequencer가 전 방 Normal로 안전 동작(구조만 비활성).</summary>
    private async UniTask EnsureStructureConfigAsync()
    {
        if (_structureConfig != null || string.IsNullOrEmpty(_structureConfigKey)) return;
        _structureConfig = await Managers.AddressableManager.TryLoadAssetAsync<RunStructureConfig>(_structureConfigKey);
        if (_structureConfig == null)
            Debug.LogWarning($"[RunFlow] RunStructureConfig 로드 실패: {_structureConfigKey} — 전 방 Normal로 진행(구조 비활성).");
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

        var dir = WipeDir(fromEdge);
        _heading = (int)fromEdge; // 탄 출구의 절대 방향 = 새 진행 방향 → 다음 방을 이만큼 회전
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
        var result  = await grb.BuildProcRoomAsync(plan.entry, roomAnchor, mirror, _heading, ct, roomRng); // 블록 숨김 상태로 빌드(디졸브는 여기서)
        if (result == null)
        {
            await ScreenFade.RevealAsync(dir, _revealDuration, _revealCurve, ct);
            return;
        }

        _current = result;
        MovePlayer(result.entryPos);
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
        if (this == null || ct.IsCancellationRequested) return;

        // 출구 문은 봉인(막힘) 상태로 미리 배치, 들어온 입구는 잠금 → 전투 클리어 시 출구만 공개.
        CreateSealedGates();
        if (result.hasEntrance) LockEntrance(result.entrance);

        // 디졸브 후 클리어 알림 구독 → 출구 게이트 공개. 전투 없는 방(스포너 0)은 즉시 공개.
        if (result.roomGO != null && result.roomGO.TryGetComponent<RoomWaveController>(out _currentWave))
        {
            _currentWave.OnRoomCleared += HandleRoomCleared;
            _currentWave.Activate();
        }
        else
        {
            _currentWave = null;
            HandleRoomCleared();
        }

        // 방 경계 자동저장 (suspend-on-save). 이어하기 재생성 중에는 생략(동일 상태 재저장 방지).
        if (!_resuming)
            SaveRunState(plan, fromEdge, mirrorRoll);
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
        GameCameraController.Instance?.SnapToTarget(); // 텔레포트 후 카메라 즉시 스냅(슬로우 패닝/인트로 잔존 방지)
        Debug.Log($"[RunFlow] 플레이어 이동 → {pos}");
    }

    /// <summary>이전 방을 자식 단위로 몇 프레임에 나눠 파괴해 단발 대량 Destroy 스파이크를 분산한다.
    /// 플레이어는 이미 신규 방으로 이동·이전 방은 화면 밖(리프프로그 앵커)이라 안전하다.</summary>
    private async UniTaskVoid DestroyRoomStaggeredAsync(GameObject room, CancellationToken ct)
    {
        if (room == null) return;

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

    /// <summary>빌드 시 모든 출구 슬롯에 봉인(막힌) 게이트를 미리 만든다 — 전투 중엔 통과 불가.</summary>
    private void CreateSealedGates()
    {
        ClearGates();
        if (_current?.exits == null) return;
        foreach (var slot in _current.exits)
            _gates.Add(CreateSealedGate(slot));
    }

    /// <summary>클리어 시 롤된 출구를 슬롯에 매칭해 색 전환+글로우로 공개하고 통과 가능하게 한다.</summary>
    private void RevealGates(List<DoorPlan> exits)
    {
        int n = Mathf.Min(_gates.Count, exits.Count);
        for (int i = 0; i < n; i++)
            if (_gates[i] != null) RevealGateAsync(_gates[i], exits[i]).Forget();
        // 매칭 안 된 여분 슬롯은 봉인 상태 유지(목적지 없음)
    }

    private GateView CreateSealedGate(ProcExitSlot slot)
    {
        var go = CreateGatePanel("ProcGate_Sealed", slot, SealedColor, out var marker, out var blocker);

        // 트리거 — 공개 후 통과 감지용 (봉인 중에는 armed=false)
        var trig = go.AddComponent<BoxCollider>();
        trig.isTrigger = true;
        trig.size = new Vector3(MarkerW(slot), MarkerH(slot), 3f);

        var gate = go.AddComponent<ProcRoomGate>();
        gate.InitializeSealed(slot.edge, OnGateChosen);

        return new GateView { gate = gate, marker = marker, blocker = blocker };
    }

    /// <summary>들어온 입구를 잠금 패널로 막는다 (되돌아가기 차단). 가벼운 잠금 페이드인 연출.</summary>
    private void LockEntrance(ProcExitSlot slot)
    {
        if (_entranceLock != null) Destroy(_entranceLock);
        _entranceLock = CreateGatePanel("ProcEntranceLock", slot, LockedColor, out _, out _);
        LockEntranceAsync(_entranceLock).Forget();
    }

    /// <summary>게이트/잠금 패널 공통 생성 — 개구부 크기로 채운 솔리드 큐브. blocker는 통과 차단용 콜라이더.</summary>
    private GameObject CreateGatePanel(string name, ProcExitSlot slot, Color color, out Renderer marker, out Collider blocker)
    {
        float openW = MarkerW(slot);
        float openH = MarkerH(slot);

        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.position = slot.worldPos + new Vector3(0f, openH * 0.5f, 0f);
        if (slot.edge == DoorEdge.East || slot.edge == DoorEdge.West)
            go.transform.localRotation = Quaternion.Euler(0f, 90f, 0f); // 통로를 가로지르게

        var panel = GameObject.CreatePrimitive(PrimitiveType.Cube); // 솔리드 콜라이더 유지 = 통과 차단
        panel.name = "Marker";
        panel.transform.SetParent(go.transform, false);
        panel.transform.localScale = new Vector3(openW, openH, 0.4f);

        panel.TryGetComponent(out blocker);
        panel.TryGetComponent(out marker);
        if (marker != null) marker.material.color = color;

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
        _                    => new Color(0.05f, 0.06f, 0.10f), // Normal
    };

    private void ClearGates()
    {
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

        // 다음 방 정보 라벨 — 봉인 중엔 없다가 공개와 함께 페이드인
        TextMeshProUGUI label = (rend != null) ? CreateGateLabel(rend.transform.parent, plan.kind) : null;

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
    private TextMeshProUGUI CreateGateLabel(Transform gateTr, RoomPlanKind kind)
    {
        var go = new GameObject("GateLabel");
        go.transform.SetParent(gateTr, false);
        go.transform.localPosition = new Vector3(0f, 1.6f, 0f); // 개구부 상단 부근(게이트 원점 = 개구부 중앙)

        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        if (Camera.main != null) go.transform.rotation = Camera.main.transform.rotation; // 카메라 향해 빌보드(1회)

        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta  = new Vector2(320f, 90f);
        rt.localScale = Vector3.one * 0.02f;

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text      = KindKor(kind);
        tmp.fontSize  = 48f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color     = new Color(1f, 1f, 1f, 0f); // 알파 0 시작
        return tmp;
    }

    /// <summary>방 종류 → 표시용 한글 라벨.</summary>
    private static string KindKor(RoomPlanKind kind) => kind switch
    {
        RoomPlanKind.Boss    => "보스",
        RoomPlanKind.PreBoss => "보스 전",
        RoomPlanKind.Elite   => "정예",
        RoomPlanKind.Shop    => "상점",
        RoomPlanKind.Event   => "이벤트",
        _                    => "전투",
    };

    /// <summary>입구 잠금 패널을 빠르게 페이드인해 "잠김"을 알린다 (영구 차단, 공개 없음).</summary>
    private async UniTaskVoid LockEntranceAsync(GameObject lockGo)
    {
        var ct = _cts != null ? _cts.Token : this.GetCancellationTokenOnDestroy();
        if (lockGo == null) return;
        var rend = lockGo.GetComponentInChildren<Renderer>();
        var tr   = rend != null ? rend.transform : null;
        if (tr == null) return;

        Vector3 full = tr.localScale;
        Vector3 thin = new Vector3(full.x, 0.05f, full.z); // 위→아래로 닫히는 느낌
        float dur = 0.25f;
        try
        {
            float t = 0f;
            while (t < dur)
            {
                if (tr == null) return;
                ct.ThrowIfCancellationRequested();
                t += Time.deltaTime;
                tr.localScale = Vector3.Lerp(thin, full, Mathf.Clamp01(t / dur));
                await UniTask.Yield();
            }
        }
        catch (OperationCanceledException) { return; }
        if (tr != null) tr.localScale = full;
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

    private void OnDestroy()
    {
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
