namespace Abyss.Monster
{
/// <summary>
/// 달팽이 몬스터.
/// 특수 상태: 전투 중 일정 주기마다 이동 멈추고 받는 데미지 50% 감소 (SnailShellState).
/// 전환 조건은 SnailConfig.stateTransitions SO 로 관리한다.
/// </summary>
public class SnailMonster : MonsterBase
{
    public const string PrefabAddress = "Snail/Snail";
    protected override string ConfigAddress    => "Snail/SnailConfig";
    protected override string DataAddress      => "Snail/SnailData";
    protected override float  HPBarHeadOffset  => 0.7f;
}
}
