using UnityEngine;

[CreateAssetMenu(menuName = "Monster/Abilities/Chase")]
public class ChaseAbilitySO : MonsterAbilitySO 
{
    [SerializeField] private float speed;
    [SerializeField] private float attackRange;

    public override Define.MonsterAbilityType MonsterAbilityType => Define.MonsterAbilityType.Chase;

    public void SetChaseSpeed(float S)
    {
        speed = S;
    }

    public void SetAttackRange(float A)
    {
        attackRange = A;
    }

    // 플레이어 Transform을 인자로 받아서 어빌리티 인스턴스 생성 시 주입
    public IMonsterAbility CreateAbilityInstance(Transform playerTransform)
    {
        return new ChaseAbility(speed, attackRange);
    }

    // 기존 인터페이스에 맞춘 오버라이드용 메서드 (필요 시)
    public override IMonsterAbility CreateAbilityInstance()
    {
         return new ChaseAbility(speed, attackRange);
    }
}
