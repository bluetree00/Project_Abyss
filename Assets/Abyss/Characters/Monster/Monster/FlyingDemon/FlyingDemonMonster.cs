
namespace Abyss.Monster
{
/// <summary>
/// 플라잉데몬 몬스터.
/// ChaseState 가 FlyingDemonOverrideSO 에 의해 선회 추격 버전으로 교체된다.
/// MonsterConfigSO 의 stateOverrides 슬롯에 FlyingDemonOverride.asset 을 할당한다.
/// </summary>
public class FlyingDemonMonster : MonsterBase
{
    public const string PrefabAddress = "FlyingDemon/FlyingDemon";
    protected override string ConfigAddress    => "FlyingDemon/FlyingDemonConfig";
    protected override string DataAddress      => string.Empty;
    protected override string HeadBoneName     => null;   // 비행형 — 머리 본 없음
    protected override float  HPBarHeadOffset  => 0.8f;
}
}
