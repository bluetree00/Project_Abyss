using UnityEngine;

public class SkeletonKnightAttackState : IMonsterState
{
    private MonsterController controller;
    private IMonsterStateChanger stateChanger;
    private int hitIndex; // 0 시작, 1타/2타 진행 관리

    public void Init(MonsterController controller, IMonsterStateChanger stateChanger)
    {
        this.controller = controller;
        this.stateChanger = stateChanger;
    }

    public void Enter()
    {
        Debug.Log($"엔터 시에 {hitIndex}");
    hitIndex = 0;
    StartNextHit();
    }

    public MonsterController.MonsterState StateUpdate()
    {
        // 아직 애니메이션(히트) 재생 중이면 유지
        if (controller.IsAttacking)
            return MonsterController.MonsterState.Attack;


        // ---- 히트 간 전이 처리 ----
        // hitIndex 의미: 0 = 아무것도 시작 안함(Enter 이후 바로 StartNextHit로 1 증가 예정),
        // 1 = 첫 번째 히트 끝난 직후(두 번째 히트 시작 필요 여부 판단),
        // 2 = 두 번째 히트 끝남(콤보 종료), >=2 안전망.
        if (hitIndex == 1)
        {
            // 첫 히트 끝 → 두 번째 히트 무조건 진행 (사거리 무시)
            StartNextHit();
            return MonsterController.MonsterState.Attack;
        }
        else if (hitIndex >= 2)
        {
            // 두 번째 히트 완료 → 항상 Chase로 복귀
            controller.SetAttackReadyTime(controller.MyStat.attack_cooldown);
            ResetCombo();
            stateChanger.RequestStateChange(MonsterController.MonsterState.Chase);
            return MonsterController.MonsterState.Chase;
        }

        return MonsterController.MonsterState.Attack; // 기본 유지
    }

    public void Exit() { }

    private void StartNextHit()
    {
        // 목적 결정: hitIndex 0 -> Normal_01 그대로, hitIndex 1 -> Normal_02로 증가
        if (hitIndex == 1)
            controller.currentAttackPurpose = Util.CastToolPurposePlus(controller.currentAttackPurpose, true); // Normal_01 -> Normal_02
        else if (hitIndex == 0)
            controller.currentAttackPurpose = Util.CastToolPurposePlus(controller.currentAttackPurpose, false); // 시리즈 01로

        // Ability 재선택 및 슬롯 오버라이드
        if (controller.AbilitySet.TryGetAbility<AttackAbilitySet>(Define.MonsterAbilityType.Attack, out var attackSet))
        {
            var ability = attackSet.SelectAttackAbility(Define.AttackStyle.Melee, controller.currentAttackPurpose);
            controller.SetCurrentAttackAbility(ability);
            if (ability is IAnimClipProvider provider)
            {
                var clip = provider.GetAttackAnimationClip();
                string slotKey = Util.GetAnimatorSlotKeyByPurpose(controller.currentAttackPurpose);
                if (clip != null)
                {
                    controller.OverrideAnimationClip(slotKey, clip);
                    controller.animator.CrossFade(clip.name, 0.05f);
                }
            }
        }

    controller.SetAttack(true);
    controller.CurrentAttackAbility?.Execute();
        hitIndex++;

    // 첫 히트(launch 직후 hitIndex==1)가 끝나기 전엔 재시도/중단 로직 없음
    // 첫 히트 도중 플레이어가 크게 벗어나도 애니는 끝까지 재생 후 위 StateUpdate에서 중단 판단
    }

    private void ResetCombo()
    {
        // 콤보 초기화 및 Purpose를 01로 되돌림
        controller.currentAttackPurpose = Util.CastToolPurposePlus(controller.currentAttackPurpose, false);
        hitIndex = 0;
    Debug.Log($"리셋 콤보 시에 {hitIndex}");
    }
}
