using RelicFairy.UI;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 리치 보스 등장 대기 상태.
///
/// 흐름: Enter → 이동 잠금 + 대기 (카메라 팬 완료까지)
///       → TriggerEntrance() 호출 → Appear 애니메이션 + 보스 이름 표시 + 조우 기록
///       → introDuration 경과 → ChaseState 전환
/// </summary>
public class LichDormantState : IMonsterState
{
    private readonly float _duration;
    private float _elapsed;
    private bool  _triggered;

    /// <summary>false가 되면 Exit()가 호출됐음을 의미 — LichMonster가 패턴 러너 차단 해제에 사용.</summary>
    public bool IsActive { get; private set; } = true;

    public LichDormantState(float duration = 2.5f)
    {
        _duration = duration;
    }

    public void Enter(MonsterContext ctx)
    {
        _elapsed   = 0f;
        _triggered = false;
        IsActive   = true;  // 풀 재사용 시 재진입을 위해 명시적 리셋

        (ctx.Monster as LichMonster)?.MovementController?.SetLocked(true);
        // 카메라 팬 완료를 기다리는 동안 기본 Idle 포즈 유지
    }

    /// <summary>
    /// BossSpawner → LichMonster.TriggerEntrance() 경유로 호출.
    /// 카메라 팬이 끝난 뒤 Appear 애니메이션과 보스 이름 UI를 시작한다.
    /// </summary>
    public void TriggerEntrance(MonsterContext ctx)
    {
        if (_triggered) return;
        _triggered = true;

        // Appear 애니메이션 시작 시점에 Phase1 장비 노출
        (ctx.Monster as LichMonster)?.ShowPhase1Form();

        ctx.Animator?.CrossFade("Appear", 0.1f);
        UI_BossBark.Show("리치", BossBarkType.BossIntro);

        // 조우 기록 — 플레이어가 보스방에 실제 진입한 시점
        (ctx.Monster as LichMonster)?.StartEncounterRecord();
    }

    public void Update(MonsterContext ctx)
    {
        if (!_triggered) return;

        _elapsed += Time.deltaTime;
        if (_elapsed >= _duration)
            ctx.Monster.ChangeState<ChaseState>();
    }

    public void Exit(MonsterContext ctx)
    {
        IsActive = false;
        (ctx.Monster as LichMonster)?.MovementController?.SetLocked(false);
    }
}
}
