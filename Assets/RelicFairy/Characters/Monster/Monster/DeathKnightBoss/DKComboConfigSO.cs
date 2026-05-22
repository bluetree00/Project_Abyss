using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// DeathKnight 콤보 횟수 정의 SO.
/// BossConfigSO.patternEntries 에 등록해 DKComboRunner 가 몇 연속 공격할지 결정할 때 사용한다.
/// GetRuntimeState() = null — 이 SO 는 FSM 상태를 직접 실행하지 않는다.
/// </summary>
[CreateAssetMenu(menuName = "Abyss/Boss/DeathKnight/DK_ComboConfig", fileName = "DKComboConfig")]
public class DKComboConfigSO : BossPatternSO
{
    [Tooltip("이 콤보에서 브레이크 없이 연속으로 발동할 공격 횟수")]
    public int comboCount = 1;

    public override bool CanExecute(BossPatternContext ctx)
        => ctx.Ctx.Runtime.PlayerTarget != null;

    // DKComboRunner 가 직접 공격 풀을 선택해 실행하므로 RuntimeState 불필요
    public override SpecialStateBase GetRuntimeState() => null;
}
}
