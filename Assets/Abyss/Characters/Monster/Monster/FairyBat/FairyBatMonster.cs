
namespace Abyss.Monster
{
/// <summary>
/// 페어리 박쥐 몬스터.
///
/// ━━━ 스펙 ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
///  이름    : 페어리 박쥐
///  등급    : 일반
///  공격    : 근거리 몸통박치기
///  배회    : 스폰 기준 가로 3m 왕복
///  인식    : 5m
///  HP      : 10 / 방어 : 1 / 공격력 : 10 / 속도 : 3 m/s
///  사정거리: 2m / 히트 반경 : 1m
///  공격속도: 1회/s / 공격 딜레이 : 1s
/// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
///
/// 빈 오브젝트에 이 컴포넌트만 추가하면 나머지는 Addressables 주소를 통해
/// 자동으로 로드된다. Inspector에 SO를 직접 할당할 필요 없음.
///
/// Addressables 등록 경로 (에디터에서 설정):
///   MonsterConfig  →  "FairyBat/FairyBatConfig"
///   AnimController →  "FairyBat/FairyBatAnimatorController" (선택)
/// </summary>
/// <summary>
/// 페어리박쥐 몬스터.
/// 특수 상태: HP 40% 이하 도달 시 1회 도주 (FairyBatFleeState).
/// </summary>

public class FairyBatMonster : MonsterBase
{
    public const string PrefabAddress = "FairyBat/FairyBat";
    protected override string ConfigAddress    => "FairyBat/FairyBatConfig";
    protected override string DataAddress      => "FairyBat/FairyBatData";
    protected override string HeadBoneName     => null;
    protected override float  HPBarHeadOffset  => 0.2f;

}
}
