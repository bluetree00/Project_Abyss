using RelicFairy.Monster;
using UnityEngine;

/// <summary>
/// 선인장 가시 반격 특수 상태 데이터 SO.
/// 피격 시마다 발동 — 즉시 주변 가시 반격 범위 공격.
/// </summary>
[CreateAssetMenu(fileName = "CactusThornData", menuName = "Lee/Monster/Special/CactusThornData")]
public class CactusThornData : SpecialStateDataBase
{
    [Tooltip("가시 반격 반경 (m)")]
    public float thornRadius = 2.5f;

    [Tooltip("가시 반격 데미지")]
    public float thornDamage = 6f;

    [Tooltip("피격 반응 애니메이션 상태 이름")]
    public string thornStateName = "Cactus_GetHit";

    public override SpecialStateBase CreateState() => new CactusThornRetaliateState(this);
}
