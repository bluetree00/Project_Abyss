namespace Abyss.Monster
{
/// <summary>
/// 비홀더 몬스터.
/// 특수 상태: HP 60% 이하일 때 반복 발동 — 차지 후 눈 빔 범위 공격 (BeholderEyeBeamState).
/// </summary>
public class BeholderMonster : MonsterBase
{
    public const string PrefabAddress = "Beholder/Beholder";
    protected override string ConfigAddress => "Beholder/BeholderConfig";
    protected override string DataAddress   => string.Empty;

    // Head 본 이름이 없으면 렌더러 바운드 상단 기준으로 HP 바를 배치한다.
    protected override string HeadBoneName  => null;
    protected override float  HPBarHeadOffset => 0.25f;
}
}
