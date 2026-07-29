/// <summary>
/// 데미지를 받을 수 있는 모든 오브젝트가 구현하는 인터페이스.
/// 플레이어/몬스터/파괴 가능 오브젝트 등에 적용.
/// </summary>
public interface IDamageable
{
    void TakeDamage(float amount, UnityEngine.GameObject instigator,
                    float knockbackMultiplier = 1f, bool isCrit = false);
}
