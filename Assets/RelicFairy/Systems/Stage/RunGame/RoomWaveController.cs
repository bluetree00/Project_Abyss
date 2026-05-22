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
        // CheckWaveComplete를 호출하면 _currentWaveAlive==0으로 거짓 클리어가 발생하므로 차단.
        if (totalSpawned == 0)
        {
            Debug.LogError(
                $"[RoomWave] 웨이브 {waveIndex + 1}/{_totalWaves}: 소환된 몬스터 0마리 — " +
                "SpawnTable 등급 필터(Common/Rare/Elite) 또는 NavMesh 설정을 확인하세요.", this);
            return;
        }

        // 스폰 완료 — 이미 죽은 몬스터가 있어도 안전하게 체크
        CheckWaveComplete(waveIndex);
    }

    private void HandleWaveMonsterSpawned(MonsterBase monster)
    {
        if (monster == null || _currentWave < 0) return;
        _currentWaveAlive++;
        monster.OnDied += HandleWaveMonsterDied;
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
        if (!_active || _cleared) return;

        _killed++;
        if (_killed >= _targetKillCount)
            ClearRoomAsync().Forget();
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 공통 클리어 루틴 (기존 RoomClearController와 동일)
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

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
            await UniTask.Delay(TimeSpan.FromSeconds(PreExitDelay), cancellationToken: ct);

            _run?.EnterStandby();

            var gate = GetComponent<RoomClearGate>() ?? gameObject.AddComponent<RoomClearGate>();
            gate.Initialize(_run, _luckTable, _clearEndEffectPrefab, _clearEndEffect2Prefab, _bossSpawner != null);
            gate.Activate(_hasKillPosition ? _lastKillPosition : transform.position);
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Debug.LogError($"[RoomWave] 클리어 시퀀스 예외: {e.Message}", this);
        }
    }
}
