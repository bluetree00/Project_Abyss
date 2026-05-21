namespace RelicFairy.Monster
{
/// <summary>
/// 외눈슬라임 몬스터.
/// 특수 상태: 배회 중 일정 주기마다 이동을 멈추고 HP를 회복한다 (SlimeRegenState).
/// 전환 조건은 SlimeConfig.stateTransitions SO 로 관리한다.
/// </summary>
public class SlimeMonster : MonsterBase
{
    public const string PrefabAddress = "Slime/Slime";
    protected override string ConfigAddress    => "Slime/SlimeConfig";
    protected override string DataAddress      => "Slime/SlimeData";
    protected override float  HPBarHeadOffset  => 0.7f;
}
}
