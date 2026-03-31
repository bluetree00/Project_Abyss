using UnityEngine;
using Abyss.Monster;

/// <summary>
/// 몬스터 공격 형태 추상 SO.
/// MonsterStatSO.attackShape 에 할당하면 MonsterBase.DealDamageToPlayer()가
/// 기본 구체 판정 대신 이 SO에 위임한다.
/// </summary>
public abstract class MonsterAttackShapeSO : ScriptableObject
{
    /// <summary>
    /// 공격 판정 실행. 데미지, 넉백, 히트 대상 필터를 SO가 결정한다.
    /// </summary>
    /// <param name="ctx">몬스터 컨텍스트 (위치, 방향, SO 등 접근용).</param>
    /// <param name="damage">최종 데미지 (attackPower * AttackMultiplier 적용 후).</param>
    /// <param name="knockbackForce">적용할 넉백 힘.</param>
    public abstract void Execute(MonsterContext ctx, int damage, float knockbackForce);
}
