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

    private RunSequencer                 _sequencer;
    private List<ZonePoolEntry>          _pool;
    private System.Random                _rng;
    private ProcRoomResult               _current;
    private RoomWaveController           _currentWave;
    private readonly List<ProcRoomGate>  _gates = new();
    private CancellationTokenSource      _cts;

    private Vector3 Anchor => _anchor != null ? _anchor.position : Vector3.zero;

    // ── Public ──────────────────────────────────────

    /// <summary>절차 런 시작 — 풀 로드 → 시퀀서 생성 → 첫 방 진입.</summary>
    public async UniTask StartRunAsync()
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
        var ct = _cts.Token;

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

        await EnterRoomAsync(new DoorPlan { kind = RoomPlanKind.Normal, entry = startEntry }, ct);
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

    private async UniTask EnterRoomAsync(DoorPlan plan, CancellationToken ct)
    {
        var grb = GameRunBootstrapper.Instance;
        if (grb == null || plan.entry == null) return;

        bool mirror = _rng.Next(2) == 0;
        var result  = await grb.BuildProcRoomAsync(plan.entry, Anchor, mirror, ct);
        if (result == null) return;

        _current = result;
        MovePlayer(result.entryPos);

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
            _gates.Add(CreateGate(slots[i].worldPos, exits[i]));
    }

    private ProcRoomGate CreateGate(Vector3 pos, DoorPlan plan)
    {
        var go = new GameObject($"ProcGate_{plan.kind}");
        go.transform.SetParent(transform, false);
        go.transform.position = pos;

        var col = go.AddComponent<BoxCollider>();
        col.isTrigger = true;
        col.size = new Vector3(2f, 3f, 2f);

        var gate = go.AddComponent<ProcRoomGate>();
        gate.Initialize(plan, OnGateChosen);
        // TODO(후속): 방 종류 아이콘/라벨 비주얼 (CategoryKor 매핑)
        return gate;
    }

    private void ClearGates()
    {
        for (int i = 0; i < _gates.Count; i++)
            if (_gates[i] != null) Destroy(_gates[i].gameObject);
        _gates.Clear();
    }

    private async UniTaskVoid TransitionAsync(DoorPlan plan)
    {
        var ct = _cts != null ? _cts.Token : this.GetCancellationTokenOnDestroy();
        try
        {
            foreach (var g in _gates)
                if (g != null) g.Disarm();

            _sequencer.CommitEntry(plan);

            ClearGates();
            if (_current?.roomGO != null) Destroy(_current.roomGO);

            await EnterRoomAsync(plan, ct);
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
        TransitionAsync(gate.Plan).Forget();
    }
}
