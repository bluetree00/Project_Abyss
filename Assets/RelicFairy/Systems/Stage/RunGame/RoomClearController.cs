using System;
using System.Collections.Generic;
using RelicFairy.Monster;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 방 클리어 감지 + 포탈 등장 + 아이템 드랍.
///
/// 규칙:
///   · 방에 배치된 모든 MonsterSpawner의 MaxTotalSpawns를 합산 → 킬 목표
///   · 각 스포너에서 몬스터가 스폰될 때 OnDied를 체이닝하여 사망 카운트 누적
///   · 킬 카운트가 목표에 도달하면 단 한 번만 클리어 루틴 실행
///
/// 클리어 루틴:
///   1) 짧은 딜레이 (마지막 몬스터 드롭 연출 겹침 방지)
///   2) GameRunSession.EnterStandby() → RunState.Standby (방 클리어 훅)
///   3) RoomClearGate.Activate(roomCenter) → 포탈 등장 + 행운치 기반 아이템 드랍
///   4) 플레이어가 포탈 진입 시 RoomExitTrigger가 StageMap 복귀 트리거
///
/// 디졸브 퇴장 연출은 제거됨 — 몬스터 처치 후 그 자리에서 포탈이 등장하여 플레이어가 직접 입장.
///
/// 무제한 스포너(MaxTotalSpawns ≤ 0)가 포함되어 있으면 클리어 조건을 세울 수 없어
/// Initialize에서 비활성 상태로 남겨둔다.
/// </summary>
public sealed class RoomClearController : MonoBehaviour
{
    private const float PreExitDelay = 0.8f;

    private GameRunSession _run;
    private LuckRollTableSO _luckTable;
    private int  _targetKillCount;
    private int  _killed;
    private bool _active;
    private bool _cleared;
    private readonly List<MonsterSpawner> _spawners = new();
    private BossSpawner _bossSpawner;

    /// <summary>Bootstrapper에서 방 빌드 직후 호출. 무제한 스포너가 포함되면 비활성.
    /// bossSpawner는 null 허용 — 보스 방이 아닐 경우 null을 전달한다.</summary>
    public void Initialize(GameRunSession run, IList<MonsterSpawner> spawners, BossSpawner bossSpawner, LuckRollTableSO luckTable)
    {
        _run = run;
        _luckTable = luckTable;

        int sum = 0;

        if (spawners != null)
        {
            foreach (var s in spawners)
            {
                if (s == null) continue;
                if (s.MaxTotalSpawns <= 0)
                {
                    Debug.LogWarning($"[RoomClear] 무제한 스포너 '{s.name}' 포함 — 킬 목표 계산 불가, 클리어 조건 비활성");
                    return;
                }
                sum += s.MaxTotalSpawns;
                _spawners.Add(s);
            }
        }

        if (bossSpawner != null)
        {
            sum += bossSpawner.MaxTotalSpawns; // 항상 1
            _bossSpawner = bossSpawner;
        }

        if (sum == 0)
        {
            Debug.Log("[RoomClear] 스포너 0개 — 클리어 조건 없음 (비활성)");
            return;
        }

        _targetKillCount = sum;
        _active = true;

        foreach (var s in _spawners)
            s.OnMonsterSpawned += HandleMonsterSpawned;

        if (_bossSpawner != null)
            _bossSpawner.OnMonsterSpawned += HandleMonsterSpawned;

        string bossTag = _bossSpawner != null ? " + 보스 1" : "";
        Debug.Log($"[RoomClear] 초기화 완료 — 킬 목표 {_targetKillCount}마리 ({_spawners.Count}개 스포너{bossTag})");
    }

    private void OnDestroy()
    {
        foreach (var s in _spawners)
            if (s != null) s.OnMonsterSpawned -= HandleMonsterSpawned;
        _spawners.Clear();

        if (_bossSpawner != null)
            _bossSpawner.OnMonsterSpawned -= HandleMonsterSpawned;
    }

    private void HandleMonsterSpawned(MonsterBase monster)
    {
        if (monster == null) return;
        monster.OnDied += HandleMonsterDied;
    }

    private void HandleMonsterDied(MonsterBase monster)
    {
        if (monster != null) monster.OnDied -= HandleMonsterDied;
        if (!_active || _cleared) return;

        _killed++;
        if (_killed >= _targetKillCount)
            ClearRoomAsync().Forget();
    }

    private async UniTaskVoid ClearRoomAsync()
    {
        if (_cleared) return;
        _cleared = true;

        Debug.Log($"[RoomClear] 방 클리어 달성 {_killed}/{_targetKillCount} — 포탈 등장 시퀀스 시작");

        var token = this.GetCancellationTokenOnDestroy();

        try
        {
            // 1) 마지막 몬스터 사망 연출과 겹치지 않도록 짧은 딜레이
            await UniTask.Delay(TimeSpan.FromSeconds(PreExitDelay), cancellationToken: token);

            // 2) 방 클리어 상태 통지 (아이템 효과 hook 등)
            _run?.EnterStandby();
            // [서약] 방 클리어 통보 (ClearRoomAsync는 _cleared 가드로 방당 1회)
            _run?.CovenantHandler?.OnRoomClear();

            // 3) 룸 중앙에 포탈 등장 + 행운치 기반 아이템 드랍
            //    보스방(_bossSpawner!=null)이면 isBossRoom=true 전달 → 보상 수령 후 챕터 전환/런 클리어 분기(ClearRewardTrigger).
            var gate = GetComponent<RoomClearGate>();
            if (gate == null) gate = gameObject.AddComponent<RoomClearGate>();
            gate.Initialize(_run, _luckTable, isBossRoom: _bossSpawner != null);

            // mapGO의 origin = 룸 중앙 (블록 맵은 중심 기준 배치)
            Vector3 roomCenter = transform.position;
            gate.Activate(roomCenter);

            // 4) StageMap 전환은 RoomExitTrigger가 담당 — 여기서는 호출하지 않음
        }
        catch (OperationCanceledException) { /* 씬 파괴 시 정상 경로 */ }
        catch (Exception e)
        {
            Debug.LogError($"[RoomClear] 클리어 시퀀스 예외: {e.Message}");
        }
    }
}
