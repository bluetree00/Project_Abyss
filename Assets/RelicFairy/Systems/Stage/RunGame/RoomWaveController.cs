using System;
using System.Collections.Generic;
using RelicFairy.Monster;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 전투방 웨이브 기반 클리어 컨트롤러.
///
/// 웨이브 모드 (MonsterSpawner에 WaveEntry[]가 설정된 경우):
///   · 웨이브 0 시작 → 모든 스포너가 해당 웨이브 설정 마릿수를 병렬 소환
///   · 현재 웨이브 모든 몬스터 사망 → BetweenWaveDelay 후 다음 웨이브
///   · 마지막 웨이브 클리어 → 포탈 + 아이템 등장
///
/// 레거시 모드 (WaveEntry[]가 없는 경우):
///   · 기존 RoomClearController와 동일 — 스포너 MaxTotalSpawns 합산 킬 목표
///
/// 외부 이벤트: OnWaveStarted(currentWave, totalWaves) — HUD 웨이브 표시 연동용.
/// </summary>
public sealed class RoomWaveController : MonoBehaviour
{
    private const float PreExitDelay     = 0.8f;
    private const float BetweenWaveDelay = 2.0f;
    // alive 카운터 정합 감시 주기(초). 전투 중 계속 도는 값이라 너무 촘촘하면 낭비다.
    private const float AliveWatchdogInterval = 3.0f;

    // ── 공통 ──────────────────────────────────────────────
    private GameRunSession  _run;
    private LuckRollTableSO _luckTable;
    private readonly List<MonsterSpawner> _spawners = new();
    private BossSpawner     _bossSpawner;
    private bool            _active;
    private bool            _cleared;

    // ── 웨이브 모드 ───────────────────────────────────────
    private bool _waveMode;
    private int  _totalWaves;
    private int  _currentWave      = -1;
    private int  _currentWaveAlive;
    private bool _waveSpawningDone;  // 현재 웨이브 스폰이 모두 완료됐는지 (조기 사망 레이스 방지)
    // 이번 웨이브에서 스폰된 몬스터 실물. alive 카운터가 유실됐는지 실측하는 워치독 전용.
    private readonly List<MonsterBase> _waveMonsters = new();

    // ── 레거시 모드 ───────────────────────────────────────
    private int _targetKillCount;
    private int _killed;

    // ── 마지막 킬 위치 ────────────────────────────────────
    private Vector3 _lastKillPosition;
    private bool    _hasKillPosition;

    // ── Room Clear Effects ────────────────────────────────
    private GameObject _clearEndEffectPrefab;
    private GameObject _clearEndEffect2Prefab;

    // ── 이벤트 ────────────────────────────────────────────
    /// <summary>웨이브가 시작될 때 발행. (현재 웨이브 0-based 인덱스, 총 웨이브 수)</summary>
    public event Action<int, int> OnWaveStarted;

    /// <summary>방이 완전히 클리어됐을 때 1회 발행. 절차 진행(RunFlowController)이 출구 게이트 배치에 사용.</summary>
    public event Action OnRoomCleared;

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 초기화
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    /// <summary>부트스트래퍼에서 방 빌드 직후 호출.
    /// bossSpawner는 null 허용 — 보스 방이 아닐 경우 null을 전달한다.</summary>
    public void Initialize(
        GameRunSession run,
        IList<MonsterSpawner> spawners,
        BossSpawner bossSpawner,
        LuckRollTableSO luckTable,
        GameObject endEffectPrefab   = null,
        GameObject endEffect2Prefab  = null)
    {
        _run                  = run;
        _luckTable            = luckTable;
        _clearEndEffectPrefab  = endEffectPrefab;
        _clearEndEffect2Prefab = endEffect2Prefab;

        if (spawners != null)
            foreach (var s in spawners)
                if (s != null) _spawners.Add(s);

        _bossSpawner = bossSpawner;

        // 하나라도 WaveEntry가 있으면 웨이브 모드
        bool anyWave = false;
        int  minWaves = int.MaxValue;
        foreach (var s in _spawners)
        {
            if (s.WaveCount > 0)
            {
                anyWave   = true;
                minWaves  = Mathf.Min(minWaves, s.WaveCount);
            }
        }

        _waveMode   = anyWave;
        _totalWaves = _waveMode && minWaves != int.MaxValue ? minWaves : 0;

        if (_waveMode)
            InitWaveMode();
        else
            InitLegacyMode();
    }

    private void InitWaveMode()
    {
        if (_totalWaves == 0)
        {
            Debug.LogWarning("[RoomWave] 웨이브 스포너가 있으나 유효한 WaveCount가 없음 — 비활성", this);
            return;
        }

        // OnMonsterSpawned 구독 — 스폰되는 몬스터마다 alive 카운트 증가 + OnDied 체이닝
        foreach (var s in _spawners)
            s.OnMonsterSpawned += HandleWaveMonsterSpawned;
        if (_bossSpawner != null)
            _bossSpawner.OnMonsterSpawned += HandleWaveMonsterSpawned;

        // 활성화는 Activate() 호출 시 시작 (기존 방: SpawnBlockMapAsync에서 즉시, 지연 존: ZoneEntryTrigger에서)
        Debug.Log($"[RoomWave] 웨이브 모드 준비 완료 — {_totalWaves}웨이브 / 스포너 {_spawners.Count}개", this);
    }

    private void InitLegacyMode()
    {
        int sum = 0;
        foreach (var s in _spawners)
        {
            if (s.MaxTotalSpawns <= 0)
            {
                Debug.LogWarning($"[RoomWave] 무제한 스포너 '{s.name}' 포함 — 클리어 조건 비활성", this);
                return;
            }
            sum += s.MaxTotalSpawns;
            s.OnMonsterSpawned += HandleLegacyMonsterSpawned;
        }

        if (_bossSpawner != null)
        {
            sum += _bossSpawner.MaxTotalSpawns;
            _bossSpawner.OnMonsterSpawned += HandleLegacyMonsterSpawned;
        }

        if (sum == 0)
        {
            Debug.Log("[RoomWave] 스포너 0개 — 클리어 조건 없음 (비활성)", this);
            return;
        }

        _targetKillCount = sum;

        string bossTag = _bossSpawner != null ? " + 보스 1" : "";
        Debug.Log($"[RoomWave] 레거시 모드 준비 완료 — 킬 목표 {_targetKillCount}마리 ({_spawners.Count}개 스포너{bossTag})", this);
    }

    /// <summary>
    /// 웨이브/레거시 모드를 시작한다.
    /// 기존 방(SpawnBlockMapAsync): 스포너 활성화 직후 즉시 호출.
    /// 지연 존(SpawnZoneByIndexAsync): ZoneEntryTrigger가 플레이어 진입 시 호출.
    /// </summary>
    public void Activate()
    {
        if (_active || _cleared) return;
        _active = true;

        if (_waveMode)
        {
            Debug.Log($"[RoomWave] 웨이브 모드 활성화", this);
            StartWaveAsync(0).Forget();
        }
        else
        {
            Debug.Log($"[RoomWave] 레거시 모드 활성화 — 킬 카운트 시작", this);
        }
    }

    /// <summary>
    /// 이어하기 복원 — 저장 당시 "클리어했지만 아직 안 받은" 클리어 보상만 방에 다시 세운다.
    /// 전투는 재개하지 않는다(Activate 미호출). 연료 재지급도 없다 — RoomClearGate.ActivateRestoredReward 참조.
    /// </summary>
    public void SpawnPendingClearReward(Vector3 center, int rerollSeed)
    {
        _cleared = true;   // 이후 어떤 경로로도 클리어 시퀀스가 다시 돌지 않게 잠근다

        var gate = GetComponent<RoomClearGate>() ?? gameObject.AddComponent<RoomClearGate>();
        gate.Initialize(_run, _luckTable, _clearEndEffectPrefab, _clearEndEffect2Prefab);
        gate.ActivateRestoredReward(center, rerollSeed);
        Debug.Log("[RoomWave] 이어하기 — 미수령 클리어 보상 재배치", this);
    }

    private void OnDestroy()
    {
        foreach (var s in _spawners)
        {
            if (s == null) continue;
            s.OnMonsterSpawned -= HandleWaveMonsterSpawned;
            s.OnMonsterSpawned -= HandleLegacyMonsterSpawned;
        }

        if (_bossSpawner != null)
        {
            _bossSpawner.OnMonsterSpawned -= HandleWaveMonsterSpawned;
            _bossSpawner.OnMonsterSpawned -= HandleLegacyMonsterSpawned;
        }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 웨이브 모드
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private async UniTaskVoid StartWaveAsync(int waveIndex)
    {
        if (!_active || _cleared) return;

        _currentWave      = waveIndex;
        _currentWaveAlive = 0;
        _waveSpawningDone = false;
        _waveMonsters.Clear();

        Debug.Log($"[RoomWave] 웨이브 {waveIndex + 1}/{_totalWaves} 시작", this);
        OnWaveStarted?.Invoke(waveIndex, _totalWaves);

        var ct = this.GetCancellationTokenOnDestroy();

        // 모든 스포너를 병렬로 스폰 — 실제 스폰 수는 OnMonsterSpawned 이벤트로 추적
        var tasks = new UniTask<int>[_spawners.Count];
        for (int i = 0; i < _spawners.Count; i++)
        {
            var s = _spawners[i];
            tasks[i] = (s != null && s.WaveCount > waveIndex)
                ? s.SpawnWaveAsync(waveIndex, ct)
                : UniTask.FromResult(0);
        }

        int totalSpawned = 0;
        try
        {
            var results = await UniTask.WhenAll(tasks);
            foreach (var r in results) totalSpawned += r;
        }
        catch (OperationCanceledException) { return; }

        _waveSpawningDone = true;

        // 스폰 0마리: 스폰 테이블 등급 필터·NavMesh 설정 문제로 모든 시도가 실패한 경우.
        // 예전엔 "거짓 클리어 차단"을 이유로 그냥 return 했는데, 그러면 CheckWaveComplete가
        // 영영 호출되지 않아 출구도 입구도 잠긴 채 방이 영구 봉인됐다(진행 불가 = 런 사망).
        // 보상 없는 방 포기가 진행 불가보다 낫다 — 출구만 연다.
        if (totalSpawned == 0)
        {
            Debug.LogError(
                $"[RoomWave] 웨이브 {waveIndex + 1}/{_totalWaves}: 소환된 몬스터 0마리 — " +
                "SpawnTable 등급 필터(Common/Rare/Elite) 또는 NavMesh 설정을 확인하세요.", this);
            AbandonRoom($"웨이브 {waveIndex + 1} 소환 0마리");
            return;
        }

        // 스폰된 몬스터가 죽지 않고 사라지는 경우(풀 회수 등) alive 카운터가 0으로 못 내려온다 — 실측 감시 시작.
        WatchWaveAliveAsync(waveIndex).Forget();

        // 스폰 완료 — 이미 죽은 몬스터가 있어도 안전하게 체크
        CheckWaveComplete(waveIndex);
    }

    private void HandleWaveMonsterSpawned(MonsterBase monster)
    {
        if (monster == null || _currentWave < 0) return;
        _currentWaveAlive++;
        _waveMonsters.Add(monster);
        monster.OnDied += HandleWaveMonsterDied;
    }

    /// <summary>
    /// alive 카운터 정합 감시. 몬스터가 <b>죽지 않고</b> 사라지면(풀 회수·씬 정리 등) OnDied가 오지 않아
    /// _currentWaveAlive가 0으로 내려오지 못하고 방이 영구 봉인된다.
    /// 실제로 남아있는 몬스터를 세어 0이면 카운터를 실측값으로 바로잡고 클리어 판정을 다시 태운다.
    /// 한 마리라도 살아있으면 아무 것도 하지 않으므로 정상 전투에는 개입하지 않는다.
    /// </summary>
    private async UniTaskVoid WatchWaveAliveAsync(int waveIndex)
    {
        var ct = this.GetCancellationTokenOnDestroy();
        try
        {
            while (_active && !_cleared && _currentWave == waveIndex)
            {
                await UniTask.Delay(TimeSpan.FromSeconds(AliveWatchdogInterval),
                                    ignoreTimeScale: true, cancellationToken: ct);

                if (_cleared || _currentWave != waveIndex) return;
                if (_currentWaveAlive <= 0) return;   // 정상 경로가 이미 처리 중

                int actual = 0;
                foreach (var m in _waveMonsters)
                    if (m != null && m.gameObject.activeInHierarchy && !m.IsDead) actual++;
                if (actual > 0) continue;

                Debug.LogWarning(
                    $"[RoomWave] 웨이브 {waveIndex + 1} alive 카운터 유실 감지 " +
                    $"(장부 {_currentWaveAlive}마리 / 실측 0마리) — 클리어 판정 복구", this);
                _currentWaveAlive = 0;
                CheckWaveComplete(waveIndex);
                return;
            }
        }
        catch (OperationCanceledException) { }
    }

    private void HandleWaveMonsterDied(MonsterBase monster)
    {
        if (monster != null)
        {
            monster.OnDied -= HandleWaveMonsterDied;
            _lastKillPosition = monster.transform.position;
            _hasKillPosition  = true;
        }
        if (!_active || _cleared) return;

        _currentWaveAlive--;

        if (_waveSpawningDone)
            CheckWaveComplete(_currentWave);
    }

    private void CheckWaveComplete(int waveIndex)
    {
        if (_currentWaveAlive > 0) return;
        if (waveIndex != _currentWave)  return; // 오래된 콜백 무시

        Debug.Log($"[RoomWave] 웨이브 {waveIndex + 1} 클리어", this);

        if (waveIndex < _totalWaves - 1)
            NextWaveAsync(waveIndex + 1).Forget();
        else
            ClearRoomAsync().Forget();
    }

    private async UniTaskVoid NextWaveAsync(int nextIndex)
    {
        var ct = this.GetCancellationTokenOnDestroy();
        try
        {
            await UniTask.Delay(
                TimeSpan.FromSeconds(BetweenWaveDelay),
                cancellationToken: ct);
        }
        catch (OperationCanceledException) { return; }

        StartWaveAsync(nextIndex).Forget();
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 레거시 모드
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private void HandleLegacyMonsterSpawned(MonsterBase monster)
    {
        if (monster == null) return;
        Debug.Log($"[RoomWave] 몬스터 스폰 감지 '{monster.name}' — OnDied 구독", this);
        monster.OnDied += HandleLegacyMonsterDied;
    }

    private void HandleLegacyMonsterDied(MonsterBase monster)
    {
        if (monster != null)
        {
            monster.OnDied -= HandleLegacyMonsterDied;
            _lastKillPosition = monster.transform.position;
            _hasKillPosition  = true;
        }
        Debug.Log($"[RoomWave] 몬스터 사망 감지 '{(monster != null ? monster.name : "null")}' — active={_active} cleared={_cleared} killed={_killed+1}/{_targetKillCount}", this);
        if (!_active || _cleared) return;

        _killed++;
        if (_killed >= _targetKillCount)
            ClearRoomAsync().Forget();
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 공통 클리어 루틴 (기존 RoomClearController와 동일)
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    /// <summary>
    /// 방을 <b>보상 없이</b> 클리어 처리한다 — 전투를 성립시키지 못한 방에서 진행을 보장하는 탈출구.
    /// 정상 클리어(<see cref="ClearRoomAsync"/>)와 달리 RoomClearGate(보상)·퀘스트 집계·
    /// 방 클리어 기록(EnterStandby)·보스 신호를 <b>일절 태우지 않고</b>,
    /// 절차 진행이 출구 게이트를 여는 데 쓰는 OnRoomCleared만 발행한다.
    /// (RunFlowController.HandleRoomCleared → RollExits → RevealGates 경로가 정상 클리어와 동일하게 돈다)
    /// </summary>
    private void AbandonRoom(string reason)
    {
        if (_cleared) return;
        _cleared = true;

        Debug.LogError($"[RoomWave] 방 포기 — {reason}. 보상 없이 출구만 연다(진행 보장).", this);
        OnRoomCleared?.Invoke();
    }

    private async UniTaskVoid ClearRoomAsync()
    {
        if (_cleared) return;
        _cleared = true;
        QuestEvents.ReportRoomClear("Normal");
        Managers.Sound?.PlayEvent(SoundEvent.RoomClear);

        string modeTag = _waveMode ? $"웨이브 {_totalWaves}/{_totalWaves}" : $"{_killed}/{_targetKillCount}";
        Debug.Log($"[RoomWave] 방 클리어 달성 ({modeTag}) — 포탈 등장 시퀀스 시작", this);

        var ct = this.GetCancellationTokenOnDestroy();
        try
        {
            await UniTask.Delay(TimeSpan.FromSeconds(PreExitDelay), ignoreTimeScale: true, cancellationToken: ct);

            _run?.EnterStandby();

            var gate = GetComponent<RoomClearGate>() ?? gameObject.AddComponent<RoomClearGate>();
            // 이벤트 챌린지 오버레이가 있으면 성과 등급을 산출해 보상에 반영(없으면 null=일반 보상).
            ChallengeGrade? challengeGrade = GetComponent<CombatChallengeOverlay>()?.EvaluateGrade();
            gate.Initialize(_run, _luckTable, _clearEndEffectPrefab, _clearEndEffect2Prefab, _bossSpawner != null, challengeGrade);
            gate.Activate(_hasKillPosition ? _lastKillPosition : transform.position);

            // 보스방 클리어 신호 발행 — 챕터 게이트 스폰 트리거(이벤트 기반, 보스/DieState 코드 무수정).
            if (_bossSpawner != null)
                _run?.NotifyBossRoomCleared(_hasKillPosition ? _lastKillPosition : transform.position);

            // 절차 진행: 출구 게이트 배치 트리거 (레거시 contiguous 경로엔 구독자 없음 → 무영향)
            OnRoomCleared?.Invoke();
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Debug.LogError($"[RoomWave] 클리어 시퀀스 예외: {e.Message}", this);
        }
    }
}
