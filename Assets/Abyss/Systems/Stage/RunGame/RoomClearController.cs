using System;
using System.Collections.Generic;
using Abyss.Monster;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 방 클리어 감지 + 퇴장 연출 + 씬 복귀.
///
/// 규칙:
///   · 방에 배치된 모든 MonsterSpawner의 MaxTotalSpawns를 합산 → 킬 목표
///   · 각 스포너에서 몬스터가 스폰될 때 OnDied를 체이닝하여 사망 카운트 누적
///   · 킬 카운트가 목표에 도달하면 단 한 번만 클리어 루틴 실행
///
/// 클리어 루틴:
///   1) 짧은 딜레이 (마지막 몬스터 드롭 연출 겹침 방지)
///   2) 플레이어에 디졸브 퇴장 연출 (DissolveEffect.PlayDisappear)
///   3) GameRunSession.EnterStandby() → RunState.Standby (방 클리어 훅)
///   4) StageMap 씬 재진입
///
/// 무제한 스포너(MaxTotalSpawns ≤ 0)가 포함되어 있으면 클리어 조건을 세울 수 없어
/// Initialize에서 비활성 상태로 남겨둔다.
/// </summary>
public sealed class RoomClearController : MonoBehaviour
{
    private const float PreExitDelay      = 0.8f;
    private const float PlayerDissolveDur = 0.9f;
    private const float PostExitBuffer    = 0.1f;

    private GameRunSession _run;
    private int  _targetKillCount;
    private int  _killed;
    private bool _active;
    private bool _cleared;
    private readonly List<MonsterSpawner> _spawners = new();

    /// <summary>Bootstrapper에서 방 빌드 직후 호출. 무제한 스포너가 포함되면 비활성.</summary>
    public void Initialize(GameRunSession run, IList<MonsterSpawner> spawners)
    {
        _run = run;
        if (spawners == null || spawners.Count == 0)
        {
            Debug.Log("[RoomClear] 스포너 0개 — 클리어 조건 없음 (비활성)");
            return;
        }

        int sum = 0;
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

        _targetKillCount = sum;
        _active = true;

        foreach (var s in _spawners)
            s.OnMonsterSpawned += HandleMonsterSpawned;

        Debug.Log($"[RoomClear] 초기화 완료 — 킬 목표 {_targetKillCount}마리 ({_spawners.Count}개 스포너)");
    }

    private void OnDestroy()
    {
        foreach (var s in _spawners)
            if (s != null) s.OnMonsterSpawned -= HandleMonsterSpawned;
        _spawners.Clear();
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

        Debug.Log($"[RoomClear] 방 클리어 달성 {_killed}/{_targetKillCount} — 퇴장 시퀀스 시작");

        var token = this.GetCancellationTokenOnDestroy();

        try
        {
            // 1) 마지막 몬스터 사망 연출과 겹치지 않도록 짧은 딜레이
            await UniTask.Delay(TimeSpan.FromSeconds(PreExitDelay), cancellationToken: token);

            // 2) 플레이어 퇴장 디졸브 — 완료 콜백에서 씬 전환으로 이어짐
            var run = _run;
            var player = run?.Player;
            if (player != null)
            {
                var completed = false;
                DissolveEffect.PlayDisappear(player.gameObject, PlayerDissolveDur, () => completed = true);

                // 디졸브 완료 대기 (최대 PlayerDissolveDur + 약간 여유)
                float elapsed = 0f;
                while (!completed && elapsed < PlayerDissolveDur + PostExitBuffer)
                {
                    elapsed += Time.deltaTime;
                    await UniTask.Yield(PlayerLoopTiming.Update, token);
                }
            }
            else
            {
                Debug.LogWarning("[RoomClear] Player 참조 없음 — 퇴장 연출 스킵");
            }

            // 3) 방 클리어 상태 통지 (아이템 효과 hook 등)
            run?.EnterStandby();

            // 4) StageMap 복귀
            var app = AppBootstrapper.Instance;
            if (app == null)
            {
                Debug.LogError("[RoomClear] AppBootstrapper 없음 — 씬 전환 실패");
                return;
            }

            app.RequestLoad(Define.Scene.StageMap);
        }
        catch (OperationCanceledException) { /* 씬 파괴 시 정상 경로 */ }
        catch (Exception e)
        {
            Debug.LogError($"[RoomClear] 퇴장 시퀀스 예외: {e.Message}");
        }
    }
}
