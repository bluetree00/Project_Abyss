/// <summary>
/// 데미지를 받을 수 있는 모든 오브젝트가 구현하는 인터페이스.
/// 플레이어/몬스터/파괴 가능 오브젝트 등에 적용.
///
/// element / elementAmount 는 기본값 None / 0 이므로
/// 기존 호출부 변경 없이 원소 미적용 공격을 그대로 사용할 수 있다.
/// 플레이어 무기에 원소 속성이 생기면 해당 값을 전달한다.
/// </summary>
public interface IDamageable
{
    void TakeDamage(float amount, UnityEngine.GameObject instigator,
                    float knockbackMultiplier = 1f,
                    ElementType element       = ElementType.None,
                    float elementAmount       = 0f);
}
