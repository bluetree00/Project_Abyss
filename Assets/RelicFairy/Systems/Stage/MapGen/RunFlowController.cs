using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
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
    [SerializeField] private string _poolKey = "CHAPTER_1_ROOM_POOL";
    [SerializeField, Tooltip("시작 방 pool_key. 비우면 첫 Normal 방 사용.")]
    private string _startPoolKey;
    [SerializeField, Tooltip("방을 빌드할 고정 앵커. 비우면 원점.")]
    private Transform _anchor;
    [SerializeField, Tooltip("0이면 매 런 랜덤 시드.")]
    private int _seed;

    [Header("전환 연출 (방 진입 와이프)")]
    [SerializeField, Tooltip("덮기 시간(초). 가속 곡선 권장.")]          private float          _coverDuration  = 0.18f;
    [SerializeField, Tooltip("드러내기 시간(초). 감속 곡선 권장.")]      private float          _revealDuration = 0.32f;
    [SerializeField] private AnimationCurve _coverCurve  = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [SerializeField] private AnimationCurve _revealCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    private RunSequencer                 _sequencer;
    private List<ZonePoolEntry>          _pool;
    private System.Random                _rng;
    private ProcRoomResult               _current;
    private RoomWaveController           _currentWave;
    private readonly List<ProcRoomGate>  _gates = new();
    private CancellationTokenSource      _cts;

    private Vector3 _baseAnchor;
    private int     _anchorToggle;
    private int     _heading; // 현재 진행 방향(0=N,1=E,2=S,3=W). 탄 출구 엣지로 갱신 → 다음 방 회전에 사용.

    // ── Public ──────────────────────────────────────

    /// <summary>절차 런 시작 — 풀 로드 → 시퀀서 생성 → 첫 방 진입.
    /// anchor: 방을 빌드할 고정 월드 위치(허브와 겹치지 않게 먼 곳). null이면 _anchor 또는 원점.</summary>
    public async UniTask StartRunAsync(Vector3? anchor = null)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
        var ct = _cts.Token;
        _baseAnchor = anchor ?? (_anchor != null ? _anchor.position : Vector3.zero);

        _pool = await Managers.ZoneLayout.LoadPoolAsync(_poolKey);
        if (_pool == null || _pool.Count == 0)
        {
            Debug.LogError($"[RunFlow] 풀 로드 실패: {_poolKey}");
            return;
        }

        int seed   = _seed != 0 ? _seed : Environment.TickCount;
        _rng       = new System.Random(seed);
        _sequencer = new RunSequencer(_pool, _structureConfig, seed);

        var startEntry = FindStartEntry();
        if (startEntry == null)
        {
            Debug.LogError("[RunFlow] 시작 방 없음");
            return;
        }

        await EnterRoomAsync(new DoorPlan { kind = RoomPlanKind.Normal, entry = startEntry }, DoorEdge.North, ct);
    }

    // ── Private ─────────────────────────────────────

    private ZonePoolEntry FindStartEntry()
    {
        if (!string.IsNullOrEmpty(_startPoolKey))
        {
            var byKey = _pool.Find(p => string.Equals(p.pool_key, _startPoolKey, StringComparison.OrdinalIgnoreCase));
            if (byKey != null) return byKey;
        }
        return _pool.Find(p => string.Equals(p.category, "Normal", StringComparison.OrdinalIgnoreCase)) ?? _pool[0];
    }

    private async UniTask EnterRoomAsync(DoorPlan plan, DoorEdge fromEdge, CancellationToken ct)
    {
        var grb = GameRunBootstrapper.Instance;
        if (grb == null || plan.entry == null) return;

        var dir = WipeDir(fromEdge);
        _heading = (int)fromEdge; // 탄 출구의 절대 방향 = 새 진행 방향 → 다음 방을 이만큼 회전
        await ScreenFade.CoverAsync(dir, KindColor(plan.kind), _coverDuration, _coverCurve, ct); // 종류색 + 방향 와이프로 덮기

        var prevRoom = _current?.roomGO; // 새 방 준비까지 이전 방 유지 → 플레이어 발판 보존(추락 방지)

        // 리프프로그 앵커: 이전 방과 겹치지 않게 z를 번갈아 배치 (최대 2개 방만 잠깐 공존)
        var roomAnchor = _baseAnchor + new Vector3(0f, 0f, (_anchorToggle++ % 2) * 300f);

        bool mirror = _rng.Next(2) == 0;
        var result  = await grb.BuildProcRoomAsync(plan.entry, roomAnchor, mirror, _heading, ct);
        if (result == null) { await ScreenFade.RevealAsync(dir, _revealDuration, _revealCurve, ct); return; }

        _current = result;
        MovePlayer(result.entryPos);

        if (prevRoom != null) Destroy(prevRoom); // 이동 완료 후 이전 방 디스폰

        // 클리어 알림 구독 → 출구 게이트 배치. 전투 없는 방(스포너 0)은 즉시 출구 제공.
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

        await ScreenFade.RevealAsync(dir, _revealDuration, _revealCurve, ct); // 같은 방향으로 빠져나가며 새 방 드러내기
    }

    private void MovePlayer(Vector3 pos)
    {
        var player = AppBootstrapper.Instance?.CurrentRun?.Player;
        if (player != null)
            player.transform.position = pos;
    }

    private void HandleRoomCleared()
    {
        if (_currentWave != null) _currentWave.OnRoomCleared -= HandleRoomCleared;

        var exits = _sequencer.RollExits();
        if (exits == null || exits.Count == 0)
        {
            Debug.Log("[RunFlow] 출구 없음 — 런 종료(보스 처치 등)");
            return;
        }
        PlaceGates(exits);
    }

    private void PlaceGates(List<DoorPlan> exits)
    {
        ClearGates();
        if (_current?.exits == null) return;

        var slots = _current.exits;
        // 직진 슬롯 → exits[0], 턴 슬롯 → exits[1] (best-effort 슬롯 매칭)
        for (int i = 0; i < slots.Count && i < exits.Count; i++)
            _gates.Add(CreateGate(slots[i], exits[i]));
    }

    private ProcRoomGate CreateGate(ProcExitSlot slot, DoorPlan plan)
    {
        var go = new GameObject($"ProcGate_{plan.kind}");
        go.transform.SetParent(transform, false);
        go.transform.position = slot.worldPos;

        var col = go.AddComponent<BoxCollider>();
        col.isTrigger = true;
        col.size = new Vector3(2f, 3f, 2f);

        var gate = go.AddComponent<ProcRoomGate>();
        gate.Initialize(plan, slot.edge, OnGateChosen);
        // TODO(후속): 방 종류 아이콘/라벨 비주얼 (CategoryKor 매핑)
        return gate;
    }

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
            if (_gates[i] != null) Destroy(_gates[i].gameObject);
        _gates.Clear();
    }

    private async UniTaskVoid TransitionAsync(DoorPlan plan, DoorEdge edge)
    {
        var ct = _cts != null ? _cts.Token : this.GetCancellationTokenOnDestroy();
        try
        {
            foreach (var g in _gates)
                if (g != null) g.Disarm();

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
