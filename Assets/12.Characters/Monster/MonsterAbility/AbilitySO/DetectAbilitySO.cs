using UnityEngine;

[CreateAssetMenu(menuName = "Monster/Abilities/Detect", fileName = "New_Detect")]
public class DetectAbilitySO : MonsterAbilitySO 
{
    [SerializeField] private float range;

    public override Define.MonsterAbilityType MonsterAbilityType => Define.MonsterAbilityType.Detect;

    public void SetRange(float r)
    {
        range = r;
    }

    // 플레이어 Transform을 인자로 받아서 어빌리티 인스턴스 생성 시 주입
    public IMonsterAbility CreateAbilityInstance(Transform playerTransform)
    {
        return new DetectAbility(range);
    }

    // 기존 인터페이스에 맞춘 오버라이드용 메서드 (필요 시)
    public override IMonsterAbility ReturnAbilityInstance()
    {
        // Managers.Player.PlayerTransform 이 null 일 수 있으니 주의
        return new DetectAbility(range);
    }
}
