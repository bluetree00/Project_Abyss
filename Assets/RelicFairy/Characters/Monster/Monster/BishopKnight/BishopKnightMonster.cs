
namespace RelicFairy.Monster
{
/// <summary>
/// 비숍나이트 몬스터.
/// HP 50% 이하 도달 시 1회 방패 방어 + 반격(BishopShieldGuardState) 발동.
/// MonsterConfigSO 의 specialStates 슬롯에 BishopShieldData.asset 과 HP 조건 SO 를 조합해 할당한다.
/// </summary>
public class BishopKnightMonster : MonsterBase
{
    public const string PrefabAddress = "BishopKnight/BishopKnight";
    protected override string ConfigAddress => "BishopKnight/BishopKnightConfig";
    protected override string DataAddress   => string.Empty;

    // Head 본 이름이 없으면 렌더러 바운드 상단 기준으로 HP 바를 배치한다.
    protected override string HeadBoneName  => null;
    protected override float  HPBarHeadOffset => 0.25f;
}
}
